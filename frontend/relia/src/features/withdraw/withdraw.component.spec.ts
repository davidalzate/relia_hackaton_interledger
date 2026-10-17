import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { of } from 'rxjs';

import { AgentService } from '../../core/services/agent.service';
import { WithdrawComponent } from './withdraw.component';

registerLocaleData(localeEsCo);

function setup(cash: number) {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), ConfirmationService, { provide: LOCALE_ID, useValue: 'es-CO' }],
  });
  const agent = TestBed.inject(AgentService);
  agent['_balances'].set({ cupo: 0, cash, commission: 0 });
  vi.spyOn(agent, 'validateKey').mockReturnValue(
    of({ phone: '3000000001', walletAddressMasked: '$ilp...test.dev/je***' }),
  );
  const confirm = vi
    .spyOn(TestBed.inject(ConfirmationService), 'confirm')
    .mockImplementation((c) => (c.accept?.(), TestBed.inject(ConfirmationService)));
  const fixture = TestBed.createComponent(WithdrawComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  const fillAndSend = () => {
    c['form'].patchValue({ phone: '300 000 0001', amount: 50_000, roundForUser: true });
    c['submit'](); // valida llave
    c['submit'](); // envía solicitud
    render();
  };
  return { agent, c, el, render, fillAndSend, confirm };
}

describe('WithdrawComponent', () => {
  it('envía la solicitud y espera la aprobación con cuenta regresiva', () => {
    const { el, fillAndSend } = setup(80_000);
    fillAndSend();
    expect(el.textContent).toContain('Esperando que el usuario apruebe en su billetera');
    expect(el.querySelector('.countdown')?.textContent).toBe('5:00');
    expect(el.textContent).toContain('No entregues efectivo todavía');
  });

  it('al aprobar muestra la entrega y Listo confirma en B4 y vuelve a Caja', () => {
    const { agent, c, el, render, fillAndSend, confirm } = setup(80_000);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fillAndSend();
    agent.approveWithdrawal();
    render();
    expect(el.textContent).toContain('Pago aprobado');
    expect(el.querySelector('.amount.big')?.textContent).toContain('50.000');

    c['done']();
    expect(confirm.mock.calls[0][0].message).toMatch(
      /^¿Confirmas que entregaste en efectivo un total de \$\s50\.000 al usuario\?$/,
    );
    expect(agent.withdrawal()).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/caja');
  });

  it('bloquea con efectivo insuficiente', () => {
    const { c, el, render } = setup(10_000);
    c['form'].patchValue({ phone: '300 000 0001', amount: 50_000 });
    render();
    expect(el.textContent).toContain('Efectivo insuficiente, faltan');
  });

  it('rechazo muestra B3.4 e Intentar de nuevo restaura el formulario', () => {
    const { agent, c, el, render, fillAndSend } = setup(80_000);
    fillAndSend();
    agent.failWithdrawal('rejected');
    render();
    expect(el.textContent).toContain('El usuario rechazó el pago');
    expect(el.textContent).toContain('No se movió dinero. No entregues efectivo.');

    c['retry']();
    render();
    expect(c['form'].getRawValue()).toMatchObject({
      phone: '300 000 0001',
      amount: 50_000,
      roundForUser: true,
    });
    expect(el.textContent).toContain('Enviar solicitud de pago');
  });
});
