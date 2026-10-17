import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { InputText } from 'primeng/inputtext';

import { ApiError } from '../../core/http/api';
import { AuthService } from '../../core/services/auth.service';
import { Role } from '../../shared/schemas/auth.schema';

const HOME_BY_ROLE: Record<Role, string> = { agent: '/caja', admin: '/admin' };

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, InputText],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  // Precargado con el corresponsal demo
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['rosa@demo.relia', [Validators.required, Validators.email]],
    password: ['demo1234', Validators.required],
  });
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);
    this.auth
      .login(this.form.getRawValue())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (role) => void this.router.navigateByUrl(HOME_BY_ROLE[role]),
        error: (err: unknown) => {
          this.loading.set(false);
          this.error.set(
            err instanceof ApiError && err.code === 'invalid_credentials'
              ? 'Correo o contraseña incorrectos.'
              : 'No pudimos iniciar sesión. Intenta de nuevo.',
          );
        },
      });
  }
}
