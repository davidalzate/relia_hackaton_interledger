import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { QuotaTopUpService } from '../../../core/services/quota-top-up.service';

// shortcut: panel Modo demo mínimo (simulador Bre-B de cargas de cupo); el resto de E1 llega con la
// feature Modo demo
@Component({
  selector: 'app-demo-panel',
  imports: [CurrencyPipe],
  templateUrl: './demo-panel.component.html',
  styleUrl: './demo-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DemoPanelComponent {
  private readonly topUps = inject(QuotaTopUpService);

  protected readonly open = signal(false);
  protected readonly cupoLoad = computed(() => {
    const l = this.topUps.load();
    return l?.status === 'waiting' ? l : null;
  });

  /** `POST /demo/quota-top-ups/{operationId}/breb-payment` */
  protected simulateBreB(): void {
    this.topUps.simulateBreBPayment();
  }
}
