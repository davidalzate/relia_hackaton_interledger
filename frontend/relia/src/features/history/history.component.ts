import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { ReceiptService } from '../../core/services/receipt.service';
import {
  OPERATION_LABEL,
  OperationType,
  STATUS_BADGE,
  STATUS_LABEL,
} from '../../shared/types/agent';

type Filter = 'all' | 'deposit' | 'withdrawal' | 'cupo' | 'cash' | 'commission';

const FILTER_TYPES: Record<Exclude<Filter, 'all'>, OperationType[]> = {
  deposit: ['deposit'],
  withdrawal: ['withdrawal'],
  cupo: ['cupo'],
  cash: ['cash', 'cash-set'],
  commission: ['commission'],
};

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
    { value: 'cupo', label: 'Carga de cupo' },
    { value: 'cash', label: 'Efectivo' },
    { value: 'commission', label: 'Pago de comisión' },
  ];
  protected readonly filter = signal<Filter>('all');
  protected readonly operations = computed(() => {
    const f = this.filter();
    const ops = this.agent.operations();
    return f === 'all' ? ops : ops.filter((op) => FILTER_TYPES[f].includes(op.type));
  });

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
