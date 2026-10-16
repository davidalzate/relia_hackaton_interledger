import { AgentBalances } from '../../shared/types/agent';

/** Comisión que paga el usuario por depósito (COP) */
export const RELIA_FEE = 1_000;
/** Parte de esa comisión que gana el corresponsal (COP) */
export const AGENT_COMMISSION = 500;

export interface DepositBreakdown {
  /** Efectivo que entrega el usuario y recibe el corresponsal */
  cashIn: number;
  /** Lo que llega a la billetera del usuario */
  walletOut: number;
  cupoAfter: number;
  cashAfter: number;
  /** Cupo que falta para cubrir el depósito; 0 si alcanza */
  cupoShortfall: number;
}

/**
 * `amount` es lo que el corresponsal digita. Con `roundForUser` el usuario recibe ese valor
 * exacto y la comisión se suma al efectivo; sin él, la comisión se descuenta de lo que recibe.
 * El cupo se debita por todo el efectivo recibido (billetera + comisión Relia).
 * Devuelve null si el monto no supera la comisión.
 */
export function depositBreakdown(
  amount: number,
  roundForUser: boolean,
  { cupo, cash }: AgentBalances,
): DepositBreakdown | null {
  if (amount <= RELIA_FEE) return null;
  const cashIn = roundForUser ? amount + RELIA_FEE : amount;
  return {
    cashIn,
    walletOut: cashIn - RELIA_FEE,
    cupoAfter: cupo - cashIn,
    cashAfter: cash + cashIn,
    cupoShortfall: Math.max(0, cashIn - cupo),
  };
}
