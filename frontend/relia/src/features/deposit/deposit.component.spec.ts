import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Observable, Subject, of, throwError } from 'rxjs';

import { ApiError } from '../../core/http/api';
import { AgentService } from '../../core/services/agent.service';
import { OperationsService } from '../../core/services/operations.service';
import { ApiOperation, OperationPreview } from '../../shared/schemas/operation.schema';
import { Operation } from '../../shared/types/agent';
import { DepositComponent } from './deposit.component';

registerLocaleData(localeEsCo);

const preview = (violations: OperationPreview['violations'] = []): OperationPreview => ({
  type: 'deposit',
  requestedAmount: 50_000,
  roundAmount: true,
  fee: { total: 1_000, relia: 500, agent: 500 },
  netAmount: 50_000,
  chargedAmount: 51_000,
  quotaAfter: 49_000,
  cashOnHandAfter: 51_000,
  accruedCommissionAfter: 500,
  violations,
});

const apiOp = (status: ApiOperation['status'], failureCode: ApiOperation['failureCode'] = null) =>
  ({
    id: 'op-1',
    reference: 'OP-000001',
    status,
    failureCode,
    netAmount: 50_000,
    chargedAmount: 51_000,
    fee: { total: 1_000, relia: 500, agent: 500 },
  }) as ApiOperation;

const recorded: Operation = {
  id: 'op-1',
  reference: 'OP-000001',
  type: 'deposit',
  amount: 51_000,
  status: 'completed',
  createdAt: '',
  before: { cupo: 100_000, cash: 0, commission: 0 },
  after: { cupo: 49_000, cash: 51_000, commission: 500 },
};

function setup(previewResult: OperationPreview = preview(), keyExists = true) {
  vi.useFakeTimers();
  const agent = {
    balances: signal({ cupo: 100_000, commission: 0, cash: 0 }),
    validateKey: vi.fn(() =>
      of(
        keyExists ? { phone: '+573000000001', walletAddressMasked: '$ilp...test.dev/je***' } : null,
      ),
    ),
    previewOperation: vi.fn(() => of(previewResult)),
    createDeposit: vi.fn((): Observable<ApiOperation> => of(apiOp('pending'))),
    recordApiOperation: vi.fn((op: ApiOperation) => ({ ...recorded, status: op.status })),
  };
  const watch = new Subject<ApiOperation>();
  const operations = { watch: vi.fn(() => watch) };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      ConfirmationService,
      { provide: LOCALE_ID, useValue: 'es-CO' },
      { provide: AgentService, useValue: agent },
      { provide: OperationsService, useValue: operations },
    ],
  });
  // Acepta B4 de inmediato y guarda el mensaje mostrado
  const confirm = vi
    .spyOn(TestBed.inject(ConfirmationService), 'confirm')
    .mockImplementation((c) => (c.accept?.(), TestBed.inject(ConfirmationService)));
  const fixture = TestBed.createComponent(DepositComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const button = () => el.querySelector('button[type=submit]') as HTMLButtonElement;
  const fill = (phone: string, amount: number, roundForUser = false) => {
    c['form'].patchValue({ phone, amount, roundForUser });
    vi.advanceTimersByTime(300);
    fixture.detectChanges();
  };
  return { fixture, c, el, agent, operations, watch, button, fill, confirm };
}

describe('DepositComponent', () => {
  afterEach(() => vi.useRealTimers());

  it('pide el desglose al API, valida la llave y crea el depósito', () => {
    const { fixture, c, el, agent, operations, button, fill, confirm } = setup();
    fill('+573000000001', 50_000, true);
    expect(agent.previewOperation).toHaveBeenCalledWith('deposit', 50_000, true);
    expect(el.querySelector('.tell')?.textContent).toContain('51.000');
    expect(button().textContent).toContain('Validar llave');

    c['submit']();
    fixture.detectChanges();
    expect(agent.validateKey).toHaveBeenCalledWith('+573000000001');
    expect(el.textContent).toContain('$ilp...test.dev/je***');
    expect(button().textContent).toContain('Confirmar depósito');

    c['submit']();
    expect(confirm.mock.calls[0][0].message).toMatch(
      /^¿Confirmas que recibiste en efectivo un total de \$\s51\.000 por parte del usuario\?$/,
    );
    expect(agent.createDeposit).toHaveBeenCalledWith(
      { phone: '+573000000001', requestedAmount: 50_000, roundAmount: true },
      expect.any(String),
    );
    expect(operations.watch).toHaveBeenCalledWith('op-1');
  });

  it('muestra Enviando mientras está pending y luego Completado', () => {
    const { fixture, c, el, watch, fill } = setup();
    fill('+573000000001', 50_000, true);
    c['submit']();
    c['submit']();
    watch.next(apiOp('pending'));
    fixture.detectChanges();
    expect(el.textContent).toContain('Enviando a la billetera');
    expect(el.textContent).toContain('No cierres esta pantalla.');

    watch.next(apiOp('completed'));
    fixture.detectChanges();
    expect(el.textContent).toContain('Depósito completado');
    expect(el.textContent).toContain('OP-000001');
    expect(el.querySelector('.sms')?.textContent).toContain('Recibiste');
  });

  it('422 insufficient_quota → Cupo insuficiente; reintentar usa la misma clave', () => {
    const { fixture, c, el, agent, fill } = setup();
    agent.createDeposit.mockReturnValue(
      throwError(
        () => new ApiError(422, { title: 'Cupo', status: 422, code: 'insufficient_quota' }),
      ),
    );
    fill('+573000000001', 50_000, true);
    c['submit']();
    c['submit']();
    fixture.detectChanges();
    expect(el.textContent).toContain('No hay cupo para este depósito.');

    c['retry']();
    fixture.detectChanges();
    expect(c['form'].getRawValue()).toMatchObject({ phone: '+573000000001', amount: 50_000 });
    c['submit']();
    const keys = agent.createDeposit.mock.calls.map((call) => (call as unknown[])[1]);
    expect(keys[1]).toBe(keys[0]);
  });

  it('pago fallido → B2.6 y el reintento usa una clave nueva', () => {
    const { fixture, c, el, agent, watch, fill } = setup();
    fill('+573000000001', 50_000, true);
    c['submit']();
    c['submit']();
    watch.next(apiOp('failed', 'payment_failed'));
    fixture.detectChanges();
    expect(el.textContent).toContain('El pago falló');
    expect(el.textContent).toContain('Operación OP-000001');

    c['retry']();
    c['submit']();
    const keys = agent.createDeposit.mock.calls.map((call) => (call as unknown[])[1]);
    expect(keys[1]).not.toBe(keys[0]);
  });

  it('503 rail_unavailable → El simulador no respondió', () => {
    const { fixture, c, el, agent, fill } = setup();
    agent.createDeposit.mockReturnValue(throwError(() => new ApiError(503, null)));
    fill('+573000000001', 50_000, true);
    c['submit']();
    c['submit']();
    fixture.detectChanges();
    expect(el.textContent).toContain('El simulador no respondió. No se movió dinero.');
  });

  it('cambiar el celular borra la validación', () => {
    const { c, fill, button } = setup();
    fill('+573000000001', 50_000);
    c['submit']();
    fill('+573000000002', 50_000);
    expect(button().textContent).toContain('Validar llave');
  });

  it('si la llave no existe abre B5 y al enviar marca la invitación', () => {
    const { fixture, c, el, fill } = setup(preview(), false);
    fill('+573000000002', 50_000);
    c['submit']();
    fixture.detectChanges();
    expect(c['inviteOpen']()).toBe(true);

    c['inviteSent'].set(true);
    c['inviteOpen'].set(false);
    fixture.detectChanges();
    expect(el.textContent).toContain('Invitación enviada por WhatsApp.');
  });

  it('violaciones del preview bloquean el botón', () => {
    const { el, fill, button } = setup(
      preview([{ code: 'insufficient_quota', shortfall: 10_000 }]),
    );
    fill('+573000000001', 50_000);
    expect(el.textContent).toContain('Cupo insuficiente, faltan');
    expect(button().disabled).toBe(true);
  });

  it('amount_too_low muestra el aviso sin desglose', () => {
    const { el, fill } = setup(preview([{ code: 'amount_too_low' }]));
    fill('+573000000001', 900);
    expect(el.textContent).toContain('El monto debe ser mayor a $1.000.');
    expect(el.querySelector('.tell')).toBeNull();
  });
});
