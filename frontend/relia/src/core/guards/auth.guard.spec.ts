import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  it('redirige a /login sin sesión', () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient()] });
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    expect(result instanceof UrlTree && result.toString()).toBe('/login');
  });
});
