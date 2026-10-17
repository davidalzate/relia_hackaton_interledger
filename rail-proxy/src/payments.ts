import { createHash, randomUUID } from 'node:crypto';
import {
  OpenPaymentsClientError,
  type AuthenticatedClient,
  type IncomingPayment,
  type OutgoingPayment,
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
}

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

    if (request.direction === 'incoming') {
      throw new ProblemError(422, 'validation_error', 'Solicitud inválida', 'Los cobros (incoming) aún no están disponibles.');
    }
    const counterparty = await this.validateCounterparty(request);
    const row = { ...(await this.createOutgoing(idempotencyKey, request, counterparty)), request_hash: requestHash };

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

  /** Trabajo del sincronizador: confirma los pagos en `processing` que ya toca consultar. */
  async sync(): Promise<void> {
    const now = this.now();
    const due = this.deps.db
      .prepare(`SELECT * FROM payments WHERE status = 'processing' AND next_poll_at <= ? ORDER BY next_poll_at`)
      .all(now) as PaymentRow[];
    for (const row of due) await this.confirm(row, now);
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
    if (!sameAsset(counterparty, relia)) {
      throw unsupported(
        `${counterparty.pointer} opera en ${counterparty.assetCode} con escala ${counterparty.assetScale}; el riel opera en ${relia.assetCode} con escala ${relia.assetScale}.`,
      );
    }
    return counterparty;
  }

  /**
   * Relia paga: incoming payment en el receptor, quote y outgoing payment en la billetera de Relia con el
   * permiso permanente. Si algo falla, no queda pago y el cobro abierto en el receptor se cierra.
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

    let incoming: IncomingPayment;
    try {
      incoming = await grants.withToken(receiver.authServer, 'incoming-payment', (accessToken) =>
        client.incomingPayment.create(
          { url: receiver.resourceServer, accessToken },
          { walletAddress: receiver.url, incomingAmount: request.receiveAmount, metadata },
        ),
      );
    } catch (error) {
      throw railUnavailable(error);
    }

    let quoteId: string;
    let debitValue: string;
    let outgoing: OutgoingPayment;
    try {
      const quote = await grants.withToken(relia.authServer, 'quote', (accessToken) =>
        client.quote.create(
          { url: relia.resourceServer, accessToken },
          { walletAddress: relia.url, receiver: incoming.id, method: 'ilp' },
        ),
      );
      quoteId = quote.id;
      debitValue = quote.debitAmount.value;
    } catch (error) {
      await this.closeIncoming(receiver.authServer, incoming.id);
      throw railUnavailable(error);
    }
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
      debit_value: debitValue,
      received_value: null,
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
    };
  }

  /**
   * Éxito: el incoming payment recibió el monto completo. Falla: el outgoing payment quedó `failed`.
   * Si el riel no confirma, el pago sigue en `processing` y se vuelve a consultar.
   */
  private async confirm(row: PaymentRow, now: number): Promise<void> {
    const { client, grants } = this.deps;
    const changes: PaymentUpdate = {};

    let incoming: IncomingPayment | undefined;
    if (row.incoming_payment_url && row.incoming_auth_server) {
      try {
        incoming = await grants.withToken(row.incoming_auth_server, 'incoming-payment', (accessToken) =>
          client.incomingPayment.get({ url: row.incoming_payment_url!, accessToken }),
        );
      } catch (error) {
        this.deps.log.warn({ paymentId: row.id, error: describeRailError(error) }, 'no se pudo leer el incoming payment');
      }
    }
    // `receivedAmount` es null hasta que empiece a llegar dinero.
    if (incoming && BigInt(incoming.receivedAmount.value) > 0n && incoming.receivedAmount.value !== row.received_value) {
      changes.received_value = incoming.receivedAmount.value;
    }
    if (incoming && BigInt(incoming.receivedAmount.value) >= BigInt(row.receive_value)) {
      this.update(row, { ...changes, status: 'completed', completed_at: iso(now), next_poll_at: null }, now);
      this.deps.log.info({ paymentId: row.id }, 'pago completado');
      return;
    }

    const outgoing = await this.readOutgoing(row);
    if (outgoing?.failed) {
      const sent = BigInt(outgoing.sentAmount.value);
      this.update(
        row,
        {
          ...changes,
          status: 'failed',
          failure_code: sent > 0n ? 'partial_payment' : 'rail_error',
          failure_detail: `El riel marcó el pago como fallido: enviado ${outgoing.sentAmount.value} de ${outgoing.debitAmount.value} ${outgoing.debitAmount.assetCode}.`,
          next_poll_at: null,
        },
        now,
      );
      this.deps.log.warn({ paymentId: row.id, sent: outgoing.sentAmount.value }, 'pago fallido en el riel');
      if (row.incoming_auth_server && row.incoming_payment_url) {
        await this.closeIncoming(row.incoming_auth_server, row.incoming_payment_url);
      }
      return;
    }

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
  const amount = (value: string | null): Amount | null =>
    value === null ? null : { value, assetCode: row.asset_code, assetScale: row.asset_scale };
  return {
    id: row.id,
    idempotencyKey: row.idempotency_key,
    direction: row.direction,
    status: row.status,
    sender: row.sender,
    receiver: row.receiver,
    receiveAmount: amount(row.receive_value)!,
    debitAmount: amount(row.debit_value),
    receivedAmount: amount(row.received_value),
    approval: null,
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

function iso(ms: number): string {
  return new Date(ms).toISOString();
}
