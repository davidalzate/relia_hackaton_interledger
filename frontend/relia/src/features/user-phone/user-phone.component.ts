import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { InvitationService } from '../../core/services/invitation.service';
import { SmsService } from '../../core/services/sms.service';
import { WithdrawalService } from '../../core/services/withdrawal.service';

// shortcut: C2 mínimo (SMS e invitaciones de B5). El retiro ya no llega aquí: el usuario escanea
// el QR de B3. "Mi billetera" con GET /demo/payment-requests llega con la feature Modo demo
@Component({
  selector: 'app-user-phone',
  imports: [RouterLink],
  templateUrl: './user-phone.component.html',
  styleUrl: './user-phone.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserPhoneComponent {
  private readonly withdrawals = inject(WithdrawalService);

  protected readonly invitations = inject(InvitationService).invitations;
  /** Comprobantes enviados por SMS desde B9 */
  protected readonly messages = inject(SmsService).messages;
  /** Vuelve al retiro si hay una solicitud en curso; si no, a Caja */
  protected readonly backLink = computed(() =>
    this.withdrawals.withdrawal() ? '/caja/retirar' : '/caja',
  );
}
