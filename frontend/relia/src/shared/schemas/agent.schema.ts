import * as z from 'zod/mini';

// `Agent` y `AgentBalances` de contracts/relia-api.openapi.yaml (montos en COP enteros)
export const AgentBalancesSchema = z.object({
  quota: z.number(),
  quotaReserved: z.number(),
  cashOnHand: z.number(),
  cashReserved: z.number(),
  accruedCommission: z.number(),
  settledCommissionTotal: z.number(),
});

export const AgentSchema = z.object({
  id: z.string(),
  name: z.string(),
  managerName: z.string(),
  city: z.string(),
  address: z.string(),
  phone: z.string(),
  walletAddress: z.string(),
  balances: AgentBalancesSchema,
});
export type Agent = z.infer<typeof AgentSchema>;

export const HistoryKindSchema = z.enum([
  'deposit',
  'withdrawal',
  'quotaTopUp',
  'cashAdjustment',
  'settlement',
]);
export type HistoryKind = z.infer<typeof HistoryKindSchema>;

const BalancePairSchema = z.object({ before: z.number(), after: z.number() });

export const HistoryEntrySchema = z.object({
  id: z.string(),
  kind: HistoryKindSchema,
  reference: z.optional(z.nullable(z.string())),
  occurredAt: z.string(),
  status: z.enum(['pending', 'completed', 'failed', 'expired', 'cancelled']),
  amount: z.number(),
  balances: z.object({
    quota: BalancePairSchema,
    accruedCommission: BalancePairSchema,
    cashOnHand: BalancePairSchema,
  }),
  reason: z.optional(z.nullable(z.string())),
  operationId: z.optional(z.nullable(z.string())),
  settlementId: z.optional(z.nullable(z.string())),
});
export type HistoryEntry = z.infer<typeof HistoryEntrySchema>;

export const HistoryPageSchema = z.object({
  items: z.array(HistoryEntrySchema),
  nextCursor: z.optional(z.nullable(z.string())),
});

export const CashAdjustmentSchema = z.object({
  id: z.string(),
  mode: z.enum(['add', 'set']),
  variation: z.number(),
  reason: z.string(),
  cashOnHand: BalancePairSchema,
  createdAt: z.string(),
});
