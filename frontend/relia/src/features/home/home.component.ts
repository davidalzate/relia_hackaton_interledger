import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AgentService, LOW_BALANCE_THRESHOLD } from '../../core/services/agent.service';
import { OPERATION_LABEL, STATUS_BADGE, STATUS_LABEL } from '../../shared/types/agent';

// B1: saldos de un vistazo y acceso a las operaciones
@Component({
  selector: 'app-home',
  imports: [RouterLink, CurrencyPipe, DatePipe],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {
  private readonly agent = inject(AgentService);

  protected readonly balances = this.agent.balances;
  protected readonly operations = this.agent.recentOperations;
  protected readonly lowCupo = computed(() => this.balances().cupo < LOW_BALANCE_THRESHOLD);
  protected readonly lowCash = computed(() => this.balances().cash < LOW_BALANCE_THRESHOLD);

  protected readonly typeLabel = OPERATION_LABEL;
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly statusBadge = STATUS_BADGE;
}
