import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Subject, of } from 'rxjs';

import { AgentService } from '../../core/services/agent.service';
import { Withdrawal, WithdrawalService } from '../../core/services/withdrawal.service';
import { OperationPreview } from '../../shared/schemas/operation.schema';
import { Operation } from '../../shared/types/agent';
import { WithdrawComponent } from './withdraw.component';

registerLocaleData(localeEsCo);

const preview = (violations: OperationPreview['violations'] = []): OperationPreview => ({
  type: 'withdrawal',
  requestedAmount: 50_000,
  roundAmount: true,
  fee: { total: 1_000, relia: 500, agent: 500 },
  netAmount: 50_000,
  chargedAmount: 51_000,
  quotaAfter: 50_000,
  cashOnHandAfter: 30_000,
  accruedCommissionAfter: 500,
  violations,
});

const request = {
  phone: '+573000000001',
  amount: 50_000,
  roundForUser: true,
  cashOut: 50_000,
  walletIn: 51_000,
  commission: 500,
};
const operation: Operation = {
  id: 'op-1',
  reference: 'OP-000001',
  type: 'withdrawal',
  amount: 51_000,
  status: 'pending',
  createdAt: '',
};

function setup(previewResult = preview()) {
  vi.useFakeTimers();
  const agent = {
    validateKey: vi.fn(() =>
      of({ phone: '+573000000001', walletAddressMasked: '$ilp...test.dev/je***' }),
    ),
    previewOperation: vi.fn(() => of(previewResult)),
  };
  const state = signal<Withdrawal | null>(null);
  const handover = new Subject<unknown>();
  const withdrawals = {
    withdrawal: state.asReadonly(),
    creating: signal(false).asReadonly(),
    start: vi.fn(),
    cancel: vi.fn(),
    clear: vi.fn(() => state.set(null)),
    handover: vi.fn(() => handover),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      ConfirmationService,
      { provide: LOCALE_ID, useValue: 'es-CO' },
      { provide: AgentService, useValue: agent },
      { provide: WithdrawalService, useValue: withdrawals },
    ],
  });
  const confirm = vi
    .spyOn(TestBed.inject(ConfirmationService), 'confirm')
    .mockImplementation((c) => (c.accept?.(), TestBed.inject(ConfirmationService)));
  const fixture = TestBed.createComponent(WithdrawComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  const fill = () => {
    c['form'].patchValue({ phone: '+573000000001', amount: 50_000, roundForUser: true });
    vi.advanceTimersByTime(300);
    render();
  };
  return { agent, withdrawals, state, handover, c, el, render, fill, confirm };
}

describe('WithdrawComponent', () => {
  afterEach(() => vi.useRealTimers());

  it('desglose del API, llave y envío con Idempotency-Key', () => {
    const { agent, withdrawals, c, el, render, fill } = setup();
    fill();
    expect(agent.previewOperation).toHaveBeenCalledWith('withdrawal', 50_000, true);
    expect(el.querySelector('.tell')?.textContent).toContain('51.000');

    c['submit']();
    render();
    expect(el.textContent).toContain('$ilp...test.dev/je***');
    expect(el.querySelector('button[type=submit]')?.textContent?.trim()).toBe('Generar código QR');
    c['submit']();
    expect(withdrawals.start).toHaveBeenCalledWith(request, expect.any(String));
  });

  it('esperando: QR de la approvalUrl, cuenta regresiva desde expiresAt y cancelar', () => {
    const { state, el, render, withdrawals, c } = setup();
    state.set({
      status: 'waiting',
      request,
      operation,
      expiresAt: Date.now() + 4 * 60_000,
      approvalUrl: 'https://wallet.test/aprobar',
    });
    render();
    expect(el.textContent).toContain('Pide al usuario que escanee el código');
    const qr = el.querySelector('app-qr-code');
    expect(qr).toBeTruthy();
    expect(el.querySelector('a[href="https://wallet.test/aprobar"]')?.textContent).toContain(
      'Abrir enlace de pago',
    );
    expect(el.querySelector('.countdown')?.textContent?.trim()).toBe('Esperando aprobación · 4:00');
    expect(el.textContent).toContain('No entregues efectivo todavía');
    c['cancelRequest']();
    expect(withdrawals.cancel).toHaveBeenCalled();
  });

  it('aprobado: Listo confirma en B4, registra la entrega y vuelve a Caja', () => {
    const { state, handover, withdrawals, c, el, render, confirm } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    state.set({
      status: 'approved',
      request,
      operation: { ...operation, status: 'completed' },
      before: { cupo: 0, cash: 80_000, commission: 0 },
      after: { cupo: 50_000, cash: 30_000, commission: 500 },
    });
    render();
    expect(el.textContent).toContain('Pago aprobado');
    expect(el.querySelector('.amount.big')?.textContent).toContain('50.000');
    expect(el.textContent).toContain('OP-000001');

    c['done']();
    expect(confirm.mock.calls[0][0].message).toMatch(
      /^¿Confirmas que entregaste en efectivo un total de \$\s50\.000 al usuario\?$/,
    );
    expect(withdrawals.handover).toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    handover.next({});
    expect(withdrawals.clear).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/caja');
  });

  it('efectivo insuficiente en el preview bloquea el envío', () => {
    const { el, fill } = setup(preview([{ code: 'insufficient_cash', shortfall: 20_000 }]));
    fill();
    expect(el.textContent).toContain('Efectivo insuficiente, faltan');
  });

  it('rechazo muestra B3.4; reintentar restaura el formulario con clave nueva', () => {
    const { state, withdrawals, c, el, render } = setup();
    state.set({
      status: 'error',
      request,
      operation: { ...operation, status: 'failed' },
      error: 'rejected',
      retryKey: null,
    });
    render();
    expect(el.textContent).toContain('El usuario rechazó el pago');
    expect(el.textContent).toContain('No se movió dinero. No entregues efectivo.');

    c['retry']();
    render();
    expect(c['form'].getRawValue()).toMatchObject({
      phone: '+573000000001',
      amount: 50_000,
      roundForUser: true,
    });
    expect(c['idempotencyKey']).toBeNull();
    expect(withdrawals.clear).toHaveBeenCalled();
  });

  it('error al crear: reintentar reusa la misma clave', () => {
    const { state, c } = setup();
    state.set({ status: 'error', request, operation: null, error: 'network', retryKey: 'k-1' });
    c['retry']();
    expect(c['idempotencyKey']).toBe('k-1');
  });
});
