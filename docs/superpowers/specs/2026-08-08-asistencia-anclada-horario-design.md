# Asistencia anclada al horario — horario del docente y retiro del modo transición

> Fecha: 2026-08-08 · Hito propuesto: **M4c** · Rama: `dv_jb`
> Continúa [`2026-08-07-horarios-inspector-design.md`](2026-08-07-horarios-inspector-design.md)
> y [`2026-08-07-horarios-frontend-design.md`](2026-08-07-horarios-frontend-design.md).
> Modifica [ADR-008](../../adr/ADR-008-horarios-en-tabla-compartida.md) decisión 9.

**Objetivo.** Que el docente vea su horario del periodo vigente y que la asistencia
solo pueda registrarse desde un bloque planificado de ese horario. Hoy la asistencia
todavía puede nacer "por el día actual", sin relación con lo que el inspector planificó.

## 1. Estado de partida

Lo que ya existe y no se rehace:

- **Backend M4b completo** (commit `abfc863`, 247 tests verdes): grid de horarios,
  franjas `Z`, solapamiento, operaciones por rango, `GET /api/paralelos/{id}/agenda`,
  reportes de tardías y días sin registrar.
- `SesionService.CrearAsync` ya ancla al horario: deriva `idFecha` y `numeroBloque` del
  `idHorarioInicio` (`SesionService.cs:106-110`), congela `esTardia`/`diasRetraso` en el
  primer guardado, y rechaza el futuro con `SesionFuturaException`.
- `HorarioRequeridoException` (422 `HORARIO_REQUERIDO`) ya existe y se lanza cuando la
  asignación **sí** tiene horario pero el cliente no mandó el bloque.
- `MisParalelosService` ya implementa la vigencia por ventana de la asignación.
- Frontend: `mis-paralelos`, `pasar-lista` (con store), `agenda` del docente, y las
  páginas de inspector `horarios` / `franjas`.

## 2. Los dos huecos

### H1 — El modo transición es la puerta trasera del "registro por día"

`SesionService.cs:112-117`: si la asignación no tiene ninguna celda activa en
`horario_detalle`, la sesión se crea "libre" a partir de `request.Fecha`. Esa rama es
exactamente el comportamiento que se quiere eliminar: el docente elige el día.

ADR-008 decisión 9 la justificó como red de seguridad del despliegue. **Esta spec la
retira**: decisión explícita del dueño del producto, tomada sabiendo que deja sin
registrar asistencia a todo paralelo sin horario cargado.

### H2 — El docente no tiene una vista de su horario

Existe `GET /api/paralelos/{idAsignacion}/agenda` (un paralelo, rango de fechas) y
`GET /api/horarios/grid` (un paralelo, una semana). No existe nada que responda
"¿qué clases tengo yo esta semana?" sin que el cliente itere sobre sus asignaciones.

## 3. Decisiones

| # | Decisión | Alternativa descartada |
|---|---|---|
| D1 | **Periodo vigente = ventana de la asignación.** `fecha_inicial..fecha_fin` contiene la fecha de referencia, con 15 días de gracia; ventana `NULL` cuenta como vigente. Es la semántica que `MisParalelosService` ya aplica. | `periodos.activo = 1`: está en 1 hasta en periodos de 2022 (CLAUDE.md). |
| D2 | **Bloqueo duro.** Sin horario cargado no hay asistencia → 422 `SIN_HORARIO`. | Mantener el modo transición de ADR-008 §9. |
| D3 | **Registro retroactivo sin tope**, marcado con `esTardia`/`diasRetraso` congelados en el primer guardado. Futuro sigue bloqueado. | Tope de N días con reapertura del inspector: agrega una excepción y un flujo manual que nadie pidió. |
| D4 | **La única entrada a pasar lista es un bloque.** La ruta exige `idHorarioInicio`. | Atajo "bloque de hoy": reintroduce la noción de día actual por la puerta de atrás. |

## 4. Backend

### 4.1 `SinHorarioException` y el retiro del modo transición

Nueva excepción en `Application/Common/Exceptions/`:

```csharp
/// 422 SIN_HORARIO — la asignación no tiene horario planificado, así que no
/// puede registrarse asistencia hasta que el inspector lo cargue.
public sealed class SinHorarioException : AppException
{
    public SinHorarioException()
        : base("SIN_HORARIO", 422,
               "Este paralelo no tiene horario planificado. Pide al inspector que lo cargue.") { }
}
```

`SesionService.CrearAsync` queda con dos ramas de rechazo y una de éxito:

| Situación | Resultado |
|---|---|
| La asignación no tiene ninguna celda `horario_detalle.activo = 1` | `422 SIN_HORARIO` |
| Tiene celdas pero el request no trae `idHorarioInicio` | `422 HORARIO_REQUERIDO` (ya existe) |
| `idHorarioInicio` no pertenece a un bloque de esa asignación | `403 DISTRIBUTIVO_AJENO` (ya existe) |
| Bloque válido | Se crea; `idFecha` y `numeroBloque` salen del bloque |

Efectos:

- `CrearSesionRequestDto.Fecha` y `.NumeroBloque` dejan de participar en la creación.
  **Se conservan en el DTO** por compatibilidad de deserialización, marcados como
  obsoletos en el XML doc; el servicio los ignora. No se rompe ningún cliente que aún
  los envíe: simplemente ya no deciden nada.
- Se mantienen intactas la validación de ventana (`FUERA_DE_VENTANA`), la de futuro
  (`SesionFuturaException`) y la idempotencia por `(idAsignacion, idFecha, numeroBloque)`.
- Las sesiones ya persistidas con `origen: "libre"` siguen legibles, editables,
  cerrables y reabribles. **No se migran ni se ocultan.** El único camino que se cierra
  es el de crear nuevas.

### 4.2 `GET /api/mi-horario`

Rol: `cplec_docente`. El `idProfesor` sale del claim `sub` — nunca del query string.

```
GET /api/mi-horario?desde=2026-08-03&hasta=2026-08-09
```

- Ambos parámetros opcionales. Por defecto, la semana en curso (lunes a domingo)
  calculada con `TimeProvider`.
- Tope: **16 semanas** entre `desde` y `hasta`, coherente con ADR-008 decisión 11 →
  `422 RANGO_EXCEDE_TOPE` si se supera, reutilizando el código que ya emite
  `HorarioRangoService.cs:202`.
- `desde > hasta` → `400 VALIDACION`.

Respuesta: lista plana de bloques, ordenada por fecha y hora de inicio.

```json
[
  {
    "idAsignacion": 41822,
    "tipoLicencia": "C",
    "jornada": "MATUTINA",
    "paralelo": "A",
    "asignatura": "Normativa de tránsito",
    "fecha": "2026-08-04",
    "dia": "MARTES",
    "idHorarioInicio": 90311,
    "numeroBloque": 1,
    "horaInicio": "07:00",
    "horaFin": "09:00",
    "franjasPlanificadas": 2,
    "minutosPlanificados": 120,
    "estado": "Pendiente",
    "idSesion": null,
    "diasRetraso": 4
  }
]
```

`estado` reutiliza `EstadoBloque` (`Pendiente | Borrador | Cerrada | Futura`), ya
definido en `AgendaService.cs:8`.

### 4.3 `MiHorarioService` y el batch de agenda

`Application/Horarios/Services/MiHorarioService.cs`:

1. `IMisParalelosService.ResolverAsync(sub, idPeriodo: null, fechaReferencia: desde)`
   → asignaciones vigentes con su descripción de paralelo. Aquí vive D1; no se
   reimplementa la vigencia.
2. Bloques de esas asignaciones en el rango, en **una** consulta.
3. Se une la descripción del paralelo a cada bloque y se ordena.

Para el paso 2, `AgendaService` gana una sobrecarga batch:

```csharp
Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(
    IReadOnlyCollection<int> idsAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default);
```

`BloqueAgendaDto` gana `IdAsignacion` (hoy no lo trae porque el endpoint por paralelo
lo tiene en la ruta). Es campo agregado, no rompe al cliente actual.

La sobrecarga de un solo `idAsignacion` pasa a delegar en la batch, y
`DiasSinRegistrarAsync` (`AgendaService.cs:139-146`) también — hoy hace una consulta por
asignación dentro de un `foreach`, un N+1 sobre todas las asignaciones de carrera 6 con
horario en el rango. Corregirlo es parte de esta spec porque tocamos ese método.

**Autorización.** El endpoint no recibe `idAsignacion`, así que no puede filtrarse por
`DistributivoGuard`: el alcance lo da el paso 1, que ya está acotado al `sub`. Un test
debe fijar que un docente no ve bloques de asignaciones ajenas aunque compartan paralelo.
Un inspector que llame a este endpoint recibe lista vacía (no tiene distributivo), igual
que `GET /api/auth/me`.

`MiHorarioController` en `Controllers/Horarios/`, registrado en DI junto a los demás
servicios de horarios.

### 4.4 Sin cambios de esquema

No hay `.sql` nuevo en `database/migrations/` ni en `database/rollback/`. `scripts/`
tampoco cambia. La migración 005 ya trajo todo lo que hace falta.

## 5. Frontend

### 5.1 Página `mi-horario`

`client/src/app/features/docente/pages/mi-horario/` — `mi-horario.page.ts` +
`mi-horario.store.ts`, siguiendo el patrón de `pasar-lista` (store `@Injectable()`
provisto por la página, signals privadas con selectores `asReadonly()`).

- Ruta `/mi-horario`, `roleGuard` con `Roles.docente`.
- Grid semanal: filas = franjas horarias distintas de la semana, columnas = días
  lunes-domingo. Celda ocupada muestra paralelo (`C · MATUTINA · A`) y hora.
- Navegación semana anterior / siguiente / "esta semana".
- Color por `estado`: Pendiente (ámbar, con `diasRetraso` si > 0), Borrador (azul),
  Cerrada (verde), Futura (gris).
- Click en bloque con estado ≠ `Futura` → navega a
  `/paralelos/{idAsignacion}/pasar-lista?idHorarioInicio={id}`. Bloque `Futura` no es
  clickeable, con `aria-disabled` y tooltip.
- Estado vacío explícito: "No tienes clases planificadas en esta semana."

Modelo `features/docente/models/mi-horario.model.ts` + servicio
`features/docente/services/mi-horario.service.ts` (`GET /api/mi-horario`).

Entrada en el sidebar (`main-layout.ts:21`): `{ label: 'Mi horario', path: '/mi-horario', icon: 'calendar_month', roles: [Roles.docente] }`, antes de "Mis paralelos".

Redirección por rol (`app.routes.ts:31-38`): el docente aterriza en `/mi-horario`.

### 5.2 `pasar-lista` exige el bloque

- La ruta lee `idHorarioInicio` de query param (`withComponentInputBinding` ya está activo).
- Si falta → redirige a `/paralelos/{idAsignacion}/agenda` con un snackbar:
  "Elige el bloque de clase para pasar lista."
- `pasar-lista.store.cargar` deja de recibir y enviar `fecha`; manda solo
  `idHorarioInicio`. La cabecera muestra la fecha y el horario **del bloque**, no
  `hoyISO()` (`pasar-lista.page.ts:244,296-297`).
- Mapeo de errores por `codigo`: `SIN_HORARIO` → "Este paralelo no tiene horario
  planificado. Pide al inspector que lo cargue."; `HORARIO_REQUERIDO` → el mensaje ya
  existente.

### 5.3 `mis-paralelos`

El botón "Pasar lista" desaparece. Queda "Ver agenda"
(`mis-paralelos.page.ts:98`, que ya existe) como única salida hacia el registro.

## 6. Tests

**Backend** (MSTest + Moq + FluentAssertions):

- `SesionServiceTests`: asignación sin celdas activas → `SIN_HORARIO`; con celdas y sin
  `idHorarioInicio` → `HORARIO_REQUERIDO`; `Fecha` del body contradictoria con el bloque
  → gana el bloque; los tests actuales de modo transición se reescriben para esperar 422.
- `MiHorarioServiceTests`: solo asignaciones vigentes por ventana; ventana `NULL` incluida;
  asignación vencida hace más de 15 días excluida; bloques contiguos agrupados; los cuatro
  estados; aislamiento por `sub`; inspector → lista vacía; rango > 16 semanas →
  `RANGO_EXCEDE_TOPE`; `desde > hasta` → 400.
- `AgendaServiceTests`: la sobrecarga batch devuelve lo mismo que N llamadas individuales.

**Frontend** (Vitest):

- `mi-horario.store.spec.ts`: carga, navegación de semana, agrupación en grid, bloque
  futuro no navegable.
- `pasar-lista` sin `idHorarioInicio` → redirige a la agenda.
- El store manda `idHorarioInicio` y no `fecha`.

## 7. Documentación a actualizar

| Documento | Cambio |
|---|---|
| `docs/04-contrato-api.md` | Nuevo `GET /api/mi-horario`; fila `422 SIN_HORARIO` y `422 HORARIO_REQUERIDO` en la tabla de errores; reescribir §`POST /api/paralelos/{idAsignacion}/sesiones`, que hoy dice "No hay horas ni bloques horarios: el grano es el día" — falso desde M4b. |
| `docs/adr/ADR-008-horarios-en-tabla-compartida.md` | Addendum fechado a la decisión 9: el modo transición se retira; se conserva `idHorarioInicio` NULL-able solo por las filas históricas. |
| `docs/10-navegacion-distributivo.md` | Regla de vigencia (D1) y de anclaje obligatorio (D2). |
| `docs/00-roadmap.md` | Hito **M4c** con su DoD. |
| `CLAUDE.md` | §Estado actual y §Modelo de datos: la asistencia ya no nace por día. |

## 8. Riesgo aceptado

Con D2 en producción, cualquier paralelo sin horario cargado queda sin poder registrar
asistencia: el inspector pasa a ser prerequisito duro del docente. El despliegue debe ir
acompañado de la carga de horarios de los paralelos vigentes, o los docentes ven
`SIN_HORARIO` el primer día. El reporte `GET /api/reportes/dias-sin-registrar` no cubre
este caso — solo ve asignaciones que **sí** tienen horario —, así que la verificación
previa al despliegue es una consulta manual sobre `asignaciones_profesores` vigentes de
carrera 6 sin filas en `horario_detalle`.
