import { randomUUID } from 'node:crypto';
import {
  isFinalizedGrantWithAccessToken,
  isPendingGrant,
  OpenPaymentsClientError,
  type AuthenticatedClient,
} from '@interledger/open-payments';
import type { Logger } from 'pino';
import { sameAsset, type Amount } from './amount.js';
import { describeRailError, ProblemError, railUnavailable } from './problem.js';
import type { Db } from './store.js';
import type { ResolvedWallet } from './wallets.js';

export type ReliaGrantStatus = 'active' | 'awaiting_approval' | 'exhausted' | 'missing';

/** `ReliaGrant` del contrato. */
export interface ReliaGrantView {
  status: ReliaGrantStatus;
  approvalUrl: string | null;
  debitLimit: Amount | null;
  interval: string | null;
  approvedAt: string | null;
}

type GrantState = 'pending' | 'approved' | 'replaced' | 'superseded' | 'rejected' | 'abandoned';

interface GrantRow {
  id: string;
  state: GrantState;
  debit_value: string;
  asset_code: string;
  asset_scale: number;
  interval: string;
  approval_url: string | null;
  continue_uri: string;
  continue_token: string;
  next_poll_at: number | null;
  interaction_expires_at: number | null;
  access_token: string | null;
  manage_url: string | null;
  token_expires_at: number | null;
  approved_at: string | null;
}

// Rafiki deja aprobar durante 10 min (INTERACTION_EXPIRY_SECONDS) y el sondeo no informa el vencimiento.
const INTERACTION_WINDOW_MS = 11 * 60 * 1000;
const DEFAULT_WAIT_S = 5;
const RETRY_AFTER_ERROR_MS = 5000;

const DURATION = /^P(?!$)(\d+Y)?(\d+M)?(\d+W)?(\d+D)?(T(?=\d)(\d+H)?(\d+M)?(\d+S)?)?$/;

/** Intervalo ISO 8601 repetitivo: `R[n]/<inicio>/<duración>`, p. ej. `R/2026-10-16T00:00:00-05:00/P1D`. */
export function isRepeatingInterval(value: string): boolean {
  const [repeat, start, duration, ...rest] = value.split('/');
  return (
    rest.length === 0 &&
    /^R\d*$/.test(repeat ?? '') &&
    /^\d{4}-\d{2}-\d{2}T/.test(start ?? '') &&
    !Number.isNaN(Date.parse(start ?? '')) &&
    DURATION.test(duration ?? '')
  );
}

/**
 * Permiso permanente de la billetera de Relia: un grant `outgoing-payment` con tope por intervalo que un
 * operador aprueba una vez. Se pide sin `finish`; el sincronizador consulta la continuación hasta saber
 * si se aprobó, se rechazó o venció la ventana.
 */
export class ReliaGrantService {
  constructor(
    private readonly db: Db,
    private readonly client: AuthenticatedClient,
    private readonly wallet: ResolvedWallet,
    private readonly log: Logger,
    private readonly now: () => number = Date.now,
  ) {}

  private readonly revocations = new Set<Promise<void>>();

  status(): ReliaGrantStatus {
    return statusOf(this.findOne('pending'), this.findOne('approved'));
  }

  view(): ReliaGrantView {
    const pending = this.findOne('pending');
    const approved = this.findOne('approved');
    const row = pending ?? approved;
    return {
      status: statusOf(pending, approved),
      approvalUrl: pending?.approval_url ?? null,
      debitLimit: row ? { value: row.debit_value, assetCode: row.asset_code, assetScale: row.asset_scale } : null,
      interval: row?.interval ?? null,
      approvedAt: pending ? null : (row?.approved_at ?? null),
    };
  }

  async request(debitLimit: Amount, interval: string): Promise<ReliaGrantView> {
    if (!sameAsset(debitLimit, this.wallet)) {
      throw new ProblemError(
        422,
        'unsupported_asset',
        'Moneda no soportada',
        `La billetera de Relia opera en ${this.wallet.assetCode} con escala ${this.wallet.assetScale}.`,
      );
    }
    if (BigInt(debitLimit.value) === 0n) {
      throw new ProblemError(422, 'validation_error', 'Solicitud inválida', 'debitLimit.value debe ser mayor que 0.');
    }
    if (!isRepeatingInterval(interval)) {
      throw new ProblemError(
        422,
        'validation_error',
        'Solicitud inválida',
        'interval debe ser un intervalo ISO 8601 repetitivo, p. ej. R/2026-10-16T00:00:00-05:00/P1D.',
      );
    }

    let grant;
    try {
      grant = await this.client.grant.request(
        { url: this.wallet.authServer },
        {
          access_token: {
            access: [
              {
                type: 'outgoing-payment',
                actions: ['create', 'read', 'list'],
                identifier: this.wallet.url,
                limits: { debitAmount: debitLimit, interval },
              },
            ],
          },
          interact: { start: ['redirect'] },
        },
      );
    } catch (error) {
      throw railUnavailable(error);
    }
    if (!isPendingGrant(grant)) {
      throw new ProblemError(503, 'rail_unavailable', 'El riel no respondió', 'El riel concedió el permiso sin pedir aprobación.');
    }

    const now = this.now();
    const id = randomUUID();
    const superseded = this.db.transaction(() => {
      const previous = this.findAll('pending');
      this.db
        .prepare(`UPDATE relia_grants SET state = 'superseded', next_poll_at = NULL, updated_at = ? WHERE state = 'pending'`)
        .run(iso(now));
      this.db
        .prepare(
          `INSERT INTO relia_grants (id, state, debit_value, asset_code, asset_scale, interval, approval_url,
             continue_uri, continue_token, next_poll_at, interaction_expires_at, created_at, updated_at)
           VALUES (?, 'pending', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
        )
        .run(
          id,
          debitLimit.value,
          debitLimit.assetCode,
          debitLimit.assetScale,
          interval,
          grant.interact.redirect,
          grant.continue.uri,
          grant.continue.access_token.value,
          now + (grant.continue.wait ?? DEFAULT_WAIT_S) * 1000,
          now + INTERACTION_WINDOW_MS,
          iso(now),
          iso(now),
        );
      return previous;
    })();

    this.log.info({ grantId: id, debitLimit, interval }, 'permiso de Relia pedido; falta la aprobación del operador');
    for (const row of superseded) this.revokeInBackground(row, 'otra petición lo reemplazó antes de aprobarse');
    return this.view();
  }

  /** Espera las revocaciones en curso; se llama al apagar. */
  async drain(): Promise<void> {
    await Promise.all([...this.revocations]);
  }

  /** Trabajo del sincronizador: consulta las peticiones pendientes que ya pueden consultarse. */
  async sync(): Promise<void> {
    const now = this.now();
    const due = this.db
      .prepare(`SELECT * FROM relia_grants WHERE state = 'pending' AND next_poll_at <= ?`)
      .all(now) as GrantRow[];
    for (const row of due) await this.poll(row, now);
  }

  private async poll(row: GrantRow, now: number): Promise<void> {
    if (row.interaction_expires_at !== null && now >= row.interaction_expires_at) {
      if (this.transition(row.id, 'pending', 'abandoned', now)) {
        this.log.warn({ grantId: row.id }, 'venció la ventana para aprobar el permiso de Relia; pídelo de nuevo');
        await this.revokeInRail(row, 'venció la ventana de aprobación');
      }
      return;
    }

    let result;
    try {
      result = await this.client.grant.continue({ url: row.continue_uri, accessToken: row.continue_token });
    } catch (error) {
      // `request_denied` = el operador rechazó. Otros 401 (firma, reloj, token) se reintentan hasta que venza la ventana.
      if (error instanceof OpenPaymentsClientError && error.code === 'request_denied') {
        if (this.transition(row.id, 'pending', 'rejected', now)) {
          this.log.info({ grantId: row.id }, 'el operador rechazó el permiso de Relia');
        }
        return;
      }
      const tooFast = error instanceof OpenPaymentsClientError && error.code === 'too_fast';
      if (!tooFast) {
        this.log.warn({ grantId: row.id, error: describeRailError(error) }, 'no se pudo consultar el permiso de Relia');
      }
      this.db
        .prepare(`UPDATE relia_grants SET next_poll_at = ? WHERE id = ? AND state = 'pending'`)
        .run(now + (tooFast ? DEFAULT_WAIT_S * 1000 : RETRY_AFTER_ERROR_MS), row.id);
      return;
    }

    if (!result.continue) {
      this.log.warn({ grantId: row.id }, 'el riel respondió la continuación sin `continue`; se reintenta');
      this.db
        .prepare(`UPDATE relia_grants SET next_poll_at = ? WHERE id = ? AND state = 'pending'`)
        .run(now + RETRY_AFTER_ERROR_MS, row.id);
      return;
    }

    if (!isFinalizedGrantWithAccessToken(result)) {
      const updated = this.db
        .prepare(
          `UPDATE relia_grants SET continue_uri = ?, continue_token = ?, next_poll_at = ?
           WHERE id = ? AND state = 'pending'`,
        )
        .run(
          result.continue.uri,
          result.continue.access_token.value,
          now + (result.continue.wait ?? DEFAULT_WAIT_S) * 1000,
          row.id,
        );
      if (updated.changes === 0) {
        // Dejó de estar pendiente mientras se consultaba: revocar con la continuación más reciente.
        this.revokeInBackground(
          { id: row.id, continue_uri: result.continue.uri, continue_token: result.continue.access_token.value },
          'dejó de estar pendiente durante la consulta',
        );
      }
      return;
    }

    const { access_token: token, continue: cont } = result;
    const replaced = this.db.transaction(() => {
      const approved = this.db
        .prepare(
          `UPDATE relia_grants
           SET state = 'approved', access_token = ?, manage_url = ?, token_expires_at = ?,
               continue_uri = ?, continue_token = ?, next_poll_at = NULL, interaction_expires_at = NULL,
               approved_at = ?, updated_at = ?
           WHERE id = ? AND state = 'pending'`,
        )
        .run(
          token.value,
          token.manage,
          token.expires_in ? now + token.expires_in * 1000 : null,
          cont.uri,
          cont.access_token.value,
          iso(now),
          iso(now),
          row.id,
        );
      if (approved.changes === 0) return null;
      const previous = this.findAll('approved').filter((other) => other.id !== row.id);
      this.db
        .prepare(`UPDATE relia_grants SET state = 'replaced', updated_at = ? WHERE state = 'approved' AND id != ?`)
        .run(iso(now), row.id);
      return previous;
    })();

    if (replaced === null) {
      // Otra petición lo reemplazó mientras se consultaba: el grant recién emitido sobra.
      await this.revokeInRail({ id: row.id, continue_uri: cont.uri, continue_token: cont.access_token.value }, 'se aprobó tarde');
      return;
    }
    this.log.info({ grantId: row.id }, 'permiso de Relia aprobado y activo');
    for (const old of replaced) this.revokeInBackground(old, 'lo reemplazó un permiso nuevo');
  }

  private revokeInBackground(row: Pick<GrantRow, 'id' | 'continue_uri' | 'continue_token'>, reason: string): void {
    const revocation = this.revokeInRail(row, reason).finally(() => this.revocations.delete(revocation));
    this.revocations.add(revocation);
  }

  /** Revoca el grant en el riel (DELETE de la continuación). Sin bloquear: si falla, solo se registra. */
  private async revokeInRail(row: Pick<GrantRow, 'id' | 'continue_uri' | 'continue_token'>, reason: string): Promise<void> {
    try {
      await this.client.grant.cancel({ url: row.continue_uri, accessToken: row.continue_token });
      this.log.info({ grantId: row.id, reason }, 'permiso de Relia revocado en el riel');
    } catch (error) {
      this.log.warn({ grantId: row.id, reason, error: describeRailError(error) }, 'no se pudo revocar el permiso en el riel');
    }
  }

  private transition(id: string, from: GrantState, to: GrantState, now: number): boolean {
    return (
      this.db
        .prepare(`UPDATE relia_grants SET state = ?, next_poll_at = NULL, updated_at = ? WHERE id = ? AND state = ?`)
        .run(to, iso(now), id, from).changes === 1
    );
  }

  private findOne(state: GrantState): GrantRow | undefined {
    return this.db
      .prepare(`SELECT * FROM relia_grants WHERE state = ? ORDER BY created_at DESC LIMIT 1`)
      .get(state) as GrantRow | undefined;
  }

  private findAll(state: GrantState): GrantRow[] {
    return this.db.prepare(`SELECT * FROM relia_grants WHERE state = ?`).all(state) as GrantRow[];
  }
}

function statusOf(pending: GrantRow | undefined, approved: GrantRow | undefined): ReliaGrantStatus {
  if (pending) return 'awaiting_approval';
  if (approved) return 'active';
  return 'missing';
}

function iso(ms: number): string {
  return new Date(ms).toISOString();
}
