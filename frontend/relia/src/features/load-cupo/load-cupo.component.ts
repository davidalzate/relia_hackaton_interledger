import { CurrencyPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InputNumber } from 'primeng/inputnumber';
import { interval, map } from 'rxjs';

import { newIdempotencyKey } from '../../core/http/api';
import { AgentService } from '../../core/services/agent.service';
import {
  CUPO_LOAD_TTL_MS,
  CupoMethod,
  QuotaTopUpService,
} from '../../core/services/quota-top-up.service';
import { formatCountdown } from '../../shared/utils/time';
import { WalletAmountPipe } from '../../shared/utils/wallet-amount';

// B6: el corresponsal sube su cupo (saldo operativo) por Bre-B o desde su Wallet
@Component({
  selector: 'app-load-cupo',
  imports: [RouterLink, ReactiveFormsModule, CurrencyPipe, InputNumber, WalletAmountPipe],
  templateUrl: './load-cupo.component.html',
  styleUrl: './load-cupo.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoadCupoComponent {
  private readonly agent = inject(AgentService);
  private readonly topUps = inject(QuotaTopUpService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly balances = this.agent.balances;
  /** Wallet: el API cobra al payment pointer registrado del corresponsal */
  protected readonly walletAddress = computed(() => this.agent.agent()?.walletAddress ?? null);
  protected readonly load = this.topUps.load;
  protected readonly creating = this.topUps.creating;
  /** B6.1 → B6.2: método elegido; null muestra el paso 1 */
  protected readonly method = signal<CupoMethod | null>(null);
  protected readonly methodLabel: Record<CupoMethod, string> = { breb: 'Bre-B', wallet: 'Wallet' };

  protected readonly form = this.fb.group({
    amount: this.fb.control<number | null>(null),
  });
  private readonly value = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });
  protected readonly canSubmit = computed(() => (this.value().amount ?? 0) > 0 && !this.creating());

  private readonly now = toSignal(interval(1_000).pipe(map(() => Date.now())), {
    initialValue: Date.now(),
  });
  protected readonly countdown = computed(() => {
    const l = this.load();
    return l?.status === 'waiting'
      ? formatCountdown(l.expiresAt - this.now(), CUPO_LOAD_TTL_MS[l.request.method])
      : '';
  });

  // Misma clave al reintentar la misma carga tras 4xx/503; otra si cambia el monto o la carga falló
  private idempotencyKey: string | null = null;

  constructor() {
    // Un resultado (completado o error) no se vuelve a mostrar al salir; una carga en espera sí
    inject(DestroyRef).onDestroy(() => {
      if (this.load()?.status !== 'waiting') this.topUps.clear();
    });
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => (this.idempotencyKey = null));
  }

  protected choose(method: CupoMethod): void {
    this.method.set(method);
    this.idempotencyKey = null;
  }

  protected submit(): void {
    const method = this.method();
    if (!method || !this.canSubmit()) return;
    this.idempotencyKey ??= newIdempotencyKey();
    this.topUps.start({ method, amount: this.form.getRawValue().amount ?? 0 }, this.idempotencyKey);
  }

  protected cancel(): void {
    this.topUps.cancel();
  }

  /** B6.5: vuelve al paso 2 con el mismo método y monto */
  protected retry(): void {
    const l = this.load();
    if (l?.status !== 'error') return;
    this.method.set(l.request.method);
    this.form.patchValue({ amount: l.request.amount });
    // Después de patchValue, que descarta la clave: se reusa solo si no se creó nada
    this.idempotencyKey = l.retryKey;
    this.topUps.clear();
  }

  protected finish(): void {
    this.topUps.clear();
  }
}
