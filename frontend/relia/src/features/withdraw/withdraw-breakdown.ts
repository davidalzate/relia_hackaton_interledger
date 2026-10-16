import { AgentBalances, RELIA_FEE } from '../../shared/types/agent';

export interface WithdrawBreakdown {
  /** Efectivo que recibe el usuario y entrega el corresponsal */
  cashOut: number;
  /** Lo que paga el usuario desde su billetera */
  walletIn: number;
  cupoAfter: number;
  cashAfter: number;
  /** Efectivo que falta para cubrir el retiro; 0 si alcanza */
  cashShortfall: number;
}

/**
 * `amount` es lo que el corresponsal digita. Con `roundForUser` el usuario recibe ese valor
 * exacto en efectivo y la comisión se suma a lo que paga; sin él, se descuenta del efectivo.
 * El cupo se acredita por el efectivo entregado (espejo del depósito).
 * Devuelve null si el monto no supera la comisión.
 */
export function withdrawBreakdown(
  amount: number,
  roundForUser: boolean,
  { cupo, cash }: AgentBalances,
): WithdrawBreakdown | null {
  if (amount <= RELIA_FEE) return null;
  const walletIn = roundForUser ? amount + RELIA_FEE : amount;
  const cashOut = walletIn - RELIA_FEE;
  return {
    cashOut,
    walletIn,
    cupoAfter: cupo + cashOut,
    cashAfter: cash - cashOut,
    cashShortfall: Math.max(0, cashOut - cash),
  };
}
