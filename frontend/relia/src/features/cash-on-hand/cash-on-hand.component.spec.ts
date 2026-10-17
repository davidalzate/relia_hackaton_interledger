import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { AgentService } from '../../core/services/agent.service';
import { CashOnHandComponent } from './cash-on-hand.component';

registerLocaleData(localeEsCo);

function setup(cash: number) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      { provide: LOCALE_ID, useValue: 'es-CO' },
    ],
  });
  const agent = TestBed.inject(AgentService);
  agent['_balances'].set({ cupo: 0, commission: 0, cash });
  const fixture = TestBed.createComponent(CashOnHandComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  const button = () => el.querySelector('button[type=submit]') as HTMLButtonElement;
  const http = TestBed.inject(HttpTestingController);
  const respond = (before: number, after: number, mode = 'add') =>
    http
      .expectOne((r) => r.url.endsWith('/cash-adjustments'))
      .flush({
        id: 'adj-1',
        agentId: 'a',
        mode,
        variation: after - before,
        reason: 'Asignación de efectivo',
        cashOnHand: { before, after },
        createdAt: '2026-10-16T15:00:00Z',
      });
  return { agent, c, el, render, button, http, respond };
}

describe('CashOnHandComponent', () => {
  it('sumar: vista previa, guarda y muestra el recuadro de actualización', () => {
    const { agent, c, el, render, button, respond } = setup(20_000);
    expect(button().disabled).toBe(true);
    c['form'].patchValue({ amount: 30_000 });
    render();
    expect(el.querySelector('dd')?.textContent).toContain('50.000');
    expect(button().disabled).toBe(false);

    c['save']();
    render();
    expect(button().disabled).toBe(true);
    respond(20_000, 50_000);
    render();
    expect(agent.balances().cash).toBe(50_000);
    expect(el.querySelector('.saved')?.textContent).toMatch(
      /Efectivo actualizado · \$\s20\.000 → \$\s50\.000 · adj-1/,
    );
  });

  it('fijar acepta 0 y cambia la etiqueta del campo', () => {
    const { agent, c, el, render, button, respond } = setup(20_000);
    c['form'].patchValue({ mode: 'set', amount: 0 });
    render();
    expect(el.querySelector('label[for=amount]')?.textContent?.trim()).toBe(
      'Total de efectivo en caja',
    );
    expect(button().disabled).toBe(false);
    c['save']();
    respond(20_000, 0, 'set');
    expect(agent.balances().cash).toBe(0);
    expect(agent.recentOperations()[0].type).toBe('cash-set');
  });

  it('422 insufficient_cash muestra el motivo y reintenta con la misma clave', () => {
    const { c, el, render, http } = setup(20_000);
    c['form'].patchValue({ mode: 'set', amount: 1_000 });
    c['save']();
    const first = http.expectOne((r) => r.url.endsWith('/cash-adjustments'));
    const key = first.request.headers.get('Idempotency-Key');
    first.flush(
      { title: 'Efectivo', status: 422, code: 'insufficient_cash' },
      { status: 422, statusText: 'Unprocessable' },
    );
    render();
    expect(el.querySelector('.error')?.textContent).toContain('efectivo reservado');

    c['save']();
    const retry = http.expectOne((r) => r.url.endsWith('/cash-adjustments'));
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush(null, { status: 503, statusText: 'Unavailable' });

    c['form'].patchValue({ amount: 30_000 });
    c['save']();
    const changed = http.expectOne((r) => r.url.endsWith('/cash-adjustments'));
    expect(changed.request.headers.get('Idempotency-Key')).not.toBe(key);
  });
});
