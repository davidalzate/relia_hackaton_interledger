import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { Observable, Subject, throwError } from 'rxjs';

import { ApiError } from '../../core/http/api';
import { KeyLinkService, OtpResult, PendingLink } from '../../core/services/key-link.service';
import { LinkPhoneComponent } from './link-phone.component';

function setup(phone: string | null = null, pending: PendingLink | null = null) {
  const linked = new Subject<{ phone: string }>();
  const keyLinks = {
    pending: signal(pending).asReadonly(),
    reset: vi.fn(),
    sendCode: vi.fn(),
    verify: vi.fn((): OtpResult => ({ ok: true })),
    link: vi.fn((): Observable<{ phone: string }> => linked),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: KeyLinkService, useValue: keyLinks },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { queryParamMap: convertToParamMap(phone ? { phone } : {}) } },
      },
    ],
  });
  const fixture = TestBed.createComponent(LinkPhoneComponent);
  fixture.detectChanges();
  const c = fixture.componentInstance as unknown as Record<string, any>;
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  return { keyLinks, linked, c, el, render };
}

describe('LinkPhoneComponent (C1)', () => {
  it('precarga el celular del enlace de invitación', () => {
    const { c } = setup('3000000002');
    expect(c['form'].controls.phone.value).toBe('300 000 0002');
  });

  it('valida celular y payment pointer antes de enviar el código', () => {
    const { keyLinks, c, el, render } = setup();
    c['form'].patchValue({ phone: '300 000', walletAddress: 'ilp.test/maria' });
    c['sendCode']();
    render();
    expect(el.textContent).toContain('Escribe un celular de 10 dígitos.');
    expect(el.textContent).toContain('El payment pointer empieza por $ y tiene una ruta');
    expect(keyLinks.sendCode).not.toHaveBeenCalled();
  });

  it('paso 1 → 2 → 3: OTP correcto crea la llave en el API', () => {
    const { keyLinks, linked, c, el, render } = setup('3000000002');
    c['form'].patchValue({ walletAddress: '$ilp.interledger-test.dev/maria' });
    c['sendCode']();
    render();
    expect(keyLinks.sendCode).toHaveBeenCalledWith('3000000002', '$ilp.interledger-test.dev/maria');
    expect(el.textContent).toContain('Te enviamos un SMS al 300 000 0002.');

    c['form'].patchValue({ code: '123456' });
    c['verify']();
    expect(keyLinks.verify).toHaveBeenCalledWith('123456');
    expect(keyLinks.link).toHaveBeenCalledWith('3000000002', '$ilp.interledger-test.dev/maria');
    linked.next({ phone: '3000000002' });
    render();
    expect(el.textContent).toContain('Tu celular quedó vinculado a tu billetera');
    expect(el.textContent).toContain('$ilp.interledger-test.dev/maria');
  });

  it('código incorrecto no llama al API y muestra los intentos', () => {
    const { keyLinks, c, el, render } = setup('3000000002');
    keyLinks.verify.mockReturnValue({ ok: false, reason: 'invalid', attemptsRemaining: 4 });
    c['form'].patchValue({ walletAddress: '$ilp.interledger-test.dev/maria' });
    c['sendCode']();
    c['verify']();
    render();
    expect(el.textContent).toContain('El código no coincide. Revisa el SMS. Te quedan 4 intentos.');
    expect(keyLinks.link).not.toHaveBeenCalled();
  });

  it('409 wallet_already_linked muestra el motivo', () => {
    const { keyLinks, c, el, render } = setup('3000000002');
    keyLinks.link.mockReturnValue(
      throwError(
        () => new ApiError(409, { title: 'Conflicto', status: 409, code: 'wallet_already_linked' }),
      ),
    );
    c['form'].patchValue({ walletAddress: '$ilp.interledger-test.dev/jeffdev' });
    c['sendCode']();
    c['verify']();
    render();
    expect(el.textContent).toContain('Esta billetera ya está vinculada a otro celular.');
  });

  it('al volver de Ver SMS retoma el paso 2 con los datos', () => {
    const { c, el } = setup(null, {
      phone: '3000000002',
      walletAddress: '$ilp.interledger-test.dev/maria',
    });
    expect(el.textContent).toContain('Escribe el código');
    expect(c['form'].getRawValue()).toMatchObject({
      phone: '300 000 0002',
      walletAddress: '$ilp.interledger-test.dev/maria',
    });
  });
});
