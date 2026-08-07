import type { ApplicationConfig} from '@angular/core';
import { provideAppInitializer, provideBrowserGlobalErrorListeners, inject } from '@angular/core';
import { provideRouter, withComponentInputBinding, withHashLocation } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';

import { routes } from './app.routes';
import { authInterceptor } from '@core/auth/interceptors/auth.interceptor';
import { AuthService } from '@core/auth/services/auth.service';

/**
 * Bootstrap de la app.
 * - Hash location: las rutas no chocan con el proxy `/api` del dev server.
 * - provideAppInitializer: hidrata la sesión al arrancar (si hay refresh token
 *   en localStorage, se revalida contra `/api/auth/me`).
 * - HttpClient con el interceptor de auth: adjunta Bearer y refresca en 401.
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withHashLocation(), withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    provideAppInitializer(async () => {
      const auth = inject(AuthService);
      if (auth.isAuthenticated()) {
        try {
          await auth.cargarMiPerfil();
        } catch {
          // Si falla la hidratación (token revocado), el refresh del interceptor lo maneja.
        }
      }
    }),
  ],
};
