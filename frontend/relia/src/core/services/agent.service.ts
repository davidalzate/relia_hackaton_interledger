import { Injectable, computed, signal } from '@angular/core';
import { Observable, catchError, delay, map, of, throwError, timeout, timer } from 'rxjs';

import { environment } from '../../environments/environment';
import { AgentBalances, Operation } from '../../shared/types/agent';

/** Por debajo de este valor (COP) la Caja muestra avisos de cupo/efectivo bajo */
export const LOW_BALANCE_THRESHOLD = 50_000;
/** Lo que tarda el simulador en acreditar la billetera (B2.4) */
export const DEPOSIT_SEND_MS = 2_000;

export interface DepositRequest {
  phone: string;
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

// shortcut: estado demo en memoria que arranca vacío; cambiar por la API (validada con Zod y
// timeout) cuando exista backend: GET /api/agent/summary, GET /api/keys/:phone, POST /api/deposits
@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly _balances = signal<AgentBalances>({ cupo: 0, commission: 0, cash: 0 });
  /** Más recientes primero */
  private readonly _operations = signal<Operation[]>([]);
  private sequence = 0;

  readonly balances = this._balances.asReadonly();
  readonly operations = this._operations.asReadonly();
  readonly recentOperations = computed(() => this._operations().slice(0, 3));

  /** Verifica que el celular tenga billetera. `simulateMissing` fuerza "no existe" (demo) */
  validateKey(_phone: string, simulateMissing: boolean): Observable<boolean> {
    return of(!simulateMissing).pipe(delay(600), timeout(environment.httpTimeoutMs));
  }

  operation(id: string): Operation | undefined {
    return this._operations().find((op) => op.id === id);
  }

  deposit(request: DepositRequest): Observable<DepositReceipt> {
    return timer(DEPOSIT_SEND_MS).pipe(
      map(() => this.applyDeposit(request)),
      timeout(environment.httpTimeoutMs),
      catchError((err: unknown) =>
        throwError(() =>
          err instanceof DepositError
            ? err
            : new DepositError('network', this.record(request.cashIn, 'failed')),
        ),
      ),
    );
  }

  private applyDeposit({ cashIn, walletOut, commission }: DepositRequest): DepositReceipt {
    const before = this._balances();
    // El cupo pudo cambiar desde que se armó el formulario
    if (before.cupo < cashIn) throw new DepositError('cupo', this.record(cashIn, 'failed'));
    const after: AgentBalances = {
      cupo: before.cupo - cashIn,
      cash: before.cash + cashIn,
      commission: before.commission + commission,
    };
    this._balances.set(after);
    return { operation: this.record(cashIn, 'completed'), walletOut, before, after };
  }

  private record(amount: number, status: Operation['status']): Operation {
    const operation: Operation = {
      id: `RL-${String(++this.sequence).padStart(4, '0')}`,
      type: 'deposit',
      amount,
      status,
      createdAt: new Date().toISOString(),
    };
    this._operations.update((ops) => [operation, ...ops]);
    return operation;
  }
}
