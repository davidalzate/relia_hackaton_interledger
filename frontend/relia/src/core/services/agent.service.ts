import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Observable, catchError, map, of, tap, throwError, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import { ApiError, IDEMPOTENCY_HEADER, newIdempotencyKey } from '../http/api';
import {
  Agent,
  AgentSchema,
  CashAdjustmentSchema,
  HistoryEntry,
  HistoryKind,
  HistoryPageSchema,
} from '../../shared/schemas/agent.schema';
import {
  ApiOperation,
  FAILURE_TEXT,
  KeyLookup,
  KeyLookupSchema,
  OperationPreview,
  OperationPreviewSchema,
  OperationSchema,
} from '../../shared/schemas/operation.schema';
import {
  AgentBalances,
  Operation,
  OperationDetails,
  OperationType,
} from '../../shared/types/agent';
import { AuthService } from './auth.service';

/** Por debajo de este valor (COP) la Caja muestra avisos de cupo/efectivo bajo */
export const LOW_BALANCE_THRESHOLD = 50_000;
export interface DepositRequest {
  phone: string;
  requestedAmount: number;
  roundAmount: boolean;
}

export interface DepositReceipt {
  operation: Operation;
  walletOut: number;
  fee: number;
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

const API_TYPE: Record<ApiOperation['type'], OperationType> = {
  deposit: 'deposit',
  withdrawal: 'withdrawal',
  quotaTopUp: 'cupo',
};

const KIND_TYPE: Record<HistoryKind, OperationType> = {
  deposit: 'deposit',
  withdrawal: 'withdrawal',
  quotaTopUp: 'cupo',
  cashAdjustment: 'cash',
  settlement: 'commission',
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

@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly _agent = signal<Agent | null>(null);
  private readonly _balances = signal<AgentBalances>({ cupo: 0, commission: 0, cash: 0 });

  /** Perfil y saldos crudos del API (`cashReserved`, `quotaReserved`, `walletAddress`…) */
  readonly agent = this._agent.asReadonly();
  readonly balances = this._balances.asReadonly();
  /** Últimas operaciones del API (B1) y las que se crean en esta sesión, más recientes primero */
  private readonly _recent = signal<Operation[]>([]);
  private readonly _history = signal<HistoryPage & { kind: HistoryKind | null }>({
    kind: null,
    items: [],
    nextCursor: null,
  });
  readonly recentOperations = computed(() => this._recent().slice(0, 3));
  /** B8: páginas cargadas del filtro actual */
  readonly history = computed(() => {
    const { items, nextCursor } = this._history();
    return { items, nextCursor };
  });

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

  /** `GET /agents/{agentId}` de cualquier corresponsal (el admin lo usa en D2 y D3) */
  fetchAgent(agentId: string): Observable<Agent> {
    return this.http.get(`${environment.apiUrl}/agents/${agentId}`).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => AgentSchema.parse(res)),
    );
  }

  /** Saldos del corresponsal de la sesión; llamarlo tras cada operación terminal */
  refresh(agentId = this.auth.agentId() ?? ''): Observable<Agent> {
    return this.fetchAgent(agentId).pipe(
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

  /** `GET /agents/{agentId}/history`; por defecto el corresponsal de la sesión (D2 pasa el suyo) */
  fetchHistory(
    query: { kind?: HistoryKind | null; cursor?: string | null; limit?: number },
    agentId = this.auth.agentId(),
  ): Observable<HistoryPage> {
    const params = Object.fromEntries(Object.entries(query).filter(([, v]) => v != null)) as Record<
      string,
      string | number
    >;
    return this.http.get(`${environment.apiUrl}/agents/${agentId}/history`, { params }).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => HistoryPageSchema.parse(res)),
      map(({ items, nextCursor }) => ({
        items: items.map(toOperation),
        nextCursor: nextCursor ?? null,
      })),
    );
  }

  /** `GET /keys/{phone}`: null si el celular no tiene billetera (`404 key_not_found`) */
  validateKey(phone: string): Observable<KeyLookup | null> {
    return this.http.get(`${environment.apiUrl}/keys/${phone}`).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => KeyLookupSchema.parse(res)),
      catchError((err: unknown) =>
        err instanceof ApiError && err.code === 'key_not_found' ? of(null) : throwError(() => err),
      ),
    );
  }

  /** Desglose en vivo (B2.1, B3.1). Responde 200 aunque haya `violations` */
  previewOperation(
    type: 'deposit' | 'withdrawal',
    requestedAmount: number,
    roundAmount: boolean,
  ): Observable<OperationPreview> {
    return this.http
      .post(`${environment.apiUrl}/agents/${this.auth.agentId()}/operations/preview`, {
        type,
        requestedAmount,
        roundAmount,
      })
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => OperationPreviewSchema.parse(res)),
      );
  }

  /** `POST /agents/{agentId}/deposits` → 202 `pending`; seguir con `OperationsService.watch` */
  createDeposit(request: DepositRequest, idempotencyKey: string): Observable<ApiOperation> {
    return this.http
      .post(
        `${environment.apiUrl}/agents/${this.auth.agentId()}/deposits`,
        { ...request, cashReceivedConfirmed: true },
        { headers: { [IDEMPOTENCY_HEADER]: idempotencyKey } },
      )
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => OperationSchema.parse(res)),
      );
  }

  /** Guarda una operación del API en las recientes (B1, comprobante) y la devuelve mapeada */
  recordApiOperation(op: ApiOperation, details?: OperationDetails): Operation {
    const { quota, accruedCommission, cashOnHand } = op.balances;
    const done = op.status !== 'pending';
    const operation: Operation = {
      id: op.id,
      reference: op.reference,
      type: API_TYPE[op.type],
      amount: op.chargedAmount,
      status: op.status,
      createdAt: op.createdAt,
      before: done
        ? { cupo: quota.before, commission: accruedCommission.before, cash: cashOnHand.before }
        : undefined,
      after: done
        ? { cupo: quota.after, commission: accruedCommission.after, cash: cashOnHand.after }
        : undefined,
      details: {
        ...details,
        ...(op.failureCode && { reason: FAILURE_TEXT[op.failureCode] }),
        ...(op.walletAmount && { walletAmount: op.walletAmount }),
      },
    };
    this._recent.update((ops) => [operation, ...ops.filter((o) => o.id !== op.id)]);
    return operation;
  }

  operation(id: string): Operation | undefined {
    const all = [...this._recent(), ...this._history().items];
    return all.find((op) => op.id === id);
  }

  /** B7: `POST /agents/{agentId}/cash-adjustments` (201 síncrono, sin polling) */
  updateCash(
    mode: CashMode,
    amount: number,
    idempotencyKey = newIdempotencyKey(),
  ): Observable<CashUpdate> {
    return this.http
      .post(
        `${environment.apiUrl}/agents/${this.auth.agentId()}/cash-adjustments`,
        { mode, amount },
        { headers: { [IDEMPOTENCY_HEADER]: idempotencyKey } },
      )
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => CashAdjustmentSchema.parse(res)),
        map(({ id, reason, cashOnHand, createdAt }) => {
          const current = this._balances();
          const before = { ...current, cash: cashOnHand.before };
          const after = { ...current, cash: cashOnHand.after };
          this._balances.set(after);
          const operation: Operation = {
            id,
            type: mode === 'add' ? 'cash' : 'cash-set',
            amount,
            status: 'completed',
            createdAt,
            before,
            after,
            details: { reason },
          };
          this._recent.update((ops) => [operation, ...ops]);
          return { operation, before: cashOnHand.before, after: cashOnHand.after };
        }),
      );
  }
}
