import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import { LOCALE_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { CupoLoad, QuotaTopUpService } from '../../core/services/quota-top-up.service';
import { Operation } from '../../shared/types/agent';
import { LoadCupoComponent } from './load-cupo.component';

registerLocaleData(localeEsCo);

const operation: Operation = {
  id: 'op-1',
  reference: 'OP-000001',
  type: 'cupo',
  amount: 300_000,
  status: 'pending',
  createdAt: '',
};

function setup() {
  const state = signal<CupoLoad | null>(null);
  const topUps = {
    load: state.asReadonly(),
    creating: signal(false).asReadonly(),
    start: vi.fn(),
    cancel: vi.fn(),
    clear: vi.fn(() => state.set(null)),
  };
  const agent = {
    balances: signal({ cupo: 0, commission: 0, cash: 0 }),
    agent: signal({ walletAddress: '$ilp.interledger-test.dev/tienda-rosa' }),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: LOCALE_ID, useValue: 'es-CO' },
      { provide: AgentService, useValue: agent },
      { provide: QuotaTopUpService, useValue: topUps },
    ],
  });
  const fixture = TestBed.createComponent(LoadCupoComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  const submitButton = () => el.querySelector('button[type=submit]') as HTMLButtonElement;
  return { topUps, state, fixture, c, el, render, submitButton };
}

describe('LoadCupoComponent', () => {
  it('Bre-B: paso 1 → paso 2 → crea la carga con Idempotency-Key', () => {
    const { topUps, c, el, render, submitButton } = setup();
    expect(el.textContent).toContain('Paso 1 · Elige el método');
    c['choose']('breb');
    render();
    expect(el.textContent).toContain('Paso 2 · Bre-B');
    expect(submitButton().textContent?.trim()).toBe('Generar QR');
    expect(submitButton().disabled).toBe(true);

    c['form'].patchValue({ amount: 300_000 });
    render();
    c['submit']();
    expect(topUps.start).toHaveBeenCalledWith(
      { method: 'breb', amount: 300_000 },
      expect.any(String),
    );
  });

  it('Bre-B esperando: QR con qrPayload y cuenta regresiva desde expiresAt', () => {
    vi.useFakeTimers();
    const { state, el, render } = setup();
    state.set({
      status: 'waiting',
      request: { method: 'breb', amount: 300_000 },
      operation,
      expiresAt: Date.now() + 10 * 60_000,
      approvalUrl: null,
      qrPayload: 'BREB|OP-000001|300000',
    });
    render();
    expect(el.textContent).toContain('Simulación Bre-B');
    expect(el.querySelector('.qr')).toBeTruthy();
    expect(el.textContent).toContain('BREB|OP-000001|300000');
    expect(el.querySelector('.countdown')?.textContent).toContain('Esperando pago · 10:00');
    vi.useRealTimers();
  });

  it('Wallet: muestra la billetera registrada y enlaza a approvalUrl mientras espera', () => {
    const { state, c, el, render } = setup();
    c['choose']('wallet');
    render();
    expect(el.textContent).toContain('$ilp.interledger-test.dev/tienda-rosa');

    state.set({
      status: 'waiting',
      request: { method: 'wallet', amount: 50_000 },
      operation,
      expiresAt: Date.now() + 5 * 60_000,
      approvalUrl: 'https://wallet.test/aprobar',
      qrPayload: null,
    });
    render();
    expect(el.querySelector('a[href="https://wallet.test/aprobar"]')).toBeTruthy();
  });

  it('completada muestra Cupo cargado; rechazo permite reintentar con clave nueva', () => {
    const { state, c, el, render } = setup();
    state.set({
      status: 'done',
      request: { method: 'breb', amount: 300_000 },
      operation: { ...operation, status: 'completed' },
      before: { cupo: 0, cash: 0, commission: 0 },
      after: { cupo: 300_000, cash: 0, commission: 0 },
    });
    render();
    expect(el.textContent).toContain('Cupo cargado');
    expect(el.textContent).toContain('OP-000001');

    state.set({
      status: 'error',
      request: { method: 'wallet', amount: 300_000 },
      operation: { ...operation, status: 'failed' },
      error: 'rejected',
      retryKey: null,
    });
    render();
    expect(el.textContent).toContain('La carga no se completó. Tu cupo no cambió.');
    c['retry']();
    render();
    expect(el.textContent).toContain('Paso 2 · Wallet');
    expect(c['form'].getRawValue().amount).toBe(300_000);
    expect(c['idempotencyKey']).toBeNull();
  });

  it('error al crear: reintentar reusa la misma clave', () => {
    const { state, c } = setup();
    state.set({
      status: 'error',
      request: { method: 'breb', amount: 300_000 },
      operation: null,
      error: 'network',
      retryKey: 'k-1',
    });
    c['retry']();
    expect(c['idempotencyKey']).toBe('k-1');
  });
});
