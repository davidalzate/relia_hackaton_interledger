import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InputNumber } from 'primeng/inputnumber';
import { SelectButton } from 'primeng/selectbutton';

import { AgentService, CashMode, CashUpdate } from '../../core/services/agent.service';

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

  protected save(): void {
    const { mode, amount } = this.form.getRawValue();
    if (!this.canSave() || amount == null) return;
    this.saved.set(this.agent.updateCash(mode, amount));
    this.form.controls.amount.reset();
  }
}
