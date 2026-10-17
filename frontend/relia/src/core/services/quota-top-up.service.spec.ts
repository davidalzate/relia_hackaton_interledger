import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';

import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { ApiOperation } from '../../shared/schemas/operation.schema';
import { AuthService } from './auth.service';
import { OperationsService } from './operations.service';
import { QuotaTopUpService } from './quota-top-up.service';

const op = (status: string, extra: object = {}) => ({
  id: 'op-1',
  reference: 'OP-000001',
  agentId: 'agent-rosa',
  type: 'quotaTopUp',
  status,
  createdAt: '2026-10-16T15:00:00Z',
  expiresAt: '2026-10-16T15:10:00Z',
  qrPayload: 'BREB|OP-000001',
  requestedAmount: 300_000,
  roundAmount: false,
  fee: { total: 0, relia: 0, agent: 0 },
  netAmount: 300_000,
  chargedAmount: 300_000,
  topUpMethod: 'breB',
  balances: {
    quota: { before: 0, after: status === 'completed' ? 300_000 : 0 },
    accruedCommission: { before: 0, after: 0 },
    cashOnHand: { before: 0, after: 0 },
  },
  ...extra,
});

describe('QuotaTopUpService', () => {
  let service: QuotaTopUpService;
  let http: HttpTestingController;
  let watch: Subject<ApiOperation>;
  const operations = { watch: vi.fn(), cancel: vi.fn() };

  beforeEach(() => {
    watch = new Subject<ApiOperation>();
    operations.watch.mockReturnValue(watch);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        { provide: OperationsService, useValue: operations },
      ],
    });
    vi.spyOn(TestBed.inject(AuthService), 'token').mockReturnValue('tok');
    service = TestBed.inject(QuotaTopUpService);
    http = TestBed.inject(HttpTestingController);
  });

  it('Bre-B: crea con method breB, espera con qrPayload y el simulador la completa', () => {
    service.start({ method: 'breb', amount: 300_000 }, 'k-1');
    const req = http.expectOne((r) => r.url.endsWith('/quota-top-ups'));
    expect(req.request.body).toEqual({ method: 'breB', requestedAmount: 300_000 });
    expect(req.request.headers.get('Idempotency-Key')).toBe('k-1');
    req.flush(op('pending'));
    expect(service.load()).toMatchObject({
      status: 'waiting',
      qrPayload: 'BREB|OP-000001',
      expiresAt: Date.parse('2026-10-16T15:10:00Z'),
    });

    service.simulateBreBPayment();
    const demo = http.expectOne('/api/demo/quota-top-ups/op-1/breb-payment');
    expect(demo.request.headers.has('Authorization')).toBe(false);
    demo.flush(op('completed'));
    expect(service.load()).toMatchObject({
      status: 'done',
      before: { cupo: 0 },
      after: { cupo: 300_000 },
    });
  });

  it('Wallet: espera con approvalUrl; customer_rejected → rejected', () => {
    service.start({ method: 'wallet', amount: 50_000 }, 'k-1');
    http
      .expectOne((r) => r.url.endsWith('/quota-top-ups'))
      .flush(op('pending', { approvalUrl: 'https://wallet.test/aprobar', qrPayload: null }));
    expect(service.load()).toMatchObject({
      status: 'waiting',
      approvalUrl: 'https://wallet.test/aprobar',
    });

    watch.next(op('failed', { failureCode: 'customer_rejected' }) as ApiOperation);
    expect(service.load()).toMatchObject({ status: 'error', error: 'rejected', retryKey: null });
  });

  it('error al crear: network con la misma clave', () => {
    service.start({ method: 'breb', amount: 300_000 }, 'k-1');
    http
      .expectOne((r) => r.url.endsWith('/quota-top-ups'))
      .flush(null, { status: 503, statusText: 'Unavailable' });
    expect(service.load()).toMatchObject({ status: 'error', error: 'network', retryKey: 'k-1' });
  });
});
