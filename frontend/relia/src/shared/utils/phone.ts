/** Deja solo dígitos (máx. 10) con el formato 300 000 0001 */
export function formatPhone(value: string): string {
  const digits = value.replace(/\D/g, '').slice(0, 10);
  return [digits.slice(0, 3), digits.slice(3, 6), digits.slice(6)].filter(Boolean).join(' ');
}

export const phoneDigits = (value: string): string => value.replace(/\D/g, '');
