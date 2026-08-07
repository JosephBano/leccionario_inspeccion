import { inject } from '@angular/core';
import { Router, type ActivatedRouteSnapshot, type CanActivateFn } from '@angular/router';

import { AuthService } from '@core/auth/services/auth.service';
import type { Role } from '@core/auth/models/roles';

/**
 * Guard de rol: aplica una lista de roles permitidos declarados en `route.data['roles']`.
 *
 * UX, NO seguridad — el backend revalida el rol en cada endpoint.
 */
export const roleGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const required = (route.data['roles'] as Role[] | undefined) ?? [];
  if (required.length === 0) {
    return true;
  }

  const tieneAlguno = required.some((rol) => auth.tieneRol(rol));
  if (tieneAlguno) {
    return true;
  }

  return router.createUrlTree(['/unauthorized']);
};
