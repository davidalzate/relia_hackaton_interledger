import { TestBed } from '@angular/core/testing';
import { AdminService } from './admin.service';
import { AgentService } from './agent.service';

describe('AdminService', () => {
  it('Tienda Doña Rosa refleja en vivo depósitos, retiros, saldos y comisión', () => {
    vi.useFakeTimers();
    const admin = TestBed.inject(AdminService);
    const agent = TestBed.inject(AgentService);
    const before = admin.totals();

    agent['_balances'].set({ cupo: 0, commission: 0, cash: 80_000 });
    agent['record'](
      50_000,
      'completed',
      'withdrawal',
      {
        before: { cupo: 0, commission: 0, cash: 80_000 },
        after: { cupo: 50_000, commission: 500, cash: 30_000 },
      },
      { phone: '3000000001', cash: 50_000, wallet: 51_000, roundForUser: true },
    );
    agent['_balances'].set({ cupo: 50_000, commission: 500, cash: 30_000 });
    vi.useRealTimers();

    const rosa = admin.agents()[0];
    expect(rosa).toMatchObject({
      name: 'Tienda Doña Rosa',
      operations: 1,
      withdrawals: { total: 50_000, count: 1 },
      totalMoved: 50_000,
      cupo: 50_000,
      cash: 30_000,
      commissionDue: 500,
    });
    expect(admin.totals().moved.total).toBe(before.moved.total + 50_000);
    expect(admin.totals().moved.count).toBe(before.moved.count + 1);
    expect(admin.totals().commissionDue).toBe(before.commissionDue + 500);
    expect(admin.totals().paidToday).toBe(0);
  });
});
