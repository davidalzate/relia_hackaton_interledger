import * as z from 'zod/mini';

// Enum `ErrorCode` de contracts/relia-api.openapi.yaml
export const ErrorCodeSchema = z.enum([
  'validation_error',
  'unauthorized',
  'invalid_credentials',
  'forbidden',
  'not_found',
  'key_not_found',
  'idempotency_conflict',
  'amount_too_low',
  'insufficient_quota',
  'insufficient_cash',
  'no_change',
  'operation_not_cancellable',
  'operation_not_completed',
  'receipt_not_available',
  'rail_unavailable',
  'rate_limited',
  'key_already_linked',
  'wallet_already_linked',
  'phone_reserved',
  'wallet_not_found',
  'wallet_currency_unsupported',
  'invalid_otp',
  'otp_expired',
  'otp_attempts_exceeded',
  'settlement_in_progress',
  'no_commission',
]);
export type ErrorCode = z.infer<typeof ErrorCodeSchema>;

// application/problem+json (RFC 9457)
export const ProblemSchema = z.object({
  title: z.string(),
  status: z.number(),
  code: ErrorCodeSchema,
  detail: z.optional(z.string()),
  shortfall: z.optional(z.number()),
  attemptsRemaining: z.optional(z.number()),
  errors: z.optional(z.array(z.object({ field: z.string(), message: z.string() }))),
});
export type Problem = z.infer<typeof ProblemSchema>;
