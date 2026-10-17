import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { loginAs } from '../services/auth.testing';
import { authGuard } from './auth.guard';

const run = (role?: string) =>
  TestBed.runInInjectionContext(() =>
    authGuard({ data: role ? { role } : {} } as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
  );

describe('authGuard', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }),
  );

  it('redirige a /login sin sesión', () => {
    const result = run();
    expect(result instanceof UrlTree && result.toString()).toBe('/login');
  });

  it('bloquea al corresponsal en rutas de admin', async () => {
    await loginAs('agent');
    expect(run('agent')).toBe(true);
    expect(run('admin') instanceof UrlTree).toBe(true);
  });
});
