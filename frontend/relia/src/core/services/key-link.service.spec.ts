import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '../interceptors/auth.interceptor';
import { AuthService } from './auth.service';
import { KeyLinkService, OTP_TTL_MS } from './key-link.service';
import { SmsService } from './sms.service';

describe('KeyLinkService', () => {
  let service: KeyLinkService;
  const code = () => /\d{6}/.exec(TestBed.inject(SmsService).messages()[0].text)?.[0] ?? '';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(KeyLinkService);
  });
  afterEach(() => vi.useRealTimers());

  it('envía el OTP por SMS simulado al celular y lo verifica', () => {
    service.sendCode('3000000002', '$ilp.interledger-test.dev/maria');
    expect(TestBed.inject(SmsService).messages()[0].phone).toBe('3000000002');
    expect(service.pending()?.walletAddress).toBe('$ilp.interledger-test.dev/maria');
    expect(service.verify(code())).toEqual({ ok: true });
  });

  it('cuenta intentos fallidos y se bloquea al quinto', () => {
    service.sendCode('3000000002', '$ilp.interledger-test.dev/maria');
    const wrong = code() === '000000' ? '111111' : '000000';
    expect(service.verify(wrong)).toEqual({ ok: false, reason: 'invalid', attemptsRemaining: 4 });
    for (let i = 0; i < 3; i++) service.verify(wrong);
    expect(service.verify(wrong)).toEqual({ ok: false, reason: 'exceeded' });
    expect(service.verify(code())).toEqual({ ok: false, reason: 'exceeded' });
  });

  it('vence a los 5 minutos y reenviar invalida el código anterior', () => {
    vi.useFakeTimers();
    service.sendCode('3000000002', '$ilp.interledger-test.dev/maria');
    const first = code();
    vi.advanceTimersByTime(OTP_TTL_MS + 1);
    expect(service.verify(first)).toEqual({ ok: false, reason: 'expired' });

    service.sendCode('3000000002', '$ilp.interledger-test.dev/maria');
    expect(service.verify(code())).toEqual({ ok: true });
  });

  it('link hace POST /key-links sin token', () => {
    vi.spyOn(TestBed.inject(AuthService), 'token').mockReturnValue('tok');
    service.link('3000000002', '$ilp.interledger-test.dev/maria').subscribe();
    const req = TestBed.inject(HttpTestingController).expectOne('/api/key-links');
    req.flush({ phone: '3000000002', walletAddress: '$ilp.interledger-test.dev/maria' });
    expect(service.pending()).toBeNull();
    expect(req.request.body).toEqual({
      phone: '3000000002',
      walletAddress: '$ilp.interledger-test.dev/maria',
    });
    expect(req.request.headers.has('Authorization')).toBe(false);
  });
});
