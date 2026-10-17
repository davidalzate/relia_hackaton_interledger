import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { defer, firstValueFrom, lastValueFrom, of, throwError, toArray } from 'rxjs';

import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { AuthService } from '../services/auth.service';
import { ApiError, OPERATION_TERMINAL, pollUntil } from './api';

describe('HTTP base', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AuthService), 'token').mockReturnValue('tok');
  });

  afterEach(() => ctrl.verify());

  it('añade Bearer a rutas privadas y no a las públicas', () => {
    for (const url of [
      '/api/me',
      '/api/auth/login',
      '/api/key-links/x/verify',
      '/api/demo/reset',
    ]) {
      http.get(url).subscribe();
    }
    expect(ctrl.expectOne('/api/me').request.headers.get('Authorization')).toBe('Bearer tok');
    for (const url of ['/api/auth/login', '/api/key-links/x/verify', '/api/demo/reset']) {
      expect(ctrl.expectOne(url).request.headers.has('Authorization')).toBe(false);
    }
  });

  it('convierte problem+json en ApiError con code', async () => {
    const res = firstValueFrom(http.post('/api/agents/a/deposits', {}));
    ctrl.expectOne('/api/agents/a/deposits').flush(
      {
        type: 'about:blank',
        title: 'Cupo',
        status: 422,
        code: 'insufficient_quota',
        shortfall: 5000,
      },
      { status: 422, statusText: 'Unprocessable' },
    );
    const error = await res.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('insufficient_quota');
    expect((error as ApiError).problem?.shortfall).toBe(5000);
  });

  it('error sin problem+json deja code en null', async () => {
    const res = firstValueFrom(http.get('/api/me'));
    ctrl.expectOne('/api/me').flush('Bad gateway', { status: 502, statusText: 'Bad Gateway' });
    const error = (await res.catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(502);
    expect(error.code).toBeNull();
  });
});

describe('pollUntil', () => {
  it('emite hasta el primer estado terminal, incluido', async () => {
    const states = ['pending', 'pending', 'completed', 'pending'];
    let i = 0;
    const seen = await lastValueFrom(
      pollUntil(
        () => of({ status: states[i++] }),
        (op) => OPERATION_TERMINAL.has(op.status),
        1,
      ).pipe(toArray()),
    );
    expect(seen.map((op) => op.status)).toEqual(['pending', 'pending', 'completed']);
  });

  it('reintenta errores transitorios y propaga los demás', async () => {
    let calls = 0;
    // defer: como HttpClient, cada reintento vuelve a pedir
    const flaky = () =>
      defer(() =>
        calls++ === 0 ? throwError(() => new ApiError(0, null)) : of({ status: 'completed' }),
      );
    await expect(
      lastValueFrom(pollUntil(flaky, (op) => OPERATION_TERMINAL.has(op.status), 1)),
    ).resolves.toEqual({ status: 'completed' });

    const notFound = () => throwError(() => new ApiError(404, null));
    await expect(lastValueFrom(pollUntil(notFound, () => true, 1))).rejects.toBeInstanceOf(
      ApiError,
    );
  });
});
