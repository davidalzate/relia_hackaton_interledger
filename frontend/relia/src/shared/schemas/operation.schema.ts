import * as z from 'zod/mini';

// `Operation` de contracts/relia-api.openapi.yaml: depósito, retiro o carga de cupo
const BalancePairSchema = z.object({ before: z.number(), after: z.number() });

/** `WalletAmount`: monto en la moneda de la billetera de la contraparte; null si es COP */
export const WalletAmountSchema = z.object({ value: z.string(), currency: z.string() });
export type WalletAmount = z.infer<typeof WalletAmountSchema>;

export const FailureCodeSchema = z.enum([
  'customer_rejected',
  'request_expired',
  'request_cancelled',
  'payment_failed',
  'rail_unavailable',
]);
export type FailureCode = z.infer<typeof FailureCodeSchema>;

export const OperationSchema = z.object({
  id: z.string(),
  reference: z.string(),
  agentId: z.string(),
  type: z.enum(['deposit', 'withdrawal', 'quotaTopUp']),
  status: z.enum(['pending', 'completed', 'failed', 'expired', 'cancelled']),
  failureCode: z.optional(z.nullable(FailureCodeSchema)),
  createdAt: z.string(),
  completedAt: z.optional(z.nullable(z.string())),
  expiresAt: z.optional(z.nullable(z.string())),
  requestedAmount: z.number(),
  roundAmount: z.boolean(),
  fee: z.object({ total: z.number(), relia: z.number(), agent: z.number() }),
  netAmount: z.number(),
  chargedAmount: z.number(),
  customer: z.optional(
    z.nullable(z.object({ phoneMasked: z.string(), walletAddressMasked: z.string() })),
  ),
  topUpMethod: z.optional(z.nullable(z.enum(['breB', 'wallet']))),
  approvalUrl: z.optional(z.nullable(z.string())),
  qrPayload: z.optional(z.nullable(z.string())),
  cashHandedOverAt: z.optional(z.nullable(z.string())),
  balances: z.object({
    quota: BalancePairSchema,
    accruedCommission: BalancePairSchema,
    cashOnHand: BalancePairSchema,
  }),
  walletAmount: z.optional(z.nullable(WalletAmountSchema)),
});
export type ApiOperation = z.infer<typeof OperationSchema>;

/** Texto de pantalla por `failureCode` (FLUJOS.md, anexo de estados) */
export const FAILURE_TEXT: Record<FailureCode, string> = {
  customer_rejected: 'El usuario rechazó el pago',
  request_expired: 'La solicitud venció',
  request_cancelled: 'Solicitud cancelada',
  payment_failed: 'El pago falló',
  rail_unavailable: 'El simulador no respondió. No se movió dinero.',
};

export const OperationPreviewSchema = z.object({
  type: z.enum(['deposit', 'withdrawal']),
  requestedAmount: z.number(),
  roundAmount: z.boolean(),
  fee: z.object({ total: z.number(), relia: z.number(), agent: z.number() }),
  netAmount: z.number(),
  chargedAmount: z.number(),
  quotaAfter: z.number(),
  cashOnHandAfter: z.number(),
  accruedCommissionAfter: z.number(),
  violations: z.array(
    z.object({
      code: z.enum(['amount_too_low', 'insufficient_quota', 'insufficient_cash']),
      shortfall: z.optional(z.number()),
    }),
  ),
});
export type OperationPreview = z.infer<typeof OperationPreviewSchema>;

export const KeyLookupSchema = z.object({ phone: z.string(), walletAddressMasked: z.string() });
export type KeyLookup = z.infer<typeof KeyLookupSchema>;

/** `Receipt`: contenido congelado al completarse un depósito o retiro */
export const ReceiptSchema = z.object({
  code: z.string(),
  operationId: z.string(),
  isCopy: z.boolean(),
  issuedAt: z.string(),
  data: z.object({
    reference: z.string(),
    type: z.enum(['deposit', 'withdrawal']),
    date: z.string(),
    merchant: z.object({ name: z.string(), address: z.string(), phone: z.string() }),
    customer: z.object({ phoneMasked: z.string(), walletAddressMasked: z.string() }),
    amounts: z.object({
      requestedAmount: z.number(),
      roundAmount: z.boolean(),
      fee: z.number(),
      netAmount: z.number(),
      chargedAmount: z.number(),
    }),
    payment: z.object({
      rail: z.string(),
      reference: z.string(),
      walletAmount: z.optional(z.nullable(WalletAmountSchema)),
    }),
  }),
});
export type Receipt = z.infer<typeof ReceiptSchema>;
