import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { ApiOperation } from '../../shared/schemas/operation.schema';
import { AgentService } from './agent.service';
import { loginAs } from './auth.testing';

describe('AgentService depósito (API)', () => {
  let agent: AgentService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    agent = TestBed.inject(AgentService);
    http = TestBed.inject(HttpTestingController);
  });

  it('validateKey: 200 devuelve la llave y 404 key_not_found devuelve null', async () => {
    const found = firstValueFrom(agent.validateKey('3000000001'));
    http
      .expectOne('/api/keys/3000000001')
      .flush({ phone: '3000000001', walletAddressMasked: '$ilp...test.dev/je***' });
    expect((await found)?.walletAddressMasked).toBe('$ilp...test.dev/je***');

    const missing = firstValueFrom(agent.validateKey('3000000002'));
    http
      .expectOne('/api/keys/3000000002')
      .flush(
        { title: 'No existe', status: 404, code: 'key_not_found' },
        { status: 404, statusText: 'Not Found' },
      );
    expect(await missing).toBeNull();
  });

  it('createDeposit envía cashReceivedConfirmed e Idempotency-Key', () => {
    agent
      .createDeposit({ phone: '3000000001', requestedAmount: 50_000, roundAmount: true }, 'k-1')
      .subscribe();
    const req = http.expectOne((r) => r.url.endsWith('/deposits'));
    expect(req.request.body).toEqual({
      phone: '3000000001',
      requestedAmount: 50_000,
      roundAmount: true,
      cashReceivedConfirmed: true,
    });
    expect(req.request.headers.get('Idempotency-Key')).toBe('k-1');
  });
});

describe('AgentService efectivo en caja', () => {
  it('POST cash-adjustments con Idempotency-Key; actualiza solo el efectivo', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const agent = TestBed.inject(AgentService);
    agent['_balances'].set({ cupo: 10, commission: 5, cash: 20_000 });

    const done = firstValueFrom(agent.updateCash('set', 0, 'clave-123'));
    const req = TestBed.inject(HttpTestingController).expectOne((r) =>
      r.url.endsWith('/cash-adjustments'),
    );
    expect(req.request.body).toEqual({ mode: 'set', amount: 0 });
    expect(req.request.headers.get('Idempotency-Key')).toBe('clave-123');
    req.flush({
      id: 'adj-1',
      agentId: 'a',
      mode: 'set',
      variation: -20_000,
      reason: 'Ajuste de efectivo',
      cashOnHand: { before: 20_000, after: 0 },
      createdAt: '2026-10-16T15:00:00Z',
    });

    expect(await done).toMatchObject({ before: 20_000, after: 0 });
    expect(agent.balances()).toEqual({ cupo: 10, commission: 5, cash: 0 });
    expect(agent.recentOperations()[0]).toMatchObject({ id: 'adj-1', type: 'cash-set' });
  });
});

describe('AgentService.recordApiOperation', () => {
  it('pendiente sin saldos; final con antes/después y motivo por failureCode', () => {
    const agent = TestBed.inject(AgentService);
    const op = (status: string, failureCode: string | null = null) =>
      ({
        id: 'op-1',
        reference: 'OP-000001',
        type: 'withdrawal',
        status,
        failureCode,
        createdAt: '2026-10-16T15:00:00Z',
        chargedAmount: 51_000,
        balances: {
          quota: { before: 0, after: 50_000 },
          accruedCommission: { before: 0, after: 500 },
          cashOnHand: { before: 80_000, after: 30_000 },
        },
      }) as unknown as ApiOperation;

    expect(agent.recordApiOperation(op('pending')).before).toBeUndefined();
    expect(agent.recordApiOperation(op('completed'))).toMatchObject({
      before: { cupo: 0, cash: 80_000, commission: 0 },
      after: { cupo: 50_000, cash: 30_000, commission: 500 },
    });
    expect(agent.recordApiOperation(op('failed', 'customer_rejected')).details?.reason).toBe(
      'El usuario rechazó el pago',
    );
    // Reemplaza la misma operación en vez de duplicarla
    expect(agent.recentOperations().length).toBe(1);
  });
});

describe('AgentService saldos (GET /agents/{agentId})', () => {
  it('al iniciar sesión carga perfil y saldos del API', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const agent = TestBed.inject(AgentService);
    await loginAs('agent');
    TestBed.tick();

    TestBed.inject(HttpTestingController)
      .expectOne('/api/agents/agent-rosa')
      .flush({
        id: 'agent-rosa',
        name: 'Tienda Doña Rosa',
        managerName: 'Rosa Gómez',
        city: 'Bogotá',
        address: 'Calle 10 # 20-30',
        phone: '3105550101',
        walletAddress: '$ilp.interledger-test.dev/tienda-rosa',
        balances: {
          quota: 40_000,
          quotaReserved: 0,
          cashOnHand: 300_000,
          cashReserved: 20_000,
          accruedCommission: 1_500,
          settledCommissionTotal: 0,
        },
      });

    expect(agent.balances()).toEqual({ cupo: 40_000, commission: 1_500, cash: 300_000 });
    expect(agent.agent()?.balances.cashReserved).toBe(20_000);
  });
});
