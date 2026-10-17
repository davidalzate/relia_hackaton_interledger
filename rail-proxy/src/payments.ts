import { createHash, randomUUID } from 'node:crypto';
import {
  isFinalizedGrantWithAccessToken,
  isPendingGrant,
  OpenPaymentsClientError,
  type AuthenticatedClient,
  type IncomingPayment,
  type OutgoingPayment,
  type PendingGrant,
  type Quote,
} from '@interledger/open-payments';
import type { Logger } from 'pino';
import { sameAsset, type Amount } from './amount.js';
import type { NonInteractiveGrants } from './grants.js';
import { describeRailError, ProblemError, railUnavailable } from './problem.js';
import type { ReliaGrantService } from './relia-grant.js';
import type { Db } from './store.js';
import type { ResolvedWallet, WalletResolver } from './wallets.js';

export type Direction = 'outgoing' | 'incoming';
export type PaymentStatus =
  | 'pending_approval'
  | 'processing'
  | 'completed'
  | 'failed'
  | 'rejected'
  | 'expired'
  | 'cancelled';
/** Lado del pago que queda fijo en la moneda de Relia. */
type FixedSide = 'receive' | 'debit';
export type FailureCode = 'insufficient_funds' | 'quote_expired' | 'relia_grant_unavailable' | 'partial_payment' | 'rail_error';

export interface CreatePaymentRequest {
  direction: Direction;
  counterparty: string;
  receiveAmount: Amount;
  expiresAt?: string;
  description?: string;
}

/** `Payment` del contrato. */
export interface PaymentView {
  id: string;
  idempotencyKey: string;
  direction: Direction;
  status: PaymentStatus;
  sender: string;
  receiver: string;
  receiveAmount: Amount;
  debitAmount: Amount | null;
  receivedAmount: Amount | null;
  quotedReceiveAmount: Amount | null;
  approval: { method: 'redirect' | 'qr'; url?: string; payload?: string } | null;
  expiresAt: string | null;
  failureCode: FailureCode | null;
  failureDetail: string | null;
  rail: { incomingPaymentUrl: string | null; quoteUrl: string | null; outgoingPaymentUrl: string | null };
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  version: number;
}

interface PaymentRow {
  id: string;
  idempotency_key: string;
  direction: Direction;
  status: PaymentStatus;
  sender: string;
  receiver: string;
  asset_code: string;
  asset_scale: number;
  receive_value: string;
  debit_value: string | null;
  received_value: string | null;
  /** `receive`: el receptor recibe exactamente `receive_value`. `debit`: Relia debita exactamente `receive_value`. */
  fixed_side: FixedSide;
  // Moneda de cada monto; NULL = la de `asset_code` (pagos anteriores a multimoneda).
  debit_asset_code: string | null;
  debit_asset_scale: number | null;
  received_asset_code: string | null;
  received_asset_scale: number | null;
  /** Lo que el quote promete entregar al receptor, en su moneda. */
  quoted_receive_value: string | null;
  quoted_receive_asset_code: string | null;
  quoted_receive_asset_scale: number | null;
  description: string | null;
  expires_at: string | null;
  failure_code: FailureCode | null;
  failure_detail: string | null;
  incoming_payment_url: string | null;
  incoming_auth_server: string | null;
  quote_url: string | null;
  outgoing_payment_url: string | null;
  processing_since: number | null;
  next_poll_at: number | null;
  created_at: string;
  updated_at: string;
  completed_at: string | null;
  version: number;
  /** Huella del cuerpo normalizado que creó el pago, para detectar reusos de la clave con otro cuerpo. */
  request_hash: string | null;
  // Solo en `incoming`.
  approval_url: string | null;
  continue_uri: string | null;
  continue_token: string | null;
  payer_wallet_url: string | null;
  payer_auth_server: string | null;
  payer_resource_server: string | null;
  outgoing_access_token: string | null;
  outgoing_token_manage: string | null;
  requoted: number;
}

const NO_INCOMING_FIELDS = {
  approval_url: null,
  continue_uri: null,
  continue_token: null,
  payer_wallet_url: null,
  payer_auth_server: null,
  payer_resource_server: null,
  outgoing_access_token: null,
  outgoing_token_manage: null,
  requoted: 0,
} satisfies Partial<PaymentRow>;

const DEFAULT_WAIT_S = 5;
const RETRY_AFTER_ERROR_MS = 5000;

type PaymentUpdate = Partial<Omit<PaymentRow, 'id' | 'idempotency_key' | 'created_at' | 'updated_at' | 'version'>>;

export interface PaymentDeps {
  db: Db;
  client: AuthenticatedClient;
  wallets: Pick<WalletResolver, 'resolve'>;
  reliaWallet: ResolvedWallet;
  reliaGrants: Pick<ReliaGrantService, 'assertCanPay' | 'withPermitToken' | 'markExhausted' | 'markPaid'>;
  grants: NonInteractiveGrants;
  log: Logger;
  now?: () => number;
}

/**
 * Pagos de riel: crea, lee y avanza la máquina de estados. Es el único que cambia el estado de un pago;
 * cada cambio sube `version` y `updatedAt` en una sola escritura.
 */
export class PaymentService {
  private readonly now: () => number;
  private readonly creating = new Map<string, Promise<void>>();
  private readonly locks = new Map<string, Promise<void>>();

  constructor(private readonly deps: PaymentDeps) {
    this.now = deps.now ?? Date.now;
  }

  get(id: string): PaymentView {
    const row = this.deps.db.prepare(`SELECT * FROM payments WHERE id = ?`).get(id) as PaymentRow | undefined;
    if (!row) throw new ProblemError(404, 'not_found', 'Pago inexistente', `No hay un pago con id ${id}.`);
    return toView(row);
  }

  /** Crea el pago, o devuelve el existente (`created: false`) si la `Idempotency-Key` ya se usó. */
  async create(idempotencyKey: string, request: CreatePaymentRequest): Promise<{ payment: PaymentView; created: boolean }> {
    // Una sola creación en vuelo por clave: la segunda petición espera y recibe el pago de la primera.
    const previous = this.creating.get(idempotencyKey) ?? Promise.resolve();
    const current = previous.catch(() => undefined).then(() => this.createOnce(idempotencyKey, request));
    const settled = current.then(
      () => undefined,
      () => undefined,
    );
    this.creating.set(idempotencyKey, settled);
    void settled.then(() => {
      if (this.creating.get(idempotencyKey) === settled) this.creating.delete(idempotencyKey);
    });
    return current;
  }

  private async createOnce(
    idempotencyKey: string,
    request: CreatePaymentRequest,
  ): Promise<{ payment: PaymentView; created: boolean }> {
    const requestHash = fingerprint(request);
    const existing = this.findByKey(idempotencyKey);
    if (existing) {
      // Pagos guardados antes de la huella (request_hash NULL) no se pueden comparar: se devuelven tal cual.
      if (existing.request_hash !== null && existing.request_hash !== requestHash) {
        throw new ProblemError(
          409,
          'idempotency_conflict',
          'Conflicto de idempotencia',
          `La Idempotency-Key ${idempotencyKey} ya se usó con otro cuerpo (pago ${existing.id}).`,
        );
      }
      return { payment: toView(existing), created: false };
    }

    let created: PaymentRow;
    if (request.direction === 'incoming') {
      const expiresAt = request.expiresAt;
      if (!expiresAt) {
        throw new ProblemError(422, 'validation_error', 'Solicitud inválida', 'expiresAt es obligatorio en incoming.');
      }
      if (Date.parse(expiresAt) <= this.now()) {
        throw new ProblemError(422, 'validation_error', 'Solicitud inválida', 'expiresAt debe estar en el futuro.');
      }
      const payer = await this.validateCounterparty(request);
      created = await this.createIncoming(idempotencyKey, { ...request, expiresAt }, payer);
    } else {
      const receiver = await this.validateCounterparty(request);
      created = await this.createOutgoing(idempotencyKey, request, receiver);
    }
    const row = { ...created, request_hash: requestHash };

    try {
      this.insert(row);
    } catch (error) {
      // El dinero ya se mueve en el riel: dejar rastro para conciliar a mano.
      this.deps.log.error(
        { err: error, idempotencyKey, rail: { incoming: row.incoming_payment_url, outgoing: row.outgoing_payment_url } },
        'el pago se creó en el riel pero no se pudo guardar',
      );
      throw error;
    }
    this.deps.log.info({ paymentId: row.id, direction: row.direction, receiver: row.receiver }, 'pago creado');
    return { payment: toView(row), created: true };
  }

  /** Trabajo del sincronizador: vence cobros, consulta aprobaciones y confirma los pagos que ya toca consultar. */
  async sync(): Promise<void> {
    const start = this.now();
    // Vencimiento: fecha límite guardada, revisada en cada vuelta (sobrevive a un reinicio). expires_at se
    // guarda con toISOString, así que se compara como texto.
    const due = this.deps.db
      .prepare(
        `SELECT id FROM payments WHERE status IN ('pending_approval', 'processing') AND next_poll_at <= @now
         UNION
         SELECT id FROM payments WHERE status = 'pending_approval' AND expires_at <= @nowIso`,
      )
      .all({ now: start, nowIso: iso(start) }) as Array<{ id: string }>;
    for (const { id } of due) {
      // Un pago ocupado (p. ej. un cancel en curso) se deja para la siguiente vuelta en vez de frenar a los demás.
      if (this.locks.has(id)) continue;
      await this.withLock(id, async () => {
        // Releer bajo el candado y con la hora actual: la vuelta pudo tardar.
        const row = this.findById(id);
        const now = this.now();
        if (!row) return;
        if (row.status === 'pending_approval') {
          if (row.expires_at !== null && Date.parse(row.expires_at) <= now) await this.expire(row, now);
          else if (row.next_poll_at !== null && row.next_poll_at <= now) await this.pollApproval(row, now);
        } else if (row.status === 'processing' && row.next_poll_at !== null && row.next_poll_at <= now) {
          await this.confirm(row, now);
        }
      });
    }
  }

  /**
   * Cancela un cobro que aún no se aprueba: revoca la aprobación en el riel, cierra el cobro y lo deja
   * `cancelled`. Cancelar uno ya cancelado devuelve el mismo pago. Después de aprobado: 409.
   */
  async cancel(id: string): Promise<PaymentView> {
    return this.withLock(id, async () => {
      const row = this.findById(id);
      if (!row) throw new ProblemError(404, 'not_found', 'Pago inexistente', `No hay un pago con id ${id}.`);
      if (row.status === 'cancelled') return toView(row);
      if (row.direction !== 'incoming' || row.status !== 'pending_approval') {
        throw new ProblemError(
          409,
          'payment_not_cancellable',
          'El pago no se puede cancelar',
          row.direction === 'outgoing'
            ? 'Solo se cancelan cobros (incoming) pendientes de aprobación.'
            : `El pago está en ${row.status}; solo se cancela en pending_approval.`,
        );
      }

      // Si el riel no responde, el pago queda como estaba y el backend puede reintentar.
      const revoked = await this.revokePayerGrant(row);
      if (revoked !== true) throw railUnavailable(revoked);
      const now = this.now();
      this.transition(row, 'pending_approval', { status: 'cancelled', next_poll_at: null }, now);
      this.deps.log.info({ paymentId: row.id }, 'cobro cancelado');
      this.clearPayerTokens(row.id);
      await this.closeIncoming(row.incoming_auth_server!, row.incoming_payment_url!);
      return toView(this.findById(id)!);
    });
  }

  /**
   * Venció `expiresAt` sin aprobación: revoca la aprobación en el riel, cierra el cobro y pasa a `expired`.
   * Si el riel no responde, sigue pendiente y se reintenta en la siguiente vuelta (una aprobación tardía no
   * se procesa: el vencimiento se revisa antes que la aprobación).
   */
  private async expire(row: PaymentRow, now: number): Promise<void> {
    const revoked = await this.revokePayerGrant(row);
    if (revoked !== true) {
      this.deps.log.warn({ paymentId: row.id, error: describeRailError(revoked) }, 'no se pudo revocar un cobro vencido; se reintenta');
      return;
    }
    if (!this.transition(row, 'pending_approval', { status: 'expired', next_poll_at: null }, now)) return;
    this.deps.log.info({ paymentId: row.id }, 'cobro vencido sin aprobación');
    this.clearPayerTokens(row.id);
    await this.closeIncoming(row.incoming_auth_server!, row.incoming_payment_url!);
  }

  /**
   * Revoca la continuación del grant del pagador (DELETE). true si quedó revocada o ya no existía (4xx);
   * el error si el riel no respondió (red o 5xx).
   */
  private async revokePayerGrant(row: PaymentRow): Promise<true | unknown> {
    const current = this.findById(row.id) ?? row;
    if (!current.continue_uri || !current.continue_token) return true;
    try {
      await this.deps.client.grant.cancel({ url: current.continue_uri, accessToken: current.continue_token });
      return true;
    } catch (error) {
      const gone = error instanceof OpenPaymentsClientError && error.status !== undefined && error.status >= 400 && error.status < 500;
      return gone ? true : error;
    }
  }

  /** Serializa el trabajo sobre un mismo pago (sincronizador y cancel). */
  private async withLock<T>(id: string, work: () => Promise<T>): Promise<T> {
    const previous = this.locks.get(id) ?? Promise.resolve();
    const current = previous.then(work);
    const settled = current.then(
      () => undefined,
      () => undefined,
    );
    this.locks.set(id, settled);
    void settled.then(() => {
      if (this.locks.get(id) === settled) this.locks.delete(id);
    });
    return current;
  }

  private async validateCounterparty(request: CreatePaymentRequest): Promise<ResolvedWallet> {
    const relia = this.deps.reliaWallet;
    const unsupported = (detail: string) => new ProblemError(422, 'unsupported_asset', 'Moneda no soportada', detail);

    if (!sameAsset(request.receiveAmount, relia)) {
      throw unsupported(`El riel opera en ${relia.assetCode} con escala ${relia.assetScale}.`);
    }
    if (BigInt(request.receiveAmount.value) === 0n) {
      throw new ProblemError(422, 'validation_error', 'Solicitud inválida', 'receiveAmount.value debe ser mayor que 0.');
    }
    const counterparty = await this.deps.wallets.resolve(request.counterparty);
    if (counterparty.url === relia.url) {
      throw new ProblemError(422, 'same_wallet', 'Misma billetera', 'La contraparte es la billetera de Relia.');
    }
    // La contraparte puede estar en cualquier moneda: si el riel no convierte, falla el quote (unsupported_asset).
    return counterparty;
  }

  /**
   * Relia paga: incoming payment en el receptor, quote y outgoing payment en la billetera de Relia con el
   * permiso permanente. Si algo falla, no queda pago y el cobro abierto en el receptor se cierra.
   *
   * Misma moneda: el incoming lleva `incomingAmount` = `receiveAmount` y el receptor recibe exacto.
   * Entre monedas: el incoming va sin monto y el quote fija `debitAmount` = `receiveAmount`; Relia envía
   * exactamente ese monto y el receptor recibe lo que el riel convierta.
   */
  private async createOutgoing(
    idempotencyKey: string,
    request: CreatePaymentRequest,
    receiver: ResolvedWallet,
  ): Promise<PaymentRow> {
    const { client, grants, reliaWallet: relia } = this.deps;
    // Fallar antes de abrir nada en el riel si el permiso no está activo.
    this.deps.reliaGrants.assertCanPay();
    const metadata = paymentMetadata(idempotencyKey, request.description);
    const fixedSide: FixedSide = sameAsset(receiver, relia) ? 'receive' : 'debit';

    let incoming: IncomingPayment;
    try {
      incoming = await grants.withToken(receiver.authServer, 'incoming-payment', (accessToken) =>
        client.incomingPayment.create(
          { url: receiver.resourceServer, accessToken },
          fixedSide === 'receive'
            ? { walletAddress: receiver.url, incomingAmount: request.receiveAmount, metadata }
            : { walletAddress: receiver.url, metadata },
        ),
      );
    } catch (error) {
      throw railUnavailable(error);
    }

    let quote: Quote;
    let outgoing: OutgoingPayment;
    try {
      quote = await grants.withToken(relia.authServer, 'quote', (accessToken) =>
        client.quote.create(
          { url: relia.resourceServer, accessToken },
          fixedSide === 'receive'
            ? { walletAddress: relia.url, receiver: incoming.id, method: 'ilp' }
            : { walletAddress: relia.url, receiver: incoming.id, method: 'ilp', debitAmount: request.receiveAmount },
        ),
      );
    } catch (error) {
      await this.closeIncoming(receiver.authServer, incoming.id);
      if (fixedSide === 'debit' && isUnsupportedConversion(error)) throw unsupportedConversion(relia, receiver, error);
      throw railUnavailable(error);
    }
    const quoteId = quote.id;
    let grantId: string | undefined;
    try {
      outgoing = await this.deps.reliaGrants.withPermitToken('create', (accessToken, usedGrant) => {
        grantId = usedGrant;
        return client.outgoingPayment.create(
          { url: relia.resourceServer, accessToken },
          { walletAddress: relia.url, quoteId, metadata },
        );
      });
      this.deps.reliaGrants.markPaid(grantId!);
    } catch (error) {
      await this.closeIncoming(receiver.authServer, incoming.id);
      if (error instanceof ProblemError) throw error;
      if (error instanceof OpenPaymentsClientError && error.status === 403) {
        // Rafiki: InsufficientGrant. Con el inicio del intervalo validado al pedir el permiso, es el tope:
        // agotado o sin espacio para este monto.
        if (grantId) this.deps.reliaGrants.markExhausted(grantId);
        throw new ProblemError(
          503,
          'relia_grant_unavailable',
          'Relia no puede pagar',
          `El permiso de Relia no alcanza para este pago en el periodo actual. Riel: ${describeRailError(error)}`,
        );
      }
      if (error instanceof OpenPaymentsClientError && error.status === 401) {
        throw new ProblemError(
          503,
          'relia_grant_unavailable',
          'Relia no puede pagar',
          `El riel no aceptó el token del permiso ni después de rotarlo. Riel: ${describeRailError(error)}`,
        );
      }
      throw railUnavailable(error);
    }

    const now = this.now();
    return {
      id: randomUUID(),
      idempotency_key: idempotencyKey,
      direction: 'outgoing',
      status: 'processing',
      sender: relia.pointer,
      receiver: receiver.pointer,
      asset_code: request.receiveAmount.assetCode,
      asset_scale: request.receiveAmount.assetScale,
      receive_value: request.receiveAmount.value,
      debit_value: quote.debitAmount.value,
      received_value: null,
      fixed_side: fixedSide,
      ...quoteAmounts(quote),
      received_asset_code: null,
      received_asset_scale: null,
      description: request.description ?? null,
      expires_at: request.expiresAt ?? null,
      failure_code: null,
      failure_detail: null,
      incoming_payment_url: incoming.id,
      incoming_auth_server: receiver.authServer,
      quote_url: quoteId,
      outgoing_payment_url: outgoing.id,
      processing_since: now,
      next_poll_at: now + pollDelay(0),
      created_at: iso(now),
      updated_at: iso(now),
      completed_at: null,
      version: 1,
      request_hash: null,
      ...NO_INCOMING_FIELDS,
    };
  }

  /**
   * Relia cobra: incoming payment en la billetera de Relia (vence en `expiresAt`), quote en la billetera del
   * pagador y grant interactivo `outgoing-payment` en su servidor de autorización, sin `finish`. El pagador
   * aprueba en su billetera y el sincronizador detecta el resultado.
   */
  private async createIncoming(
    idempotencyKey: string,
    request: CreatePaymentRequest & { expiresAt: string },
    payer: ResolvedWallet,
  ): Promise<PaymentRow> {
    const { client, grants, reliaWallet: relia } = this.deps;
    const metadata = paymentMetadata(idempotencyKey, request.description);

    let incoming: IncomingPayment;
    try {
      incoming = await grants.withToken(relia.authServer, 'incoming-payment', (accessToken) =>
        client.incomingPayment.create(
          { url: relia.resourceServer, accessToken },
          {
            walletAddress: relia.url,
            incomingAmount: request.receiveAmount,
            expiresAt: new Date(request.expiresAt).toISOString(),
            metadata,
          },
        ),
      );
    } catch (error) {
      throw railUnavailable(error);
    }

    // El quote se hace en la billetera del pagador: `debitAmount` sale en su moneda y Relia recibe exacto.
    let quote: Quote;
    try {
      quote = await grants.withToken(payer.authServer, 'quote', (accessToken) =>
        client.quote.create(
          { url: payer.resourceServer, accessToken },
          { walletAddress: payer.url, receiver: incoming.id, method: 'ilp' },
        ),
      );
    } catch (error) {
      await this.closeIncoming(relia.authServer, incoming.id);
      if (!sameAsset(payer, relia) && isUnsupportedConversion(error)) throw unsupportedConversion(relia, payer, error);
      throw railUnavailable(error);
    }

    let pending: PendingGrant;
    try {
      const grant = await client.grant.request(
        { url: payer.authServer },
        {
          access_token: {
            access: [
              {
                type: 'outgoing-payment',
                actions: ['create', 'read', 'list'],
                identifier: payer.url,
                limits: { debitAmount: quote.debitAmount, receiver: incoming.id },
              },
            ],
          },
          interact: { start: ['redirect'] },
        },
      );
      if (!isPendingGrant(grant)) {
        throw new Error('El servidor de autorización del pagador concedió el cobro sin pedir aprobación.');
      }
      pending = grant;
    } catch (error) {
      await this.closeIncoming(relia.authServer, incoming.id);
      throw railUnavailable(error);
    }

    const now = this.now();
    return {
      id: randomUUID(),
      idempotency_key: idempotencyKey,
      direction: 'incoming',
      status: 'pending_approval',
      sender: payer.pointer,
      receiver: relia.pointer,
      asset_code: request.receiveAmount.assetCode,
      asset_scale: request.receiveAmount.assetScale,
      receive_value: request.receiveAmount.value,
      debit_value: quote.debitAmount.value,
      received_value: null,
      fixed_side: 'receive',
      ...quoteAmounts(quote),
      received_asset_code: null,
      received_asset_scale: null,
      description: request.description ?? null,
      expires_at: new Date(request.expiresAt).toISOString(),
      failure_code: null,
      failure_detail: null,
      incoming_payment_url: incoming.id,
      incoming_auth_server: relia.authServer,
      quote_url: quote.id,
      outgoing_payment_url: null,
      processing_since: null,
      next_poll_at: now + (pending.continue.wait ?? DEFAULT_WAIT_S) * 1000,
      created_at: iso(now),
      updated_at: iso(now),
      completed_at: null,
      version: 1,
      request_hash: null,
      approval_url: pending.interact.redirect,
      continue_uri: pending.continue.uri,
      continue_token: pending.continue.access_token.value,
      payer_wallet_url: payer.url,
      payer_auth_server: payer.authServer,
      payer_resource_server: payer.resourceServer,
      outgoing_access_token: null,
      outgoing_token_manage: null,
      requoted: 0,
    };
  }

  /**
   * Consulta la aprobación del pagador. Aprobado: guarda el token, pasa a `processing` y crea el outgoing
   * payment en su billetera. Rechazado (`request_denied`): `rejected` y se cierra el cobro.
   */
  private async pollApproval(row: PaymentRow, now: number): Promise<void> {
    const reschedule = (ms: number) =>
      this.deps.db
        .prepare(`UPDATE payments SET next_poll_at = ? WHERE id = ? AND status = 'pending_approval'`)
        .run(now + ms, row.id);

    let result;
    try {
      result = await this.deps.client.grant.continue({ url: row.continue_uri!, accessToken: row.continue_token! });
    } catch (error) {
      if (error instanceof OpenPaymentsClientError && error.code === 'request_denied') {
        if (this.transition(row, 'pending_approval', { status: 'rejected', next_poll_at: null }, now)) {
          this.deps.log.info({ paymentId: row.id }, 'el pagador rechazó el cobro');
          await this.closeIncoming(row.incoming_auth_server!, row.incoming_payment_url!);
          await this.releasePayerGrant(row);
        }
        return;
      }
      const tooFast = error instanceof OpenPaymentsClientError && error.code === 'too_fast';
      if (!tooFast) {
        this.deps.log.warn({ paymentId: row.id, error: describeRailError(error) }, 'no se pudo consultar la aprobación');
      }
      reschedule(tooFast ? DEFAULT_WAIT_S * 1000 : RETRY_AFTER_ERROR_MS);
      return;
    }

    if (!result.continue) {
      reschedule(RETRY_AFTER_ERROR_MS);
      return;
    }
    if (!isFinalizedGrantWithAccessToken(result)) {
      // Pendiente. La continuación puede cambiar: guardar la más reciente. No es un cambio del pago.
      this.deps.db
        .prepare(
          `UPDATE payments SET continue_uri = ?, continue_token = ?, next_poll_at = ?
           WHERE id = ? AND status = 'pending_approval'`,
        )
        .run(
          result.continue.uri,
          result.continue.access_token.value,
          now + (result.continue.wait ?? DEFAULT_WAIT_S) * 1000,
          row.id,
        );
      return;
    }

    const approved = this.transition(
      row,
      'pending_approval',
      {
        status: 'processing',
        outgoing_access_token: result.access_token.value,
        outgoing_token_manage: result.access_token.manage,
        continue_uri: result.continue.uri,
        continue_token: result.continue.access_token.value,
        processing_since: now,
        next_poll_at: now + pollDelay(0),
      },
      now,
    );
    if (!approved) return;
    this.deps.log.info({ paymentId: row.id }, 'el pagador aprobó el cobro');
    await this.createPayerOutgoing(this.findById(row.id)!, now, false);
  }

  /**
   * Crea el outgoing payment en la billetera del pagador con el token que él aprobó. Si la quote venció,
   * cotiza de nuevo una vez; si la nueva quote supera el débito aprobado, el cobro falla con `quote_expired`.
   * Un error transitorio se reintenta en la siguiente vuelta; antes de reintentar se busca si el intento
   * anterior sí creó el pago (respuesta perdida), para no debitar dos veces al pagador.
   */
  private async createPayerOutgoing(row: PaymentRow, now: number, isRetry: boolean): Promise<void> {
    const { client, grants } = this.deps;
    const metadata = paymentMetadata(row.idempotency_key, row.description ?? undefined);
    const create = (quoteId: string) =>
      this.withPayerToken(row, (accessToken) =>
        client.outgoingPayment.create(
          { url: row.payer_resource_server!, accessToken },
          { walletAddress: row.payer_wallet_url!, quoteId, metadata },
        ),
      );
    const created = (outgoingId: string) =>
      this.transition(row, 'processing', { outgoing_payment_url: outgoingId }, now);

    if (isRetry) {
      const existing = await this.findPayerOutgoing(row);
      if (existing === undefined) return; // No se pudo consultar: no arriesgar un segundo débito.
      if (existing) {
        this.deps.log.info({ paymentId: row.id }, 'el outgoing del pagador ya existía (respuesta perdida)');
        created(existing.id);
        return;
      }
    }

    try {
      created((await create(row.quote_url!)).id);
      return;
    } catch (error) {
      if (!isInvalidQuote(error) || row.requoted) {
        await this.handlePayerOutgoingError(row, error, now);
        return;
      }
    }

    // La quote venció antes de la aprobación: una sola recotización.
    let quote: Quote;
    try {
      quote = await grants.withToken(row.payer_auth_server!, 'quote', (accessToken) =>
        client.quote.create(
          { url: row.payer_resource_server!, accessToken },
          { walletAddress: row.payer_wallet_url!, receiver: row.incoming_payment_url!, method: 'ilp' },
        ),
      );
    } catch (error) {
      // Sin quote nueva no se gasta la única recotización.
      await this.handlePayerOutgoingError(row, error, now);
      return;
    }
    if (BigInt(quote.debitAmount.value) > BigInt(row.debit_value!)) {
      await this.fail(row, 'quote_expired', `La nueva cotización (${quote.debitAmount.value}) supera el débito aprobado (${row.debit_value}).`, now);
      return;
    }
    // Guardar la quote nueva antes de usarla: si la creación falla de forma transitoria, el reintento la usa.
    const requote = { quote_url: quote.id, debit_value: quote.debitAmount.value, ...quoteAmounts(quote), requoted: 1 };
    this.transition(row, 'processing', requote, now);
    row = { ...row, ...requote };
    try {
      created((await create(quote.id)).id);
    } catch (error) {
      if (isInvalidQuote(error)) {
        await this.fail(row, 'quote_expired', `La cotización venció de nuevo: ${describeRailError(error)}`, now);
        return;
      }
      await this.handlePayerOutgoingError(row, error, now);
    }
  }

  /**
   * Busca en la billetera del pagador el outgoing payment de este cobro (por quote o por `externalRef`).
   * null si no existe; undefined si no se pudo consultar.
   */
  private async findPayerOutgoing(row: PaymentRow): Promise<OutgoingPayment | null | undefined> {
    try {
      const page = await this.withPayerToken(row, (accessToken) =>
        this.deps.client.outgoingPayment.list(
          // Solo la primera página. Sin verificar contra la red: el orden por defecto de la lista en Rafiki.
          { url: row.payer_resource_server!, walletAddress: row.payer_wallet_url!, accessToken },
        ),
      );
      return (
        page.result.find(
          (payment) => payment.quoteId === row.quote_url || payment.metadata?.externalRef === row.idempotency_key,
        ) ?? null
      );
    } catch (error) {
      this.deps.log.warn({ paymentId: row.id, error: describeRailError(error) }, 'no se pudo buscar el outgoing del pagador');
      return undefined;
    }
  }

  /** Usa el token que aprobó el pagador; si el riel responde 401 (vencido), lo rota una vez y reintenta. */
  private async withPayerToken<T>(row: PaymentRow, use: (accessToken: string) => Promise<T>): Promise<T> {
    const current = this.findById(row.id) ?? row;
    try {
      return await use(current.outgoing_access_token!);
    } catch (error) {
      if (!(error instanceof OpenPaymentsClientError && error.status === 401) || !current.outgoing_token_manage) throw error;
      const rotated = await this.deps.client.token.rotate({
        url: current.outgoing_token_manage,
        accessToken: current.outgoing_access_token!,
      });
      this.deps.db
        .prepare(`UPDATE payments SET outgoing_access_token = ?, outgoing_token_manage = ? WHERE id = ?`)
        .run(rotated.access_token.value, rotated.access_token.manage, row.id);
      return use(rotated.access_token.value);
    }
  }

  private async handlePayerOutgoingError(row: PaymentRow, error: unknown, now: number): Promise<void> {
    const transient =
      !(error instanceof OpenPaymentsClientError) || error.status === undefined || error.status >= 500 || error.status === 429;
    const expired = row.expires_at !== null && now >= Date.parse(row.expires_at);
    if (transient && !expired) {
      this.deps.log.warn({ paymentId: row.id, error: describeRailError(error) }, 'no se pudo crear el outgoing del pagador; se reintenta');
      return;
    }
    const code = isInvalidQuote(error) ? 'quote_expired' : 'rail_error';
    await this.fail(row, code, `No se pudo crear el pago en la billetera del pagador: ${describeRailError(error)}`, now);
  }

  /** `processing` → `failed`, cierra el cobro y libera el permiso del pagador. */
  private async fail(row: PaymentRow, code: FailureCode, detail: string, now: number, extra: PaymentUpdate = {}): Promise<void> {
    const changed = this.transition(
      row,
      'processing',
      { ...extra, status: 'failed', failure_code: code, failure_detail: detail, next_poll_at: null },
      now,
    );
    if (!changed) return;
    this.deps.log.warn({ paymentId: row.id, failureCode: code }, 'pago fallido');
    if (row.incoming_auth_server && row.incoming_payment_url) {
      await this.closeIncoming(row.incoming_auth_server, row.incoming_payment_url);
    }
    await this.releasePayerGrant(row);
  }

  /**
   * En un estado final de un cobro: revoca en el riel el permiso que dio el pagador y borra sus tokens del
   * almacén. Sin bloquear: si la revocación falla, solo se registra.
   */
  private async releasePayerGrant(row: PaymentRow): Promise<void> {
    if (row.direction !== 'incoming') return;
    const revoked = await this.revokePayerGrant(row);
    if (revoked !== true) {
      this.deps.log.debug({ paymentId: row.id, error: describeRailError(revoked) }, 'no se pudo revocar el permiso del pagador');
    }
    this.clearPayerTokens(row.id);
  }

  private clearPayerTokens(id: string): void {
    this.deps.db
      .prepare(
        `UPDATE payments SET continue_token = NULL, outgoing_access_token = NULL, outgoing_token_manage = NULL WHERE id = ?`,
      )
      .run(id);
  }

  /** Cambia el estado solo si sigue en `from`. Devuelve si lo cambió. */
  private transition(row: PaymentRow, from: PaymentStatus, changes: PaymentUpdate, now: number): boolean {
    const columns = Object.keys(changes);
    const result = this.deps.db
      .prepare(
        `UPDATE payments SET ${[...columns.map((c) => `${c} = @${c}`), 'updated_at = @updated_at', 'version = version + 1'].join(', ')}
         WHERE id = @id AND status = @from`,
      )
      .run({ ...changes, updated_at: iso(now), id: row.id, from });
    return result.changes === 1;
  }

  private findById(id: string): PaymentRow | undefined {
    return this.deps.db.prepare(`SELECT * FROM payments WHERE id = ?`).get(id) as PaymentRow | undefined;
  }

  /**
   * Éxito: el incoming payment recibió el monto completo. Falla: el outgoing payment quedó `failed`.
   * Si el riel no confirma, el pago sigue en `processing` y se vuelve a consultar.
   */
  private async confirm(row: PaymentRow, now: number): Promise<void> {
    if (row.fixed_side === 'debit') {
      await this.confirmFixedDebit(row, now);
      return;
    }

    // Primero el incoming: si el dinero ya llegó, el pago está completo aunque falte el outgoing en el registro.
    const incoming = await this.readIncoming(row);
    const changes = receivedChanges(row, incoming);
    if (incoming && BigInt(incoming.receivedAmount.value) >= BigInt(row.receive_value)) {
      if (this.complete(row, changes, now)) await this.releasePayerGrant(row);
      return;
    }

    // Cobro aprobado cuyo outgoing payment aún no se pudo crear: reintentar.
    if (row.direction === 'incoming' && !row.outgoing_payment_url) {
      await this.createPayerOutgoing(row, now, true);
      this.deps.db
        .prepare(`UPDATE payments SET next_poll_at = ? WHERE id = ? AND status = 'processing' AND outgoing_payment_url IS NULL`)
        .run(now + RETRY_AFTER_ERROR_MS, row.id);
      return;
    }

    const outgoing = await this.readOutgoing(row);
    if (outgoing?.failed) {
      const sent = BigInt(outgoing.sentAmount.value);
      this.deps.log.warn({ paymentId: row.id, sent: outgoing.sentAmount.value }, 'pago fallido en el riel');
      await this.fail(
        row,
        sent > 0n ? 'partial_payment' : 'rail_error',
        `El riel marcó el pago como fallido: enviado ${outgoing.sentAmount.value} de ${outgoing.debitAmount.value} ${outgoing.debitAmount.assetCode}.`,
        now,
        changes,
      );
      return;
    }

    this.reschedule(row, changes, now);
  }

  /**
   * `outgoing` entre monedas (Relia debita exacto). Éxito: el outgoing de Relia envió todo su `debitAmount` sin
   * fallar, o el receptor ya recibió lo que prometió el quote (respaldo si no se puede leer el outgoing).
   * `receivedAmount` sale del incoming del receptor, en su moneda: si no se pudo leer, no se completa en esa vuelta. Al terminar se cierra ese incoming, que sin
   * monto no se cierra solo.
   */
  private async confirmFixedDebit(row: PaymentRow, now: number): Promise<void> {
    // Primero el outgoing y después el incoming: así lo recibido refleja al menos lo ya enviado.
    const outgoing = await this.readOutgoing(row);
    const incoming = await this.readIncoming(row);
    const changes = receivedChanges(row, incoming);

    if (outgoing?.failed) {
      this.deps.log.warn({ paymentId: row.id, sent: outgoing.sentAmount.value }, 'pago fallido en el riel');
      await this.fail(
        row,
        BigInt(outgoing.sentAmount.value) > 0n ? 'partial_payment' : 'rail_error',
        `El riel marcó el pago como fallido: enviado ${outgoing.sentAmount.value} de ${outgoing.debitAmount.value} ${outgoing.debitAmount.assetCode}.`,
        now,
        changes,
      );
      return;
    }

    // Sin leer el incoming del receptor no se completa: `receivedAmount` quedaría vacío. Se reintenta.
    if (incoming === undefined) {
      this.reschedule(row, changes, now);
      return;
    }

    const sentAll = outgoing !== undefined && BigInt(outgoing.sentAmount.value) >= BigInt(outgoing.debitAmount.value);
    const deliveredQuote =
      row.quoted_receive_value !== null && BigInt(incoming.receivedAmount.value) >= BigInt(row.quoted_receive_value);
    if (sentAll || deliveredQuote) {
      if (this.complete(row, changes, now)) await this.closeIncoming(row.incoming_auth_server!, row.incoming_payment_url!);
      return;
    }

    this.reschedule(row, changes, now);
  }

  /** Pasa el pago de `processing` a `completed` con lo recibido. Devuelve false si otro proceso ya lo movió. */
  private complete(row: PaymentRow, changes: PaymentUpdate, now: number): boolean {
    const done = this.transition(row, 'processing', { ...changes, status: 'completed', completed_at: iso(now), next_poll_at: null }, now);
    if (done) this.deps.log.info({ paymentId: row.id }, 'pago completado');
    return done;
  }

  /** Lee el incoming payment del pago (el de Relia en `incoming`, el del receptor en `outgoing`). */
  private async readIncoming(row: PaymentRow): Promise<IncomingPayment | undefined> {
    if (!row.incoming_payment_url || !row.incoming_auth_server) return undefined;
    try {
      return await this.deps.grants.withToken(row.incoming_auth_server, 'incoming-payment', (accessToken) =>
        this.deps.client.incomingPayment.get({ url: row.incoming_payment_url!, accessToken }),
      );
    } catch (error) {
      this.deps.log.warn({ paymentId: row.id, error: describeRailError(error) }, 'no se pudo leer el incoming payment');
      return undefined;
    }
  }

  /** Programa la siguiente consulta de un pago en `processing`, guardando lo que haya cambiado. */
  private reschedule(row: PaymentRow, changes: PaymentUpdate, now: number): void {
    const delay = pollDelay(now - (row.processing_since ?? now));
    if (Object.keys(changes).length > 0) {
      this.update(row, { ...changes, next_poll_at: now + delay }, now);
    } else {
      // Reprogramar no es un cambio del pago: no sube `version`.
      this.deps.db.prepare(`UPDATE payments SET next_poll_at = ? WHERE id = ?`).run(now + delay, row.id);
    }
  }

  private async readOutgoing(row: PaymentRow): Promise<OutgoingPayment | undefined> {
    if (!row.outgoing_payment_url) return undefined;
    if (row.direction === 'incoming') {
      // En la billetera del pagador, con el token que él aprobó (rotado si venció).
      try {
        return await this.withPayerToken(row, (accessToken) =>
          this.deps.client.outgoingPayment.get({ url: row.outgoing_payment_url!, accessToken }),
        );
      } catch (error) {
        this.deps.log.debug({ paymentId: row.id, error: describeRailError(error) }, 'no se pudo leer el outgoing del pagador');
        return undefined;
      }
    }
    try {
      return await this.deps.reliaGrants.withPermitToken('read', (accessToken) =>
        this.deps.client.outgoingPayment.get({ url: row.outgoing_payment_url!, accessToken }),
      );
    } catch (error) {
      // Sin permiso aprobado no hay con qué leer; el éxito igual se detecta por el incoming payment.
      if (!(error instanceof ProblemError)) {
        this.deps.log.warn({ paymentId: row.id, error: describeRailError(error) }, 'no se pudo leer el outgoing payment');
      }
      return undefined;
    }
  }

  /** Cierra un incoming payment para que no reciba más dinero. Sin bloquear: si falla, solo se registra. */
  private async closeIncoming(authServer: string, url: string): Promise<void> {
    try {
      await this.deps.grants.withToken(authServer, 'incoming-payment', (accessToken) =>
        this.deps.client.incomingPayment.complete({ url, accessToken }),
      );
    } catch (error) {
      this.deps.log.warn({ incomingPayment: url, error: describeRailError(error) }, 'no se pudo cerrar el incoming payment');
    }
  }

  private findByKey(idempotencyKey: string): PaymentRow | undefined {
    return this.deps.db.prepare(`SELECT * FROM payments WHERE idempotency_key = ?`).get(idempotencyKey) as
      | PaymentRow
      | undefined;
  }

  private insert(row: PaymentRow): void {
    const columns = Object.keys(row);
    this.deps.db
      .prepare(`INSERT INTO payments (${columns.join(', ')}) VALUES (${columns.map((c) => `@${c}`).join(', ')})`)
      .run(row);
  }

  private update(row: PaymentRow, changes: PaymentUpdate, now: number): void {
    const columns = Object.keys(changes);
    const assignments = columns.map((column) => `${column} = @${column}`);
    this.deps.db
      .prepare(
        `UPDATE payments SET ${[...assignments, 'updated_at = @updated_at', 'version = version + 1'].join(', ')}
         WHERE id = @id`,
      )
      .run({ ...changes, updated_at: iso(now), id: row.id });
  }
}

/**
 * Huella del cuerpo normalizado: mismos campos en el mismo orden, montos sin ceros a la izquierda y fechas en
 * UTC. Así dos cuerpos equivalentes escritos distinto no se toman como conflicto.
 */
function fingerprint(request: CreatePaymentRequest): string {
  const normalized = {
    direction: request.direction,
    counterparty: request.counterparty,
    receiveAmount: {
      value: BigInt(request.receiveAmount.value).toString(),
      assetCode: request.receiveAmount.assetCode,
      assetScale: request.receiveAmount.assetScale,
    },
    expiresAt: request.expiresAt ? new Date(request.expiresAt).toISOString() : null,
    description: request.description ?? null,
  };
  return createHash('sha256').update(JSON.stringify(normalized)).digest('hex');
}

function paymentMetadata(idempotencyKey: string, description: string | undefined): Record<string, string> {
  return description ? { description, externalRef: idempotencyKey } : { externalRef: idempotencyKey };
}

/** Cada ~1 s los primeros ~10 s, luego cada ~3 s y después de ~2 min cada ~30 s, sin fin. */
function pollDelay(elapsedMs: number): number {
  if (elapsedMs < 10_000) return 1_000;
  if (elapsedMs < 120_000) return 3_000;
  return 30_000;
}

function toView(row: PaymentRow): PaymentView {
  // Sin moneda propia guardada (pagos anteriores a multimoneda): la de `receiveAmount`.
  const amount = (value: string | null, assetCode: string | null = null, assetScale: number | null = null): Amount | null =>
    value === null
      ? null
      : { value, assetCode: assetCode ?? row.asset_code, assetScale: assetScale ?? row.asset_scale };
  // Pagos con `receiveAmount` fijo guardados antes de multimoneda: el quote prometía exactamente ese monto.
  const quotedReceive =
    row.quoted_receive_value === null && row.fixed_side === 'receive' && row.quote_url !== null
      ? amount(row.receive_value)
      : amount(row.quoted_receive_value, row.quoted_receive_asset_code, row.quoted_receive_asset_scale);
  return {
    id: row.id,
    idempotencyKey: row.idempotency_key,
    direction: row.direction,
    status: row.status,
    sender: row.sender,
    receiver: row.receiver,
    receiveAmount: amount(row.receive_value)!,
    debitAmount: amount(row.debit_value, row.debit_asset_code, row.debit_asset_scale),
    receivedAmount: amount(row.received_value, row.received_asset_code, row.received_asset_scale),
    quotedReceiveAmount: quotedReceive,
    approval: row.status === 'pending_approval' && row.approval_url ? { method: 'redirect', url: row.approval_url } : null,
    expiresAt: row.expires_at,
    failureCode: row.failure_code,
    failureDetail: row.failure_detail,
    rail: {
      incomingPaymentUrl: row.incoming_payment_url,
      quoteUrl: row.quote_url,
      outgoingPaymentUrl: row.outgoing_payment_url,
    },
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    completedAt: row.completed_at,
    version: row.version,
  };
}

/** Débito (moneda del pagador) y entrega prometida (moneda del receptor) de un quote, en columnas del almacén. */
function quoteAmounts(quote: Quote) {
  return {
    debit_asset_code: quote.debitAmount.assetCode,
    debit_asset_scale: quote.debitAmount.assetScale,
    quoted_receive_value: quote.receiveAmount.value,
    quoted_receive_asset_code: quote.receiveAmount.assetCode,
    quoted_receive_asset_scale: quote.receiveAmount.assetScale,
  } satisfies PaymentUpdate;
}

/** Lo recibido según el incoming payment, con su moneda. `receivedAmount` es null hasta que empiece a llegar. */
function receivedChanges(row: PaymentRow, incoming: IncomingPayment | undefined): PaymentUpdate {
  if (!incoming || BigInt(incoming.receivedAmount.value) === 0n || incoming.receivedAmount.value === row.received_value) {
    return {};
  }
  return {
    received_value: incoming.receivedAmount.value,
    received_asset_code: incoming.receivedAmount.assetCode,
    received_asset_scale: incoming.receivedAmount.assetScale,
  };
}

/**
 * El quote falló porque el riel no cotiza entre las dos monedas: solo un 4xx de Rafiki cuya descripción o código
 * apunta a la tasa, la moneda o la conversión (p. ej. "Rate probe failed", "Could not get rates", "unsupported
 * asset"). Cualquier 5xx, 401/403/408/429 o un 4xx sin esa pista sigue el camino de error del riel. Solo se usa
 * en pagos entre monedas distintas.
 */
function isUnsupportedConversion(error: unknown): boolean {
  if (!(error instanceof OpenPaymentsClientError)) return false;
  const status = error.status;
  if (status === undefined || status < 400 || status >= 500 || [401, 403, 408, 429].includes(status)) return false;
  const hint = /\b(rates?|rate[ _-]?probe|assets?|conver(t|sion)|currenc(y|ies)|exchange)\b/i;
  return hint.test(error.description ?? '') || hint.test(error.code ?? '') || hint.test(error.message ?? '');
}

function unsupportedConversion(relia: ResolvedWallet, counterparty: ResolvedWallet, error: unknown): ProblemError {
  return new ProblemError(
    422,
    'unsupported_asset',
    'Moneda no soportada',
    `El riel no cotiza entre ${relia.assetCode} y ${counterparty.assetCode} (${counterparty.pointer}). Riel: ${describeRailError(error)}`,
  );
}

/** Rafiki responde 400 "invalid quote" (OutgoingPaymentError.InvalidQuote) cuando la quote venció. */
function isInvalidQuote(error: unknown): boolean {
  return error instanceof OpenPaymentsClientError && error.status === 400 && /quote/i.test(error.description);
}

function iso(ms: number): string {
  return new Date(ms).toISOString();
}
