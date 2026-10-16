import { CurrencyPipe, formatCurrency } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  LOCALE_ID,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Checkbox } from 'primeng/checkbox';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { ProgressSpinner } from 'primeng/progressspinner';

import {
  AgentService,
  DepositError,
  DepositErrorKind,
  DepositReceipt,
} from '../../core/services/agent.service';
import { AGENT_COMMISSION, RELIA_FEE } from '../../shared/types/agent';
import { InviteDialogComponent } from '../../shared/components/invite-dialog/invite-dialog.component';
import { formatPhone, phoneDigits } from '../../shared/utils/phone';
import { depositBreakdown } from './deposit-breakdown';

type KeyState = 'idle' | 'checking' | 'found' | 'missing' | 'error';

/** B2.1 formulario → B2.4 enviando → B2.5 completado | B2.6 error */
type Step =
  | { kind: 'form' }
  | { kind: 'sending'; walletOut: number }
  | { kind: 'done'; receipt: DepositReceipt }
  | { kind: 'error'; error: DepositErrorKind; operationId: string; amount: number };

// B2: el usuario entrega efectivo y recibe dinero en su billetera
@Component({
  selector: 'app-deposit',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    CurrencyPipe,
    Checkbox,
    InputNumber,
    InputText,
    ProgressSpinner,
    InviteDialogComponent,
  ],
  templateUrl: './deposit.component.html',
  styleUrl: './deposit.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DepositComponent {
  private readonly agent = inject(AgentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmation = inject(ConfirmationService);
  private readonly locale = inject(LOCALE_ID);

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
  /** B5: modal de invitación cuando la llave no existe */
  protected readonly inviteOpen = signal(false);
  protected readonly inviteSent = signal(false);
  protected readonly step = signal<Step>({ kind: 'form' });

  protected readonly amount = computed(() => this.value().amount ?? 0);
  // El campo guarda el formato "300 000 0001"; se trabaja con los dígitos
  protected readonly phoneDigits = computed(() => phoneDigits(this.value().phone ?? ''));
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
    this.form.controls.phone.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.keyState.set('idle');
      this.inviteSent.set(false);
    });
  }

  protected formatPhone(): void {
    const formatted = formatPhone(this.form.controls.phone.value);
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
        next: (exists) => {
          this.keyState.set(exists ? 'found' : 'missing');
          this.inviteOpen.set(!exists);
        },
        error: () => this.keyState.set('error'),
      });
  }

  // B2.3: el depósito solo se registra tras confirmar en B4 que se recibió el efectivo
  protected confirm(): void {
    const breakdown = this.breakdown();
    if (!breakdown || !this.canConfirm()) return;
    const total = formatCurrency(breakdown.cashIn, this.locale, '$', 'COP', '1.0-0');
    this.confirmation.confirm({
      message: `¿Confirmas que recibiste en efectivo un total de ${total} por parte del usuario?`,
      accept: () => this.send(breakdown.cashIn, breakdown.walletOut),
    });
  }

  private send(cashIn: number, walletOut: number): void {
    this.step.set({ kind: 'sending', walletOut });
    this.agent
      .deposit({ phone: this.phoneDigits(), cashIn, walletOut, commission: AGENT_COMMISSION })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (receipt) => this.step.set({ kind: 'done', receipt }),
        error: (err: unknown) =>
          this.step.set({
            kind: 'error',
            error: err instanceof DepositError ? err.kind : 'network',
            operationId: err instanceof DepositError ? err.operation.id : '',
            amount: cashIn,
          }),
      });
  }

  /** B2.6: vuelve al formulario con los mismos datos */
  protected retry(): void {
    this.step.set({ kind: 'form' });
  }

  protected submit(): void {
    if (this.keyState() === 'found') this.confirm();
    else this.validateKey();
  }

  protected newOperation(): void {
    this.form.reset();
    this.keyState.set('idle');
    this.step.set({ kind: 'form' });
  }
}
