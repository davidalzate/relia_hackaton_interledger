import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';

import { ApiError } from '../http/api';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { ApiOperation } from '../../shared/schemas/operation.schema';
import { OperationsService } from './operations.service';
import { WithdrawalService } from './withdrawal.service';

const op = (status: string, extra: object = {}) => ({
  id: 'op-1',
  reference: 'OP-000001',
  agentId: 'agent-rosa',
  type: 'withdrawal',
  status,
  createdAt: '2026-10-16T15:00:00Z',
  expiresAt: '2026-10-16T15:05:00Z',
  approvalUrl: 'https://wallet.test/aprobar',
  requestedAmount: 50_000,
  roundAmount: true,
  fee: { total: 1_000, relia: 500, agent: 500 },
  netAmount: 50_000,
  chargedAmount: 51_000,
  balances: {
    quota: { before: 0, after: 50_000 },
    accruedCommission: { before: 0, after: 500 },
    cashOnHand: { before: 80_000, after: 30_000 },
  },
  ...extra,
});

const request = {
  phone: '3000000001',
  amount: 50_000,
  roundForUser: true,
  cashOut: 0,
  walletIn: 0,
  commission: 0,
};

describe('WithdrawalService', () => {
  let service: WithdrawalService;
  let http: HttpTestingController;
  let watch: Subject<ApiOperation>;
  const operations = { watch: vi.fn(), cancel: vi.fn() };

  beforeEach(() => {
    watch = new Subject<ApiOperation>();
    operations.watch.mockReturnValue(watch);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: OperationsService, useValue: operations },
      ],
    });
    service = TestBed.inject(WithdrawalService);
    http = TestBed.inject(HttpTestingController);
  });

  it('crea el retiro, queda esperando con approvalUrl y pasa a aprobado', () => {
    service.start(request, 'k-1');
    const req = http.expectOne((r) => r.url.endsWith('/withdrawals'));
    expect(req.request.body).toEqual({
      phone: '3000000001',
      requestedAmount: 50_000,
      roundAmount: true,
    });
    expect(req.request.headers.get('Idempotency-Key')).toBe('k-1');
    req.flush(op('pending'));

    expect(service.withdrawal()).toMatchObject({
      status: 'waiting',
      approvalUrl: 'https://wallet.test/aprobar',
      expiresAt: Date.parse('2026-10-16T15:05:00Z'),
      request: { cashOut: 50_000, walletIn: 51_000, commission: 500 },
    });
    expect(operations.watch).toHaveBeenCalledWith('op-1');

    watch.next(op('completed') as ApiOperation);
    expect(service.withdrawal()).toMatchObject({
      status: 'approved',
      after: { cupo: 50_000, cash: 30_000 },
    });
  });

  it('failureCode → tipo de error, sin clave para reintentar', () => {
    service.start(request, 'k-1');
    http.expectOne((r) => r.url.endsWith('/withdrawals')).flush(op('pending'));
    watch.next(op('expired', { failureCode: 'request_expired' }) as ApiOperation);
    expect(service.withdrawal()).toMatchObject({
      status: 'error',
      error: 'expired',
      retryKey: null,
    });
  });

  it('422 insufficient_cash al crear: error "cash" con la misma clave', () => {
    service.start(request, 'k-1');
    http
      .expectOne((r) => r.url.endsWith('/withdrawals'))
      .flush(
        { title: 'Efectivo', status: 422, code: 'insufficient_cash' },
        { status: 422, statusText: 'Unprocessable' },
      );
    expect(service.withdrawal()).toMatchObject({
      status: 'error',
      error: 'cash',
      operation: null,
      retryKey: 'k-1',
    });
  });

  it('cancelar: 200 pasa a cancelada; 409 deja seguir esperando', () => {
    service.start(request, 'k-1');
    http.expectOne((r) => r.url.endsWith('/withdrawals')).flush(op('pending'));

    operations.cancel.mockReturnValueOnce(throwError(() => new ApiError(409, null)));
    service.cancel();
    expect(service.withdrawal()?.status).toBe('waiting');

    operations.cancel.mockReturnValueOnce(
      of(op('cancelled', { failureCode: 'request_cancelled' })),
    );
    service.cancel();
    expect(service.withdrawal()).toMatchObject({ status: 'error', error: 'cancelled' });
  });

  it('handover hace POST cash-handover del retiro aprobado', () => {
    service.start(request, 'k-1');
    http.expectOne((r) => r.url.endsWith('/withdrawals')).flush(op('pending'));
    watch.next(op('completed') as ApiOperation);

    service.handover().subscribe();
    const req = http.expectOne('/api/operations/op-1/cash-handover');
    expect(req.request.method).toBe('POST');
  });
});
