import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, tap, timeout } from 'rxjs';
import * as z from 'zod/mini';

import { environment } from '../../environments/environment';
import { SmsService } from './sms.service';

export const OTP_TTL_MS = 5 * 60_000;
export const OTP_MAX_ATTEMPTS = 5;

export type OtpResult =
  | { ok: true }
  | { ok: false; reason: 'invalid'; attemptsRemaining: number }
  | { ok: false; reason: 'expired' | 'exceeded' };

export interface PendingLink {
  phone: string;
  walletAddress: string;
}

interface Challenge {
  phone: string;
  code: string;
  expiresAt: number;
  attempts: number;
}

const KeyLinkResponseSchema = z.object({ phone: z.string() });

/**
 * C1: el OTP se simula en el navegador para el demo (no hay endpoint de verificación ni SMS real);
 * el SMS llega al teléfono simulado (C2). La llave se crea en el API con `POST /key-links`
 * solo después de verificar el código.
 */
@Injectable({ providedIn: 'root' })
export class KeyLinkService {
  private readonly http = inject(HttpClient);
  private readonly sms = inject(SmsService);
  private challenge: Challenge | null = null;
  private readonly _pending = signal<PendingLink | null>(null);
  /** Datos del paso 2 en curso: C1 los retoma al volver de "Ver SMS" (C2) */
  readonly pending = this._pending.asReadonly();

  /** "Enviar código" y "Reenviar código": invalida el anterior */
  sendCode(phone: string, walletAddress: string): void {
    this._pending.set({ phone, walletAddress });
    const code = String(crypto.getRandomValues(new Uint32Array(1))[0] % 1_000_000).padStart(6, '0');
    this.challenge = { phone, code, expiresAt: Date.now() + OTP_TTL_MS, attempts: 0 };
    this.sms.send(phone, `Tu código Relia es ${code}. Vence en 5 minutos. No lo compartas.`);
  }

  verify(code: string): OtpResult {
    const c = this.challenge;
    if (!c || Date.now() > c.expiresAt) return { ok: false, reason: 'expired' };
    if (c.attempts >= OTP_MAX_ATTEMPTS) return { ok: false, reason: 'exceeded' };
    // El código sigue válido hasta que `link()` termine: si el API falla, "Verificar" reintenta
    if (code === c.code) return { ok: true };
    c.attempts++;
    const attemptsRemaining = OTP_MAX_ATTEMPTS - c.attempts;
    return attemptsRemaining
      ? { ok: false, reason: 'invalid', attemptsRemaining }
      : { ok: false, reason: 'exceeded' };
  }

  /** `POST /key-links` (público). Errores: 409 `key_already_linked`, `wallet_already_linked`, `phone_reserved`; 422 `wallet_not_found`; 429 */
  link(phone: string, walletAddress: string): Observable<{ phone: string }> {
    return this.http.post(`${environment.apiUrl}/key-links`, { phone, walletAddress }).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => KeyLinkResponseSchema.parse(res)),
      tap(() => this.reset()),
    );
  }

  /** "Cambiar datos" o vinculación terminada */
  reset(): void {
    this.challenge = null;
    this._pending.set(null);
  }
}
