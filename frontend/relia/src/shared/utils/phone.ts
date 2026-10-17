export interface Country {
  iso: string;
  name: string;
  /** Indicativo sin "+" */
  dial: string;
  /** Dígitos del celular sin indicativo */
  digits: number;
  flag: string;
}

/** Colombia primero: es el país por defecto */
export const COUNTRIES: readonly Country[] = [
  { iso: 'CO', name: 'Colombia', dial: '57', digits: 10, flag: '🇨🇴' },
  { iso: 'MX', name: 'México', dial: '52', digits: 10, flag: '🇲🇽' },
  { iso: 'PE', name: 'Perú', dial: '51', digits: 9, flag: '🇵🇪' },
  { iso: 'CL', name: 'Chile', dial: '56', digits: 9, flag: '🇨🇱' },
  { iso: 'AR', name: 'Argentina', dial: '54', digits: 10, flag: '🇦🇷' },
  { iso: 'EC', name: 'Ecuador', dial: '593', digits: 9, flag: '🇪🇨' },
  { iso: 'BR', name: 'Brasil', dial: '55', digits: 11, flag: '🇧🇷' },
  { iso: 'US', name: 'Estados Unidos', dial: '1', digits: 10, flag: '🇺🇸' },
];
export const DEFAULT_COUNTRY = COUNTRIES[0];

export const countryByIso = (iso: string): Country =>
  COUNTRIES.find((c) => c.iso === iso) ?? DEFAULT_COUNTRY;

export const phoneDigits = (value: string): string => value.replace(/\D/g, '');

/** Solo dígitos (máx. `maxDigits`) agrupados de a 3: "300 000 0001" */
export function formatLocal(value: string, maxDigits = DEFAULT_COUNTRY.digits): string {
  const digits = phoneDigits(value).slice(0, maxDigits);
  return [digits.slice(0, 3), digits.slice(3, 6), digits.slice(6)].filter(Boolean).join(' ');
}

/** E.164 ("+573000000001") si el número tiene los dígitos del país; si no, '' */
export function toE164(country: Country, local: string): string {
  const digits = phoneDigits(local);
  return digits.length === country.digits ? `+${country.dial}${digits}` : '';
}

/** "+573000000001" → Colombia y "3000000001". Sin indicativo conocido: país por defecto y el número entero */
export function splitE164(e164: string): { country: Country; local: string } {
  const digits = phoneDigits(e164);
  const country = COUNTRIES.find(
    (c) => digits.startsWith(c.dial) && digits.length === c.dial.length + c.digits,
  );
  return country
    ? { country, local: digits.slice(country.dial.length) }
    : { country: DEFAULT_COUNTRY, local: digits };
}

/** "+573000000001" → "+57 300 000 0001" */
export function displayPhone(e164: string): string {
  const { country, local } = splitE164(e164);
  return `+${country.dial} ${formatLocal(local, country.digits)}`;
}

/** "+573000000001" → "+57 300 *** 0001" */
export function maskPhone(e164: string): string {
  const { country, local } = splitE164(e164);
  return `+${country.dial} ${local.slice(0, 3)} *** ${local.slice(-4)}`;
}
