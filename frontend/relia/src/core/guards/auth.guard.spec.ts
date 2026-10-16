import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { authGuard } from './auth.guard';

const run = (role?: string) =>
  TestBed.runInInjectionContext(() =>
    authGuard({ data: role ? { role } : {} } as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
  );

describe('authGuard', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  it('redirige a /login sin sesión', () => {
    const result = run();
    expect(result instanceof UrlTree && result.toString()).toBe('/login');
  });

  it('bloquea al corresponsal en rutas de admin', async () => {
    await firstValueFrom(
      TestBed.inject(AuthService).login({ email: 'rosa@demo.relia', password: 'demo1234' }),
    );
    expect(run('agent')).toBe(true);
    expect(run('admin') instanceof UrlTree).toBe(true);
  });
});
