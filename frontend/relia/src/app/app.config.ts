import { registerLocaleData } from '@angular/common';
import localeEsCo from '@angular/common/locales/es-CO';
import {
  ApplicationConfig,
  DEFAULT_CURRENCY_CODE,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';
import { credentialsInterceptor } from '../core/interceptors/credentials.interceptor';
import { authInterceptor } from '../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../core/interceptors/error.interceptor';
import { AuthService } from '../core/services/auth.service';
import { ReliaPreset } from '../shared/theme/relia-preset';

// Montos en COP y fechas en español (CurrencyPipe, DatePipe)
registerLocaleData(localeEsCo);

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: LOCALE_ID, useValue: 'es-CO' },
    { provide: DEFAULT_CURRENCY_CODE, useValue: 'COP' },
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([credentialsInterceptor, authInterceptor, errorInterceptor]),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
    ),
    // Recargar la app no cierra la sesión: el guard ve el rol ya rehidratado
    provideAppInitializer(() => firstValueFrom(inject(AuthService).restore())),
    providePrimeNG({
      theme: { preset: ReliaPreset, options: { darkModeSelector: '.app-dark' } },
      ripple: false,
    }),
  ],
};
