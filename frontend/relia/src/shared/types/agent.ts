export interface AgentBalances {
  cupo: number;
  commission: number;
  cash: number;
}

export type OperationType = 'deposit' | 'withdrawal' | 'cupo' | 'cash' | 'cash-set' | 'commission';
export type OperationStatus = 'pending' | 'completed' | 'failed' | 'expired' | 'cancelled';

/** Datos para el comprobante (B9) según el tipo de operación */
export interface OperationDetails {
  /** Depósito/retiro: celular del usuario, 10 dígitos */
  phone?: string;
  /** Depósito: efectivo recibido. Retiro: efectivo entregado */
  cash?: number;
  /** Depósito: enviado a la billetera. Retiro: pagado desde la billetera */
  wallet?: number;
  /** true = valor redondo (comisión aparte) */
  roundForUser?: boolean;
  /** Carga de cupo */
  method?: 'breb' | 'wallet';
  /** Pago de comisión */
  destination?: string;
  /** Motivo si falló, venció o se canceló */
  reason?: string;
}

export interface Operation {
  id: string;
  /** `OP-000123` / `LQ-000007` del API; las operaciones aún en memoria no la tienen */
  reference?: string;
  type: OperationType;
  amount: number;
  /** ISO 8601 */
  createdAt: string;
  status: OperationStatus;
  /** Saldos alrededor de la operación; ausentes mientras está pendiente */
  before?: AgentBalances;
  after?: AgentBalances;
  details?: OperationDetails;
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
