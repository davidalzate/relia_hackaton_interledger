import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, tap, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import { LoginRequest, LoginResponseSchema } from '../../shared/schemas/auth.schema';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  // Nunca en localStorage: el token vive solo en memoria
  private readonly _token = signal<string | null>(null);
  private readonly _authenticated = signal(false);

  readonly token = this._token.asReadonly();
  readonly authenticated = this._authenticated.asReadonly();

  login(credentials: LoginRequest): Observable<void> {
    return this.http.post(`${environment.apiUrl}/auth/login`, credentials).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => LoginResponseSchema.parse(res)),
      tap(({ token }) => {
        this._token.set(token ?? null);
        this._authenticated.set(true);
      }),
      map(() => undefined),
    );
  }

  clear(): void {
    this._token.set(null);
    this._authenticated.set(false);
  }
}
