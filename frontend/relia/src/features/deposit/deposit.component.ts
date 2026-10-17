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
import { ProgressSpinner } from 'primeng/progressspinner';
import { catchError, debounceTime, distinctUntilChanged, filter, map, of, switchMap } from 'rxjs';

import { createErrorKind, newIdempotencyKey } from '../../core/http/api';
import { AgentService, DepositReceipt } from '../../core/services/agent.service';
import { OperationsService } from '../../core/services/operations.service';
import { ReceiptService } from '../../core/services/receipt.service';
import { InviteDialogComponent } from '../../shared/components/invite-dialog/invite-dialog.component';
import { PhoneFieldComponent } from '../../shared/components/phone-field/phone-field.component';
import { OperationPreview } from '../../shared/schemas/operation.schema';
import { ErrorCode } from '../../shared/schemas/problem.schema';
import { WalletAmountPipe } from '../../shared/utils/wallet-amount';

type KeyState = 'idle' | 'checking' | 'found' | 'missing' | 'error';
/**
 * cupo: `422 insufficient_quota`; currency: `422 wallet_currency_unsupported`;
 * network: riel caído o sin respuesta; failed: `payment_failed`
 */
type DepositErrorKind = 'cupo' | 'currency' | 'network' | 'failed';

const CREATE_ERROR: Partial<Record<ErrorCode, DepositErrorKind>> = {
  insufficient_quota: 'cupo',
  wallet_currency_unsupported: 'currency',
};

/** B2.1 formulario → B2.4 enviando → B2.5 completado | B2.6 error */
type Step =
  | { kind: 'form' }
  | { kind: 'sending'; walletOut: number }
  | { kind: 'done'; receipt: DepositReceipt }
  | { kind: 'error'; error: DepositErrorKind; reference: string | null; amount: number };

/** Lo que muestra B2.1, tomado del desglose del API */
function toBreakdown(p: OperationPreview) {
  return {
    cashIn: p.chargedAmount,
    walletOut: p.netAmount,
    fee: p.fee.total,
    commission: p.fee.agent,
    cupoAfter: p.quotaAfter,
    cashAfter: p.cashOnHandAfter,
    cupoShortfall: p.violations.find((v) => v.code === 'insufficient_quota')?.shortfall ?? 0,
  };
}

const PREVIEW_DEBOUNCE_MS = 300;

// B2: el usuario entrega efectivo y recibe dinero en su billetera
@Component({
  selector: 'app-deposit',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    CurrencyPipe,
    Checkbox,
    InputNumber,
    ProgressSpinner,
    InviteDialogComponent,
    PhoneFieldComponent,
    WalletAmountPipe,
  ],
  templateUrl: './deposit.component.html',
  styleUrl: './deposit.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DepositComponent {
  private readonly agent = inject(AgentService);
  private readonly operations = inject(OperationsService);
  protected readonly receipts = inject(ReceiptService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmation = inject(ConfirmationService);
  private readonly locale = inject(LOCALE_ID);

  private readonly fb = inject(NonNullableFormBuilder);

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
  /** B5: modal de invitación cuando la llave no existe */
  protected readonly inviteOpen = signal(false);
  protected readonly inviteSent = signal(false);
  protected readonly step = signal<Step>({ kind: 'form' });

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
          ? this.agent.previewOperation('deposit', amount, round).pipe(catchError(() => of(null)))
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
  protected readonly cupoOk = computed(
    () => !!this.preview() && !this.preview()?.violations.length,
  );
  protected readonly canConfirm = computed(() => this.keyState() === 'found' && this.cupoOk());
  protected readonly canValidate = computed(
    () => this.phoneComplete() && this.cupoOk() && this.keyState() !== 'checking',
  );

  // Misma clave al reintentar el mismo depósito tras 4xx/503; otra si cambian los datos o el pago falló
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
    const phone = this.phone();
    const { amount, roundForUser } = this.form.getRawValue();
    if (amount == null) return;
    this.idempotencyKey ??= newIdempotencyKey();
    this.step.set({ kind: 'sending', walletOut });
    this.agent
      .createDeposit(
        { phone, requestedAmount: amount, roundAmount: roundForUser },
        this.idempotencyKey,
      )
      .pipe(
        switchMap((created) => this.operations.watch(created.id)),
        filter((op) => op.status !== 'pending'),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (op) => {
          const operation = this.agent.recordApiOperation(op, {
            phone,
            cash: op.chargedAmount,
            wallet: op.netAmount,
            roundForUser,
          });
          if (op.status === 'completed' && operation.before && operation.after) {
            this.step.set({
              kind: 'done',
              receipt: {
                operation,
                walletOut: op.netAmount,
                fee: op.fee.total,
                before: operation.before,
                after: operation.after,
              },
            });
            return;
          }
          // La operación se creó y terminó mal: reintentar es un depósito nuevo
          this.idempotencyKey = null;
          this.step.set({
            kind: 'error',
            error: op.failureCode === 'payment_failed' ? 'failed' : 'network',
            reference: op.reference,
            amount: cashIn,
          });
        },
        error: (err: unknown) =>
          this.step.set({
            kind: 'error',
            error: createErrorKind(err, CREATE_ERROR),
            reference: null,
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
    this.walletMasked.set(null);
    this.step.set({ kind: 'form' });
  }
}
