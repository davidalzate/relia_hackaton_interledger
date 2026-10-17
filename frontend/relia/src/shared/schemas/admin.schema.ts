import * as z from 'zod/mini';

// `AgentList`, `Settlement` y `SettlementPage` de contracts/relia-api.openapi.yaml
const CountAmountSchema = z.object({ count: z.number(), amount: z.number() });

export const AgentListSchema = z.object({
  items: z.array(
    z.object({
      id: z.string(),
      name: z.string(),
      managerName: z.string(),
      city: z.string(),
      walletAddress: z.string(),
      walletSimulated: z.optional(z.boolean()),
      operations: z.number(),
      deposits: CountAmountSchema,
      withdrawals: CountAmountSchema,
      totalMoved: z.number(),
      quota: z.number(),
      cashOnHand: z.number(),
      accruedCommission: z.number(),
    }),
  ),
  totals: z.object({
    totalMoved: CountAmountSchema,
    deposits: CountAmountSchema,
    withdrawals: CountAmountSchema,
    commissionPayable: z.number(),
    commissionPaidToday: z.number(),
  }),
});

export const SettlementSchema = z.object({
  id: z.string(),
  reference: z.string(),
  agentId: z.string(),
  agentName: z.string(),
  walletAddress: z.string(),
  amount: z.number(),
  status: z.enum(['pending', 'paid', 'voided']),
  voidReason: z.optional(z.nullable(z.string())),
  createdAt: z.string(),
  paidAt: z.optional(z.nullable(z.string())),
});
export type Settlement = z.infer<typeof SettlementSchema>;

export const SettlementPageSchema = z.object({
  items: z.array(SettlementSchema),
  nextCursor: z.optional(z.nullable(z.string())),
});
