import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AdminService, AgentSummary } from '../../core/services/admin.service';
import { AdminHomeComponent } from './admin-home.component';

registerLocaleData(localeEsCo);

const agent = (id: string, commissionDue: number): AgentSummary => ({
  id,
  name: id === 'agent-rosa' ? 'Tienda Doña Rosa' : 'Papelería La 70',
  owner: id === 'agent-rosa' ? 'Rosa Gómez' : 'Jorge Mora',
  city: 'Bogotá',
  pointer: `$ilp.interledger-test.dev/${id}`,
  operations: 0,
  deposits: { total: 0, count: 0 },
  withdrawals: { total: 0, count: 0 },
  totalMoved: 0,
  cupo: 0,
  cash: 0,
  commissionDue,
});

describe('AdminHomeComponent', () => {
  it('carga la red y muestra indicadores, tabla con enlaces y el vacío de pagos', () => {
    const admin = {
      agents: signal([agent('agent-rosa', 1_500), agent('la-70', 0)]),
      totals: signal({
        moved: { total: 0, count: 0 },
        deposits: { total: 0, count: 0 },
        withdrawals: { total: 0, count: 0 },
        commissionDue: 1_500,
        paidToday: 0,
      }),
      payments: signal([]),
      loadAgents: vi.fn(() => of([])),
      loadPayments: vi.fn(() => of([])),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LOCALE_ID, useValue: 'es-CO' },
        { provide: AdminService, useValue: admin },
      ],
    });
    const fixture = TestBed.createComponent(AdminHomeComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(admin.loadAgents).toHaveBeenCalled();
    expect(admin.loadPayments).toHaveBeenCalled();
    expect([...el.querySelectorAll('.kpi h2')].map((h) => h.textContent?.trim())).toEqual([
      'Total dinero movido',
      'Depósitos',
      'Retiros',
      'Comisiones por pagar',
      'Comisiones pagadas hoy',
    ]);
    const rosa = el.querySelector('tbody tr') as HTMLElement;
    expect(rosa.querySelector('a')?.getAttribute('href')).toBe('/admin/corresponsal/agent-rosa');
    expect(rosa.textContent).toContain('Rosa Gómez');
    expect(rosa.querySelector('.btn-pay')?.getAttribute('href')).toBe('/admin/pagar/agent-rosa');
    expect(el.textContent).toContain('Sin comisión');
    expect(el.textContent).toContain('Aún no has pagado comisiones hoy.');
  });
});
