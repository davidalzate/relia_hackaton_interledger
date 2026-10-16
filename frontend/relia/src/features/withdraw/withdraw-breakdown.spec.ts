import { withdrawBreakdown } from './withdraw-breakdown';

const balances = { cupo: 10_000, commission: 0, cash: 60_000 };

describe('withdrawBreakdown', () => {
  it('sin valor redondo descuenta la comisión del efectivo', () => {
    expect(withdrawBreakdown(50_000, false, balances)).toEqual({
      cashOut: 49_000,
      walletIn: 50_000,
      cupoAfter: 59_000,
      cashAfter: 11_000,
      cashShortfall: 0,
    });
  });

  it('con valor redondo suma la comisión a lo que paga', () => {
    const b = withdrawBreakdown(50_000, true, balances);
    expect(b?.cashOut).toBe(50_000);
    expect(b?.walletIn).toBe(51_000);
  });

  it('reporta el efectivo faltante', () => {
    expect(withdrawBreakdown(70_000, true, balances)?.cashShortfall).toBe(10_000);
  });

  it('montos de hasta $1.000 no generan desglose', () => {
    expect(withdrawBreakdown(1_000, true, balances)).toBeNull();
  });
});
