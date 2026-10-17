import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap, throwError, timeout } from 'rxjs';
import * as z from 'zod/mini';

import { environment } from '../../environments/environment';
import { SmsService } from './sms.service';

export interface PendingLink {
  phone: string;
  walletAddress: string;
  challengeId: string;
  /** Moneda de la billetera según el riel (COP, USD…) */
  walletAssetCode: string;
}

const ChallengeSchema = z.object({
  challengeId: z.string(),
  phone: z.string(),
  walletAssetCode: z.string(),
});
const KeyLinkSchema = z.object({ phone: z.string(), walletAddress: z.string() });
export type KeyLink = z.infer<typeof KeyLinkSchema>;
const DemoMessagesSchema = z.object({ items: z.array(z.object({ content: z.string() })) });

/**
 * C1 (pública, sin token): `POST /key-links` crea el desafío y el API manda el OTP por SMS simulado;
 * `POST /key-links/{challengeId}/verify` crea la llave.
 */
@Injectable({ providedIn: 'root' })
export class KeyLinkService {
  private readonly http = inject(HttpClient);
  private readonly sms = inject(SmsService);
  private readonly _pending = signal<PendingLink | null>(null);
  /** Datos del paso 2 en curso: C1 los retoma al volver de "Ver SMS" (C2) */
  readonly pending = this._pending.asReadonly();

  /** "Enviar código" y "Reenviar código" (el API invalida el anterior). Errores: 409 `key_already_linked`, `wallet_already_linked`, `phone_reserved`; 422 `wallet_not_found`; 429; 503. No se rechaza por moneda: trae `walletAssetCode` */
  start(phone: string, walletAddress: string): Observable<void> {
    return this.http.post(`${environment.apiUrl}/key-links`, { phone, walletAddress }).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => ChallengeSchema.parse(res)),
      tap(({ challengeId, walletAssetCode }) =>
        this._pending.set({ phone, walletAddress, challengeId, walletAssetCode }),
      ),
      switchMap(() => this.copyOtpToPhone(phone)),
    );
  }

  /** Errores: 422 `invalid_otp` (con `attemptsRemaining`), `otp_expired`, `otp_attempts_exceeded`; 404; 409 */
  verify(code: string): Observable<KeyLink> {
    const pending = this._pending();
    if (!pending) return throwError(() => new Error('Sin desafío de vinculación en curso'));
    return this.http
      .post(`${environment.apiUrl}/key-links/${pending.challengeId}/verify`, { code })
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => KeyLinkSchema.parse(res)),
        tap(() => this.reset()),
      );
  }

  /** "Cambiar datos" o vinculación terminada */
  reset(): void {
    this._pending.set(null);
  }

  // shortcut: el OTP queda en la bandeja simulada del API (`GET /demo/messages`, solo con DEMO_MODE);
  // se copia al teléfono simulado del front (C2). Sin modo demo el código no llega a C2.
  private copyOtpToPhone(phone: string): Observable<void> {
    return (
      this.http
        // HttpParams deja el '+' sin codificar y el API lo leería como espacio
        .get(`${environment.apiUrl}/demo/messages?phone=${encodeURIComponent(phone)}&limit=1`)
        .pipe(
          timeout(environment.httpTimeoutMs),
          map((res) => DemoMessagesSchema.parse(res).items[0]),
          tap((msg) => msg && this.sms.send(phone, msg.content)),
          map(() => undefined),
          catchError(() => of(undefined)),
        )
    );
  }
}
