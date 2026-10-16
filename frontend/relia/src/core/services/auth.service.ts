import { Injectable, computed, signal } from '@angular/core';
import { Observable, map, of, throwError, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import {
  LoginRequest,
  LoginResponse,
  LoginResponseSchema,
  Role,
  SessionUser,
} from '../../shared/schemas/auth.schema';

// Credenciales públicas del prototipo (se muestran como ayuda en el login)
const DEMO_USERS: readonly (SessionUser & { password: string })[] = [
  {
    email: 'rosa@demo.relia',
    password: 'demo1234',
    role: 'agent',
    name: 'Rosa Gómez',
    store: 'Tienda Doña Rosa',
  },
  { email: 'admin@relia.co', password: 'admin1234', role: 'admin', name: 'Admin Relia' },
];

export class InvalidCredentialsError extends Error {}

@Injectable({ providedIn: 'root' })
export class AuthService {
  // Nunca en localStorage: la sesión vive solo en memoria
  private readonly _token = signal<string | null>(null);
  private readonly _user = signal<SessionUser | null>(null);

  readonly token = this._token.asReadonly();
  readonly user = this._user.asReadonly();
  readonly role = computed(() => this._user()?.role ?? null);
  readonly authenticated = computed(() => this._user() !== null);

  // shortcut: valida contra DEMO_USERS en el navegador; cambiar por
  // inject(HttpClient).post(`${environment.apiUrl}/auth/login`, credentials) cuando exista backend
  login({ email, password }: LoginRequest): Observable<Role> {
    const found = DEMO_USERS.find(
      (u) => u.email === email.trim().toLowerCase() && u.password === password,
    );
    const response$: Observable<unknown> = found
      ? of({ user: { ...found, password: undefined } })
      : throwError(() => new InvalidCredentialsError());
    return response$.pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => LoginResponseSchema.parse(res)),
      map((res) => this.start(res)),
    );
  }

  clear(): void {
    this._token.set(null);
    this._user.set(null);
  }

  private start({ user, token }: LoginResponse): Role {
    this._token.set(token ?? null);
    this._user.set(user);
    return user.role;
  }
}
