import { Injectable, computed, signal } from '@angular/core';
import { Observable, delay, of, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import { AgentBalances, Operation } from '../../shared/types/agent';

/** Por debajo de este valor (COP) la Caja muestra avisos de cupo/efectivo bajo */
export const LOW_BALANCE_THRESHOLD = 50_000;

export interface DepositRequest {
  phone: string;
  cashIn: number;
  commission: number;
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

  deposit({ cashIn, commission }: DepositRequest): Operation {
    this._balances.update((b) => ({
      cupo: b.cupo - cashIn,
      cash: b.cash + cashIn,
      commission: b.commission + commission,
    }));
    return this.record({ type: 'deposit', amount: cashIn, status: 'completed' }, 'DEP');
  }

  private record(op: Pick<Operation, 'type' | 'amount' | 'status'>, prefix: string): Operation {
    const operation: Operation = {
      ...op,
      id: `${prefix}-${String(++this.sequence).padStart(4, '0')}`,
      createdAt: new Date().toISOString(),
    };
    this._operations.update((ops) => [operation, ...ops]);
    return operation;
  }
}
