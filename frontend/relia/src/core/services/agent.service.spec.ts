import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorInterceptor } from '../interceptors/error.interceptor';
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

describe('AgentService retiros', () => {
  const request = {
    phone: '3000000001',
    amount: 50_000,
    roundForUser: true,
    cashOut: 50_000,
    walletIn: 51_000,
    commission: 500,
  };
  let agent: AgentService;
  beforeEach(() => {
    vi.useFakeTimers();
    agent = TestBed.inject(AgentService);
  });
  afterEach(() => vi.useRealTimers());

  it('aprobar con efectivo mueve saldos y completa la operación', () => {
    agent['_balances'].set({ cupo: 0, cash: 80_000, commission: 0 });
    agent.requestWithdrawal(request);
    expect(agent.withdrawal()?.status).toBe('waiting');
    expect(agent.operations()[0]).toMatchObject({ type: 'withdrawal', status: 'pending' });

    agent.approveWithdrawal();
    expect(agent.withdrawal()?.status).toBe('approved');
    expect(agent.balances()).toEqual({ cupo: 50_000, cash: 30_000, commission: 500 });
    expect(agent.operations()[0].status).toBe('completed');
  });

  it('aprobar sin efectivo falla con error cash y no mueve saldos', () => {
    agent.requestWithdrawal(request);
    agent.approveWithdrawal();
    expect(agent.withdrawal()).toMatchObject({ status: 'error', error: 'cash' });
    expect(agent.balances().cash).toBe(0);
  });

  it('vence a los 5 minutos', () => {
    agent.requestWithdrawal(request);
    vi.advanceTimersByTime(5 * 60_000);
    expect(agent.withdrawal()).toMatchObject({ status: 'error', error: 'expired' });
    expect(agent.operations()[0].status).toBe('expired');
  });

  it('cerrar el flujo en espera cancela la operación', () => {
    agent.requestWithdrawal(request);
    agent.clearWithdrawal();
    expect(agent.withdrawal()).toBeNull();
    expect(agent.operations()[0].status).toBe('cancelled');
  });
});

describe('AgentService carga de cupo', () => {
  let agent: AgentService;
  beforeEach(() => {
    vi.useFakeTimers();
    agent = TestBed.inject(AgentService);
  });
  afterEach(() => vi.useRealTimers());

  it('confirmar suma el monto al cupo y completa la operación', () => {
    agent.requestCupoLoad({ method: 'breb', amount: 300_000 });
    expect(agent.operations()[0]).toMatchObject({ type: 'cupo', status: 'pending' });
    agent.confirmCupoLoad();
    expect(agent.cupoLoad()).toMatchObject({
      status: 'done',
      before: { cupo: 0 },
      after: { cupo: 300_000 },
    });
    expect(agent.balances().cupo).toBe(300_000);
    expect(agent.operations()[0].status).toBe('completed');
  });

  it('Bre-B vence a los 10 min y Wallet a los 5', () => {
    agent.requestCupoLoad({ method: 'breb', amount: 1 });
    vi.advanceTimersByTime(5 * 60_000);
    expect(agent.cupoLoad()?.status).toBe('waiting');
    vi.advanceTimersByTime(5 * 60_000);
    expect(agent.cupoLoad()).toMatchObject({ status: 'error', error: 'expired' });

    agent.clearCupoLoad();
    agent.requestCupoLoad({ method: 'wallet', amount: 1, key: '3105550101' });
    vi.advanceTimersByTime(5 * 60_000);
    expect(agent.cupoLoad()).toMatchObject({ status: 'error', error: 'expired' });
  });

  it('rechazar no cambia el cupo', () => {
    agent.requestCupoLoad({ method: 'wallet', amount: 50_000, key: '3105550101' });
    agent.failCupoLoad('rejected');
    expect(agent.cupoLoad()).toMatchObject({ status: 'error', error: 'rejected' });
    expect(agent.balances().cupo).toBe(0);
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
    expect(agent.operations()).toEqual([]);
  });
});

describe('AgentService saldos por operación (B8)', () => {
  it('pendiente sin saldos; completada y fallida con antes/después', () => {
    vi.useFakeTimers();
    const agent = TestBed.inject(AgentService);
    agent['_balances'].set({ cupo: 0, commission: 0, cash: 80_000 });

    agent.requestWithdrawal({
      phone: '3000000001',
      amount: 50_000,
      roundForUser: true,
      cashOut: 50_000,
      walletIn: 51_000,
      commission: 500,
    });
    expect(agent.operations()[0].before).toBeUndefined();
    agent.approveWithdrawal();
    expect(agent.operations()[0]).toMatchObject({
      before: { cupo: 0, cash: 80_000, commission: 0 },
      after: { cupo: 50_000, cash: 30_000, commission: 500 },
    });

    agent.requestCupoLoad({ method: 'breb', amount: 10 });
    agent.failCupoLoad('rejected');
    const failed = agent.operations()[0];
    expect(failed.before).toEqual(failed.after);
    vi.useRealTimers();
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
