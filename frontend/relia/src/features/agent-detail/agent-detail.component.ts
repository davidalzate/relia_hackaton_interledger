import { CurrencyPipe, DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { AgentService } from '../../core/services/agent.service';
import { ReceiptService } from '../../core/services/receipt.service';
import { Agent } from '../../shared/schemas/agent.schema';
import { OPERATION_LABEL, Operation, STATUS_BADGE, STATUS_LABEL } from '../../shared/types/agent';

// D2: perfil, saldos e historial de un corresponsal para el admin
@Component({
  selector: 'app-agent-detail',
  imports: [RouterLink, CurrencyPipe, DatePipe],
  templateUrl: './agent-detail.component.html',
  styleUrl: './agent-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgentDetailComponent {
  private readonly agents = inject(AgentService);
  protected readonly receipts = inject(ReceiptService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly agent = signal<Agent | null>(null);
  protected readonly operations = signal<Operation[]>([]);
  private readonly nextCursor = signal<string | null>(null);
  protected readonly hasMore = computed(() => this.nextCursor() !== null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  protected readonly typeLabel = OPERATION_LABEL;
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly statusBadge = STATUS_BADGE;
  protected readonly balanceColumns = [
    { key: 'cupo', label: 'Cupo' },
    { key: 'commission', label: 'Comisión' },
    { key: 'cash', label: 'Efectivo' },
  ] as const;

  constructor() {
    this.agents
      .fetchAgent(this.id)
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (agent) => this.agent.set(agent),
        error: () => this.loadError.set(true),
      });
    this.loadHistory();
  }

  protected loadHistory(more = false): void {
    this.loading.set(true);
    this.agents
      .fetchHistory({ cursor: more ? this.nextCursor() : null }, this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ items, nextCursor }) => {
          this.operations.update((ops) => (more ? [...ops, ...items] : items));
          this.nextCursor.set(nextCursor);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.loadError.set(true);
        },
      });
  }

  protected open(op: Operation): void {
    this.receipts.open(op.id, op);
  }
}
