import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { of } from 'rxjs';

import { AgentService } from '../../core/services/agent.service';
import { DepositComponent } from './deposit.component';

registerLocaleData(localeEsCo);

function setup(cupo: number, keyExists = true) {
  const agent = {
    balances: signal({ cupo, commission: 0, cash: 0 }),
    validateKey: vi.fn(() => of(keyExists)),
    deposit: vi.fn(() => ({
      id: 'DEP-0001',
      type: 'deposit',
      amount: 51_000,
      status: 'completed',
      createdAt: '',
    })),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      ConfirmationService,
      { provide: LOCALE_ID, useValue: 'es-CO' },
      { provide: AgentService, useValue: agent },
    ],
  });
  // Acepta B4 de inmediato y guarda el mensaje mostrado
  const confirm = vi
    .spyOn(TestBed.inject(ConfirmationService), 'confirm')
    .mockImplementation((c) => (c.accept?.(), TestBed.inject(ConfirmationService)));
  const fixture = TestBed.createComponent(DepositComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const button = () =>
    (fixture.nativeElement as HTMLElement).querySelector(
      'button[type=submit]',
    ) as HTMLButtonElement;
  const fill = (phone: string, amount: number, roundForUser = false) => {
    c['form'].patchValue({ phone, amount, roundForUser });
    fixture.detectChanges();
  };
  return { fixture, c, agent, button, fill, confirm };
}

describe('DepositComponent', () => {
  it('valida la llave y confirma el depósito con el desglose', () => {
    const { fixture, c, agent, button, fill, confirm } = setup(100_000);
    fill('300 000 0001', 50_000, true);
    expect(button().textContent).toContain('Validar llave');
    expect(button().disabled).toBe(false);

    c['submit']();
    fixture.detectChanges();
    expect(agent.validateKey).toHaveBeenCalledWith('3000000001', false);
    expect(button().textContent).toContain('Confirmar depósito');

    c['submit']();
    // es-CO separa el símbolo con espacio no separable
    expect(confirm.mock.calls[0][0].message).toMatch(
      /^¿Confirmas que recibiste en efectivo un total de \$\s51\.000 por parte del usuario\?$/,
    );
    expect(agent.deposit).toHaveBeenCalledWith({
      phone: '3000000001',
      cashIn: 51_000,
      commission: 500,
    });
  });

  it('cambiar el celular borra la validación', () => {
    const { c, fill, button } = setup(100_000);
    fill('300 000 0001', 50_000);
    c['submit']();
    fill('300 000 0002', 50_000);
    expect(button().textContent).toContain('Validar llave');
  });

  it('formatea el celular a 300 000 0001 con máximo 10 dígitos', () => {
    const { c } = setup(0);
    c['form'].controls.phone.setValue('300-000a00019999');
    c['formatPhone']();
    expect(c['form'].controls.phone.value).toBe('300 000 0001');
  });

  it('bloquea con cupo insuficiente y monto bajo', () => {
    const { fixture, fill, button } = setup(10_000);
    fill('300 000 0001', 20_000);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Cupo insuficiente, faltan');
    expect(button().disabled).toBe(true);

    fill('300 000 0001', 900);
    expect(el.textContent).toContain('El monto debe ser mayor a $1.000.');
  });
});
