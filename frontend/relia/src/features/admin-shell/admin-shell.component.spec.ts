import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';
import { AdminShellComponent } from './admin-shell.component';

describe('AdminShellComponent (D0)', () => {
  it('muestra wordmark + Admin enlazado a D1 y Cerrar sesión limpia y va a /login', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const auth = TestBed.inject(AuthService);
    await firstValueFrom(auth.login({ email: 'admin@relia.co', password: 'admin1234' }));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(AdminShellComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const brand = el.querySelector('.brand') as HTMLAnchorElement;
    expect(brand.getAttribute('href')).toBe('/admin');
    expect(brand.textContent).toContain('Admin');

    (el.querySelector('.logout') as HTMLButtonElement).click();
    expect(auth.authenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/login');
  });
});
