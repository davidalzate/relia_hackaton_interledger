import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, lastValueFrom, toArray } from 'rxjs';

import { AdminService, todayInBogota } from './admin.service';

const settlement = (status: string) => ({
  id: 'lq-1',
  reference: 'LQ-000001',
  agentId: 'agent-rosa',
  agentName: 'Tienda Doña Rosa',
  walletAddress: '$ilp.interledger-test.dev/tienda-rosa',
  amount: 1_500,
  periodFrom: '2026-10-01',
  periodTo: '2026-10-16',
  status,
  createdAt: '2026-10-16T15:00:00Z',
  paidAt: status === 'paid' ? '2026-10-16T15:00:05Z' : null,
  createdByAdmin: { id: 'admin-1', name: 'Admin Relia' },
});

describe('AdminService', () => {
  let admin: AdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    admin = TestBed.inject(AdminService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => vi.useRealTimers());

  it('GET /agents llena filas y tarjetas de D1', async () => {
    const done = firstValueFrom(admin.loadAgents());
    http.expectOne('/api/agents').flush({
      items: [
        {
          id: 'agent-rosa',
          name: 'Tienda Doña Rosa',
          managerName: 'Rosa Gómez',
          city: 'Bogotá',
          walletAddress: '$ilp.interledger-test.dev/tienda-rosa',
          operations: 2,
          deposits: { count: 1, amount: 50_000 },
          withdrawals: { count: 1, amount: 100_000 },
          totalMoved: 150_000,
          quota: 40_000,
          cashOnHand: 250_000,
          accruedCommission: 1_000,
          settledCommissionTotal: 0,
          commissionPaidToday: 0,
        },
      ],
      totals: {
        totalMoved: { count: 2, amount: 150_000 },
        deposits: { count: 1, amount: 50_000 },
        withdrawals: { count: 1, amount: 100_000 },
        commissionPayable: 1_000,
        commissionPaidToday: 500,
      },
    });
    await done;
    expect(admin.agents()[0]).toMatchObject({
      owner: 'Rosa Gómez',
      pointer: '$ilp.interledger-test.dev/tienda-rosa',
      cupo: 40_000,
      cash: 250_000,
      commissionDue: 1_000,
      withdrawals: { total: 100_000, count: 1 },
    });
    expect(admin.totals()).toEqual({
      moved: { total: 150_000, count: 2 },
      deposits: { total: 50_000, count: 1 },
      withdrawals: { total: 100_000, count: 1 },
      commissionDue: 1_000,
      paidToday: 500,
    });
  });

  it('GET /settlements usa hoy en America/Bogota', () => {
    admin.loadPayments().subscribe();
    const req = http.expectOne((r) => r.url === '/api/settlements');
    expect(req.request.params.get('from')).toBe(todayInBogota());
    req.flush({ items: [settlement('paid')] });
    expect(admin.payments()[0].reference).toBe('LQ-000001');
    // 03:00 UTC del 17 sigue siendo 16 en Bogotá (UTC-5)
    expect(todayInBogota(new Date('2026-10-17T03:00:00Z'))).toBe('2026-10-16');
  });

  it('crea la liquidación con Idempotency-Key y la sigue hasta paid', async () => {
    vi.useFakeTimers();
    admin.createSettlement('agent-rosa', 'k-1').subscribe();
    const req = http.expectOne('/api/agents/agent-rosa/settlements');
    expect(req.request.headers.get('Idempotency-Key')).toBe('k-1');
    req.flush(settlement('pending'));

    const seen = lastValueFrom(admin.watchSettlement('lq-1', 1_000).pipe(toArray()));
    await vi.advanceTimersByTimeAsync(0);
    http.expectOne('/api/settlements/lq-1').flush(settlement('pending'));
    await vi.advanceTimersByTimeAsync(1_000);
    http.expectOne('/api/settlements/lq-1').flush(settlement('paid'));
    expect((await seen).map((s) => s.status)).toEqual(['pending', 'paid']);
  });
});
