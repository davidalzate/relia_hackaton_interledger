import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID, computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { AgentBalances, Operation } from '../../shared/types/agent';
import { HomeComponent } from './home.component';

registerLocaleData(localeEsCo);

const op = (id: string, status: Operation['status']): Operation => ({
  id,
  type: 'deposit',
  amount: 20_000,
  createdAt: '2026-10-16T15:05:00Z',
  status,
});

function render(balances: AgentBalances, operations: Operation[] = []): HTMLElement {
  const ops = signal(operations);
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: LOCALE_ID, useValue: 'es-CO' },
      {
        provide: AgentService,
        useValue: {
          balances: signal(balances),
          operations: ops,
          recentOperations: computed(() => ops().slice(0, 3)),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(HomeComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('HomeComponent', () => {
  it('sin saldo muestra ambos avisos y el estado vacío', () => {
    const el = render({ cupo: 0, commission: 0, cash: 49_999 });
    expect(el.querySelectorAll('.notice').length).toBe(2);
    expect(el.textContent).toContain('Aún no hay operaciones');
  });

  it('con saldo suficiente oculta avisos y lista hasta 3 operaciones', () => {
    const el = render({ cupo: 50_000, commission: 1_200, cash: 80_000 }, [
      op('OP-4', 'pending'),
      op('OP-3', 'completed'),
      op('OP-2', 'failed'),
      op('OP-1', 'completed'),
    ]);
    expect(el.querySelectorAll('.notice').length).toBe(0);
    expect(el.querySelectorAll('.recent li').length).toBe(3);
    expect([...el.querySelectorAll('.recent li [class^="badge-"]')].map((b) => b.textContent?.trim())).toEqual([
      'Pendiente',
      'Fallido',
    ]);
    expect(el.querySelector('.balance .amount')?.textContent).toContain('50.000');
  });
});
