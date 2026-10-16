import { CurrencyPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { interval, map } from 'rxjs';

import { AgentService, CUPO_LOAD_TTL_MS, CupoMethod } from '../../core/services/agent.service';
import { formatPhone, phoneDigits } from '../../shared/utils/phone';
import { formatCountdown } from '../../shared/utils/time';

/** Llave Wallet del corresponsal demo */
const DEMO_AGENT_KEY = '310 555 0101';

// B6: el corresponsal sube su cupo (saldo operativo) por Bre-B o desde su Wallet
@Component({
  selector: 'app-load-cupo',
  imports: [RouterLink, ReactiveFormsModule, CurrencyPipe, InputNumber, InputText],
  templateUrl: './load-cupo.component.html',
  styleUrl: './load-cupo.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoadCupoComponent {
  private readonly agent = inject(AgentService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly balances = this.agent.balances;
  protected readonly load = this.agent.cupoLoad;
  /** B6.1 → B6.2: método elegido; null muestra el paso 1 */
  protected readonly method = signal<CupoMethod | null>(null);
  protected readonly methodLabel: Record<CupoMethod, string> = { breb: 'Bre-B', wallet: 'Wallet' };
  protected readonly phoneText = formatPhone;

  protected readonly form = this.fb.group({
    amount: this.fb.control<number | null>(null),
    key: [DEMO_AGENT_KEY],
  });
  private readonly value = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });
  protected readonly canSubmit = computed(
    () =>
      (this.value().amount ?? 0) > 0 &&
      (this.method() !== 'wallet' || phoneDigits(this.value().key ?? '').length === 10),
  );

  private readonly now = toSignal(interval(1_000).pipe(map(() => Date.now())), {
    initialValue: Date.now(),
  });
  protected readonly countdown = computed(() => {
    const l = this.load();
    return l?.status === 'waiting'
      ? formatCountdown(l.expiresAt - this.now(), CUPO_LOAD_TTL_MS[l.request.method])
      : '';
  });

  constructor() {
    // Un resultado (completado o error) no se vuelve a mostrar al salir; una carga en espera sí
    inject(DestroyRef).onDestroy(() => {
      if (this.load()?.status !== 'waiting') this.agent.clearCupoLoad();
    });
  }

  protected formatKey(): void {
    const formatted = formatPhone(this.form.controls.key.value);
    if (formatted !== this.form.controls.key.value) this.form.controls.key.setValue(formatted);
  }

  protected choose(method: CupoMethod): void {
    this.method.set(method);
  }

  protected submit(): void {
    const method = this.method();
    if (!method || !this.canSubmit()) return;
    const { amount, key } = this.form.getRawValue();
    this.agent.requestCupoLoad({
      method,
      amount: amount ?? 0,
      key: method === 'wallet' ? phoneDigits(key) : undefined,
    });
  }

  protected cancel(): void {
    this.agent.failCupoLoad('cancelled');
  }

  /** B6.5: vuelve al paso 2 con el mismo método y monto */
  protected retry(): void {
    const l = this.load();
    if (!l) return;
    this.method.set(l.request.method);
    this.form.patchValue({
      amount: l.request.amount,
      key: l.request.key ? formatPhone(l.request.key) : DEMO_AGENT_KEY,
    });
    this.agent.clearCupoLoad();
  }

  protected finish(): void {
    this.agent.clearCupoLoad();
  }
}
