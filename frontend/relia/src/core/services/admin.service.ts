import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, tap, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import {
  AgentListSchema,
  Settlement,
  SettlementPageSchema,
  SettlementSchema,
} from '../../shared/schemas/admin.schema';
import { IDEMPOTENCY_HEADER, SETTLEMENT_TERMINAL, pollUntil } from '../http/api';

export interface Totals {
  total: number;
  count: number;
}

/** Fila de D1 */
export interface AgentSummary {
  id: string;
  name: string;
  /** Responsable del punto */
  owner: string;
  city: string;
  /** Payment pointer Interledger al que se pagan las comisiones */
  pointer: string;
  /** Depósitos y retiros completados */
  operations: number;
  deposits: Totals;
  withdrawals: Totals;
  totalMoved: number;
  cupo: number;
  cash: number;
  commissionDue: number;
}

export interface NetworkTotals {
  moved: Totals;
  deposits: Totals;
  withdrawals: Totals;
  commissionDue: number;
  paidToday: number;
}

const EMPTY_TOTALS: NetworkTotals = {
  moved: { total: 0, count: 0 },
  deposits: { total: 0, count: 0 },
  withdrawals: { total: 0, count: 0 },
  commissionDue: 0,
  paidToday: 0,
};

const toTotals = ({ count, amount }: { count: number; amount: number }): Totals => ({
  total: amount,
  count,
});

/** Hoy en Colombia (YYYY-MM-DD), no en la zona del navegador */
export const todayInBogota = (now = new Date()): string =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Bogota' }).format(now);

/** Visión global de la red para el admin (D1) y pago de comisiones (D3) */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  private readonly _agents = signal<AgentSummary[]>([]);
  private readonly _totals = signal<NetworkTotals>(EMPTY_TOTALS);
  private readonly _payments = signal<Settlement[]>([]);
  readonly agents = this._agents.asReadonly();
  readonly totals = this._totals.asReadonly();
  /** Pagos de comisión de hoy, más recientes primero */
  readonly payments = this._payments.asReadonly();

  /** `GET /agents`: filas y tarjetas de D1 en una llamada */
  loadAgents(): Observable<AgentSummary[]> {
    return this.http.get(`${environment.apiUrl}/agents`).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => AgentListSchema.parse(res)),
      map(({ items, totals }) => {
        const agents = items.map((a) => ({
          id: a.id,
          name: a.name,
          owner: a.managerName,
          city: a.city,
          pointer: a.walletAddress,
          operations: a.operations,
          deposits: toTotals(a.deposits),
          withdrawals: toTotals(a.withdrawals),
          totalMoved: a.totalMoved,
          cupo: a.quota,
          cash: a.cashOnHand,
          commissionDue: a.accruedCommission,
        }));
        this._agents.set(agents);
        this._totals.set({
          moved: toTotals(totals.totalMoved),
          deposits: toTotals(totals.deposits),
          withdrawals: toTotals(totals.withdrawals),
          commissionDue: totals.commissionPayable,
          paidToday: totals.commissionPaidToday,
        });
        return agents;
      }),
    );
  }

  /** `GET /settlements?from=<hoy>`: "Pagos de comisión" de D1 */
  loadPayments(): Observable<Settlement[]> {
    return this.http
      .get(`${environment.apiUrl}/settlements`, { params: { from: todayInBogota() } })
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => SettlementPageSchema.parse(res).items),
        tap((items) => this._payments.set(items)),
      );
  }

  /** D3: `POST /agents/{agentId}/settlements` → 202 `pending` */
  createSettlement(agentId: string, idempotencyKey: string): Observable<Settlement> {
    return this.http
      .post(`${environment.apiUrl}/agents/${agentId}/settlements`, null, {
        headers: { [IDEMPOTENCY_HEADER]: idempotencyKey },
      })
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => SettlementSchema.parse(res)),
      );
  }

  getSettlement(id: string): Observable<Settlement> {
    return this.http.get(`${environment.apiUrl}/settlements/${id}`).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => SettlementSchema.parse(res)),
    );
  }

  /** Polling de `GET /settlements/{id}` hasta `paid` o `voided` */
  watchSettlement(id: string, intervalMs?: number): Observable<Settlement> {
    return pollUntil(
      () => this.getSettlement(id),
      (s) => SETTLEMENT_TERMINAL.has(s.status),
      intervalMs,
    );
  }
}
