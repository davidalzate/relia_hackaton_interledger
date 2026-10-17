import { formatCurrency, getCurrencySymbol } from '@angular/common';
import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';

import { WalletAmount } from '../schemas/operation.schema';

/**
 * `WalletAmount` del API ("12.34", USD) → "US$ 12,34". Conserva los decimales que trae `value`
 * (la escala de la moneda en el riel); un código desconocido se muestra tal cual.
 */
export function formatWalletAmount({ value, currency }: WalletAmount, locale: string): string {
  const decimals = value.split('.')[1]?.length ?? 0;
  return formatCurrency(
    Number(value),
    locale,
    getCurrencySymbol(currency, 'wide', locale),
    currency,
    `1.${decimals}-${decimals}`,
  );
}

/** `{{ op.walletAmount | walletAmount }}`; vacío si es null (billetera COP) */
@Pipe({ name: 'walletAmount' })
export class WalletAmountPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(amount: WalletAmount | null | undefined): string {
    return amount ? formatWalletAmount(amount, this.locale) : '';
  }
}
