import { DOCUMENT, formatCurrency, formatDate } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  LOCALE_ID,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Dialog } from 'primeng/dialog';
import { catchError, map, of, startWith, switchMap } from 'rxjs';

import { AgentService } from '../../../core/services/agent.service';
import { AuthService } from '../../../core/services/auth.service';
import { ReceiptService } from '../../../core/services/receipt.service';
import { Receipt } from '../../schemas/operation.schema';
import {
  OPERATION_LABEL,
  Operation,
  OperationType,
  STATUS_BADGE,
  STATUS_LABEL,
} from '../../types/agent';

const CAPTION: Record<OperationType, string> = {
  deposit: 'enviados a la billetera del usuario',
  withdrawal: 'entregados en efectivo',
  cupo: 'cargados a tu cupo',
  cash: 'agregados a la caja',
  'cash-set': 'nuevo total en caja',
  commission: 'pagados en comisiones',
};

interface Row {
  label: string;
  value: string;
}

/** 300 *** 0001 */
const maskPhone = (digits: string): string => `${digits.slice(0, 3)} *** ${digits.slice(6)}`;

/** Recibo oficial del API: depósitos y retiros completados creados en el servidor (tienen `reference`) */
const hasOfficialReceipt = (op: Operation): boolean =>
  !!op.reference &&
  op.status === 'completed' &&
  (op.type === 'deposit' || op.type === 'withdrawal');

type ReceiptState =
  { status: 'loading' } | { status: 'ok'; receipt: Receipt } | { status: 'error' };

// B9: comprobante de una operación como modal (máx. 400 px); lo abre ReceiptService
@Component({
  selector: 'app-receipt-dialog',
  imports: [Dialog],
  templateUrl: './receipt-dialog.component.html',
  styleUrl: './receipt-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptDialogComponent {
  private readonly receipt = inject(ReceiptService);
  private readonly agent = inject(AgentService);
  private readonly auth = inject(AuthService);
  private readonly locale = inject(LOCALE_ID);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  // Getters y no campos: como campos llegaban undefined al template en el bundle de tests
  // cuando esta spec corre junto a otras (historial, depósito); la app no se ve afectada
  protected get statusBadge(): typeof STATUS_BADGE {
    return STATUS_BADGE;
  }
  protected get statusLabel(): typeof STATUS_LABEL {
    return STATUS_LABEL;
  }

  protected readonly op = computed(() => {
    const id = this.receipt.operationId();
    return id ? (this.agent.operation(id) ?? null) : null;
  });
  /** `GET /operations/{id}/receipt?channel=screen` al abrir (registra la emisión) */
  private readonly official = toSignal(
    toObservable(this.op).pipe(
      switchMap((op) =>
        op && hasOfficialReceipt(op)
          ? this.receipt.get(op.id, 'screen').pipe(
              map((receipt): ReceiptState => ({ status: 'ok', receipt })),
              catchError(() => of<ReceiptState>({ status: 'error' })),
              startWith<ReceiptState>({ status: 'loading' }),
            )
          : of(null),
      ),
    ),
    { initialValue: null },
  );
  protected readonly receiptData = computed(() => {
    const r = this.official();
    return r?.status === 'ok' ? r.receipt : null;
  });
  protected readonly loading = computed(() => this.official()?.status === 'loading');
  protected readonly loadError = computed(() => this.official()?.status === 'error');
  protected readonly isCopy = computed(() => !!this.receiptData()?.isCopy);
  protected readonly store = computed(
    () => this.receiptData()?.data.merchant.name ?? this.auth.user()?.store ?? 'Relia',
  );

  protected readonly title = computed(() => {
    const op = this.op();
    return op ? `Comprobante de ${OPERATION_LABEL[op.type].toLowerCase()}` : '';
  });
  protected readonly mainAmount = computed(() => {
    const op = this.op();
    if (!op) return '';
    const r = this.receiptData();
    if (r) return this.money(r.data.amounts.netAmount);
    const value =
      op.type === 'deposit'
        ? (op.details?.wallet ?? op.amount)
        : op.type === 'withdrawal'
          ? (op.details?.cash ?? op.amount)
          : op.amount;
    return this.money(value);
  });
  protected readonly caption = computed(() => {
    const op = this.op();
    return op ? CAPTION[op.type] : '';
  });
  protected readonly rows = computed(() => {
    const op = this.op();
    const r = this.receiptData();
    return op ? (r ? this.receiptRows(r) : this.buildRows(op)) : [];
  });
  /** SMS · Simulación: solo con recibo oficial (depósito o retiro completado) */
  protected readonly canSms = computed(() => !!this.receiptData());
  /** Operaciones cuyo recibo ya se envió por SMS en esta sesión */
  private readonly smsSentIds = signal<ReadonlySet<string>>(new Set());
  protected readonly smsSent = computed(() => {
    const op = this.op();
    return !!op && this.smsSentIds().has(op.id);
  });
  protected readonly smsError = signal(false);

  protected close(): void {
    this.receipt.close();
  }

  /** Con recibo oficial registra la emisión impresa (`channel=print`) antes de imprimir */
  protected print(): void {
    const op = this.op();
    if (!op || !this.receiptData()) {
      this.document.defaultView?.print();
      return;
    }
    this.receipt
      .get(op.id, 'print')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.document.defaultView?.print(),
        // Imprimir no debe bloquearse si falla el registro
        error: () => this.document.defaultView?.print(),
      });
  }

  /** `POST /operations/{id}/receipt/send` → 202; el SMS llega al teléfono simulado */
  protected sendSms(): void {
    const op = this.op();
    if (!op || !this.canSms()) return;
    this.smsError.set(false);
    this.receipt
      .sendSms(op.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.smsSentIds.update((ids) => new Set(ids).add(op.id)),
        error: () => this.smsError.set(true),
      });
  }

  private receiptRows({ data }: Receipt): Row[] {
    const deposit = data.type === 'deposit';
    const { amounts } = data;
    return [
      { label: 'Fecha', value: formatDate(data.date, 'd MMM y, h:mm a', this.locale) },
      { label: 'Operación', value: data.reference },
      { label: 'Celular', value: data.customer.phoneMasked },
      { label: 'Billetera', value: data.customer.walletAddressMasked },
      {
        label: deposit ? 'Efectivo recibido' : 'Efectivo entregado',
        value: this.money(deposit ? amounts.chargedAmount : amounts.netAmount),
      },
      {
        label: deposit ? 'Enviado a la billetera' : 'Pagado desde la billetera',
        value: this.money(deposit ? amounts.netAmount : amounts.chargedAmount),
      },
      { label: 'Comisión Relia', value: this.money(amounts.fee) },
      {
        label: 'Modalidad',
        value: amounts.roundAmount ? 'Valor redondo (comisión aparte)' : 'Comisión incluida',
      },
      { label: 'Estado', value: STATUS_LABEL.completed },
    ];
  }

  private buildRows(op: Operation): Row[] {
    const d = op.details ?? {};
    const rows: Row[] = [
      { label: 'Fecha', value: formatDate(op.createdAt, 'd MMM y, h:mm a', this.locale) },
      { label: 'Operación', value: op.reference ?? op.id },
    ];
    const balance = (key: 'cupo' | 'cash' | 'commission', label: string) => [
      { label: `${label} antes`, value: op.before ? this.money(op.before[key]) : 'Pendiente' },
      { label: `${label} después`, value: op.after ? this.money(op.after[key]) : 'Pendiente' },
    ];

    if (op.type === 'deposit' || op.type === 'withdrawal') {
      const deposit = op.type === 'deposit';
      rows.push(
        { label: 'Celular', value: d.phone ? maskPhone(d.phone) : '—' },
        { label: 'Billetera', value: 'Oculta' },
        {
          label: deposit ? 'Efectivo recibido' : 'Efectivo entregado',
          value: this.money(d.cash ?? op.amount),
        },
        {
          label: deposit ? 'Enviado a la billetera' : 'Pagado desde la billetera',
          value: this.money(d.wallet ?? op.amount),
        },
        ...(d.cash != null && d.wallet != null
          ? [{ label: 'Comisión Relia', value: this.money(Math.abs(d.wallet - d.cash)) }]
          : []),
        {
          label: 'Modalidad',
          value: d.roundForUser ? 'Valor redondo (comisión aparte)' : 'Comisión incluida',
        },
      );
    } else if (op.type === 'cupo') {
      rows.push(
        { label: 'Método', value: d.method === 'wallet' ? 'Wallet' : 'Bre-B' },
        ...balance('cupo', 'Cupo'),
      );
    } else if (op.type === 'cash' || op.type === 'cash-set') {
      rows.push(...balance('cash', 'Efectivo'));
    } else {
      rows.push(
        { label: 'Destino', value: d.destination ?? '—' },
        ...balance('commission', 'Comisión'),
      );
    }

    rows.push({ label: 'Estado', value: STATUS_LABEL[op.status] });
    if (d.reason) rows.push({ label: 'Motivo', value: d.reason });
    return rows;
  }

  private money(value: number): string {
    return formatCurrency(value, this.locale, '$', 'COP', '1.0-0');
  }
}
