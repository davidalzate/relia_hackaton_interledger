import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import {
  Observable,
  Subscription,
  catchError,
  delay,
  map,
  of,
  tap,
  throwError,
  timeout,
  timer,
} from 'rxjs';

import { environment } from '../../environments/environment';
import {
  Agent,
  AgentSchema,
  HistoryEntry,
  HistoryKind,
  HistoryPageSchema,
} from '../../shared/schemas/agent.schema';
import {
  AgentBalances,
  Operation,
  OperationDetails,
  OperationType,
} from '../../shared/types/agent';
import { AuthService } from './auth.service';

/** Por debajo de este valor (COP) la Caja muestra avisos de cupo/efectivo bajo */
export const LOW_BALANCE_THRESHOLD = 50_000;
/** Lo que tarda el simulador en acreditar la billetera (B2.4) */
export const DEPOSIT_SEND_MS = 2_000;
/** Tiempo que tiene el usuario para aprobar un retiro en su billetera (B3.2) */
export const WITHDRAWAL_TTL_MS = 5 * 60_000;

export type CupoMethod = 'breb' | 'wallet';
/** Tiempo para pagar una carga de cupo (B6.3) */
export const CUPO_LOAD_TTL_MS: Record<CupoMethod, number> = {
  breb: 10 * 60_000,
  wallet: 5 * 60_000,
};

export interface DepositRequest {
  phone: string;
  roundForUser: boolean;
  cashIn: number;
  walletOut: number;
  commission: number;
}

export interface DepositReceipt {
  operation: Operation;
  walletOut: number;
  before: AgentBalances;
  after: AgentBalances;
}

export type DepositErrorKind = 'network' | 'cupo';

/** El depósito falló sin mover dinero; `operation` queda registrada como fallida */
export class DepositError extends Error {
  constructor(
    readonly kind: DepositErrorKind,
    readonly operation: Operation,
  ) {
    super(kind);
  }
}

/** Datos del formulario B3.1 con el desglose ya calculado */
export interface WithdrawalRequest {
  phone: string;
  amount: number;
  roundForUser: boolean;
  /** Efectivo que entrega el corresponsal */
  cashOut: number;
  /** Lo que paga el usuario desde su billetera */
  walletIn: number;
  commission: number;
}

export type WithdrawalErrorKind = 'rejected' | 'expired' | 'cancelled' | 'failed' | 'cash';

/** Solicitud de pago en curso: la comparten la vista B3 y el teléfono del usuario (C2) */
interface WithdrawalBase {
  request: WithdrawalRequest;
  operation: Operation;
}
export type Withdrawal =
  | (WithdrawalBase & { status: 'waiting'; expiresAt: number })
  | (WithdrawalBase & { status: 'approved'; before: AgentBalances; after: AgentBalances })
  | (WithdrawalBase & { status: 'error'; error: WithdrawalErrorKind });

export interface CupoLoadRequest {
  method: CupoMethod;
  amount: number;
  /** Solo Wallet: llave (celular, 10 dígitos) a la que se envía la solicitud */
  key?: string;
}

export type CupoLoadErrorKind = 'rejected' | 'expired' | 'cancelled';

interface CupoLoadBase {
  request: CupoLoadRequest;
  operation: Operation;
}
/** Carga de cupo en curso (B6.3-B6.5); el panel Modo demo la confirma o rechaza */
export type CupoLoad =
  | (CupoLoadBase & { status: 'waiting'; expiresAt: number })
  | (CupoLoadBase & { status: 'done'; before: AgentBalances; after: AgentBalances })
  | (CupoLoadBase & { status: 'error'; error: CupoLoadErrorKind });

interface BalanceChange {
  before: AgentBalances;
  after: AgentBalances;
}

/** B7: sumar efectivo o fijar el total contado en caja */
export type CashMode = 'add' | 'set';

export interface CashUpdate {
  operation: Operation;
  before: number;
  after: number;
}

const CUPO_ERROR_STATUS: Record<CupoLoadErrorKind, Operation['status']> = {
  rejected: 'failed',
  expired: 'expired',
  cancelled: 'cancelled',
};

const WITHDRAWAL_ERROR_REASON: Record<WithdrawalErrorKind, string> = {
  rejected: 'El usuario rechazó el pago',
  expired: 'La solicitud venció',
  cancelled: 'Solicitud cancelada',
  failed: 'La billetera no completó el pago',
  cash: 'Efectivo insuficiente',
};
const CUPO_ERROR_REASON: Record<CupoLoadErrorKind, string> = {
  rejected: 'Pago rechazado',
  expired: 'La solicitud venció',
  cancelled: 'Solicitud cancelada',
};
const DEPOSIT_ERROR_REASON: Record<DepositErrorKind, string> = {
  network: 'El simulador no respondió',
  cupo: 'Cupo insuficiente',
};

const WITHDRAWAL_ERROR_STATUS: Record<WithdrawalErrorKind, Operation['status']> = {
  rejected: 'failed',
  expired: 'expired',
  cancelled: 'cancelled',
  failed: 'failed',
  cash: 'failed',
};

const KIND_TYPE: Record<HistoryKind, OperationType> = {
  deposit: 'deposit',
  withdrawal: 'withdrawal',
  quotaTopUp: 'cupo',
  cashAdjustment: 'cash',
  settlement: 'commission',
};

/** Tipos locales que cubre cada `kind` del API (filtro de B8) */
export const KIND_TYPES: Record<HistoryKind, OperationType[]> = {
  deposit: ['deposit'],
  withdrawal: ['withdrawal'],
  quotaTopUp: ['cupo'],
  cashAdjustment: ['cash', 'cash-set'],
  settlement: ['commission'],
};

export interface HistoryPage {
  items: Operation[];
  nextCursor: string | null;
}

// Fila del historial del API → modelo que ya usan B1, B8 y el comprobante
function toOperation(entry: HistoryEntry): Operation {
  const { quota, accruedCommission, cashOnHand } = entry.balances;
  const pending = entry.status === 'pending';
  return {
    id: entry.operationId ?? entry.settlementId ?? entry.id,
    reference: entry.reference ?? undefined,
    type: KIND_TYPE[entry.kind],
    amount: entry.amount,
    createdAt: entry.occurredAt,
    status: entry.status,
    before: pending
      ? undefined
      : { cupo: quota.before, commission: accruedCommission.before, cash: cashOnHand.before },
    after: pending
      ? undefined
      : { cupo: quota.after, commission: accruedCommission.after, cash: cashOnHand.after },
    details: entry.reason ? { reason: entry.reason } : undefined,
  };
}

// shortcut: saldos iniciales del API; operaciones, historial y llaves siguen en memoria y
// mueven los saldos localmente hasta integrar su endpoint (docs/wayfinder/integracion-api)
@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly _agent = signal<Agent | null>(null);
  private readonly _balances = signal<AgentBalances>({ cupo: 0, commission: 0, cash: 0 });
  /** Más recientes primero */
  private readonly _operations = signal<Operation[]>([]);
  private sequence = 0;
  private readonly _withdrawal = signal<Withdrawal | null>(null);
  private expiry?: Subscription;
  private readonly _cupoLoad = signal<CupoLoad | null>(null);
  private cupoExpiry?: Subscription;

  /** Perfil y saldos crudos del API (`cashReserved`, `quotaReserved`, `walletAddress`…) */
  readonly agent = this._agent.asReadonly();
  readonly balances = this._balances.asReadonly();
  readonly operations = this._operations.asReadonly();
  /** Del API; `_operations` guarda solo las creadas en memoria (aún no están en el servidor) */
  private readonly _recent = signal<Operation[]>([]);
  private readonly _history = signal<HistoryPage & { kind: HistoryKind | null }>({
    kind: null,
    items: [],
    nextCursor: null,
  });
  readonly recentOperations = computed(() =>
    [...this._operations(), ...this._recent()].slice(0, 3),
  );
  /** B8: las de memoria del filtro actual y luego las páginas cargadas del API */
  readonly history = computed(() => {
    const { kind, items, nextCursor } = this._history();
    const local = kind
      ? this._operations().filter((op) => KIND_TYPES[kind].includes(op.type))
      : this._operations();
    return { items: [...local, ...items], nextCursor };
  });
  readonly withdrawal = this._withdrawal.asReadonly();
  readonly cupoLoad = this._cupoLoad.asReadonly();

  constructor() {
    // Al iniciar o restaurar la sesión del corresponsal
    effect(() => {
      const id = this.auth.agentId();
      if (!id) return;
      untracked(() => {
        this.refresh(id).subscribe({ error: () => undefined });
        this.fetchHistory({ limit: 3 }).subscribe({
          next: ({ items }) => this._recent.set(items),
          error: () => undefined,
        });
      });
    });
  }

  /** `GET /agents/{agentId}`; llamarlo tras cada operación terminal */
  refresh(agentId = this.auth.agentId()): Observable<Agent> {
    return this.http.get(`${environment.apiUrl}/agents/${agentId}`).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => AgentSchema.parse(res)),
      tap((agent) => {
        const { quota, accruedCommission, cashOnHand } = agent.balances;
        this._agent.set(agent);
        this._balances.set({ cupo: quota, commission: accruedCommission, cash: cashOnHand });
      }),
    );
  }

  /** B8: primera página de `kind` (null = todas), o la siguiente con `more` */
  loadHistory(kind: HistoryKind | null, more = false): Observable<HistoryPage> {
    const current = this._history();
    const cursor = more ? current.nextCursor : null;
    return this.fetchHistory({ kind, cursor }).pipe(
      tap((page) =>
        this._history.set({
          kind,
          items: more ? [...current.items, ...page.items] : page.items,
          nextCursor: page.nextCursor,
        }),
      ),
    );
  }

  private fetchHistory(query: {
    kind?: HistoryKind | null;
    cursor?: string | null;
    limit?: number;
  }): Observable<HistoryPage> {
    const params = Object.fromEntries(Object.entries(query).filter(([, v]) => v != null)) as Record<
      string,
      string | number
    >;
    return this.http
      .get(`${environment.apiUrl}/agents/${this.auth.agentId()}/history`, { params })
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => HistoryPageSchema.parse(res)),
        map(({ items, nextCursor }) => ({
          items: items.map(toOperation),
          nextCursor: nextCursor ?? null,
        })),
      );
  }

  /** Verifica que el celular tenga billetera. `simulateMissing` fuerza "no existe" (demo) */
  validateKey(_phone: string, simulateMissing: boolean): Observable<boolean> {
    return of(!simulateMissing).pipe(delay(600), timeout(environment.httpTimeoutMs));
  }

  operation(id: string): Operation | undefined {
    const all = [...this._operations(), ...this._recent(), ...this._history().items];
    return all.find((op) => op.id === id);
  }

  deposit(request: DepositRequest): Observable<DepositReceipt> {
    return timer(DEPOSIT_SEND_MS).pipe(
      map(() => this.applyDeposit(request)),
      timeout(environment.httpTimeoutMs),
      catchError((err: unknown) =>
        throwError(() =>
          err instanceof DepositError
            ? err
            : new DepositError('network', this.recordDeposit(request, 'failed', 'network')),
        ),
      ),
    );
  }

  /** B3.2: crea la solicitud de pago; vence sola a los 5 min si el usuario no responde */
  requestWithdrawal(request: WithdrawalRequest): void {
    this.expiry?.unsubscribe();
    this._withdrawal.set({
      status: 'waiting',
      request,
      operation: this.record(request.cashOut, 'pending', 'withdrawal', undefined, {
        phone: request.phone,
        cash: request.cashOut,
        wallet: request.walletIn,
        roundForUser: request.roundForUser,
      }),
      expiresAt: Date.now() + WITHDRAWAL_TTL_MS,
    });
    this.expiry = timer(WITHDRAWAL_TTL_MS).subscribe(() => this.failWithdrawal('expired'));
  }

  /** El usuario aprueba en su billetera (C2): mueve saldos si hay efectivo para entregar */
  approveWithdrawal(): void {
    const current = this._withdrawal();
    if (current?.status !== 'waiting') return;
    const { cashOut, commission } = current.request;
    const before = this._balances();
    if (before.cash < cashOut) return this.failWithdrawal('cash');
    const after: AgentBalances = {
      cupo: before.cupo + cashOut,
      cash: before.cash - cashOut,
      commission: before.commission + commission,
    };
    this.expiry?.unsubscribe();
    this._balances.set(after);
    this._withdrawal.set({
      status: 'approved',
      request: current.request,
      operation: this.setStatus(current.operation, 'completed', { before, after }),
      before,
      after,
    });
  }

  /** Rechazo del usuario, vencimiento, cancelación del corresponsal o falla de la billetera */
  failWithdrawal(error: WithdrawalErrorKind): void {
    const current = this._withdrawal();
    if (current?.status !== 'waiting') return;
    this.expiry?.unsubscribe();
    this._withdrawal.set({
      status: 'error',
      request: current.request,
      operation: this.setStatus(
        current.operation,
        WITHDRAWAL_ERROR_STATUS[error],
        undefined,
        WITHDRAWAL_ERROR_REASON[error],
      ),
      error,
    });
  }

  /** Cierra el flujo de retiro (Listo, reintento o cierre de sesión); cancela si seguía esperando */
  clearWithdrawal(): void {
    this.failWithdrawal('cancelled');
    this._withdrawal.set(null);
  }

  /** B7: ajusta solo el efectivo; cupo y comisión no cambian */
  updateCash(mode: CashMode, amount: number): CashUpdate {
    const before = this._balances();
    const after = { ...before, cash: mode === 'add' ? before.cash + amount : amount };
    this._balances.set(after);
    const operation = this.record(amount, 'completed', mode === 'add' ? 'cash' : 'cash-set', {
      before,
      after,
    });
    return { operation, before: before.cash, after: after.cash };
  }

  /** B6.3: genera el QR Bre-B o la solicitud a la Wallet; vence según el método */
  requestCupoLoad(request: CupoLoadRequest): void {
    this.cupoExpiry?.unsubscribe();
    const ttl = CUPO_LOAD_TTL_MS[request.method];
    this._cupoLoad.set({
      status: 'waiting',
      request,
      operation: this.record(request.amount, 'pending', 'cupo', undefined, {
        method: request.method,
      }),
      expiresAt: Date.now() + ttl,
    });
    this.cupoExpiry = timer(ttl).subscribe(() => this.failCupoLoad('expired'));
  }

  /** Modo demo: el pago llegó; suma el monto al cupo */
  confirmCupoLoad(): void {
    const current = this._cupoLoad();
    if (current?.status !== 'waiting') return;
    this.cupoExpiry?.unsubscribe();
    const before = this._balances();
    const after = { ...before, cupo: before.cupo + current.request.amount };
    this._balances.set(after);
    this._cupoLoad.set({
      status: 'done',
      request: current.request,
      operation: this.setStatus(current.operation, 'completed', { before, after }),
      before,
      after,
    });
  }

  failCupoLoad(error: CupoLoadErrorKind): void {
    const current = this._cupoLoad();
    if (current?.status !== 'waiting') return;
    this.cupoExpiry?.unsubscribe();
    this._cupoLoad.set({
      status: 'error',
      request: current.request,
      operation: this.setStatus(
        current.operation,
        CUPO_ERROR_STATUS[error],
        undefined,
        CUPO_ERROR_REASON[error],
      ),
      error,
    });
  }

  /** Cierra el flujo de carga; cancela si seguía esperando el pago */
  clearCupoLoad(): void {
    this.failCupoLoad('cancelled');
    this._cupoLoad.set(null);
  }

  private applyDeposit(request: DepositRequest): DepositReceipt {
    const { cashIn, walletOut, commission } = request;
    const before = this._balances();
    // El cupo pudo cambiar desde que se armó el formulario
    if (before.cupo < cashIn) {
      throw new DepositError('cupo', this.recordDeposit(request, 'failed', 'cupo'));
    }
    const after: AgentBalances = {
      cupo: before.cupo - cashIn,
      cash: before.cash + cashIn,
      commission: before.commission + commission,
    };
    this._balances.set(after);
    return {
      operation: this.recordDeposit(request, 'completed', undefined, { before, after }),
      walletOut,
      before,
      after,
    };
  }

  private recordDeposit(
    request: DepositRequest,
    status: Operation['status'],
    error?: DepositErrorKind,
    balances?: BalanceChange,
  ): Operation {
    return this.record(request.cashIn, status, 'deposit', balances, {
      phone: request.phone,
      cash: request.cashIn,
      wallet: request.walletOut,
      roundForUser: request.roundForUser,
      ...(error && { reason: DEPOSIT_ERROR_REASON[error] }),
    });
  }

  private record(
    amount: number,
    status: Operation['status'],
    type: Operation['type'] = 'deposit',
    balances?: BalanceChange,
    details?: OperationDetails,
  ): Operation {
    const operation: Operation = {
      id: `RL-${String(++this.sequence).padStart(4, '0')}`,
      type,
      amount,
      status,
      createdAt: new Date().toISOString(),
      ...this.snapshot(status, balances),
      ...(details && { details }),
    };
    this._operations.update((ops) => [operation, ...ops]);
    return operation;
  }

  private setStatus(
    operation: Operation,
    status: Operation['status'],
    balances?: BalanceChange,
    reason?: string,
  ): Operation {
    const updated: Operation = {
      ...operation,
      status,
      ...this.snapshot(status, balances),
      ...(reason && { details: { ...operation.details, reason } }),
    };
    this._operations.update((ops) => ops.map((op) => (op.id === operation.id ? updated : op)));
    return updated;
  }

  /** Saldos para el historial (B8): sin cambios si no se pasan; ninguno si sigue pendiente */
  private snapshot(status: Operation['status'], balances?: BalanceChange): Partial<BalanceChange> {
    if (status === 'pending') return {};
    const current = this._balances();
    return balances ?? { before: current, after: current };
  }
}
