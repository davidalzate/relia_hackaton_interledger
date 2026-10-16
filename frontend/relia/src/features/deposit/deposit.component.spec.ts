import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Observable, Subject, of, throwError } from 'rxjs';

import { AgentService, DepositError, DepositReceipt } from '../../core/services/agent.service';
import { Operation } from '../../shared/types/agent';
import { DepositComponent } from './deposit.component';

registerLocaleData(localeEsCo);

const op: Operation = {
  id: 'RL-0001',
  type: 'deposit',
  amount: 51_000,
  status: 'completed',
  createdAt: '',
};

function setup(cupo: number, keyExists = true) {
  const agent = {
    balances: signal({ cupo, commission: 0, cash: 0 }),
    validateKey: vi.fn(() => of(keyExists)),
    deposit: vi.fn((): Observable<DepositReceipt> => new Subject<DepositReceipt>()),
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
      roundForUser: true,
      cashIn: 51_000,
      walletOut: 50_000,
      commission: 500,
    });
  });

  it('muestra Enviando y luego Completado con el resumen', () => {
    const { fixture, c, agent, fill } = setup(100_000);
    const result = new Subject<DepositReceipt>();
    agent.deposit.mockReturnValue(result);
    fill('300 000 0001', 50_000, true);
    c['submit']();
    c['submit']();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Enviando a la billetera');
    expect(el.textContent).toContain('No cierres esta pantalla.');

    result.next({
      operation: { ...op, status: 'completed' },
      walletOut: 50_000,
      before: { cupo: 100_000, cash: 0, commission: 0 },
      after: { cupo: 49_000, cash: 51_000, commission: 500 },
    });
    fixture.detectChanges();
    expect(el.textContent).toContain('Depósito completado');
    expect(el.textContent).toContain('llegaron a la billetera del usuario');
    expect(el.querySelector('.sms')?.textContent).toContain('Recibiste');
    expect(el.querySelector('button.btn-outline-light')?.textContent?.trim()).toBe(
      'Ver comprobante',
    );
  });

  it('error de cupo muestra B2.6 e Intentar de nuevo conserva los datos', () => {
    const { fixture, c, agent, fill } = setup(100_000);
    agent.deposit.mockReturnValue(
      throwError(() => new DepositError('cupo', { ...op, status: 'failed' })),
    );
    fill('300 000 0001', 50_000, true);
    c['submit']();
    c['submit']();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain(
      'No hay cupo para este depósito. Carga cupo y vuelve a intentar.',
    );
    expect(el.textContent).toContain('Operación RL-0001');

    c['retry']();
    fixture.detectChanges();
    expect(c['form'].getRawValue()).toMatchObject({
      phone: '300 000 0001',
      amount: 50_000,
      roundForUser: true,
    });
    expect(el.textContent).toContain('Confirmar depósito');
  });

  it('cambiar el celular borra la validación', () => {
    const { c, fill, button } = setup(100_000);
    fill('300 000 0001', 50_000);
    c['submit']();
    fill('300 000 0002', 50_000);
    expect(button().textContent).toContain('Validar llave');
  });

  it('si la llave no existe abre B5 y al enviar marca la invitación', () => {
    const { fixture, c, fill } = setup(100_000, false);
    fill('300 000 0002', 50_000);
    c['submit']();
    fixture.detectChanges();
    expect(c['inviteOpen']()).toBe(true);

    c['inviteSent'].set(true);
    c['inviteOpen'].set(false);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Invitación enviada por WhatsApp.',
    );
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
