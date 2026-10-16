import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  model,
  output,
} from '@angular/core';
import { Dialog } from 'primeng/dialog';

import {
  INVITE_MESSAGE,
  InvitationService,
  whatsappLink,
} from '../../../core/services/invitation.service';
import { formatPhone } from '../../utils/phone';

// B5: la llave no existe; ofrece invitar al usuario por WhatsApp a vincular su billetera
@Component({
  selector: 'app-invite-dialog',
  imports: [Dialog],
  templateUrl: './invite-dialog.component.html',
  styleUrl: './invite-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InviteDialogComponent {
  private readonly invitations = inject(InvitationService);

  /** Celular del usuario, 10 dígitos */
  readonly phone = input.required<string>();
  readonly visible = model(false);
  readonly sent = output<void>();

  protected readonly message = INVITE_MESSAGE;
  protected readonly formattedPhone = computed(() => formatPhone(this.phone()));
  protected readonly link = computed(() => whatsappLink(this.phone()));

  protected send(): void {
    this.invitations.send(this.phone());
    this.visible.set(false);
    this.sent.emit();
  }
}
