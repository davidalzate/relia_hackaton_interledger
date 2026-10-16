export interface AgentBalances {
  cupo: number;
  commission: number;
  cash: number;
}

export type OperationType = 'deposit' | 'withdrawal' | 'cupo' | 'cash';
export type OperationStatus = 'pending' | 'completed' | 'failed' | 'expired' | 'cancelled';

export interface Operation {
  id: string;
  type: OperationType;
  amount: number;
  /** ISO 8601 */
  createdAt: string;
  status: OperationStatus;
}

export const OPERATION_LABEL: Record<OperationType, string> = {
  deposit: 'Depósito',
  withdrawal: 'Retiro',
  cupo: 'Carga de cupo',
  cash: 'Efectivo asignado',
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
