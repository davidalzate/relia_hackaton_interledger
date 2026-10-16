import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { CashOnHandComponent } from './cash-on-hand.component';

registerLocaleData(localeEsCo);

function setup(cash: number) {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: LOCALE_ID, useValue: 'es-CO' }],
  });
  const agent = TestBed.inject(AgentService);
  agent['_balances'].set({ cupo: 0, commission: 0, cash });
  const fixture = TestBed.createComponent(CashOnHandComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  const button = () => el.querySelector('button[type=submit]') as HTMLButtonElement;
  return { agent, c, el, render, button };
}

describe('CashOnHandComponent', () => {
  it('sumar: vista previa, guarda y muestra el recuadro de actualización', () => {
    const { agent, c, el, render, button } = setup(20_000);
    expect(button().disabled).toBe(true);
    c['form'].patchValue({ amount: 30_000 });
    render();
    expect(el.querySelector('dd')?.textContent).toContain('50.000');
    expect(button().disabled).toBe(false);

    c['save']();
    render();
    expect(agent.balances().cash).toBe(50_000);
    expect(el.querySelector('.saved')?.textContent).toMatch(
      /Efectivo actualizado · \$\s20\.000 → \$\s50\.000 · RL-0001/,
    );
  });

  it('fijar acepta 0 y cambia la etiqueta del campo', () => {
    const { agent, c, el, render, button } = setup(20_000);
    c['form'].patchValue({ mode: 'set', amount: 0 });
    render();
    expect(el.querySelector('label[for=amount]')?.textContent?.trim()).toBe(
      'Total de efectivo en caja',
    );
    expect(button().disabled).toBe(false);
    c['save']();
    expect(agent.balances().cash).toBe(0);
    expect(agent.operations()[0].type).toBe('cash-set');
  });
});
