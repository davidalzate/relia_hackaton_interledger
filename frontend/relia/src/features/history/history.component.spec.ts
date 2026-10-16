import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { HistoryComponent } from './history.component';

registerLocaleData(localeEsCo);

function setup() {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: LOCALE_ID, useValue: 'es-CO' }],
  });
  const agent = TestBed.inject(AgentService);
  const fixture = TestBed.createComponent(HistoryComponent);
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  return { agent, c, el, render };
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
    const { agent, c, el, render } = setup();
    agent.updateCash('set', 50_000);
    render();
    c['filter'].set('cash');
    render();
    expect(el.querySelectorAll('tbody tr').length).toBe(1);
    expect(el.querySelector('.pill.active')?.textContent?.trim()).toBe('Efectivo');

    c['filter'].set('deposit');
    render();
    expect(el.textContent).toContain('No hay operaciones de este tipo.');
  });
});
