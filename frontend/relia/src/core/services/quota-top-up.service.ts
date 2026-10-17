import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Subscription, map, switchMap, tap, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import { ApiOperation, FailureCode, OperationSchema } from '../../shared/schemas/operation.schema';
import { ErrorCode } from '../../shared/schemas/problem.schema';
import { AgentBalances, Operation } from '../../shared/types/agent';
import { IDEMPOTENCY_HEADER, createErrorKind } from '../http/api';
import { AgentService } from './agent.service';
import { AuthService } from './auth.service';
import { OperationsService } from './operations.service';

export type CupoMethod = 'breb' | 'wallet';
/** Plazos del contrato para pagar una carga (B6.3); el real viene en `expiresAt` */
export const CUPO_LOAD_TTL_MS: Record<CupoMethod, number> = {
  breb: 10 * 60_000,
  wallet: 5 * 60_000,
};
const API_METHOD: Record<CupoMethod, 'breB' | 'wallet'> = { breb: 'breB', wallet: 'wallet' };

export interface CupoLoadRequest {
  method: CupoMethod;
  amount: number;
}

/**
 * network: no se pudo crear (riel caído, sin respuesta); currency: Wallet con
 * `422 wallet_currency_unsupported`
 */
export type CupoLoadErrorKind =
  'rejected' | 'expired' | 'cancelled' | 'failed' | 'currency' | 'network';

interface CupoLoadBase {
  request: CupoLoadRequest;
  operation: Operation;
}
/** Carga de cupo en curso (B6.3-B6.5); la comparten B6 y el panel Modo demo */
export type CupoLoad =
  | (CupoLoadBase & {
      status: 'waiting';
      expiresAt: number;
      /** Wallet: donde el corresponsal aprueba en su billetera de prueba */
      approvalUrl: string | null;
      /** Bre-B: contenido del QR simulado */
      qrPayload: string | null;
    })
  | (CupoLoadBase & { status: 'done'; before: AgentBalances; after: AgentBalances })
  | {
      status: 'error';
      request: CupoLoadRequest;
      /** Null si falló al crear (no hay operación) */
      operation: Operation | null;
      error: CupoLoadErrorKind;
      /** Clave para "Intentar de nuevo": la misma si no se creó nada, null si la carga terminó mal */
      retryKey: string | null;
    };

const CREATE_ERROR: Partial<Record<ErrorCode, CupoLoadErrorKind>> = {
  wallet_currency_unsupported: 'currency',
};

const FAILURE_KIND: Partial<Record<FailureCode, CupoLoadErrorKind>> = {
  customer_rejected: 'rejected',
  request_expired: 'expired',
  request_cancelled: 'cancelled',
};

/** B6: `POST .../quota-top-ups` → polling de la operación; Bre-B se completa desde Modo demo */
@Injectable({ providedIn: 'root' })
export class QuotaTopUpService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly agent = inject(AgentService);
  private readonly operations = inject(OperationsService);

  private readonly _load = signal<CupoLoad | null>(null);
  private readonly _creating = signal(false);
  readonly load = this._load.asReadonly();
  /** Mientras responde el `POST` de creación */
  readonly creating = this._creating.asReadonly();
  private tracking?: Subscription;

  start(request: CupoLoadRequest, idempotencyKey: string): void {
    this.tracking?.unsubscribe();
    this._creating.set(true);
    this.tracking = this.http
      .post(
        `${environment.apiUrl}/agents/${this.auth.agentId()}/quota-top-ups`,
        { method: API_METHOD[request.method], requestedAmount: request.amount },
        { headers: { [IDEMPOTENCY_HEADER]: idempotencyKey } },
      )
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => OperationSchema.parse(res)),
        tap((op) => {
          this._creating.set(false);
          this.update(request, op);
        }),
        // El polling vive aquí (no en la vista) para seguir al salir de B6 o abrir el panel demo
        switchMap((op) => this.operations.watch(op.id)),
      )
      .subscribe({
        next: (op) => this.update(request, op),
        error: (err: unknown) => {
          this._creating.set(false);
          this._load.set({
            status: 'error',
            request,
            operation: null,
            error: createErrorKind(err, CREATE_ERROR),
            retryKey: idempotencyKey,
          });
        },
      });
  }

  /** Solo antes de que se apruebe; con 409 `operation_not_cancellable` el polling sigue */
  cancel(): void {
    const l = this._load();
    if (l?.status !== 'waiting') return;
    this.operations.cancel(l.operation.id).subscribe({
      next: (op) => this.update(l.request, op),
      error: () => undefined,
    });
  }

  /** Modo demo: "Simular pago recibido" de una carga Bre-B pendiente */
  simulateBreBPayment(): void {
    const l = this._load();
    if (l?.status !== 'waiting' || l.request.method !== 'breb') return;
    this.http
      .post(`${environment.apiUrl}/demo/quota-top-ups/${l.operation.id}/breb-payment`, null)
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => OperationSchema.parse(res)),
      )
      // 409 si ya no estaba pendiente: el polling trae el estado real
      .subscribe({ next: (op) => this.update(l.request, op), error: () => undefined });
  }

  /** Cierra el flujo (volver a Caja, reintento o cierre de sesión); cancela si seguía esperando */
  clear(): void {
    this.cancel();
    this.tracking?.unsubscribe();
    this._creating.set(false);
    this._load.set(null);
  }

  private update(request: CupoLoadRequest, op: ApiOperation): void {
    const confirmed = { ...request, amount: op.netAmount };
    const operation = this.agent.recordApiOperation(op, { method: request.method });
    if (op.status === 'pending') {
      this._load.set({
        status: 'waiting',
        request: confirmed,
        operation,
        expiresAt: op.expiresAt
          ? Date.parse(op.expiresAt)
          : Date.now() + CUPO_LOAD_TTL_MS[request.method],
        approvalUrl: op.approvalUrl ?? null,
        qrPayload: op.qrPayload ?? null,
      });
    } else if (op.status === 'completed' && operation.before && operation.after) {
      this._load.set({
        status: 'done',
        request: confirmed,
        operation,
        before: operation.before,
        after: operation.after,
      });
    } else {
      this._load.set({
        status: 'error',
        request: confirmed,
        operation,
        error: (op.failureCode && FAILURE_KIND[op.failureCode]) ?? 'failed',
        retryKey: null,
      });
    }
  }
}
