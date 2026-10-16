import { Injectable, computed, signal } from '@angular/core';
import { AgentBalances, Operation } from '../../shared/types/agent';

/** Por debajo de este valor (COP) la Caja muestra avisos de cupo/efectivo bajo */
export const LOW_BALANCE_THRESHOLD = 50_000;

@Injectable({ providedIn: 'root' })
export class AgentService {
  // shortcut: estado demo en memoria que arranca vacío; cambiar por GET /api/agent/summary
  // (validado con Zod y timeout) cuando exista backend
  private readonly _balances = signal<AgentBalances>({ cupo: 0, commission: 0, cash: 0 });
  /** Más recientes primero */
  private readonly _operations = signal<Operation[]>([]);

  readonly balances = this._balances.asReadonly();
  readonly operations = this._operations.asReadonly();
  readonly recentOperations = computed(() => this._operations().slice(0, 3));
}
