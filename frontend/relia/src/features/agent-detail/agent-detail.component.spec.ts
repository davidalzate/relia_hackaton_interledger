import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AgentService } from '../../core/services/agent.service';
import { ReceiptService } from '../../core/services/receipt.service';
import { Operation } from '../../shared/types/agent';
import { AgentDetailComponent } from './agent-detail.component';

registerLocaleData(localeEsCo);

const deposit: Operation = {
  id: 'op-1',
  reference: 'OP-000001',
  type: 'deposit',
  amount: 51_000,
  status: 'completed',
  createdAt: '2026-10-16T15:00:00Z',
  before: { cupo: 100_000, cash: 0, commission: 0 },
  after: { cupo: 49_000, cash: 51_000, commission: 500 },
};

function setup(items: Operation[], nextCursor: string | null = null) {
  const agents = {
    fetchAgent: vi.fn(() =>
      of({
        id: 'agent-rosa',
        name: 'Tienda Doña Rosa',
        managerName: 'Rosa Gómez',
        city: 'Bogotá',
        address: '',
        phone: '3105550101',
        walletAddress: '$ilp.interledger-test.dev/tienda-rosa',
        balances: {
          quota: 49_000,
          quotaReserved: 0,
          cashOnHand: 51_000,
          cashReserved: 0,
          accruedCommission: 500,
          settledCommissionTotal: 0,
        },
      }),
    ),
    fetchHistory: vi.fn(() => of({ items, nextCursor })),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: LOCALE_ID, useValue: 'es-CO' },
      { provide: AgentService, useValue: agents },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap({ id: 'agent-rosa' }) } },
      },
    ],
  });
  const fixture = TestBed.createComponent(AgentDetailComponent);
  fixture.detectChanges();
  return { agents, el: fixture.nativeElement as HTMLElement };
}

describe('AgentDetailComponent (D2)', () => {
  it('muestra perfil, saldos con Pagar comisión e historial; la fila abre el comprobante', () => {
    const { agents, el } = setup([deposit], 'c2');
    expect(agents.fetchHistory).toHaveBeenCalledWith({ cursor: null }, 'agent-rosa');
    expect(el.querySelector('h1')?.textContent).toBe('Tienda Doña Rosa');
    expect(el.textContent).toContain('Rosa Gómez · Bogotá · $ilp.interledger-test.dev/tienda-rosa');
    expect(el.querySelector('.btn-pay')?.getAttribute('href')).toBe('/admin/pagar/agent-rosa');
    expect(el.querySelector('.op-id')?.textContent).toBe('OP-000001');
    expect(el.querySelector('.more')).toBeTruthy();

    const open = vi.spyOn(TestBed.inject(ReceiptService), 'open');
    (el.querySelector('tbody tr') as HTMLElement).click();
    expect(open).toHaveBeenCalledWith('op-1', deposit);
  });

  it('sin operaciones muestra el vacío sin tabla', () => {
    const { el } = setup([]);
    expect(el.textContent).toContain('Este corresponsal aún no tiene operaciones.');
    expect(el.querySelector('table')).toBeNull();
  });
});
