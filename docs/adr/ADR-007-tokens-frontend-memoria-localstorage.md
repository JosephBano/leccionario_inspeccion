---
title: ADR-007 — Estrategia de tokens en el frontend (memoria + localStorage)
doc: adr/ADR-007-tokens-frontend-memoria-localstorage
status: aceptado
updated: 2026-08-07
---

# ADR-007 — Estrategia de tokens en el frontend (memoria + localStorage)

## Contexto

El backend emite dos tokens en `POST /api/auth/login`:

- **Access token** (JWT HS256, 8 h). Viaja en `Authorization: Bearer …`.
- **Refresh token** (rotativo, en BD, hash SHA-256). Permite renovar el access.

El frontend necesita decidir **dónde guarda cada uno** durante la sesión del
usuario. Las opciones canónicas son:

| Opción                       | XSS | CSRF | F5 | Multi-tab |
|------------------------------|:---:|:----:|:--:|:---------:|
| Todo en `localStorage`       | Alto | No | Sí | Sí |
| Todo en cookie httpOnly      | No  | Sí | Sí | Depende |
| Access en memoria + refresh en `localStorage` | Bajo* | No | Sí (parcial) | Sí |
| Access en memoria + refresh en cookie httpOnly | Bajo | Sí | Sí | Sí |

\* "Bajo" condicional a que la SPA no tenga otras vías de ejecución XSS.
Mitigado por Material sin `innerHTML`, `DomSanitizer` por defecto y validación
de inputs.

## Decisión

- **Access token** vive en un `signal` dentro de `AuthService` (memoria del
  tab). Se pierde con F5.
- **Refresh token** vive en `localStorage` bajo la clave `cplec.refresh`.

El interceptor (`authInterceptor`) adjunta el access token a cada petición
hacia `environment.apiUrl`. Ante `401`:

1. Llama `POST /api/auth/refresh` con el refresh en `localStorage`.
2. Si el refresh responde, reemplaza ambos tokens y reintenta la petición una
   sola vez.
3. Si el refresh falla, limpia la sesión y empuja a `/login`.

Con F5, el `provideAppInitializer` rehidrata la sesión leyendo el refresh de
`localStorage` y pidiendo `GET /api/auth/me` para reconstruir el `signal` del
usuario. **No** decodifica el JWT en el cliente para extraer datos: esa es
información no confiable y duplica la fuente de verdad.

## Por qué no cookie httpOnly

El backend actual no emite cookies. Migrar a `httpOnly` requiere:

- Cambiar `AuthService.LoginAsync` y `RefreshTokenAsync` para que la respuesta
  venga con `Set-Cookie` y `Secure; SameSite=Strict`.
- Coordinar `CORS` con `AllowCredentials = true`.
- Asegurar que el dev server proxy (`proxy.conf.json`) y el reverse-proxy de
  producción preserven la cookie.

Es una mejora clara, pero **no es bloqueante para M4** y se deja explícita
como evolución en `docs/03 sección 7`.

## Consecuencias

### Positivas

- **Menor superficie XSS**: el único token expuesto a un atacante que lograra
  inyectar JS es el refresh, que solo sirve para pedir un nuevo access y el
  backend puede revocarlo por familia.
- **Sin acoplamiento al backend**: el storage decision es enteramente del
  cliente.
- **Funciona con `proxy.conf.json`** sin tocar headers.

### Negativas

- **F5 cierra la sesión del access token**. El usuario ve un flash del login
  mientras el initializer rehidrata. Es visible pero no destructivo: la URL
  se preserva y el siguiente `GET /api/auth/me` re-restaura.
- **Refresh en `localStorage` sigue siendo legible por JS**, igual que
  cualquier otro frontend que use cookies no-`httpOnly`. La mitigación es que
  el refresh solo sirve para pedir access tokens y la familia se revoca
  automáticamente ante reuso (`docs/03 sección 2`).

### Reversibilidad

Cuando el backend emita cookie `httpOnly`, `AuthService` cambia a:

- `accessToken` sigue en memoria (signal).
- `refreshToken` se lee vía `/api/auth/refresh` (cookie httpOnly no es
  accesible desde JS) y se omite `localStorage`.

El interceptor no necesita cambios — sigue adjuntando `Authorization: Bearer …`.

## Pie

> Última actualización: 2026-08-07 — motivo: cierre de M4 (frontend Angular 21).
> Antes de eso, no había frontend.
