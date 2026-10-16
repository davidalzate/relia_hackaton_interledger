/** Monto en unidades mínimas del riel (`Amount` del contrato). */
export interface Amount {
  value: string;
  assetCode: string;
  assetScale: number;
}

export const amountSchema = {
  type: 'object',
  required: ['value', 'assetCode', 'assetScale'],
  additionalProperties: false,
  properties: {
    value: { type: 'string', pattern: '^[0-9]+$' },
    assetCode: { type: 'string', minLength: 1 },
    assetScale: { type: 'integer', minimum: 0, maximum: 255 },
  },
} as const;

export function sameAsset(a: { assetCode: string; assetScale: number }, b: { assetCode: string; assetScale: number }): boolean {
  return a.assetCode === b.assetCode && a.assetScale === b.assetScale;
}
