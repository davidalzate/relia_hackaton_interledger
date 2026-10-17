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
import { SelectButton } from 'primeng/selectbutton';

import { ApiError, newIdempotencyKey } from '../../core/http/api';
import { AgentService, CashMode, CashUpdate } from '../../core/services/agent.service';
import { ErrorCode } from '../../shared/schemas/problem.schema';

const ERROR_TEXT: Partial<Record<ErrorCode, string>> = {
  insufficient_cash: 'No puedes fijar menos que el efectivo reservado para retiros pendientes.',
  no_change: 'El efectivo ya tiene ese valor.',
};

// B7: efectivo físico disponible para pagar retiros
@Component({
  selector: 'app-cash-on-hand',
  imports: [RouterLink, ReactiveFormsModule, CurrencyPipe, InputNumber, SelectButton],
  templateUrl: './cash-on-hand.component.html',
  styleUrl: './cash-on-hand.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CashOnHandComponent {
  private readonly agent = inject(AgentService);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly cash = computed(() => this.agent.balances().cash);
  protected readonly modes: { label: string; value: CashMode }[] = [
    { label: 'Sumar efectivo', value: 'add' },
    { label: 'Fijar total', value: 'set' },
  ];

  protected readonly form = this.fb.group({
    mode: this.fb.control<CashMode>('add'),
    amount: this.fb.control<number | null>(null),
  });
  private readonly value = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  protected readonly mode = computed(() => this.value().mode ?? 'add');
  /** Sumar exige monto > 0; fijar acepta cualquier monto (incluido 0) */
  protected readonly canSave = computed(() => {
    const amount = this.value().amount;
    return this.mode() === 'add' ? (amount ?? 0) > 0 : amount != null;
  });
  protected readonly cashAfter = computed(() => {
    const amount = this.value().amount ?? 0;
    return this.mode() === 'add' ? this.cash() + amount : amount;
  });
  protected readonly saved = signal<CashUpdate | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  // Misma clave al reintentar el mismo ajuste; cambiar el formulario la descarta
  private idempotencyKey: string | null = null;

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => (this.idempotencyKey = null));
  }

  protected save(): void {
    const { mode, amount } = this.form.getRawValue();
    if (!this.canSave() || amount == null || this.saving()) return;
    this.idempotencyKey ??= newIdempotencyKey();
    this.saving.set(true);
    this.error.set(null);
    this.agent
      .updateCash(mode, amount, this.idempotencyKey)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (update) => {
          this.saving.set(false);
          this.saved.set(update);
          this.form.controls.amount.reset();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.saved.set(null);
          const code = err instanceof ApiError ? err.code : null;
          this.error.set(
            (code && ERROR_TEXT[code]) ?? 'No pudimos guardar el efectivo. Intenta de nuevo.',
          );
        },
      });
  }
}
