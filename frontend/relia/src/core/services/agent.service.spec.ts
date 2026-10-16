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
        agent.deposit({ phone: '3000000001', cashIn: 51_000, walletOut: 50_000, commission: 500 }),
      ),
    ).catch((e: unknown) => e);
    const error = await result;
    expect(error).toBeInstanceOf(DepositError);
    expect((error as DepositError).kind).toBe('cupo');
    expect((error as DepositError).operation).toMatchObject({ id: 'RL-0001', status: 'failed' });
    expect(agent.balances()).toEqual({ cupo: 0, commission: 0, cash: 0 });
  });
});
