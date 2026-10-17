import { registerLocaleData } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { HistoryComponent } from './history.component';

registerLocaleData(localeEsCo);

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: LOCALE_ID, useValue: 'es-CO' },
    ],
  });
  const agent = TestBed.inject(AgentService);
  const fixture = TestBed.createComponent(HistoryComponent);
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const http = TestBed.inject(HttpTestingController);
  const render = () => fixture.detectChanges();
  return { agent, c, el, render, http };
}

describe('HistoryComponent', () => {
  it('lista más reciente primero con saldos y "Pendiente" en operaciones pendientes', () => {
    const { agent, el, render } = setup();
    agent.updateCash('add', 100_000);
    agent.requestCupoLoad({ method: 'breb', amount: 300_000 });
    render();

    const rows = [...el.querySelectorAll('tbody tr')];
    expect(rows.map((r) => r.querySelector('.link-cell')?.textContent?.trim())).toEqual([
      'Carga de cupo',
      'Asignación de efectivo',
    ]);
    expect(rows[0].querySelectorAll('.pending').length).toBe(6);
    expect(rows[1].textContent).toContain('100.000');
    expect(el.querySelectorAll('.cards li').length).toBe(2);
    expect(el.querySelector('.summary')?.textContent).toContain('Efectivo');
    agent.clearCupoLoad();
  });

  it('filtra por tipo y muestra el vacío', () => {
    const { agent, c, el, render, http } = setup();
    agent.updateCash('set', 50_000);
    render();
    c['filter'].set('cashAdjustment');
    render();
    expect(el.querySelectorAll('tbody tr').length).toBe(1);
    expect(el.querySelector('.pill.active')?.textContent?.trim()).toBe('Efectivo');

    c['filter'].set('deposit');
    render();
    http.expectOne((r) => r.params.get('kind') === 'deposit').flush({ items: [] });
    render();
    expect(el.textContent).toContain('No hay operaciones de este tipo.');
  });

  it('pide el historial al API por filtro y pagina con "Ver más"', () => {
    const { c, el, render, http } = setup();
    render();
    const entry = (id: string, status = 'completed') => ({
      id,
      kind: 'deposit',
      reference: `OP-${id}`,
      occurredAt: '2026-10-16T15:00:00Z',
      status,
      amount: 50_000,
      balances: {
        quota: { before: 100_000, after: 50_000 },
        accruedCommission: { before: 0, after: 500 },
        cashOnHand: { before: 0, after: 51_000 },
      },
      operationId: `op-${id}`,
    });

    http
      .expectOne((r) => r.url.endsWith('/history') && !r.params.has('kind'))
      .flush({ items: [entry('1')], nextCursor: 'c2' });
    render();
    expect(el.querySelector('tbody .op-id')?.textContent).toBe('OP-1');
    expect(el.querySelector('tbody tr')?.textContent).toContain('100.000');

    (el.querySelector('.more') as HTMLButtonElement).click();
    http
      .expectOne((r) => r.params.get('cursor') === 'c2')
      .flush({ items: [entry('2', 'pending')], nextCursor: null });
    render();
    expect(el.querySelectorAll('tbody tr').length).toBe(2);
    expect(el.querySelectorAll('tbody tr')[1].querySelectorAll('.pending').length).toBe(6);
    expect(el.querySelector('.more')).toBeNull();

    c['filter'].set('settlement');
    render();
    http.expectOne((r) => r.params.get('kind') === 'settlement').flush({ items: [] });
    render();
    expect(el.textContent).toContain('No hay operaciones de este tipo.');
  });
});
