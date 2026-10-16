export interface AgentBalances {
  cupo: number;
  commission: number;
  cash: number;
}

export type OperationType = 'deposit' | 'withdrawal' | 'cupo' | 'cash' | 'cash-set' | 'commission';
export type OperationStatus = 'pending' | 'completed' | 'failed' | 'expired' | 'cancelled';

export interface Operation {
  id: string;
  type: OperationType;
  amount: number;
  /** ISO 8601 */
  createdAt: string;
  status: OperationStatus;
  /** Saldos alrededor de la operación; ausentes mientras está pendiente */
  before?: AgentBalances;
  after?: AgentBalances;
}

export const OPERATION_LABEL: Record<OperationType, string> = {
  deposit: 'Depósito',
  withdrawal: 'Retiro',
  cupo: 'Carga de cupo',
  cash: 'Asignación de efectivo',
  'cash-set': 'Ajuste de efectivo',
  commission: 'Pago de comisión',
};

export const STATUS_LABEL: Record<OperationStatus, string> = {
  pending: 'Pendiente',
  completed: 'Completado',
  failed: 'Fallido',
  expired: 'Vencido',
  cancelled: 'Cancelado',
};

// Clases de insignia de styles.scss
export const STATUS_BADGE: Record<OperationStatus, string> = {
  pending: 'badge-pending',
  completed: 'badge-completed',
  failed: 'badge-failed',
  expired: 'badge-muted',
  cancelled: 'badge-muted',
};

/** Comisión que paga el usuario por depósito o retiro (COP) */
export const RELIA_FEE = 1_000;
/** Parte de esa comisión que gana el corresponsal (COP) */
export const AGENT_COMMISSION = 500;
