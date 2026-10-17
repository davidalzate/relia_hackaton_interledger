import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, Subscription, map, switchMap, tap, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import { ApiOperation, FailureCode, OperationSchema } from '../../shared/schemas/operation.schema';
import { ErrorCode } from '../../shared/schemas/problem.schema';
import { AgentBalances, Operation } from '../../shared/types/agent';
import { ApiError, IDEMPOTENCY_HEADER } from '../http/api';
import { AgentService } from './agent.service';
import { AuthService } from './auth.service';
import { OperationsService } from './operations.service';

/** Plazo del contrato para aprobar un retiro (B3.2); el real viene en `expiresAt` */
export const WITHDRAWAL_TTL_MS = 5 * 60_000;

/** Datos del formulario B3.1; el desglose se corrige con lo que devuelve el API */
export interface WithdrawalRequest {
  phone: string;
  amount: number;
  roundForUser: boolean;
  /** Efectivo que entrega el corresponsal */
  cashOut: number;
  /** Lo que paga el usuario desde su billetera */
  walletIn: number;
  commission: number;
}

/** network: no se pudo crear (riel caído, sin respuesta); currency: `422 wallet_currency_unsupported` */
export type WithdrawalErrorKind =
  'rejected' | 'expired' | 'cancelled' | 'failed' | 'cash' | 'currency' | 'network';

const CREATE_ERROR: Partial<Record<ErrorCode, WithdrawalErrorKind>> = {
  insufficient_cash: 'cash',
  wallet_currency_unsupported: 'currency',
};

/** Retiro en curso: B3 muestra la `approvalUrl` como QR para que el usuario la escanee */
interface WithdrawalBase {
  request: WithdrawalRequest;
  operation: Operation;
}
export type Withdrawal =
  | (WithdrawalBase & { status: 'waiting'; expiresAt: number; approvalUrl: string | null })
  | (WithdrawalBase & { status: 'approved'; before: AgentBalances; after: AgentBalances })
  | {
      status: 'error';
      request: WithdrawalRequest;
      /** Null si falló al crear (no hay operación) */
      operation: Operation | null;
      error: WithdrawalErrorKind;
      /** Clave para "Intentar de nuevo": la misma si no se creó nada, null si la operación terminó mal */
      retryKey: string | null;
    };

const FAILURE_KIND: Partial<Record<FailureCode, WithdrawalErrorKind>> = {
  customer_rejected: 'rejected',
  request_expired: 'expired',
  request_cancelled: 'cancelled',
};

/** B3: `POST .../withdrawals` → polling de la operación → entrega del efectivo */
@Injectable({ providedIn: 'root' })
export class WithdrawalService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly agent = inject(AgentService);
  private readonly operations = inject(OperationsService);

  private readonly _withdrawal = signal<Withdrawal | null>(null);
  private readonly _creating = signal(false);
  readonly withdrawal = this._withdrawal.asReadonly();
  /** Mientras responde el `POST` de creación */
  readonly creating = this._creating.asReadonly();
  private tracking?: Subscription;

  start(request: WithdrawalRequest, idempotencyKey: string): void {
    this.tracking?.unsubscribe();
    this._creating.set(true);
    this.tracking = this.http
      .post(
        `${environment.apiUrl}/agents/${this.auth.agentId()}/withdrawals`,
        {
          phone: request.phone,
          requestedAmount: request.amount,
          roundAmount: request.roundForUser,
        },
        { headers: { [IDEMPOTENCY_HEADER]: idempotencyKey } },
      )
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => OperationSchema.parse(res)),
        tap((op) => {
          this._creating.set(false);
          this.update(request, op);
        }),
        // El polling vive aquí (no en la vista) para seguir aunque el corresponsal salga de B3
        switchMap((op) => this.operations.watch(op.id)),
      )
      .subscribe({
        next: (op) => this.update(request, op),
        error: (err: unknown) => {
          this._creating.set(false);
          // Sin operación terminada: reintentar con la misma clave recupera la ya creada, si la hay
          this._withdrawal.set({
            status: 'error',
            request,
            operation: null,
            error: (err instanceof ApiError && err.code && CREATE_ERROR[err.code]) || 'network',
            retryKey: idempotencyKey,
          });
        },
      });
  }

  /** B3.2. Con 409 `operation_not_cancellable` el usuario ya aprobó: el polling sigue */
  cancel(): void {
    const w = this._withdrawal();
    if (w?.status !== 'waiting') return;
    this.operations.cancel(w.operation.id).subscribe({
      next: (op) => this.update(w.request, op),
      error: () => undefined,
    });
  }

  /** B3.3 → B4: registra la entrega del efectivo (auditoría, no cambia saldos) */
  handover(): Observable<ApiOperation> {
    const w = this._withdrawal();
    if (w?.status !== 'approved') throw new Error('No hay un retiro aprobado');
    return this.http
      .post(`${environment.apiUrl}/operations/${w.operation.id}/cash-handover`, null)
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => OperationSchema.parse(res)),
      );
  }

  /** Cierra el flujo (Listo, reintento o cierre de sesión); cancela si seguía esperando */
  clear(): void {
    this.cancel();
    this.tracking?.unsubscribe();
    this._creating.set(false);
    this._withdrawal.set(null);
  }

  private update(request: WithdrawalRequest, op: ApiOperation): void {
    const confirmed = {
      ...request,
      cashOut: op.netAmount,
      walletIn: op.chargedAmount,
      commission: op.fee.agent,
    };
    const operation = this.agent.recordApiOperation(op, {
      phone: request.phone,
      cash: op.netAmount,
      wallet: op.chargedAmount,
      roundForUser: request.roundForUser,
    });
    if (op.status === 'pending') {
      this._withdrawal.set({
        status: 'waiting',
        request: confirmed,
        operation,
        expiresAt: op.expiresAt ? Date.parse(op.expiresAt) : Date.now() + WITHDRAWAL_TTL_MS,
        approvalUrl: op.approvalUrl ?? null,
      });
    } else if (op.status === 'completed' && operation.before && operation.after) {
      this._withdrawal.set({
        status: 'approved',
        request: confirmed,
        operation,
        before: operation.before,
        after: operation.after,
      });
    } else {
      this._withdrawal.set({
        status: 'error',
        request: confirmed,
        operation,
        error: (op.failureCode && FAILURE_KIND[op.failureCode]) ?? 'failed',
        retryKey: null,
      });
    }
  }
}
