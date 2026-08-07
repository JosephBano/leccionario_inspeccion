import type { HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import type { Observable} from 'rxjs';
import { catchError, from, switchMap, throwError } from 'rxjs';

import { AuthService } from '@core/auth/services/auth.service';
import { environment } from '@env/environment';

let refreshInFlight = false;

/**
 * Interceptor de autenticación.
 *
 * Reglas (`docs/03 sección 7`):
 *  1. Adjunta `Authorization: Bearer …` solo a peticiones hacia `environment.apiUrl`.
 *  2. Ante `401` (cualquiera), intenta refresh UNA sola vez, encolando las peticiones
 *     concurrentes. Si el refresh falla, limpia la sesión y reenvía el error.
 *  3. Nunca reintenta un `403`: es una decisión del servidor, no transitorio.
 */
export const authInterceptor: HttpInterceptorFn = (req, next): Observable<HttpEvent<unknown>> => {
  const auth = inject(AuthService);

  if (!shouldAttachToken(req.url)) {
    return next(req);
  }

  const token = auth.accessToken();
  const authed = token ? withBearer(req, token) : req;

  return next(authed).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401) {
        return handleUnauthorized(err, authed, next, auth);
      }
      return throwError(() => err);
    }),
  );
};

function shouldAttachToken(url: string): boolean {
  return url.startsWith(environment.apiUrl) || url.startsWith('/api');
}

function withBearer(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

function handleUnauthorized(
  err: HttpErrorResponse,
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  auth: AuthService,
): Observable<HttpEvent<unknown>> {
  // No refrescar contra los endpoints de auth — ahí el 401 es legítimo.
  if (req.url.includes('/auth/login') || req.url.includes('/auth/refresh')) {
    return throwError(() => err);
  }

  if (refreshInFlight) {
    // Ya hay un refresh en curso: dejamos que el llamador lo reintente cuando
    // llegue el nuevo access token. Aquí propagamos el error; el refresh exitoso
    // desbloqueará las próximas peticiones.
    return throwError(() => err);
  }

  refreshInFlight = true;
  return from(auth.refresh()).pipe(
    switchMap(() => {
      refreshInFlight = false;
      const token = auth.accessToken();
      if (!token) {
        return throwError(() => err);
      }
      return next(withBearer(req, token));
    }),
    catchError((refreshErr: unknown) => {
      refreshInFlight = false;
      // El refresh falló: limpiamos la sesión y reenviamos el 401 original.
      void auth.logout().catch(() => { /* swallow */ });
      return throwError(() => refreshErr);
    }),
  );
}
