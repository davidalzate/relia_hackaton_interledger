import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { AgentService } from '../../../core/services/agent.service';

// shortcut: panel Modo demo mínimo (confirmar/rechazar cargas de cupo) hasta tener su especificación
@Component({
  selector: 'app-demo-panel',
  imports: [CurrencyPipe],
  templateUrl: './demo-panel.component.html',
  styleUrl: './demo-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DemoPanelComponent {
  private readonly agent = inject(AgentService);

  protected readonly open = signal(false);
  protected readonly cupoLoad = computed(() => {
    const l = this.agent.cupoLoad();
    return l?.status === 'waiting' ? l : null;
  });

  protected confirm(): void {
    this.agent.confirmCupoLoad();
  }

  protected reject(): void {
    this.agent.failCupoLoad('rejected');
  }
}
