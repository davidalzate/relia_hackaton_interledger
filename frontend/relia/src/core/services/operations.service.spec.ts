import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../http/api';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { OperationsService } from './operations.service';

const op = (status: string, extra: object = {}) => ({
  id: 'op-1',
  reference: 'OP-000001',
  agentId: 'agent-rosa',
  type: 'withdrawal',
  status,
  createdAt: '2026-10-16T15:00:00Z',
  requestedAmount: 100_000,
  roundAmount: true,
  fee: { total: 1_000, relia: 500, agent: 500 },
  netAmount: 100_000,
  chargedAmount: 101_000,
  balances: {
    quota: { before: 0, after: 0 },
    accruedCommission: { before: 0, after: 500 },
    cashOnHand: { before: 300_000, after: 200_000 },
  },
  ...extra,
});

describe('OperationsService', () => {
  let ops: OperationsService;
  let http: HttpTestingController;
  const url = '/api/operations/op-1';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    ops = TestBed.inject(OperationsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => vi.useRealTimers());

  it('watch consulta hasta el estado final y luego refresca los saldos', async () => {
    vi.useFakeTimers();
    const seen: string[] = [];
    let done = false;
    ops.watch('op-1', 1_000).subscribe({
      next: (o) => seen.push(o.status),
      complete: () => (done = true),
    });

    await vi.advanceTimersByTimeAsync(0);
    http.expectOne(url).flush(op('pending', { expiresAt: '2026-10-16T15:05:00Z' }));
    await vi.advanceTimersByTimeAsync(1_000);
    http.expectOne(url).flush(op('failed', { failureCode: 'customer_rejected' }));

    expect(seen).toEqual(['pending', 'failed']);
    expect(done).toBe(true);
    http.expectOne((r) => r.url.startsWith('/api/agents/'));
    await vi.advanceTimersByTimeAsync(5_000);
    http.verify();
  });

  it('desuscribirse detiene el polling', async () => {
    vi.useFakeTimers();
    const sub = ops.watch('op-1', 1_000).subscribe();
    await vi.advanceTimersByTimeAsync(0);
    http.expectOne(url).flush(op('pending'));
    sub.unsubscribe();
    await vi.advanceTimersByTimeAsync(5_000);
    http.verify();
  });

  it('cancel devuelve la operación cancelada', async () => {
    const res = firstValueFrom(ops.cancel('op-1'));
    const req = http.expectOne(`${url}/cancel`);
    expect(req.request.method).toBe('POST');
    req.flush(op('cancelled', { failureCode: 'request_cancelled' }));
    expect((await res).failureCode).toBe('request_cancelled');
  });

  it('cancel tras aprobar: 409 operation_not_cancellable', async () => {
    const res = firstValueFrom(ops.cancel('op-1'));
    http
      .expectOne(`${url}/cancel`)
      .flush(
        { title: 'No cancelable', status: 409, code: 'operation_not_cancellable' },
        { status: 409, statusText: 'Conflict' },
      );
    const error = (await res.catch((e: unknown) => e)) as ApiError;
    expect(error.code).toBe('operation_not_cancellable');
  });
});
