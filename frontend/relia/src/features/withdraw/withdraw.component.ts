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
import { catchError, debounceTime, distinctUntilChanged, interval, map, of, switchMap } from 'rxjs';

import { ReceiptService } from '../../core/services/receipt.service';
import { newIdempotencyKey } from '../../core/http/api';
import { AgentService } from '../../core/services/agent.service';
import { WITHDRAWAL_TTL_MS, WithdrawalService } from '../../core/services/withdrawal.service';
import { OperationPreview } from '../../shared/schemas/operation.schema';
import { InviteDialogComponent } from '../../shared/components/invite-dialog/invite-dialog.component';
import { PhoneFieldComponent } from '../../shared/components/phone-field/phone-field.component';
import { QrCodeComponent } from '../../shared/components/qr-code/qr-code.component';
import { formatCountdown } from '../../shared/utils/time';
import { WalletAmountPipe } from '../../shared/utils/wallet-amount';

type KeyState = 'idle' | 'checking' | 'found' | 'missing' | 'error';

/** Lo que muestra B3.1, tomado del desglose del API */
function toBreakdown(p: OperationPreview) {
  return {
    cashOut: p.netAmount,
    walletIn: p.chargedAmount,
    fee: p.fee.total,
    commission: p.fee.agent,
    cupoAfter: p.quotaAfter,
    cashAfter: p.cashOnHandAfter,
    cashShortfall: p.violations.find((v) => v.code === 'insufficient_cash')?.shortfall ?? 0,
  };
}

const PREVIEW_DEBOUNCE_MS = 300;

// B3: el usuario escanea un QR con la `approvalUrl`, paga desde su billetera y recibe efectivo.
// El retiro en curso vive en WithdrawalService para sobrevivir a la navegación
@Component({
  selector: 'app-withdraw',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    CurrencyPipe,
    Checkbox,
    InputNumber,
    InviteDialogComponent,
    PhoneFieldComponent,
    QrCodeComponent,
    WalletAmountPipe,
  ],
  templateUrl: './withdraw.component.html',
  styleUrl: './withdraw.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WithdrawComponent {
  private readonly agent = inject(AgentService);
  private readonly withdrawals = inject(WithdrawalService);
  protected readonly receipts = inject(ReceiptService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmation = inject(ConfirmationService);
  private readonly locale = inject(LOCALE_ID);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly withdrawal = this.withdrawals.withdrawal;
  protected readonly creating = this.withdrawals.creating;

  protected readonly form = this.fb.group({
    phone: [''],
    amount: this.fb.control<number | null>(null),
    roundForUser: [false],
  });
  private readonly value = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  protected readonly keyState = signal<KeyState>('idle');
  protected readonly walletMasked = signal<string | null>(null);
  protected readonly handoverError = signal(false);
  /** B5: modal de invitación cuando la llave no existe */
  protected readonly inviteOpen = signal(false);
  protected readonly inviteSent = signal(false);

  protected readonly amount = computed(() => this.value().amount ?? 0);
  /** E.164, o '' mientras el número está incompleto */
  protected readonly phone = computed(() => this.value().phone ?? '');
  protected readonly phoneComplete = computed(() => !!this.phone());
  /** `POST .../operations/preview` al escribir el monto o cambiar "valor redondo" */
  private readonly preview = toSignal(
    this.form.valueChanges.pipe(
      map((v) => ({ amount: v.amount ?? 0, round: v.roundForUser ?? false })),
      distinctUntilChanged((a, b) => a.amount === b.amount && a.round === b.round),
      debounceTime(PREVIEW_DEBOUNCE_MS),
      switchMap(({ amount, round }) =>
        amount > 0
          ? this.agent
              .previewOperation('withdrawal', amount, round)
              .pipe(catchError(() => of(null)))
          : of(null),
      ),
    ),
    { initialValue: null },
  );
  protected readonly amountTooLow = computed(
    () => !!this.preview()?.violations.some((v) => v.code === 'amount_too_low'),
  );
  protected readonly breakdown = computed(() => {
    const p = this.preview();
    return p && !this.amountTooLow() ? toBreakdown(p) : null;
  });
  protected readonly cashOk = computed(
    () => !!this.preview() && !this.preview()?.violations.length,
  );
  protected readonly canSend = computed(
    () => this.keyState() === 'found' && this.cashOk() && !this.creating(),
  );
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

  // Misma clave al reintentar la misma solicitud tras 4xx/503; otra si cambian los datos o falló
  private idempotencyKey: string | null = null;

  constructor() {
    // Cambiar el celular borra la validación previa
    this.form.controls.phone.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.keyState.set('idle');
      this.walletMasked.set(null);
      this.inviteSent.set(false);
    });
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => (this.idempotencyKey = null));
  }

  protected validateKey(): void {
    if (!this.canValidate()) return;
    this.keyState.set('checking');
    this.agent
      .validateKey(this.phone())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (key) => {
          this.keyState.set(key ? 'found' : 'missing');
          this.walletMasked.set(key?.walletAddressMasked ?? null);
          this.inviteOpen.set(!key);
        },
        error: () => this.keyState.set('error'),
      });
  }

  protected submit(): void {
    if (this.keyState() !== 'found') return this.validateKey();
    const b = this.breakdown();
    if (!b || !this.canSend()) return;
    const { phone, amount, roundForUser } = this.form.getRawValue();
    this.idempotencyKey ??= newIdempotencyKey();
    this.withdrawals.start(
      {
        phone,
        amount: amount ?? 0,
        roundForUser,
        cashOut: b.cashOut,
        walletIn: b.walletIn,
        commission: b.commission,
      },
      this.idempotencyKey,
    );
  }

  protected cancelRequest(): void {
    this.withdrawals.cancel();
  }

  /** B3.3 Listo: confirma en B4 la entrega del efectivo y vuelve a Caja */
  protected done(): void {
    const w = this.withdrawal();
    if (w?.status !== 'approved') return;
    const total = formatCurrency(w.request.cashOut, this.locale, '$', 'COP', '1.0-0');
    this.confirmation.confirm({
      message: `¿Confirmas que entregaste en efectivo un total de ${total} al usuario?`,
      accept: () => {
        this.handoverError.set(false);
        this.withdrawals
          .handover()
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: () => {
              this.withdrawals.clear();
              void this.router.navigateByUrl('/caja');
            },
            error: () => this.handoverError.set(true),
          });
      },
    });
  }

  /** B3.4: vuelve al formulario con los datos de la solicitud fallida */
  protected retry(): void {
    const w = this.withdrawal();
    if (w?.status !== 'error') return;
    const { phone, amount, roundForUser } = w.request;
    this.withdrawals.clear();
    this.form.setValue({ phone, amount, roundForUser });
    // Después de setValue, que descarta la clave: se reusa solo si no se creó nada
    this.idempotencyKey = w.retryKey;
    this.keyState.set('found');
  }
}
