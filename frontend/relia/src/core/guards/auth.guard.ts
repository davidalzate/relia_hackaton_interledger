import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

// Solo UX: la seguridad real la aplica el backend. `data.role` restringe la ruta a ese rol
export const authGuard: CanActivateFn = (route) => {
  const role = inject(AuthService).role();
  const required = route.data['role'];
  return (
    (role !== null && (!required || role === required)) || inject(Router).createUrlTree(['/login'])
  );
};
