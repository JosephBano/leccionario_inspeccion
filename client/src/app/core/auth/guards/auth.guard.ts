import { inject } from '@angular/core';
import type { CanActivateFn} from '@angular/router';
import { Router } from '@angular/router';

import { AuthService } from '@core/auth/services/auth.service';

/**
 * Guard de autenticación: permite entrar si el usuario tiene sesión.
 *
 * UX, NO seguridad — el backend revalida el JWT en cada petición.
 * Ver `docs/06 sección Rutas y guards`.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  if (auth.hasSession()) {
    try {
      await auth.refresh();
      return true;
    } catch {
      // refresh token no válido o revocado
    }
  }

  return router.createUrlTree(['/login'], {
    queryParams: { redirectTo: state.url },
  });
};

/**
 * Inverso: bloquea /login si ya hay sesión activa.
 */
export const noAuthGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return router.createUrlTree(['/']);
  }

  if (auth.hasSession()) {
    try {
      await auth.refresh();
      return router.createUrlTree(['/']);
    } catch {
      return true;
    }
  }

  return true;
};
