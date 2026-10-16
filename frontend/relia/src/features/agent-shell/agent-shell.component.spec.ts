import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { AgentShellComponent } from './agent-shell.component';

describe('AgentShellComponent', () => {
  it('muestra el comercio y cerrar sesión limpia la sesión y va a /login', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const auth = TestBed.inject(AuthService);
    await firstValueFrom(auth.login({ email: 'rosa@demo.relia', password: 'demo1234' }));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    const fixture = TestBed.createComponent(AgentShellComponent);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Tienda Doña Rosa');

    fixture.componentInstance['items'].find((i) => i.label === 'Cerrar sesión')?.command?.({});
    expect(auth.authenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/login');
  });
});
