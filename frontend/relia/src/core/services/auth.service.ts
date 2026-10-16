import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, switchMap, tap, throwError, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import {
  LoginRequest,
  LoginResponseSchema,
  MeSchema,
  Role,
  SessionUser,
} from '../../shared/schemas/auth.schema';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  // Nunca en localStorage: la sesión vive solo en memoria
  private readonly _token = signal<string | null>(null);
  private readonly _agentId = signal<string | null>(null);
  private readonly _user = signal<SessionUser | null>(null);

  readonly token = this._token.asReadonly();
  readonly agentId = this._agentId.asReadonly();
  readonly user = this._user.asReadonly();
  readonly role = computed(() => this._user()?.role ?? null);
  readonly authenticated = computed(() => this._user() !== null);

  // El login no trae nombre ni comercio: se completa con `GET /me` antes de entrar
  login({ email, password }: LoginRequest): Observable<Role> {
    return this.http
      .post(`${environment.apiUrl}/auth/login`, { email: email.trim().toLowerCase(), password })
      .pipe(
        map((res) => LoginResponseSchema.parse(res)),
        tap(({ accessToken, agentId }) => {
          this._token.set(accessToken);
          this._agentId.set(agentId ?? null);
        }),
        switchMap(() => this.http.get(`${environment.apiUrl}/me`)),
        map((res) => MeSchema.parse(res)),
        map(({ role, email, name, agent }) => {
          this._user.set({ role, email, name, store: agent?.name });
          return role;
        }),
        timeout(environment.httpTimeoutMs),
        catchError((error: unknown) => {
          this.clear();
          return throwError(() => error);
        }),
      );
  }

  clear(): void {
    this._token.set(null);
    this._agentId.set(null);
    this._user.set(null);
  }
}
