# 06 — Lineamientos frontend (Angular 21)

---

## Creación del proyecto

```bash
ng new Frontend --standalone --routing --style=scss --ssr=false --skip-tests
cd Frontend
ng add @angular/material
npm i -D vitest jsdom @analogjs/vite-plugin-angular
```

Sin SSR: es un portal interno detrás de login, no necesita SEO ni hidratación.

---

## Estructura

```
src/app/
├── core/                    ← singletons, se cargan una vez
│   ├── auth/
│   │   ├── auth.service.ts
│   │   ├── auth.interceptor.ts
│   │   └── auth.guard.ts / role.guard.ts
│   ├── http/api.service.ts  ← wrapper tipado sobre HttpClient
│   ├── models/              ← User, Rol, ApiError, Paginado<T>
│   └── config/environment.ts
├── features/
│   ├── auth/pages/login/
│   ├── docente/
│   │   ├── pages/mis-paralelos/
│   │   ├── pages/pasar-lista/
│   │   ├── components/
│   │   ├── models/
│   │   └── services/
│   ├── inspector/
│   │   ├── pages/reportes/
│   │   └── ...
│   └── shared-pages/        ← not-found, unauthorized
├── shared/                  ← componentes, pipes y utilidades reutilizables
└── layout/
    ├── main-layout/         ← shell autenticado (sidebar + topbar)
    └── auth-layout/
```

Reglas de import:

- `features/` → puede importar de `core/` y `shared/`.
- `core/` y `shared/` → **nunca** importan de `features/`.
- Una feature **no** importa de otra. Lo común sube a `shared/`.

Alias en `tsconfig.json`: `@core/*`, `@shared/*`, `@features/*`, `@env`.
Nada de `../../../`.

---

## Angular 21: qué se usa y qué no

| Usar | Evitar |
|---|---|
| Componentes standalone | `NgModule` |
| `inject()` | Inyección por constructor |
| `signal()`, `computed()`, `effect()` | `BehaviorSubject` para estado de UI |
| `input()` / `output()` | `@Input()` / `@Output()` decoradores |
| `@if` / `@for` / `@switch` | `*ngIf`, `*ngFor`, `*ngSwitch` |
| `ChangeDetectionStrategy.OnPush` (siempre) | detección por defecto |
| `httpResource` / `resource()` para lecturas | cadenas manuales de `subscribe` |
| Lazy loading con `loadComponent` | rutas eager |

`@for` **siempre** con `track`:

```html
@for (alumno of alumnos(); track alumno.idMatricula) { … }
```

RxJS se sigue usando para eventos y flujos (interceptores, debounce de búsqueda);
signals para estado. No se mezclan: si algo es estado, es signal.

---

## Estado

Sin NgRx. El estado vive en servicios con signals:

```ts
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _usuario = signal<Usuario | null>(null);

  readonly usuario         = this._usuario.asReadonly();
  readonly estaAutenticado = computed(() => this._usuario() !== null);
  readonly roles           = computed(() => this._usuario()?.roles ?? []);
  readonly esInspector     = computed(() => this.roles().includes('cplec_inspector'));
  readonly esDocente       = computed(() => this.roles().includes('cplec_docente'));
}
```

El signal privado es `_x`; se expone solo `asReadonly()`. Ningún componente hace
`.set()` sobre estado ajeno.

---

## Componentes

1. **Máximo ~150 líneas de TS.** Si crece, se parte en subcomponentes.
2. **Sin lógica en la plantilla**: nada de `{{ items.filter(…).length }}` — eso es un
   `computed()`.
3. **Página vs componente**: la página tiene ruta y orquesta datos; el componente recibe
   `input()` y emite `output()`. Un componente presentacional no inyecta servicios de datos.
4. **`OnPush` en todos.**
5. **Accesibilidad**: `label` asociado a cada input, `aria-label` en botones de solo
   icono, foco visible. Pasar lista se usa con teclado — es lo más rápido para el docente.

---

## HTTP

Un servicio por feature, tipado, sin `any`:

```ts
@Injectable({ providedIn: 'root' })
export class AsistenciaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api`;

  misParalelos(idPeriodo?: string): Observable<Paginado<ParaleloDto>> {
    let params = new HttpParams();
    if (idPeriodo) params = params.set('idPeriodo', idPeriodo);
    return this.http.get<Paginado<ParaleloDto>>(`${this.base}/mis-paralelos`, { params });
  }
}
```

- Los DTOs de `core/models` y `features/*/models` **espejan** el contrato de
  [`04-contrato-api.md`](04-contrato-api.md). Si cambia el backend, cambia el modelo.
- `any` está prohibido; usar `unknown` + narrowing si no queda otra.
- Los errores se manejan por `codigo`, nunca por el texto del mensaje.

---

## Rutas y guards

```ts
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('@features/auth/pages/login/login.component') },
  {
    path: '',
    component: MainLayoutComponent,
    canActivate: [authGuard],
    children: [
      {
        path: 'docente',
        canActivate: [roleGuard(['cplec_docente'])],
        loadChildren: () => import('@features/docente/docente.routes'),
      },
      {
        path: 'inspector',
        canActivate: [roleGuard(['cplec_inspector'])],
        loadChildren: () => import('@features/inspector/inspector.routes'),
      },
      { path: '', pathMatch: 'full', redirectTo: 'docente' },
    ],
  },
  { path: '**', loadComponent: () => import('@features/shared-pages/not-found') },
];
```

Los guards son **UX, no seguridad**. Ocultar un botón no protege nada: el backend
revalida rol y distributivo en cada petición.

---

## Interceptor de autenticación

- Adjunta `Authorization: Bearer …` a las peticiones hacia `environment.apiUrl`
  (y solo a esas: no filtrar el token a terceros).
- Ante `401 TOKEN_EXPIRADO`: un **único** intento de refresh, encolando las peticiones
  concurrentes. Si el refresh falla → limpiar sesión y navegar a `/login`.
- Nunca reintenta un `403`: es una decisión del servidor, no un problema transitorio.

---

## Estilos

- Angular Material como base, tema personalizado en `shared/styles/_theme.scss`.
- Tokens (colores, espaciado, tipografía) como variables SCSS. Sin hexadecimales
  sueltos en los componentes.
- Estilos encapsulados por componente. `::ng-deep` solo para sobrescribir Material,
  con un comentario que diga por qué.
- **Móvil primero.** El docente pasa lista desde el teléfono: los controles de
  presente/ausente deben tener área táctil ≥ 44 px.

---

## Pantalla crítica: pasar lista

Es donde se juega la adopción del sistema. Requisitos:

- Carga la nómina completa de una vez (los paralelos son de ~30 alumnos; no paginar).
- Todos arrancan en `presente`; el docente solo marca las excepciones.
- Un toque cambia el estado (presente → ausente → atraso → justificado → presente).
- Guardado explícito con un botón, no autosave por marca: una sola petición idempotente
  con toda la lista.
- Indicador visible de "sin guardar" y confirmación al salir con cambios pendientes.
- Debe funcionar con conexión intermitente: si la petición falla, el estado local se
  conserva y se ofrece reintentar. **No** se pierde la lista marcada.

---

## Calidad

```bash
npm run lint          # ESLint + reglas de Angular
npm run format        # Prettier
npm test              # Vitest
npm run build         # debe compilar sin warnings
```

`npm run build` con warnings no pasa review. Los presupuestos de tamaño se configuran
en `angular.json` y se respetan.
