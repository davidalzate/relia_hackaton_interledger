import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap, throwError, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import {
  LoginRequest,
  LoginResponseSchema,
  MeSchema,
  Role,
  SessionUser,
} from '../../shared/schemas/auth.schema';

// sessionStorage y no localStorage: sobrevive a recargar, muere al cerrar la pestaña
const TOKEN_KEY = 'relia.token';

const storage = {
  get: () => {
    try {
      return sessionStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  },
  set: (token: string | null) => {
    try {
      if (token) sessionStorage.setItem(TOKEN_KEY, token);
      else sessionStorage.removeItem(TOKEN_KEY);
    } catch {
      // Sin storage (modo privado): la sesión queda solo en memoria
    }
  },
};

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

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
        timeout(environment.httpTimeoutMs),
        map((res) => LoginResponseSchema.parse(res)),
        tap(({ accessToken }) => this.setToken(accessToken)),
        switchMap(() => this.loadMe()),
        this.clearOnError(),
      );
  }

  /** Al arrancar: si hay token guardado, rehidrata el perfil. Nunca falla; sin sesión emite null */
  restore(): Observable<Role | null> {
    const token = storage.get();
    if (!token) return of(null);
    this._token.set(token);
    return this.loadMe().pipe(
      this.clearOnError(),
      catchError(() => of(null)),
    );
  }

  clear(): void {
    this.setToken(null);
    this._agentId.set(null);
    this._user.set(null);
  }

  private setToken(token: string | null): void {
    this._token.set(token);
    storage.set(token);
  }

  private loadMe(): Observable<Role> {
    return this.http.get(`${environment.apiUrl}/me`).pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => MeSchema.parse(res)),
      map(({ role, email, name, agent }) => {
        this._agentId.set(agent?.id ?? null);
        this._user.set({ role, email, name, store: agent?.name });
        return role;
      }),
    );
  }

  private clearOnError<T>() {
    return catchError<T, Observable<never>>((error: unknown) => {
      this.clear();
      return throwError(() => error);
    });
  }
}
