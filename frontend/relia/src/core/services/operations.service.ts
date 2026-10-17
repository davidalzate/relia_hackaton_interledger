import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, tap, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import { ApiOperation, OperationSchema } from '../../shared/schemas/operation.schema';
import { OPERATION_TERMINAL, pollUntil } from '../http/api';
import { AgentService } from './agent.service';

/** Seguimiento de depósitos, retiros y cargas de cupo creados en el API (estado `pending` → final) */
@Injectable({ providedIn: 'root' })
export class OperationsService {
  private readonly http = inject(HttpClient);
  private readonly agent = inject(AgentService);

  get(id: string): Observable<ApiOperation> {
    return this.http.get(`${environment.apiUrl}/operations/${id}`).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => OperationSchema.parse(res)),
    );
  }

  /**
   * Emite cada consulta y completa al llegar a un estado final; entonces refresca los saldos.
   * Desuscribirse (p. ej. `takeUntilDestroyed` al salir de la pantalla) detiene el polling.
   */
  watch(id: string, intervalMs?: number): Observable<ApiOperation> {
    return pollUntil(
      () => this.get(id),
      (op) => OPERATION_TERMINAL.has(op.status),
      intervalMs,
    ).pipe(
      tap((op) => {
        if (OPERATION_TERMINAL.has(op.status)) {
          this.agent.refresh().subscribe({ error: () => undefined });
        }
      }),
    );
  }

  /**
   * Solo retiros y cargas pendientes antes de que el pagador apruebe. Con 409
   * `operation_not_cancellable` la operación sigue su curso: seguir con `watch`.
   */
  cancel(id: string): Observable<ApiOperation> {
    return this.http.post(`${environment.apiUrl}/operations/${id}/cancel`, null).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => OperationSchema.parse(res)),
    );
  }
}
