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

type GrantState = 'pending' | 'approved' | 'replaced' | 'superseded' | 'rejected' | 'abandoned' | 'revoked';

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
  /** El riel rechazó un pago por el tope: no se intentan más hasta este instante (inicio del siguiente periodo). */
  exhausted_until: number | null;
}

/** Cómo se usa el token del permiso. `create` exige el permiso `active`; `read` sirve con cualquier aprobado. */
export type PermitUse = 'create' | 'read';

// Rotar un poco antes de que el riel deje de aceptar el token.
const ROTATE_MARGIN_MS = 30_000;

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
  /** Una rotación en vuelo por permiso: rotar revoca el token anterior. */
  private readonly rotations = new Map<string, Promise<string>>();

  status(): ReliaGrantStatus {
    return statusOf(this.findOne('pending'), this.findOne('approved'), this.now());
  }

  /**
   * Falla con `503 relia_grant_unavailable` si Relia no puede intentar un pago ahora: sin permiso aprobado
   * o con una petición nueva esperando aprobación. Con el tope agotado sí se intenta: el riel decide si el
   * pago cabe en lo que queda.
   */
  assertCanPay(): void {
    const status = this.status();
    if (status === 'active' || status === 'exhausted') return;
    throw new ProblemError(503, 'relia_grant_unavailable', 'Relia no puede pagar', UNAVAILABLE_DETAIL[status]);
  }

  /**
   * Ejecuta `use` con un token vigente del permiso aprobado. Rota antes de que venza y, si el servidor de
   * recursos responde 401, rota y reintenta una vez. Con `create` aplica `assertCanPay` primero.
   */
  async withPermitToken<T>(purpose: PermitUse, use: (accessToken: string, grantId: string) => Promise<T>): Promise<T> {
    if (purpose === 'create') this.assertCanPay();
    const row = this.findOne('approved');
    if (!row?.access_token || !row.manage_url) {
      throw new ProblemError(503, 'relia_grant_unavailable', 'Relia no puede pagar', UNAVAILABLE_DETAIL.missing);
    }

    const due = row.token_expires_at !== null && this.now() >= row.token_expires_at - ROTATE_MARGIN_MS;
    const token = due ? await this.rotate(row.id, row.access_token) : row.access_token;
    try {
      return await use(token, row.id);
    } catch (error) {
      if (!(error instanceof OpenPaymentsClientError && error.status === 401)) throw error;
      return use(await this.rotate(row.id, token), row.id);
    }
  }

  /**
   * El riel rechazó un pago del permiso `grantId` por el tope: queda `exhausted` hasta el inicio del
   * siguiente periodo, o hasta que otro pago pase (cabía en lo que quedaba).
   */
  markExhausted(grantId: string): void {
    const row = this.db.prepare(`SELECT * FROM relia_grants WHERE id = ?`).get(grantId) as GrantRow | undefined;
    if (!row || row.state !== 'approved') return;
    const until = nextPeriodStart(row.interval, this.now());
    this.db
      .prepare(`UPDATE relia_grants SET exhausted_until = ?, updated_at = ? WHERE id = ? AND state = 'approved'`)
      .run(until, iso(this.now()), row.id);
    this.log.warn(
      { grantId: row.id, until: Number.isFinite(until) ? iso(until) : 'sin más periodos' },
      'el permiso de Relia agotó su tope',
    );
  }

  /** Un pago pasó con el permiso `grantId`: si estaba `exhausted`, vuelve a `active`. */
  markPaid(grantId: string): void {
    this.db
      .prepare(`UPDATE relia_grants SET exhausted_until = NULL, updated_at = ? WHERE id = ? AND exhausted_until IS NOT NULL`)
      .run(iso(this.now()), grantId);
  }

  /** Rota el token `usedToken` del permiso `grantId`, o devuelve el vigente si otro ya lo rotó. */
  private rotate(grantId: string, usedToken: string): Promise<string> {
    let rotation = this.rotations.get(grantId);
    if (!rotation) {
      rotation = this.doRotate(grantId, usedToken).finally(() => this.rotations.delete(grantId));
      this.rotations.set(grantId, rotation);
    }
    return rotation;
  }

  private async doRotate(grantId: string, usedToken: string): Promise<string> {
    const current = this.db.prepare(`SELECT * FROM relia_grants WHERE id = ?`).get(grantId) as GrantRow | undefined;
    if (!current || current.state !== 'approved' || !current.access_token || !current.manage_url) {
      throw new ProblemError(503, 'relia_grant_unavailable', 'Relia no puede pagar', UNAVAILABLE_DETAIL.missing);
    }
    // Otra rotación ya reemplazó el token que se usó.
    if (current.access_token !== usedToken) return current.access_token;

    let rotated;
    try {
      rotated = await this.client.token.rotate({ url: current.manage_url, accessToken: current.access_token });
    } catch (error) {
      // Rafiki responde 404 invalid_rotation cuando el token o el grant fueron revocados. Un 401 puede ser de
      // firma o reloj: se trata como transitorio para no perder un permiso aprobado.
      if (error instanceof OpenPaymentsClientError && error.status === 404) {
        this.db
          .prepare(`UPDATE relia_grants SET state = 'revoked', updated_at = ? WHERE id = ? AND state = 'approved'`)
          .run(iso(this.now()), current.id);
        this.log.error({ grantId: current.id, error: describeRailError(error) }, 'el riel revocó el permiso de Relia');
        throw new ProblemError(503, 'relia_grant_unavailable', 'Relia no puede pagar', 'El riel revocó el permiso de Relia; pide uno nuevo.');
      }
      throw railUnavailable(error);
    }

    const { value, manage, expires_in: expiresIn } = rotated.access_token;
    const saved = this.db
      .prepare(
        `UPDATE relia_grants SET access_token = ?, manage_url = ?, token_expires_at = ?, updated_at = ?
         WHERE id = ? AND state = 'approved'`,
      )
      .run(value, manage, expiresIn !== undefined ? this.now() + expiresIn * 1000 : null, iso(this.now()), current.id);
    if (saved.changes === 0) {
      // Lo reemplazó otro permiso mientras se rotaba: este token ya no debe usarse.
      throw new ProblemError(503, 'relia_grant_unavailable', 'Relia no puede pagar', 'El permiso de Relia cambió; reintenta.');
    }
    this.log.info({ grantId: current.id, expiresIn }, 'token del permiso de Relia rotado');
    return value;
  }

  view(): ReliaGrantView {
    const pending = this.findOne('pending');
    const approved = this.findOne('approved');
    const row = pending ?? approved;
    return {
      status: statusOf(pending, approved, this.now()),
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
    // Rafiki rechaza (403) todo pago creado antes del inicio del intervalo: el permiso no serviría hasta entonces.
    const start = Date.parse(interval.split('/')[1]!);
    if (start > this.now()) {
      throw new ProblemError(
        422,
        'validation_error',
        'Solicitud inválida',
        `El intervalo empieza en el futuro (${new Date(start).toISOString()}); el riel rechaza los pagos hasta entonces. Usa un inicio en el pasado, p. ej. la medianoche de hoy.`,
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

function statusOf(pending: GrantRow | undefined, approved: GrantRow | undefined, now: number): ReliaGrantStatus {
  if (pending) return 'awaiting_approval';
  if (approved) return approved.exhausted_until !== null && now < approved.exhausted_until ? 'exhausted' : 'active';
  return 'missing';
}

const UNAVAILABLE_DETAIL: Record<ReliaGrantStatus, string> = {
  active: 'El permiso de Relia está activo.',
  awaiting_approval: 'Hay un permiso nuevo de Relia esperando la aprobación del operador.',
  exhausted: 'El permiso de Relia agotó su tope en este periodo.',
  missing: 'Relia no tiene un permiso activo para pagar.',
};

/**
 * Inicio del periodo siguiente al que contiene `now` en un intervalo `R[n]/<inicio>/<duración>`.
 * Infinity si no quedan repeticiones.
 */
export function nextPeriodStart(interval: string, now: number): number {
  const [repeat = 'R', startText = '', durationText = ''] = interval.split('/');
  const repetitions = repeat.length > 1 ? Number(repeat.slice(1)) : Number.POSITIVE_INFINITY;
  const start = Date.parse(startText);
  const parts = parseDuration(durationText);
  if (!parts || Number.isNaN(start)) return Number.POSITIVE_INFINITY;

  // Duración fija (sin años ni meses): aritmética directa.
  if (parts.years === 0 && parts.months === 0) {
    const length = (((parts.weeks * 7 + parts.days) * 24 + parts.hours) * 60 + parts.minutes) * 60_000 + parts.seconds * 1000;
    if (length <= 0) return Number.POSITIVE_INFINITY;
    const period = Math.max(0, Math.floor((now - start) / length));
    return period <= repetitions ? start + (period + 1) * length : Number.POSITIVE_INFINITY;
  }

  // Con meses o años: periodo a periodo, encadenando desde el fin del anterior como Rafiki (luxon
  // Interval.after), con fin de mes acotado.
  let end = start;
  for (let period = 1; period <= repetitions + 1 && period <= 10_000; period++) {
    end = addCalendar(end, parts, 1);
    if (now < end) return end;
  }
  return Number.POSITIVE_INFINITY;
}

interface DurationParts {
  years: number;
  months: number;
  weeks: number;
  days: number;
  hours: number;
  minutes: number;
  seconds: number;
}

function parseDuration(text: string): DurationParts | undefined {
  const match = DURATION.exec(text);
  if (!match) return undefined;
  const n = (index: number) => Number.parseInt(match[index] ?? '0', 10) || 0;
  return { years: n(1), months: n(2), weeks: n(3), days: n(4), hours: n(6), minutes: n(7), seconds: n(8) };
}

/** `start` + `times` veces una duración con años o meses, en UTC; el día se acota al último del mes. */
function addCalendar(start: number, d: DurationParts, times: number): number {
  const date = new Date(start);
  const totalMonths = date.getUTCMonth() + (d.years * 12 + d.months) * times;
  const year = date.getUTCFullYear() + Math.floor(totalMonths / 12);
  const month = ((totalMonths % 12) + 12) % 12;
  const lastDay = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
  const shifted = Date.UTC(
    year,
    month,
    Math.min(date.getUTCDate(), lastDay),
    date.getUTCHours(),
    date.getUTCMinutes(),
    date.getUTCSeconds(),
    date.getUTCMilliseconds(),
  );
  const extra = ((((d.weeks * 7 + d.days) * 24 + d.hours) * 60 + d.minutes) * 60 + d.seconds) * 1000 * times;
  return shifted + extra;
}

function iso(ms: number): string {
  return new Date(ms).toISOString();
}
