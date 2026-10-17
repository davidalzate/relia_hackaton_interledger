import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';

import { ApiError } from '../../core/http/api';
import { AdminService } from '../../core/services/admin.service';
import { AgentService } from '../../core/services/agent.service';
import { Settlement } from '../../shared/schemas/admin.schema';
import { PayCommissionComponent } from './pay-commission.component';

registerLocaleData(localeEsCo);

const settlement = (status: Settlement['status']): Settlement => ({
  id: 'lq-1',
  reference: 'LQ-000001',
  agentId: 'agent-rosa',
  agentName: 'Tienda Doña Rosa',
  walletAddress: '$ilp.interledger-test.dev/tienda-rosa',
  amount: 1_500,
  status,
  createdAt: '2026-10-16T15:00:00Z',
});

function setup() {
  const watch = new Subject<Settlement>();
  const admin = {
    createSettlement: vi.fn((): Observable<Settlement> => of(settlement('pending'))),
    watchSettlement: vi.fn(() => watch),
  };
  const agents = {
    fetchAgent: vi.fn(() =>
      of({
        id: 'agent-rosa',
        name: 'Tienda Doña Rosa',
        walletAddress: '$ilp.interledger-test.dev/tienda-rosa',
        balances: { accruedCommission: 1_500 },
      }),
    ),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: LOCALE_ID, useValue: 'es-CO' },
      { provide: AdminService, useValue: admin },
      { provide: AgentService, useValue: agents },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap({ id: 'agent-rosa' }) } },
      },
    ],
  });
  const fixture = TestBed.createComponent(PayCommissionComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  return { admin, watch, c, el, render: () => fixture.detectChanges() };
}

describe('PayCommissionComponent (D3)', () => {
  it('confirmar → enviando → Comisión pagada', () => {
    const { admin, watch, c, el, render } = setup();
    expect(el.textContent).toContain('Para Tienda Doña Rosa');
    expect(el.textContent).toContain('$ilp.interledger-test.dev/tienda-rosa');
    expect(el.textContent).toContain('La comisión acumulada del corresponsal quedará en $0.');

    c['pay']();
    render();
    expect(admin.createSettlement).toHaveBeenCalledWith('agent-rosa', expect.any(String));
    expect(admin.watchSettlement).toHaveBeenCalledWith('lq-1');
    expect(el.textContent).toContain('Enviando');

    watch.next(settlement('pending'));
    watch.next(settlement('paid'));
    render();
    expect(el.textContent).toContain('Comisión pagada');
    expect(el.textContent).toContain('Tienda Doña Rosa · LQ-000001');
  });

  it('voided → "El pago falló" y el reintento usa clave nueva', () => {
    const { admin, watch, c, el, render } = setup();
    c['pay']();
    watch.next(settlement('voided'));
    render();
    expect(el.textContent).toContain('El pago falló. No se movió dinero.');
    c['pay']();
    const keys = admin.createSettlement.mock.calls.map((call) => (call as unknown[])[1]);
    expect(keys[1]).not.toBe(keys[0]);
  });

  it('422 no_commission → "Sin comisión"; reintentar reusa la clave', () => {
    const { admin, c, el, render } = setup();
    admin.createSettlement.mockReturnValue(
      throwError(() => new ApiError(422, { title: 'Sin', status: 422, code: 'no_commission' })),
    );
    c['pay']();
    render();
    expect(el.textContent).toContain('Sin comisión');
    c['pay']();
    const keys = admin.createSettlement.mock.calls.map((call) => (call as unknown[])[1]);
    expect(keys[1]).toBe(keys[0]);
  });
});
