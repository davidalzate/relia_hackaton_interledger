import { depositBreakdown } from './deposit-breakdown';

const balances = { cupo: 100_000, commission: 0, cash: 20_000 };

describe('depositBreakdown', () => {
  it('sin valor redondo descuenta la comisión de lo que recibe el usuario', () => {
    expect(depositBreakdown(50_000, false, balances)).toEqual({
      cashIn: 50_000,
      walletOut: 49_000,
      cupoAfter: 50_000,
      cashAfter: 70_000,
      cupoShortfall: 0,
    });
  });

  it('con valor redondo suma la comisión al efectivo', () => {
    const b = depositBreakdown(50_000, true, balances);
    expect(b?.cashIn).toBe(51_000);
    expect(b?.walletOut).toBe(50_000);
  });

  it('reporta el cupo faltante', () => {
    expect(depositBreakdown(100_000, true, balances)?.cupoShortfall).toBe(1_000);
  });

  it('montos de hasta $1.000 no generan desglose', () => {
    expect(depositBreakdown(1_000, false, balances)).toBeNull();
    expect(depositBreakdown(0, false, balances)).toBeNull();
  });
});
