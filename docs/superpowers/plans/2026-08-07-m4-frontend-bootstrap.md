---
title: M4 — Frontend Angular 21 + login + pasar lista
doc: superpowers/plans/2026-08-07-m4-frontend-bootstrap
status: en-ejecucion
updated: 2026-08-07
---

# M4 — Frontend Angular 21 + login + pasar lista

**Goal:** Bootstrap del frontend Angular 21 en `client/`, con login funcional
(refresh incluido) y la pantalla crítica de pasar lista ligada a los endpoints
de M3. CI frontend deja de omitirse.

**Architecture:** SPA standalone, signals para estado, servicios por feature,
interceptor con refresh-once. Reutiliza `docs/03 sección 7` (access token en memoria,
refresh en localStorage) y `docs/06` (estructura `core/` + `features/` + `shared/`
+ `layout/`). Consume el backend ya en pie (M1b–M3d, 140 tests verdes).

**Tech Stack:** Angular 21 (standalone, signals, Vitest default), Angular Material,
SCSS, ESLint. Sin SSR. Sin NgRx.

---

## Hitos / Tareas

1. **Bootstrap del proyecto Angular 21** — `ng new client` con flags correctos,
   limpieza de boilerplate, configuración de aliases, paths, proxy.
2. **Angular Material + tema institucional** — paleta sobria, tokens SCSS.
3. **ESLint** — `@angular-eslint` para tener `npm run lint` real.
4. **Modelos `core/`** — espejo 1-a-1 del contrato API (`UsuarioDto`, `MiPerfilDto`,
   `MiParaleloDto`, `SesionDto`, `MarcaAsistenciaDto`, `ApiError`, `Paginado`).
5. **`AuthService` + interceptor + guards** — access token en memoria (signal),
   refresh en localStorage, refresh-once con cola, role guard.
6. **Layouts** — `auth-layout` (centrado, sin chrome) y `main-layout` (topbar +
   sidebar según rol).
7. **Feature `auth/login`** — formulario con Material, error por `codigo`,
   redirige a dashboard.
8. **Feature `docente/mis-paralelos`** — lista de paralelos desde
   `/api/mis-paralelos`, agrupa por nivel/período.
9. **Feature `docente/pasar-lista`** — núcleo de M4: carga nómina, toca-para-cambiar,
   guarda idempotente, indicador "sin guardar", confirmación al salir.
10. **`PeriodosPorNivelService`** — consume `/api/periodos/por-nivel` para filtrar
    los paralelos por período más reciente.
11. **Páginas de error** — `not-found`, `unauthorized` (en construcción para
    inspector).
12. **Tests Vitest** — auth service (login/refresh/logout), interceptor (refresh
    once, error mapping), `PasarLista` component (estado inicial, ciclo, retry).
13. **Verificación** — `npm run lint`, `npm test`, `npm run build` todo verde.
14. **Docs** — actualizar `docs/06`, `docs/07`, `docs/00-roadmap.md`, este plan.

---

## Decisiones arquitectónicas

| Decisión | Alternativa descartada | Por qué |
|---|---|---|
| Angular Material 21 | Tailwind puro | El doc-lineamiento `06` lo manda como base; el equipo ya lo usa en Bienestar |
| Sin NgRx | Con NgRx / Signal Store | YAGNI: estado vive en servicios + signals |
| Access token en memoria | localStorage | `docs/03 sección 7` lo manda así (menor superficie XSS) |
| Refresh en localStorage | Cookie httpOnly | Mientras el backend no emita cookie httpOnly; `docs/03 sección 7` lo deja explícito |
| Refresh-once con cola | Lock simple / mutex | Evita tormenta de refreshes en paralelo |
| `ng-zorro-antd` NO | — | No aplica; Material ya cubre |
| `@angular-eslint` | TSLint deprecado | Estándar actual |
| SCSS | Tailwind | Material + tokens SCSS ya en Bienestar |
| `provideAnimations()` | Noop animations | Necesario para Material; OK para SPA interna |

## Convenciones

- `core/` singletons; `features/` puede importar de `core/` y `shared/`; nunca al revés.
- Aliases: `@core/*`, `@shared/*`, `@features/*`, `@env/*`.
- `OnPush` en todo.
- Sin `any`: `unknown` + narrowing si no queda otra.
- Errores se manejan por `codigo` (`docs/04`), nunca por `mensaje`.
- Tamaño de archivo: máx ~150 LOC en TS de componente; si crece, partir.
- Tests: Vitest, `HttpTestingController`, sin red real.

## Verificación

```bash
cd client
npm ci
npm run lint
npm test -- --watch=false
npm run build
```

Todo verde. Backend: `cd src && dotnet test` (140 tests) sigue verde — el
frontend no toca backend.
