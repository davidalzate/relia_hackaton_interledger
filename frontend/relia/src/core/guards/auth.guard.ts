import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

// Solo UX: la seguridad real la aplica el backend
export const authGuard: CanActivateFn = () =>
  inject(AuthService).authenticated() || inject(Router).createUrlTree(['/login']);
