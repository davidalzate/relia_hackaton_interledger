import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { Observable, Subject, of, throwError } from 'rxjs';

import { ApiError } from '../../core/http/api';
import { KeyLink, KeyLinkService, PendingLink } from '../../core/services/key-link.service';
import { Problem } from '../../shared/schemas/problem.schema';
import { LinkPhoneComponent } from './link-phone.component';

const apiError = (status: number, problem: Omit<Problem, 'title' | 'status'>) =>
  throwError(() => new ApiError(status, { title: 'Error', status, ...problem }));

function setup(phone: string | null = null, pending: PendingLink | null = null) {
  const linked = new Subject<KeyLink>();
  const keyLinks = {
    pending: signal(pending).asReadonly(),
    reset: vi.fn(),
    start: vi.fn((): Observable<void> => of(undefined)),
    verify: vi.fn((): Observable<KeyLink> => linked),
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
    const { c } = setup('+573000000002');
    expect(c['form'].controls.phone.value).toBe('+573000000002');
  });

  it('valida celular y payment pointer antes de enviar el código', () => {
    const { keyLinks, c, el, render } = setup();
    c['form'].patchValue({ phone: '', walletAddress: 'ilp.test/maria' });
    c['sendCode']();
    render();
    expect(el.textContent).toContain('Escribe el celular completo para el país elegido.');
    expect(el.textContent).toContain('El payment pointer empieza por $ y tiene una ruta');
    expect(keyLinks.start).not.toHaveBeenCalled();
  });

  it('paso 1 → 2 → 3: el API envía el OTP, lo verifica y crea la llave', () => {
    const { keyLinks, linked, c, el, render } = setup('+573000000002');
    c['form'].patchValue({ walletAddress: '$ilp.interledger-test.dev/maria' });
    c['sendCode']();
    render();
    expect(keyLinks.start).toHaveBeenCalledWith('+573000000002', '$ilp.interledger-test.dev/maria');
    expect(el.textContent).toContain('Te enviamos un SMS al +57 300 000 0002.');

    c['form'].patchValue({ code: '123456' });
    c['verify']();
    expect(keyLinks.verify).toHaveBeenCalledWith('123456');
    linked.next({ phone: '+573000000002', walletAddress: '$ilp.interledger-test.dev/maria' });
    render();
    expect(el.textContent).toContain('Tu celular quedó vinculado a tu billetera');
    expect(el.textContent).toContain('$ilp.interledger-test.dev/maria');
  });

  it('422 invalid_otp muestra los intentos que quedan', () => {
    const { keyLinks, c, el, render } = setup('+573000000002');
    keyLinks.verify.mockReturnValue(apiError(422, { code: 'invalid_otp', attemptsRemaining: 4 }));
    c['form'].patchValue({ walletAddress: '$ilp.interledger-test.dev/maria' });
    c['sendCode']();
    c['verify']();
    render();
    expect(el.textContent).toContain('El código no coincide. Revisa el SMS. Te quedan 4 intentos.');
  });

  it('409 wallet_already_linked al enviar el código se queda en el paso 1 con el motivo', () => {
    const { keyLinks, c, el, render } = setup('+573000000002');
    keyLinks.start.mockReturnValue(apiError(409, { code: 'wallet_already_linked' }));
    c['form'].patchValue({ walletAddress: '$ilp.interledger-test.dev/jeffdev' });
    c['sendCode']();
    render();
    expect(el.textContent).toContain('Esta billetera ya está vinculada a otro celular.');
    expect(el.textContent).toContain('Vincula tu billetera a tu celular');
  });

  it('al volver de Ver SMS retoma el paso 2 con los datos', () => {
    const { c, el } = setup(null, {
      phone: '+573000000002',
      walletAddress: '$ilp.interledger-test.dev/maria',
      challengeId: 'ch-1',
    });
    expect(el.textContent).toContain('Escribe el código');
    expect(c['form'].getRawValue()).toMatchObject({
      phone: '+573000000002',
      walletAddress: '$ilp.interledger-test.dev/maria',
    });
  });
});
