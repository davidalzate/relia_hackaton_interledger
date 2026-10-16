import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../environments/environment';

// Envía la cookie de sesión HttpOnly solo a nuestra API
export const credentialsInterceptor: HttpInterceptorFn = (req, next) =>
  next(req.url.startsWith(environment.apiUrl) ? req.clone({ withCredentials: true }) : req);
