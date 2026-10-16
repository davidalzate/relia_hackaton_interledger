import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AgentService, DepositError } from './agent.service';

describe('AgentService.deposit', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  async function run<T>(source: Promise<T>): Promise<T> {
    await vi.advanceTimersByTimeAsync(2_000);
    return source;
  }

  it('sin cupo falla, registra la operación fallida y no mueve saldos', async () => {
    const agent = TestBed.inject(AgentService);
    const result = run(
      firstValueFrom(
        agent.deposit({
          phone: '3000000001',
          roundForUser: true,
          cashIn: 51_000,
          walletOut: 50_000,
          commission: 500,
        }),
      ),
    ).catch((e: unknown) => e);
    const error = await result;
    expect(error).toBeInstanceOf(DepositError);
    expect((error as DepositError).kind).toBe('cupo');
    expect((error as DepositError).operation).toMatchObject({ id: 'RL-0001', status: 'failed' });
    expect(agent.balances()).toEqual({ cupo: 0, commission: 0, cash: 0 });
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
  it('sumar agrega al efectivo y fijar lo reemplaza, sin tocar cupo ni comisión', () => {
    const agent = TestBed.inject(AgentService);
    agent['_balances'].set({ cupo: 10, commission: 5, cash: 20_000 });

    expect(agent.updateCash('add', 30_000)).toMatchObject({ before: 20_000, after: 50_000 });
    expect(agent.operations()[0]).toMatchObject({
      type: 'cash',
      amount: 30_000,
      status: 'completed',
    });

    expect(agent.updateCash('set', 0)).toMatchObject({ before: 50_000, after: 0 });
    expect(agent.operations()[0].type).toBe('cash-set');
    expect(agent.balances()).toEqual({ cupo: 10, commission: 5, cash: 0 });
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
