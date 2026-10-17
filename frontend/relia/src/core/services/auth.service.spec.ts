import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../http/api';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { AuthService } from './auth.service';
import { loginAs } from './auth.testing';

describe('AuthService', () => {
  let auth: AuthService;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    auth = TestBed.inject(AuthService);
  });

  it('corresponsal: guarda token, agentId y perfil de /me', async () => {
    expect(await loginAs('agent')).toBe('agent');
    expect(auth.token()).toBe('tok-agent');
    expect(auth.agentId()).toBe('agent-rosa');
    expect(auth.user()).toEqual({
      role: 'agent',
      email: 'rosa@demo.relia',
      name: 'Rosa Gómez',
      store: 'Tienda Doña Rosa',
    });
  });

  it('normaliza el correo y pide /me con el token nuevo', async () => {
    const ctrl = TestBed.inject(HttpTestingController);
    const done = firstValueFrom(auth.login({ email: ' Admin@Relia.co ', password: 'p' }));
    const login = ctrl.expectOne('/api/auth/login');
    expect(login.request.body).toEqual({ email: 'admin@relia.co', password: 'p' });
    expect(login.request.headers.has('Authorization')).toBe(false);
    login.flush({ accessToken: 'tok', expiresIn: 60, role: 'admin', agentId: null });
    const me = ctrl.expectOne('/api/me');
    expect(me.request.headers.get('Authorization')).toBe('Bearer tok');
    me.flush({ role: 'admin', email: 'admin@relia.co', name: 'Admin Relia', agent: null });
    expect(await done).toBe('admin');
    expect(auth.agentId()).toBeNull();
  });

  it('401 invalid_credentials: ApiError y sin sesión', async () => {
    const done = firstValueFrom(auth.login({ email: 'rosa@demo.relia', password: 'mal' }));
    TestBed.inject(HttpTestingController)
      .expectOne('/api/auth/login')
      .flush(
        { title: 'No autorizado', status: 401, code: 'invalid_credentials' },
        { status: 401, statusText: 'Unauthorized' },
      );
    const error = (await done.catch((e: unknown) => e)) as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe('invalid_credentials');
    expect(auth.authenticated()).toBe(false);
  });

  it('si /me falla descarta el token', async () => {
    const ctrl = TestBed.inject(HttpTestingController);
    const done = firstValueFrom(auth.login({ email: 'a@relia.co', password: 'p' }));
    ctrl.expectOne('/api/auth/login').flush({ accessToken: 't', expiresIn: 60, role: 'admin' });
    ctrl.expectOne('/api/me').flush(null, { status: 500, statusText: 'Error' });
    await expect(done).rejects.toBeInstanceOf(ApiError);
    expect(auth.token()).toBeNull();
  });
});
