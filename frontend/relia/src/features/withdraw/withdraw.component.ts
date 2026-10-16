import { CurrencyPipe, formatCurrency } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  LOCALE_ID,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { Checkbox } from 'primeng/checkbox';
import { InputNumber } from 'primeng/inputnumber';
import { InputText } from 'primeng/inputtext';
import { ProgressSpinner } from 'primeng/progressspinner';
import { interval, map } from 'rxjs';

import { AgentService, WITHDRAWAL_TTL_MS } from '../../core/services/agent.service';
import { AGENT_COMMISSION, RELIA_FEE } from '../../shared/types/agent';
import { InviteDialogComponent } from '../../shared/components/invite-dialog/invite-dialog.component';
import { formatPhone, phoneDigits } from '../../shared/utils/phone';
import { formatCountdown } from '../../shared/utils/time';
import { withdrawBreakdown } from './withdraw-breakdown';

type KeyState = 'idle' | 'checking' | 'found' | 'missing' | 'error';

// B3: el usuario paga desde su billetera y recibe efectivo. La solicitud en curso vive en
// AgentService para sobrevivir a la visita al teléfono del usuario (C2)
@Component({
  selector: 'app-withdraw',
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
  templateUrl: './withdraw.component.html',
  styleUrl: './withdraw.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WithdrawComponent {
  private readonly agent = inject(AgentService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmation = inject(ConfirmationService);
  private readonly locale = inject(LOCALE_ID);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly fee = RELIA_FEE;
  protected readonly commission = AGENT_COMMISSION;
  protected readonly withdrawal = this.agent.withdrawal;

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

  protected readonly amount = computed(() => this.value().amount ?? 0);
  protected readonly phoneDigits = computed(() => phoneDigits(this.value().phone ?? ''));
  protected readonly phoneComplete = computed(() => this.phoneDigits().length === 10);
  protected readonly amountTooLow = computed(() => this.amount() > 0 && this.amount() <= RELIA_FEE);
  protected readonly breakdown = computed(() =>
    withdrawBreakdown(this.amount(), this.value().roundForUser ?? false, this.agent.balances()),
  );
  protected readonly cashOk = computed(() => this.breakdown()?.cashShortfall === 0);
  protected readonly canSend = computed(() => this.keyState() === 'found' && this.cashOk());
  protected readonly canValidate = computed(
    () => this.phoneComplete() && this.cashOk() && this.keyState() !== 'checking',
  );

  private readonly now = toSignal(interval(1_000).pipe(map(() => Date.now())), {
    initialValue: Date.now(),
  });
  /** Cuenta regresiva m:ss de la solicitud en espera */
  protected readonly countdown = computed(() => {
    const w = this.withdrawal();
    return formatCountdown(
      w?.status === 'waiting' ? w.expiresAt - this.now() : 0,
      WITHDRAWAL_TTL_MS,
    );
  });

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
    this.keyState.set('checking');
    this.agent
      .validateKey(this.phoneDigits(), this.form.getRawValue().simulateMissing)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (exists) => {
          this.keyState.set(exists ? 'found' : 'missing');
          this.inviteOpen.set(!exists);
        },
        error: () => this.keyState.set('error'),
      });
  }

  protected submit(): void {
    if (this.keyState() !== 'found') return this.validateKey();
    const b = this.breakdown();
    if (!b || !this.canSend()) return;
    const { phone, amount, roundForUser } = this.form.getRawValue();
    this.agent.requestWithdrawal({
      phone: phoneDigits(phone),
      amount: amount ?? 0,
      roundForUser,
      cashOut: b.cashOut,
      walletIn: b.walletIn,
      commission: AGENT_COMMISSION,
    });
  }

  protected cancelRequest(): void {
    this.agent.failWithdrawal('cancelled');
  }

  /** B3.3 Listo: confirma en B4 la entrega del efectivo y vuelve a Caja */
  protected done(): void {
    const w = this.withdrawal();
    if (w?.status !== 'approved') return;
    const total = formatCurrency(w.request.cashOut, this.locale, '$', 'COP', '1.0-0');
    this.confirmation.confirm({
      message: `¿Confirmas que entregaste en efectivo un total de ${total} al usuario?`,
      accept: () => {
        this.agent.clearWithdrawal();
        void this.router.navigateByUrl('/caja');
      },
    });
  }

  /** B3.4: vuelve al formulario con los datos de la solicitud fallida */
  protected retry(): void {
    const w = this.withdrawal();
    if (!w) return;
    const { phone, amount, roundForUser } = w.request;
    this.agent.clearWithdrawal();
    this.form.setValue({ phone: formatPhone(phone), amount, roundForUser, simulateMissing: false });
    this.keyState.set('found');
  }
}
