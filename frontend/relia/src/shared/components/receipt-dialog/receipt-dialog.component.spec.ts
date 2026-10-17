import { registerLocaleData } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import localeEsCo from '@angular/common/locales/es-CO';
import { DOCUMENT, LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AgentService } from '../../../core/services/agent.service';
import { ReceiptService } from '../../../core/services/receipt.service';
import { ApiOperation } from '../../schemas/operation.schema';
import { ReceiptDialogComponent } from './receipt-dialog.component';

registerLocaleData(localeEsCo);

const apiWithdrawal = {
  id: 'op-1',
  reference: 'OP-000001',
  type: 'withdrawal',
  status: 'completed',
  createdAt: '2026-10-16T15:00:00Z',
  failureCode: null,
  netAmount: 50_000,
  chargedAmount: 51_000,
  balances: {
    quota: { before: 0, after: 50_000 },
    accruedCommission: { before: 0, after: 500 },
    cashOnHand: { before: 80_000, after: 30_000 },
  },
} as unknown as ApiOperation;

const receipt = (isCopy = false) => ({
  code: 'A1B2C3D4E5F6',
  operationId: 'op-1',
  contentHash: 'f'.repeat(64),
  isCopy,
  issuedAt: '2026-10-16T15:01:00Z',
  data: {
    reference: 'OP-000001',
    type: 'withdrawal',
    date: '2026-10-16T15:01:00Z',
    currency: 'COP',
    merchant: { name: 'Tienda Doña Rosa', address: 'Calle 10 # 20-30', phone: '3105550101' },
    customer: { phoneMasked: '300 *** 0001', walletAddressMasked: '$ilp...test.dev/je***' },
    amounts: {
      requestedAmount: 50_000,
      roundAmount: true,
      fee: 1_000,
      netAmount: 50_000,
      chargedAmount: 51_000,
    },
    payment: { rail: 'openPayments', reference: 'pay-1' },
  },
});

async function open(prepare: (agent: AgentService) => string) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: LOCALE_ID, useValue: 'es-CO' },
    ],
  });
  const agent = TestBed.inject(AgentService);
  const http = TestBed.inject(HttpTestingController);
  const id = prepare(agent);
  const fixture = TestBed.createComponent(ReceiptDialogComponent);
  TestBed.inject(ReceiptService).open(id);
  fixture.detectChanges();
  // Con el recibo oficial pendiente, whenStable espera a que se responda el HTTP
  const stable = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
  };
  const card = () => document.querySelector('.receipt') as HTMLElement;
  const rows = () =>
    Object.fromEntries(
      [...card().querySelectorAll('dl > div')].map((d) => [
        d.querySelector('dt')?.textContent?.trim(),
        d.querySelector('dd')?.textContent?.trim().replace(/\s/g, ' '),
      ]),
    );
  const button = (text: string) =>
    [...document.querySelectorAll('.receipt-dialog .actions button')].find((b) =>
      b.textContent?.includes(text),
    ) as HTMLButtonElement | undefined;
  return { agent, http, fixture, card, rows, button, stable };
}

describe('ReceiptDialogComponent (B9)', () => {
  afterEach(() => TestBed.inject(ReceiptService).close());

  it('retiro del API: recibo oficial, COPIA, impresión registrada y SMS por API', async () => {
    const { http, fixture, card, rows, button, stable } = await open(
      (agent) => agent.recordApiOperation(apiWithdrawal).id,
    );
    const screen = http.expectOne((r) => r.url === '/api/operations/op-1/receipt');
    expect(screen.request.params.get('channel')).toBe('screen');
    screen.flush(receipt(true));
    await stable();

    expect(card().textContent).toContain('Comprobante de retiro');
    expect(card().textContent).toContain('COPIA');
    expect(card().querySelector('.amount')?.textContent).toContain('50.000');
    expect(rows()).toMatchObject({
      Operación: 'OP-000001',
      Celular: '300 *** 0001',
      Billetera: '$ilp...test.dev/je***',
      'Pagado desde la billetera': '$ 51.000',
      'Comisión Relia': '$ 1.000',
      Modalidad: 'Valor redondo (comisión aparte)',
      Estado: 'Completado',
    });
    expect(card().textContent).toContain('Tienda Doña Rosa · Corresponsal Relia.');

    const print = vi
      .spyOn(TestBed.inject(DOCUMENT).defaultView!, 'print')
      .mockImplementation(() => undefined);
    button('Imprimir')!.click();
    http
      .expectOne((r) => r.url.endsWith('/receipt') && r.params.get('channel') === 'print')
      .flush(receipt(true));
    expect(print).toHaveBeenCalled();

    button('SMS')!.click();
    const send = http.expectOne('/api/operations/op-1/receipt/send');
    expect(send.request.body).toEqual({ channel: 'sms' });
    send.flush({ channel: 'sms', sentTo: '300 *** 0001', isCopy: true, sentAt: '' });
    fixture.detectChanges();
    expect(button('SMS')?.textContent?.trim()).toBe('SMS enviado');
    expect(button('SMS')?.disabled).toBe(true);
  });

  it('si el recibo oficial falla muestra los datos de la operación', async () => {
    const { http, card, stable } = await open(
      (agent) => agent.recordApiOperation(apiWithdrawal).id,
    );
    http
      .expectOne((r) => r.url.endsWith('/receipt'))
      .flush(
        { title: 'No', status: 409, code: 'receipt_not_available' },
        { status: 409, statusText: 'Conflict' },
      );
    await stable();
    expect(card().textContent).toContain('No pudimos cargar el recibo oficial');
    expect(card().textContent).toContain('OP-000001');
  });

  it('carga de cupo rechazada: método, estado y motivo; sin recibo oficial ni SMS', async () => {
    const { http, card, rows, stable } = await open((agent) => {
      return agent['record'](20_000, 'failed', 'cupo', undefined, {
        method: 'wallet',
        reason: 'Pago rechazado',
      }).id;
    });
    await stable();
    http.expectNone((r) => r.url.endsWith('/receipt'));
    expect(card().textContent).toContain('cargados a tu cupo');
    expect(rows()).toMatchObject({ Método: 'Wallet', Estado: 'Fallido', Motivo: 'Pago rechazado' });
    expect(document.querySelector('.receipt-dialog .actions')?.textContent).not.toContain('SMS');
  });

  it('ajuste de efectivo muestra "nuevo total en caja"', async () => {
    const { card, rows, stable } = await open(
      (agent) =>
        agent['record'](70_000, 'completed', 'cash-set', {
          before: { cupo: 0, commission: 0, cash: 0 },
          after: { cupo: 0, commission: 0, cash: 70_000 },
        }).id,
    );
    await stable();
    expect(card().textContent).toContain('nuevo total en caja');
    expect(rows()['Efectivo después']).toBe('$ 70.000');
  });
});
