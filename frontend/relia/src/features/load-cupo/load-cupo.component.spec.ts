import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { LoadCupoComponent } from './load-cupo.component';

registerLocaleData(localeEsCo);

function setup() {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: LOCALE_ID, useValue: 'es-CO' }],
  });
  const agent = TestBed.inject(AgentService);
  const fixture = TestBed.createComponent(LoadCupoComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  const submitButton = () => el.querySelector('button[type=submit]') as HTMLButtonElement;
  return { agent, fixture, c, el, render, submitButton };
}

describe('LoadCupoComponent', () => {
  it('Bre-B: paso 1 → paso 2 → QR esperando pago con 10:00', () => {
    const { c, el, render, submitButton } = setup();
    expect(el.textContent).toContain('Paso 1 · Elige el método');
    c['choose']('breb');
    render();
    expect(el.textContent).toContain('Paso 2 · Bre-B');
    expect(submitButton().textContent?.trim()).toBe('Generar QR');
    expect(submitButton().disabled).toBe(true);

    c['form'].patchValue({ amount: 300_000 });
    render();
    c['submit']();
    render();
    expect(el.textContent).toContain('Simulación Bre-B');
    expect(el.querySelector('.qr')).toBeTruthy();
    expect(el.querySelector('.countdown')?.textContent).toContain('Esperando pago · 10:00');
  });

  it('Wallet exige llave de 10 dígitos y muestra la llave mientras espera', () => {
    const { agent, c, el, render, submitButton } = setup();
    c['choose']('wallet');
    c['form'].patchValue({ amount: 50_000, key: '310 555' });
    render();
    expect(submitButton().disabled).toBe(true);

    c['form'].patchValue({ key: '310 555 0101' });
    render();
    expect(submitButton().textContent?.trim()).toBe('Enviar solicitud');
    c['submit']();
    render();
    expect(agent.cupoLoad()?.request).toEqual({
      method: 'wallet',
      amount: 50_000,
      key: '3105550101',
    });
    expect(el.textContent).toContain('Llave 310 555 0101');
    expect(el.querySelector('.countdown')?.textContent).toContain('5:00');
  });

  it('confirmado muestra Cupo cargado; rechazo permite reintentar en el paso 2', () => {
    const { agent, c, el, render } = setup();
    c['choose']('breb');
    c['form'].patchValue({ amount: 300_000 });
    c['submit']();
    agent.confirmCupoLoad();
    render();
    expect(el.textContent).toContain('Cupo cargado');
    expect(el.textContent).toContain('+$ 300.000'.replace(' ', ' '));

    c['finish']();
    c['choose']('breb');
    c['submit']();
    agent.failCupoLoad('rejected');
    render();
    expect(el.textContent).toContain('La carga no se completó. Tu cupo no cambió.');
    c['retry']();
    render();
    expect(el.textContent).toContain('Paso 2 · Bre-B');
    expect(c['form'].getRawValue().amount).toBe(300_000);
  });
});
