import { DOCUMENT, formatCurrency, formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, LOCALE_ID, computed, inject } from '@angular/core';
import { Dialog } from 'primeng/dialog';

import { AgentService } from '../../../core/services/agent.service';
import { AuthService } from '../../../core/services/auth.service';
import { ReceiptService } from '../../../core/services/receipt.service';
import { SmsService } from '../../../core/services/sms.service';
import {
  OPERATION_LABEL,
  Operation,
  OperationType,
  RELIA_FEE,
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
  private readonly sms = inject(SmsService);
  private readonly locale = inject(LOCALE_ID);
  private readonly document = inject(DOCUMENT);

  protected readonly op = computed(() => {
    const id = this.receipt.operationId();
    return id ? (this.agent.operation(id) ?? null) : null;
  });
  protected readonly statusBadge = STATUS_BADGE;
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly store = computed(() => this.auth.user()?.store ?? 'Relia');

  protected readonly title = computed(() => {
    const op = this.op();
    return op ? `Comprobante de ${OPERATION_LABEL[op.type].toLowerCase()}` : '';
  });
  protected readonly mainAmount = computed(() => {
    const op = this.op();
    if (!op) return '';
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
    return op ? this.buildRows(op) : [];
  });
  /** SMS · Simulación solo aplica a depósito y retiro */
  protected readonly canSms = computed(() => {
    const op = this.op();
    return !!op?.details?.phone && (op.type === 'deposit' || op.type === 'withdrawal');
  });
  protected readonly smsSent = computed(() => {
    const op = this.op();
    // Lee `messages` para reaccionar al envío
    return !!op && this.sms.messages().some((m) => m.operationId === op.id);
  });

  protected close(): void {
    this.receipt.close();
  }

  protected print(): void {
    this.document.defaultView?.print();
  }

  protected sendSms(): void {
    const op = this.op();
    const phone = op?.details?.phone;
    if (!op || !phone) return;
    const text =
      op.type === 'deposit'
        ? `Relia: recibiste ${this.money(op.details?.wallet ?? op.amount)} en tu billetera. Comprobante ${op.id}.`
        : `Relia: pagaste ${this.money(op.details?.wallet ?? op.amount)} desde tu billetera y recibiste ${this.money(op.details?.cash ?? op.amount)} en efectivo. Comprobante ${op.id}.`;
    this.sms.send(phone, text, op.id);
  }

  private buildRows(op: Operation): Row[] {
    const d = op.details ?? {};
    const rows: Row[] = [
      { label: 'Fecha', value: formatDate(op.createdAt, 'd MMM y, h:mm a', this.locale) },
      { label: 'Operación', value: op.id },
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
        { label: 'Comisión Relia', value: this.money(RELIA_FEE) },
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
