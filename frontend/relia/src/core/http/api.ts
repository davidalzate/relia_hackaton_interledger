import { HttpErrorResponse } from '@angular/common/http';
import { Observable, exhaustMap, retry, takeWhile, throwError, timer } from 'rxjs';

import { ErrorCode, Problem, ProblemSchema } from '../../shared/schemas/problem.schema';

/** Error de la Relia API. `code` es null si no hubo problem+json (red caída, proxy, código desconocido) */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: Problem | null,
  ) {
    super(problem?.title ?? `HTTP ${status}`);
  }

  get code(): ErrorCode | null {
    return this.problem?.code ?? null;
  }
}

export function toApiError(error: unknown): unknown {
  if (!(error instanceof HttpErrorResponse)) return error;
  const parsed = ProblemSchema.safeParse(error.error);
  return new ApiError(error.status, parsed.success ? parsed.data : null);
}

const isTransient = (error: unknown) =>
  error instanceof ApiError && (error.status === 0 || error.status >= 500);

export const IDEMPOTENCY_HEADER = 'Idempotency-Key';

/**
 * Una clave por intento del usuario: se reusa al reintentar tras 4xx/503 (no se creó nada)
 * y se pide una nueva en "Intentar de nuevo" tras `failed`, `expired` o `cancelled`.
 */
export const newIdempotencyKey = (): string => crypto.randomUUID();

export const OPERATION_TERMINAL: ReadonlySet<string> = new Set([
  'completed',
  'failed',
  'expired',
  'cancelled',
]);
export const SETTLEMENT_TERMINAL: ReadonlySet<string> = new Set(['paid', 'voided']);
export const POLL_INTERVAL_MS = 1_500;

/** Consulta cada `intervalMs` y emite cada respuesta; completa tras emitir la primera terminal */
export function pollUntil<T>(
  fetch: () => Observable<T>,
  isTerminal: (value: T) => boolean,
  intervalMs = POLL_INTERVAL_MS,
): Observable<T> {
  return timer(0, intervalMs).pipe(
    // Un corte de red no debe abandonar una operación de dinero en curso
    exhaustMap(() =>
      fetch().pipe(
        retry({
          count: 3,
          delay: (error) => (isTransient(error) ? timer(intervalMs) : throwError(() => error)),
        }),
      ),
    ),
    takeWhile((value) => !isTerminal(value), true),
  );
}
