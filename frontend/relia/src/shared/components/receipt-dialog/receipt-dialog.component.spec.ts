import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AgentService } from '../../../core/services/agent.service';
import { ReceiptService } from '../../../core/services/receipt.service';
import { SmsService } from '../../../core/services/sms.service';
import { ReceiptDialogComponent } from './receipt-dialog.component';

registerLocaleData(localeEsCo);

async function open(prepare: (agent: AgentService) => string) {
  TestBed.configureTestingModule({ providers: [{ provide: LOCALE_ID, useValue: 'es-CO' }] });
  const agent = TestBed.inject(AgentService);
  const id = prepare(agent);
  const fixture = TestBed.createComponent(ReceiptDialogComponent);
  TestBed.inject(ReceiptService).open(id);
  fixture.detectChanges();
  await fixture.whenStable();
  const card = () => document.querySelector('.receipt') as HTMLElement;
  const rows = () =>
    Object.fromEntries(
      [...card().querySelectorAll('dl > div')].map((d) => [
        d.querySelector('dt')?.textContent?.trim(),
        d.querySelector('dd')?.textContent?.trim().replace(/\s/g, ' '),
      ]),
    );
  return { agent, fixture, card, rows };
}

describe('ReceiptDialogComponent (B9)', () => {
  afterEach(() => TestBed.inject(ReceiptService).close());

  it('retiro: monto en efectivo, celular enmascarado, modalidad y SMS', async () => {
    vi.useFakeTimers();
    const { fixture, card, rows } = await open((agent) => {
      agent['_balances'].set({ cupo: 0, commission: 0, cash: 80_000 });
      return agent['record'](
        50_000,
        'completed',
        'withdrawal',
        {
          before: { cupo: 0, commission: 0, cash: 80_000 },
          after: { cupo: 50_000, commission: 500, cash: 30_000 },
        },
        { phone: '3000000001', cash: 50_000, wallet: 51_000, roundForUser: true },
      ).id;
    });
    vi.useRealTimers();

    expect(card().textContent).toContain('Comprobante de retiro');
    expect(card().querySelector('.amount')?.textContent).toContain('50.000');
    expect(card().textContent).toContain('entregados en efectivo');
    expect(rows()).toMatchObject({
      Operación: 'RL-0001',
      Celular: '300 *** 0001',
      Billetera: 'Oculta',
      'Pagado desde la billetera': '$ 51.000',
      Modalidad: 'Valor redondo (comisión aparte)',
      Estado: 'Completado',
    });
    // Sin sesión en el test el pie cae a "Relia"
    expect(card().textContent).toContain('Relia · Corresponsal Relia.');

    const sms = [...document.querySelectorAll('.receipt-dialog .actions button')].find((b) =>
      b.textContent?.includes('SMS'),
    ) as HTMLButtonElement;
    sms.click();
    fixture.detectChanges();
    expect(TestBed.inject(SmsService).messages()[0]).toMatchObject({
      phone: '3000000001',
      operationId: 'RL-0001',
    });
    expect(sms.textContent?.trim()).toBe('SMS enviado');
    expect(sms.disabled).toBe(true);
  });

  it('carga de cupo rechazada: método, saldos, estado y motivo; sin botón SMS', async () => {
    const { card, rows } = await open((agent) => {
      return agent['record'](20_000, 'failed', 'cupo', undefined, {
        method: 'wallet',
        reason: 'Pago rechazado',
      }).id;
    });
    expect(card().textContent).toContain('cargados a tu cupo');
    expect(rows()).toMatchObject({ Método: 'Wallet', Estado: 'Fallido', Motivo: 'Pago rechazado' });
    expect(document.querySelector('.receipt-dialog .actions')?.textContent).not.toContain('SMS');
  });

  it('ajuste de efectivo muestra "nuevo total en caja"', async () => {
    const { card, rows } = await open(
      (agent) =>
        agent['record'](70_000, 'completed', 'cash-set', {
          before: { cupo: 0, commission: 0, cash: 0 },
          after: { cupo: 0, commission: 0, cash: 70_000 },
        }).id,
    );
    expect(card().textContent).toContain('nuevo total en caja');
    expect(rows()['Efectivo después']).toBe('$ 70.000');
  });
});
