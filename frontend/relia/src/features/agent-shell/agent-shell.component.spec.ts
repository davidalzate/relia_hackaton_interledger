import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
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

describe('B4 modal de confirmación de efectivo', () => {
  it('muestra etiqueta, pregunta y botones; No, revisar cierra sin aceptar', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(AgentShellComponent);
    fixture.detectChanges();
    const accept = vi.fn();
    fixture.debugElement.injector
      .get(ConfirmationService)
      .confirm({
        message: '¿Confirmas que entregaste en efectivo un total de $ 1 al usuario?',
        accept,
      });
    fixture.detectChanges();
    await fixture.whenStable();

    const dialog = document.querySelector('.cash-confirm') as HTMLElement;
    expect(dialog.textContent).toContain('Confirma el efectivo');
    expect(dialog.textContent).toContain('¿Confirmas que entregaste en efectivo');
    const buttons = [...dialog.querySelectorAll('.confirm-actions button')];
    expect(buttons.map((b) => b.textContent?.trim())).toEqual(['Sí, confirmo', 'No, revisar']);

    (buttons[1] as HTMLButtonElement).click();
    expect(accept).not.toHaveBeenCalled();
  });
});
