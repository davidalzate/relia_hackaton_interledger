import { TestBed } from '@angular/core/testing';

import { InvitationService, whatsappLink } from '../../../core/services/invitation.service';
import { InviteDialogComponent } from './invite-dialog.component';

describe('InviteDialogComponent (B5)', () => {
  it('arma el enlace de WhatsApp con el mensaje codificado', () => {
    expect(whatsappLink('+573000000002')).toBe(
      'https://wa.me/573000000002?text=Hola%2C%20te%20invito%20a%20vincular%20tu%20wallet%20en%20relia.com%2Fselectkey',
    );
  });

  it('muestra el celular y al enviar guarda la invitación, emite y cierra', async () => {
    const fixture = TestBed.createComponent(InviteDialogComponent);
    fixture.componentRef.setInput('phone', '+573000000002');
    fixture.componentInstance.visible.set(true);
    const sent = vi.fn();
    fixture.componentInstance.sent.subscribe(sent);
    fixture.detectChanges();
    await fixture.whenStable();

    const dialog = document.querySelector('.p-dialog') as HTMLElement;
    expect(dialog.textContent).toContain('Esta llave no existe');
    expect(dialog.textContent).toContain('El +57 300 000 0002 no tiene billetera vinculada.');
    expect(dialog.textContent).toContain(
      'Hola, te invito a vincular tu wallet en relia.com/selectkey',
    );

    (dialog.querySelector('.btn-success') as HTMLButtonElement).click();
    expect(TestBed.inject(InvitationService).invitations()[0].phone).toBe('+573000000002');
    expect(sent).toHaveBeenCalled();
    expect(fixture.componentInstance.visible()).toBe(false);
  });
});
