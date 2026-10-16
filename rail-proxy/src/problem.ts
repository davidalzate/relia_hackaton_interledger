// Errores en application/problem+json (RFC 9457) con el `code` estable del contrato.

export type ErrorCode =
  | 'validation_error'
  | 'unauthorized'
  | 'not_found'
  | 'wallet_not_found'
  | 'idempotency_conflict'
  | 'payment_not_cancellable'
  | 'unsupported_asset'
  | 'same_wallet'
  | 'relia_grant_unavailable'
  | 'rail_unavailable'
  | 'too_many_requests'
  | 'internal_error';

export interface Problem {
  type: string;
  title: string;
  status: number;
  code: ErrorCode;
  detail?: string;
}

export const PROBLEM_CONTENT_TYPE = 'application/problem+json';

export class ProblemError extends Error {
  constructor(
    readonly status: number,
    readonly code: ErrorCode,
    readonly title: string,
    readonly detail?: string,
  ) {
    super(detail ?? title);
    this.name = 'ProblemError';
  }

  toProblem(): Problem {
    const problem: Problem = { type: 'about:blank', title: this.title, status: this.status, code: this.code };
    if (this.detail) problem.detail = this.detail;
    return problem;
  }
}
