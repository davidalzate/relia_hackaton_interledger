import { CurrencyPipe, DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';

import { AgentService } from '../../core/services/agent.service';
import { ReceiptService } from '../../core/services/receipt.service';
import { HistoryKind } from '../../shared/schemas/agent.schema';
import { OPERATION_LABEL, STATUS_BADGE, STATUS_LABEL } from '../../shared/types/agent';

type Filter = 'all' | HistoryKind;

// B8: auditoría completa de saldos, más reciente primero
@Component({
  selector: 'app-history',
  imports: [RouterLink, CurrencyPipe, DatePipe],
  templateUrl: './history.component.html',
  styleUrl: './history.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HistoryComponent {
  private readonly agent = inject(AgentService);
  protected readonly receipts = inject(ReceiptService);

  protected readonly balances = this.agent.balances;
  protected readonly filters: { value: Filter; label: string }[] = [
    { value: 'all', label: 'Todas' },
    { value: 'deposit', label: 'Depósito' },
    { value: 'withdrawal', label: 'Retiro' },
    { value: 'quotaTopUp', label: 'Carga de cupo' },
    { value: 'cashAdjustment', label: 'Efectivo' },
    { value: 'settlement', label: 'Pago de comisión' },
  ];
  protected readonly filter = signal<Filter>('all');
  protected readonly operations = computed(() => this.agent.history().items);
  protected readonly hasMore = computed(() => this.agent.history().nextCursor !== null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal(false);
  private request?: Subscription;

  constructor() {
    // Cada filtro pide su primera página al API (`?kind=`)
    effect(() => {
      const filter = this.filter();
      untracked(() => this.load(filter));
    });
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  protected loadMore(): void {
    this.load(this.filter(), true);
  }

  private load(filter: Filter, more = false): void {
    // Solo cuenta la última petición: cambiar de filtro cancela la anterior
    this.request?.unsubscribe();
    this.loading.set(true);
    this.loadError.set(false);
    this.request = this.agent.loadHistory(filter === 'all' ? null : filter, more).subscribe({
      next: () => this.loading.set(false),
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  protected readonly typeLabel = OPERATION_LABEL;
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly statusBadge = STATUS_BADGE;
  /** Las seis columnas de saldos antes/después, en orden */
  protected readonly balanceColumns = [
    { key: 'cupo', label: 'Cupo' },
    { key: 'commission', label: 'Comisión' },
    { key: 'cash', label: 'Efectivo' },
  ] as const;
}
