import { Injectable, computed, signal } from '@angular/core';
import { Observable, map, of, throwError, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import {
  LoginRequest,
  LoginResponse,
  LoginResponseSchema,
  Role,
} from '../../shared/schemas/auth.schema';

// Credenciales públicas del prototipo (se muestran como ayuda en el login)
const DEMO_USERS: readonly (LoginRequest & { role: Role })[] = [
  { email: 'rosa@demo.relia', password: 'demo1234', role: 'agent' },
  { email: 'admin@relia.co', password: 'admin1234', role: 'admin' },
];

export class InvalidCredentialsError extends Error {}

@Injectable({ providedIn: 'root' })
export class AuthService {
  // Nunca en localStorage: la sesión vive solo en memoria
  private readonly _token = signal<string | null>(null);
  private readonly _role = signal<Role | null>(null);

  readonly token = this._token.asReadonly();
  readonly role = this._role.asReadonly();
  readonly authenticated = computed(() => this._role() !== null);

  // shortcut: valida contra DEMO_USERS en el navegador; cambiar por
  // inject(HttpClient).post(`${environment.apiUrl}/auth/login`, credentials) cuando exista backend
  login({ email, password }: LoginRequest): Observable<Role> {
    const user = DEMO_USERS.find(
      (u) => u.email === email.trim().toLowerCase() && u.password === password,
    );
    const response$: Observable<unknown> = user
      ? of({ role: user.role })
      : throwError(() => new InvalidCredentialsError());
    return response$.pipe(
      timeout(environment.httpTimeoutMs),
      map((res) => LoginResponseSchema.parse(res)),
      map((res) => this.start(res)),
    );
  }

  clear(): void {
    this._token.set(null);
    this._role.set(null);
  }

  private start({ role, token }: LoginResponse): Role {
    this._token.set(token ?? null);
    this._role.set(role);
    return role;
  }
}
