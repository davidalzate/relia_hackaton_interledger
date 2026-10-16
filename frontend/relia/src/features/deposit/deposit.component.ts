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
import { Checkbox } from 'primeng/checkbox';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';

import { AgentService } from '../../core/services/agent.service';
import { Operation } from '../../shared/types/agent';
import { AGENT_COMMISSION, RELIA_FEE, depositBreakdown } from './deposit-breakdown';

type KeyState = 'idle' | 'checking' | 'found' | 'missing' | 'error';

// B2: el usuario entrega efectivo y recibe dinero en su billetera
@Component({
  selector: 'app-deposit',
  imports: [RouterLink, ReactiveFormsModule, CurrencyPipe, Checkbox, InputNumber, InputText],
  templateUrl: './deposit.component.html',
  styleUrl: './deposit.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DepositComponent {
  private readonly agent = inject(AgentService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly fee = RELIA_FEE;
  protected readonly commission = AGENT_COMMISSION;

  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly form = this.fb.group({
    phone: [''],
    amount: this.fb.control<number | null>(null),
    roundForUser: [false],
    simulateMissing: [false],
  });
  private readonly value = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  protected readonly keyState = signal<KeyState>('idle');
  protected readonly completed = signal<Operation | null>(null);

  protected readonly amount = computed(() => this.value().amount ?? 0);
  // El campo guarda el formato "300 000 0001"; se trabaja con los dígitos
  private readonly phoneDigits = computed(() => (this.value().phone ?? '').replace(/\D/g, ''));
  protected readonly phoneComplete = computed(() => this.phoneDigits().length === 10);
  protected readonly amountTooLow = computed(() => this.amount() > 0 && this.amount() <= RELIA_FEE);
  protected readonly breakdown = computed(() =>
    depositBreakdown(this.amount(), this.value().roundForUser ?? false, this.agent.balances()),
  );
  protected readonly cupoOk = computed(() => this.breakdown()?.cupoShortfall === 0);
  protected readonly canConfirm = computed(() => this.keyState() === 'found' && this.cupoOk());
  protected readonly canValidate = computed(
    () => this.phoneComplete() && this.cupoOk() && this.keyState() !== 'checking',
  );

  constructor() {
    // Cambiar el celular borra la validación previa
    this.form.controls.phone.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.keyState.set('idle'));
  }

  /** Deja solo dígitos (máx. 10) con el formato 300 000 0001 */
  protected formatPhone(): void {
    const digits = this.form.controls.phone.value.replace(/\D/g, '').slice(0, 10);
    const formatted = [digits.slice(0, 3), digits.slice(3, 6), digits.slice(6)]
      .filter(Boolean)
      .join(' ');
    if (formatted !== this.form.controls.phone.value) this.form.controls.phone.setValue(formatted);
  }

  protected validateKey(): void {
    if (!this.canValidate()) return;
    const { simulateMissing } = this.form.getRawValue();
    this.keyState.set('checking');
    this.agent
      .validateKey(this.phoneDigits(), simulateMissing)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (exists) => this.keyState.set(exists ? 'found' : 'missing'),
        error: () => this.keyState.set('error'),
      });
  }

  protected confirm(): void {
    const breakdown = this.breakdown();
    if (!breakdown || !this.canConfirm()) return;
    this.completed.set(
      this.agent.deposit({
        phone: this.phoneDigits(),
        cashIn: breakdown.cashIn,
        commission: AGENT_COMMISSION,
      }),
    );
  }

  protected submit(): void {
    if (this.keyState() === 'found') this.confirm();
    else this.validateKey();
  }

  protected newOperation(): void {
    this.form.reset();
    this.keyState.set('idle');
    this.completed.set(null);
  }
}
