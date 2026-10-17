import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ProgressSpinner } from 'primeng/progressspinner';
import { filter, switchMap } from 'rxjs';

import { ApiError, newIdempotencyKey } from '../../core/http/api';
import { AdminService } from '../../core/services/admin.service';
import { AgentService } from '../../core/services/agent.service';
import { Settlement } from '../../shared/schemas/admin.schema';
import { Agent } from '../../shared/schemas/agent.schema';
import { ErrorCode } from '../../shared/schemas/problem.schema';
import { WalletAmountPipe } from '../../shared/utils/wallet-amount';

const FAILED = 'El pago falló. No se movió dinero.';
const ERROR_TEXT: Partial<Record<ErrorCode, string>> = {
  settlement_in_progress: 'Ya hay un pago de comisión en camino para este corresponsal.',
  no_commission: 'Sin comisión: este corresponsal no tiene comisión por pagar.',
  wallet_not_found: 'La billetera del corresponsal no existe en el riel de pagos.',
  wallet_currency_unsupported:
    'El riel de pagos no convierte pesos a la moneda de la billetera del corresponsal. No se movió dinero.',
  rail_unavailable: FAILED,
};

type Step =
  | { kind: 'confirm' }
  | { kind: 'sending' }
  | { kind: 'done'; settlement: Settlement }
  | { kind: 'error'; message: string };

// D3: pagar al corresponsal toda su comisión acumulada en su billetera
@Component({
  selector: 'app-pay-commission',
  imports: [RouterLink, CurrencyPipe, ProgressSpinner, WalletAmountPipe],
  templateUrl: './pay-commission.component.html',
  styleUrl: './pay-commission.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PayCommissionComponent {
  private readonly admin = inject(AdminService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly agent = signal<Agent | null>(null);
  protected readonly loadError = signal(false);
  protected readonly step = signal<Step>({ kind: 'confirm' });
  // Misma clave al reintentar tras 4xx/503 (no se creó nada); nueva si la liquidación se anuló
  private idempotencyKey: string | null = null;

  constructor() {
    inject(AgentService)
      .fetchAgent(this.id)
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (agent) => this.agent.set(agent),
        error: () => this.loadError.set(true),
      });
  }

  /** "Confirmar pago": `POST .../settlements` y polling hasta `paid` o `voided` */
  protected pay(): void {
    if (this.step().kind === 'sending') return;
    this.idempotencyKey ??= newIdempotencyKey();
    this.step.set({ kind: 'sending' });
    this.admin
      .createSettlement(this.id, this.idempotencyKey)
      .pipe(
        switchMap((created) => this.admin.watchSettlement(created.id)),
        filter((s) => s.status !== 'pending'),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (settlement) => {
          if (settlement.status === 'paid') {
            this.step.set({ kind: 'done', settlement });
            return;
          }
          this.idempotencyKey = null;
          this.step.set({ kind: 'error', message: FAILED });
        },
        error: (err: unknown) => {
          const code = err instanceof ApiError ? err.code : null;
          this.step.set({ kind: 'error', message: (code && ERROR_TEXT[code]) ?? FAILED });
        },
      });
  }
}
