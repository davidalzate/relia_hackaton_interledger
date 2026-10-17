import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '../interceptors/auth.interceptor';
import { AuthService } from './auth.service';
import { KeyLinkService } from './key-link.service';
import { SmsService } from './sms.service';

const PHONE = '+573000000002';
const WALLET = '$ilp.interledger-test.dev/maria';
const CHALLENGE = {
  challengeId: 'ch-1',
  phone: PHONE,
  expiresAt: '2026-10-17T12:05:00Z',
  maxAttempts: 5,
  walletAssetCode: 'COP',
};

describe('KeyLinkService', () => {
  let service: KeyLinkService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(KeyLinkService);
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AuthService), 'token').mockReturnValue('tok');
  });
  afterEach(() => http.verify());

  it('start hace POST /key-links sin token y copia el OTP de la bandeja demo al teléfono simulado', () => {
    service.start(PHONE, WALLET).subscribe();
    const req = http.expectOne('/api/key-links');
    expect(req.request.body).toEqual({ phone: PHONE, walletAddress: WALLET });
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush(CHALLENGE, { status: 201, statusText: 'Created' });

    expect(service.pending()).toEqual({
      phone: PHONE,
      walletAddress: WALLET,
      challengeId: 'ch-1',
      walletAssetCode: 'COP',
    });
    http
      .expectOne('/api/demo/messages?phone=%2B573000000002&limit=1')
      .flush({ items: [{ content: 'Relia: tu código es 123456' }], unreadCount: 1 });
    expect(TestBed.inject(SmsService).messages()[0]).toMatchObject({
      phone: PHONE,
      text: 'Relia: tu código es 123456',
    });
  });

  it('sin modo demo la bandeja falla y start igual completa', () => {
    let done = false;
    service.start(PHONE, WALLET).subscribe({ complete: () => (done = true) });
    http.expectOne('/api/key-links').flush(CHALLENGE);
    http
      .expectOne('/api/demo/messages?phone=%2B573000000002&limit=1')
      .flush(null, { status: 404, statusText: 'Not Found' });
    expect(done).toBe(true);
  });

  it('verify hace POST /key-links/{challengeId}/verify y limpia el desafío', () => {
    service.start(PHONE, WALLET).subscribe();
    http.expectOne('/api/key-links').flush(CHALLENGE);
    http.expectOne('/api/demo/messages?phone=%2B573000000002&limit=1').flush({ items: [] });

    service.verify('123456').subscribe();
    const req = http.expectOne('/api/key-links/ch-1/verify');
    expect(req.request.body).toEqual({ code: '123456' });
    req.flush({ phone: PHONE, walletAddress: WALLET, linkedAt: '2026-10-17T12:01:00Z' });
    expect(service.pending()).toBeNull();
  });
});
