import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { AuthService } from '../services/auth.service';

// Rutas públicas del contrato: no llevan token
const PUBLIC_PATH = /^\/(auth\/login|key-links|demo\/)/;

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).token();
  const { apiUrl } = environment;
  if (!token || !req.url.startsWith(apiUrl) || PUBLIC_PATH.test(req.url.slice(apiUrl.length))) {
    return next(req);
  }
  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
