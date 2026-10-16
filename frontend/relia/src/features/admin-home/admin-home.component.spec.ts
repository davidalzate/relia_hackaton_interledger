import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { AdminHomeComponent } from './admin-home.component';

registerLocaleData(localeEsCo);

describe('AdminHomeComponent', () => {
  it('muestra indicadores, tabla con enlace a D2 y acción de pago, y el vacío de pagos', () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: LOCALE_ID, useValue: 'es-CO' }],
    });
    TestBed.inject(AgentService)['_balances'].set({ cupo: 0, commission: 1_500, cash: 0 });
    const fixture = TestBed.createComponent(AdminHomeComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect([...el.querySelectorAll('.kpi h2')].map((h) => h.textContent?.trim())).toEqual([
      'Total dinero movido',
      'Depósitos',
      'Retiros',
      'Comisiones por pagar',
      'Comisiones pagadas hoy',
    ]);
    const rosa = el.querySelector('tbody tr') as HTMLElement;
    expect(rosa.querySelector('a')?.getAttribute('href')).toBe('/admin/corresponsal/dona-rosa');
    expect(rosa.textContent).toContain('Rosa Gómez');
    expect(rosa.querySelector('.btn-pay')?.getAttribute('href')).toBe('/admin/pagar/dona-rosa');
    expect(el.textContent).toContain('Sin comisión');
    expect(el.textContent).toContain('Aún no has pagado comisiones hoy.');
  });
});
