/**
 * Tiempo restante como m:ss. Se acota a `totalMs` porque el reloj de la vista se refresca
 * cada segundo y, recién creada la solicitud, mostraría un segundo de más (5:01).
 */
export function formatCountdown(leftMs: number, totalMs: number): string {
  const seconds = Math.ceil(Math.min(totalMs, Math.max(0, leftMs)) / 1_000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}
