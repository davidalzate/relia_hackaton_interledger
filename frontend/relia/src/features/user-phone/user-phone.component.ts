import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { AuthService } from '../../core/services/auth.service';

// shortcut: C2 mínimo (SMS + solicitud con Aprobar/Rechazar) para poder demostrar B3;
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
