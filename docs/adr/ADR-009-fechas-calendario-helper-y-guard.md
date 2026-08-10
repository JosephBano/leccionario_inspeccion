---
title: ADR-009 — Fechas calendario: helper único en cliente y guard de lunes en backend
doc: adr/ADR-009-fechas-calendario-helper-y-guard
status: aceptado
updated: 2026-08-08
---

# ADR-009 — Fechas calendario: helper único en cliente y guard de lunes en backend

## Contexto

Hallazgo del 2026-08-08 durante el cierre de M4b. La pantalla del inspector de horarios
mostraba "Lunes · 2026-07-07" para una celda con `fecha = 2026-07-07`; esa fecha es
martes. La grilla navegaba un día hacia adelante y las sesiones que aparecían como "del
lunes" no eran del lunes. El error llegaba al usuario final, no se quedaba en consola.

### Causa raíz

El frontend tenía al menos cuatro implementaciones distintas de "qué día es lunes" o
"qué día es hoy en `yyyy-MM-dd`":

- `client/src/app/features/inspector/pages/horarios/horarios.store.ts:getMondayOf`
- `client/src/app/features/docente/pages/agenda/agenda.store.ts:getSundayOf`
- `client/src/app/features/inspector/pages/reportes/reportes.page.ts:getFechaHoy` /
  `getFechaHaceDias`
- `client/src/app/features/docente/pages/mi-horario/mi-horario.store.ts:lunesDe` /
  `aISO` / `sumarDias`

Todas calculan con accesores locales (`getDay`, `getDate`, `setDate`) — eso es correcto,
el día del calendario es un concepto de hora local — pero serializan con
`Date.prototype.toISOString()`, que devuelve UTC. En husos al oeste de UTC (Ecuador,
UTC-5), entre las 19:00 y las 23:59 hora local la medianoche local aún no llegó en UTC, y
`toISOString()` se corre al día siguiente: `getMondayOf(2026-07-07 20:00 local)` produce
`"2026-07-07"` cuando debería ser `"2026-07-06"`. En Europa central (UTC+1/+2) puede
manifestarse el caso inverso entre 00:00 y 02:59 hora local.

El backend amplifica el error en lugar de detectarlo. `HorariosController.Grid` recibe un
`DateOnly lunes` y se lo pasa a `HorarioService.ObtenerGridAsync` sin verificar que sea
lunes. El service etiqueta por índice (`["Lunes", "Martes", ...]`), de modo que el
"martes" que el cliente envió por error aparece como "Lunes" silenciosamente: ninguna
excepción, ningún log, una fecha mal catalogada.

### Reproducción mínima

```ts
// Quito, TZ=America/Guayaquil. 2026-07-07 a las 20:00 local es martes.
getMondayOf(new Date(2026, 6, 7, 20, 0, 0));
// Buggy actual:  "2026-07-07"   ← martes etiquetado como lunes
// Esperado:      "2026-07-06"  ← lunes correcto
```

### Auditoría complementaria

Búsqueda de `toISOString` en `client/src/` (2026-08-08): el único uso fuera de los
cuatro sitios listados es `client/src/app/features/inspector/services/horarios.service.ts:55-67`,
en `validarTopeRango`. Parsea con `new Date('yyyy-MM-dd')`, que el motor trata como
medianoche UTC — pero las comparaciones y diferencias en milisegundos se hacen entre dos
medianoches UTC, así que el resultado **es correcto en cualquier huso**. Auditado, no
se cambia.

## Decisión

### 1. Helper único en `client/src/app/core/utils/fechas.ts`

Un único módulo exporta seis funciones, todas TZ-agnósticas en su contrato: el resultado
de cada una es el día local formateado como `yyyy-MM-dd`, sin información de hora.

| Función | Entrada | Salida |
|---|---|---|
| `fechaLocalAISO(d: Date)` | `Date` | `yyyy-MM-dd` (componentes locales) |
| `lunesDe(fecha: Date \| string)` | `Date` o `yyyy-MM-dd` | lunes local que contiene a `fecha` |
| `domingoDe(fecha: Date \| string)` | igual | domingo local que contiene a `fecha` |
| `hoyEnISO()` | — | hoy, local |
| `haceDiasISO(dias: number)` | entero | hoy menos `dias`, local |
| `sumarDiasISO(fecha: string, dias: number)` | `yyyy-MM-dd`, entero | `fecha + dias`, local |

**Regla absoluta.** Ningún archivo fuera de `core/utils/fechas` puede llamar a
`Date.prototype.toISOString()`. Se enforza con regla ESLint `no-restricted-syntax`
sobre `MemberExpression[property.name='toISOString']` en `client/eslint.config.js`.
Escape hatch: `// eslint-disable-next-line no-restricted-syntax` con comentario, en
code review. La regla es global a propósito: si vuelve a aparecer un segundo uso
indebido, falla en CI antes de llegar a `develop`.

**Construcción de fechas.** Para parsear un `yyyy-MM-dd` se usa
`new Date(\`${iso}T00:00:00\`)` (medianoche **local**, no UTC). La salida se arma con
`getFullYear`, `getMonth() + 1` y `getDate`, padded a dos dígitos. Esta es la
implementación correcta que ya existía en
`client/src/app/features/docente/pages/mi-horario/mi-horario.store.ts:13-31` sin haber
sido reutilizada.

**Ubicación `core/utils/` y no `shared/`.** `core/` aloja infraestructura cross-cutting
(`auth/`, `config/`, `http/`, `models/`); `shared/` tiene presentacionales. El helper es
JS puro, cero dependencias con Angular. La regla de import ya existente — `core` y
`shared` nunca importan de `features` — se cumple en ambas direcciones; la elección es
por convención del repo.

**DST.** El helper solo opera sobre el calendario, no sobre horas. En husos con
transición de horario, una hora inexistente (por ejemplo `02:30` en el salto de
verano) puede normalizarse, pero como el helper invoca al constructor con `T00:00:00`,
ese día siempre es válido. En Ecuador no aplica (no hay DST desde 1993). El día
calculado no cambia por una transición de hora.

### 2. Guard de backend: `LunesGuard`

```
src/Leccionario.Api/Application/Horarios/Services/LunesGuard.cs
```

- Interface `ILunesGuard` con un método: `EnsureEsLunes(DateOnly lunes)`.
- Implementación: si `lunes.DayOfWeek != DayOfWeek.Monday`, lanza `ValidacionException`
  con `codigo = "VALIDACION"` y HTTP 400 vía `ApiExceptionMiddleware`
  ([docs/04-contrato-api.md](../04-contrato-api.md), fila `400 | VALIDACION`).
- Se aplica en `HorarioService.ObtenerGridAsync` al entrar, antes de tocar la BD.
- Registro DI: `AddScoped<ILunesGuard, LunesGuard>()` en `Program.cs`, junto a los
  demás guards de la familia (`HorarioCarreraGuard`, `FranjaZGuard`, `DistributivoGuard`).

**Ubicación en el service, no en el controller.** Por simetría con `HorarioCarreraGuard`,
`FranjaZGuard` y `DistributivoGuard`: mismo patrón en todo `Application/Horarios/`. Un
`ActionFilter` o un `IAuthorizationFilter` global no aplica: la fecha llega como
parámetro de negocio, no como identidad del llamante.

**Rechazo explícito, no arreglo silencioso.** Si el cliente mandó "martes" como si fuera
"lunes", es bug del cliente y debe verse. El guard no recomputa el lunes: valida lo que
el cliente envió. Sin fallback que ajuste.

### 3. Migración — inventario de archivos a tocar

| Archivo | Cambio |
|---|---|
| `client/src/app/core/utils/fechas.ts` | Nuevo |
| `client/src/app/core/utils/fechas.spec.ts` | Nuevo: tests TZ-agnósticos que verifican `lunesDe(2026-07-07 local) === "2026-07-06"` y el resto del contrato |
| `client/src/app/features/inspector/pages/horarios/horarios.store.ts` | Borrar `getMondayOf`, importar `lunesDe` del helper |
| `client/src/app/features/docente/pages/agenda/agenda.store.ts` | Borrar `getSundayOf`, importar `domingoDe` |
| `client/src/app/features/inspector/pages/reportes/reportes.page.ts` | Borrar `getFechaHoy` / `getFechaHaceDias`, importar `hoyEnISO` / `haceDiasISO` |
| `client/src/app/features/docente/pages/mi-horario/mi-horario.store.ts` | **Se desduplica**: borrar `lunesDe` / `aISO` / `sumarDias` locales, importar del helper. Sus tests existentes (lunes anterior / lunes posterior / domingo como fin de semana) se conservan para detectar cualquier cambio de contrato del helper |
| `src/Leccionario.Api/Application/Horarios/Services/LunesGuard.cs` | Nuevo |
| `src/Leccionario.Tests/Horarios/LunesGuardTests.cs` | Nuevo |
| `src/Leccionario.Api/Application/Horarios/Services/HorarioService.cs` | Inyectar y llamar `ILunesGuard` al entrar a `ObtenerGridAsync` |
| `src/Leccionario.Api/Program.cs` | `AddScoped<ILunesGuard, LunesGuard>()` |
| `client/eslint.config.js` | Regla `no-restricted-syntax` para `.toISOString()` |

Once sitios en `client/` se consolidan contra el helper; el resto es backend
(tres archivos) y tooling de linting para bloquear la regresión.

## Consecuencias

**A favor**

- Una sola implementación de "qué día es lunes" en todo el frontend.
- El backend detecta un `lunes` mal calculado por el cliente en vez de devolver basura
  etiquetada como si fuera correcta.
- ESLint bloquea la regresión en CI antes de que llegue a `develop`: ningún
  `.toISOString()` fuera del helper pasa `npm run lint`.
- Tests TZ-agnósticos verifican el contrato "resultado = día local" sin acoplarse a
  Ecuador. La suite cubre husos negativos (Quito), centrales (Londres) y positivos
  (Tokio), cambiando `TZ` del proceso Node en runtime.
- El bug queda cerrado también para `agenda`, `reportes` y `mi-horario`, que tenían
  variantes del mismo error con distintos puntos de rotura.

**En contra**

- **Ecuador no tiene DST.** El riesgo de hora local queda cubierto solo por la suite
  con `TZ` cambiada en CI; una regresión en husos con DST aparecerá en otra corrida, no
  en la del huso del desarrollador.
- Si el usuario cambia la TZ del sistema operativo en medio de una sesión, el "lunes"
  que se muestra puede saltar un día. Es el comportamiento correcto: el calendario es
  la hora local del usuario, no de un huso fijo.
- La regla ESLint `no-restricted-syntax` sobre `.toISOString()` es global. Usos
  legítimos de `toISOString()` para **timestamps** (no fechas calendario) requieren
  `// eslint-disable-next-line no-restricted-syntax` con un comentario explicando el
  caso. En code review se evalúa el escape hatch igual que cualquier otro `disable`.
- El guard del backend rechaza con 400 una entrada que el cliente quizás solo redondeó
  mal. Es deliberado: mejor un error visible que una respuesta etiquetada con el día
  equivocado.

## Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| `date-fns` (~22 KB gzip) | El bug se arregla con ~30 líneas nativas; no se quiere una dependencia nueva para una función |
| `Temporal` (Stage 3, no en TS estable) | Resuelve el problema de raíz con `Temporal.PlainDate`, pero requiere polyfill (~50 KB); se reconsidera cuando Angular lo traiga built-in |
| Mover el cálculo del lunes al backend | El cliente necesita navegar semanas localmente ("docente abre la app un martes y ve el lunes como inicio de su semana actual"); agregar round-trip por cada semana degrada UX y suma latencia |
| Fix inline en cada call-site (sin helper) | Es exactamente lo que ya estaba mal — cuatro implementaciones, cuatro variantes del mismo bug |
| `Intl.DateTimeFormat` con `timeZone: 'America/Guayaquil'` fijo | Acopla a un solo huso; "calendario = hora local del usuario" es deliberado |
| Crear `LunesGuard` en el cliente (rechazo silencioso) | El cliente no sabe si lo que el usuario quería era ese día; el backend es la fuente de verdad institucional |

## Referencias

- [ADR-001 — Sesión de clase propia](ADR-001-sesion-de-clase-propia.md) — tabla
  `cplec_sesiones`, grano día. No afectada por este ADR.
- [ADR-004 — Sin migraciones EF](ADR-004-sin-migraciones-ef.md) — sin implicaciones de
  esquema: este ADR solo cambia código de cliente y código de aplicación.
- [ADR-008 — Horarios del inspector sobre `horario_detalle` compartida](ADR-008-horarios-en-tabla-compartida.md) — el grid del inspector sigue
  etiquetando por índice de día (`["Lunes", "Martes", ...]`); este ADR agrega la
  precondición de "el `lunes` recibido debe ser lunes" antes de que el indexado
  corra. No es enmienda: ADR-008 no se equivoca. Es la capa de defensa del lado
  backend que faltaba aguas arriba del indexado.
- Implementación de referencia ya en el repo, correcta pero no reutilizada:
  `client/src/app/features/docente/pages/mi-horario/mi-horario.store.ts:13-31`.
- Tabla de errores: [docs/04-contrato-api.md](../04-contrato-api.md), sección
  "Formato de error", fila `400 | VALIDACION | DTO inválido`.
- Guards y piezas del mismo estilo en `src/Leccionario.Api/Application/Horarios/Services/`:
  `HorarioCarreraGuard.cs`, `FranjaZGuard.cs`, `DistributivoGuard.cs`,
  `EscrituraSerializable.cs`.
- Cobertura de husos en CI: ejecutar `TZ=America/Guayaquil npm test`,
  `TZ=Europe/Madrid npm test` y `TZ=Asia/Tokyo npm test` para ejercer el contrato
  del helper en cada huso.
