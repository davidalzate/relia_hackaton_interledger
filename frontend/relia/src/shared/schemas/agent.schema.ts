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
