import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AuthService } from '../../core/services/auth.service';
import { InvitationService } from '../../core/services/invitation.service';
import { KeyLinkService } from '../../core/services/key-link.service';
import { SmsService } from '../../core/services/sms.service';
import { WithdrawalService } from '../../core/services/withdrawal.service';

// shortcut: C2 mínimo (SMS + enlace a la aprobación del retiro en curso e invitaciones de B5);
// "Mi billetera" con GET /demo/payment-requests llega con la feature Modo demo
@Component({
  selector: 'app-user-phone',
  imports: [RouterLink, CurrencyPipe],
  templateUrl: './user-phone.component.html',
  styleUrl: './user-phone.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserPhoneComponent {
  private readonly withdrawals = inject(WithdrawalService);
  private readonly auth = inject(AuthService);
  private readonly keyLinks = inject(KeyLinkService);

  protected readonly invitations = inject(InvitationService).invitations;
  /** Comprobantes enviados por SMS desde B9 */
  protected readonly messages = inject(SmsService).messages;
  /** Vuelve al retiro o a la vinculación (C1) en curso; si no, a Caja */
  protected readonly backLink = computed(() =>
    this.withdrawals.withdrawal()
      ? '/caja/retirar'
      : this.keyLinks.pending()
        ? '/vincular'
        : '/caja',
  );
  protected readonly backLabel = computed(() =>
    this.backLink() === '/vincular' ? '← Volver a vincular' : '← Volver a la caja',
  );

  protected readonly store = computed(() => this.auth.user()?.store ?? 'Relia');
  protected readonly pending = computed(() => {
    const w = this.withdrawals.withdrawal();
    return w?.status === 'waiting' ? w : null;
  });
}
