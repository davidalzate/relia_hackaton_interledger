import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { AuthService } from '../../core/services/auth.service';
import { InvitationService } from '../../core/services/invitation.service';
import { SmsService } from '../../core/services/sms.service';

// shortcut: C2 mínimo (SMS + solicitud con Aprobar/Rechazar e invitaciones de B5) para demostrar B3/B5;
// reemplazar por la pantalla "Mi billetera" cuando llegue su especificación
@Component({
  selector: 'app-user-phone',
  imports: [RouterLink, CurrencyPipe],
  templateUrl: './user-phone.component.html',
  styleUrl: './user-phone.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserPhoneComponent {
  private readonly agent = inject(AgentService);
  private readonly auth = inject(AuthService);

  protected readonly invitations = inject(InvitationService).invitations;
  /** Comprobantes enviados por SMS desde B9 */
  protected readonly messages = inject(SmsService).messages;
  /** Vuelve al retiro si hay una solicitud en curso; si no, a Caja */
  protected readonly backLink = computed(() =>
    this.agent.withdrawal() ? '/caja/retirar' : '/caja',
  );

  protected readonly store = computed(() => this.auth.user()?.store ?? 'Relia');
  protected readonly pending = computed(() => {
    const w = this.agent.withdrawal();
    return w?.status === 'waiting' ? w : null;
  });

  protected approve(): void {
    this.agent.approveWithdrawal();
  }

  protected reject(): void {
    this.agent.failWithdrawal('rejected');
  }
}
