# Horarios — frontend del inspector y del docente (M4b-7 + M4b-8)

> Contraparte de [`2026-08-07-horarios-inspector-design.md`](2026-08-07-horarios-inspector-design.md)
> (§8 lo esboza) y del plan de backend
> [`2026-08-07-horarios-inspector-backend.md`](../plans/2026-08-07-horarios-inspector-backend.md),
> cerrado en el commit `abfc863`.

**Objetivo:** que el inspector arme el horario de un paralelo desde un grid semanal, y que
el docente vea su agenda y pase lista **desde el bloque de horario**, de modo que la
tardanza que ya calcula el backend signifique algo.

**Stack:** Angular 21 standalone · signals · Material · Vitest.
**Alcance:** M4b-7 (inspector) y M4b-8 (docente + reportes), más una tarea 0 de backend.

## 1. Estado de partida

Lo que ya existe y no se rehace:

- `client/` scaffoldeado (M4): login con refresh, interceptor single-flight, `authGuard`,
  `roleGuard`, `MainLayout` con navegación filtrada por rol, 24 tests verdes.
- Patrón de página establecido en `mis-paralelos.page.ts` y `pasar-lista.page.ts` +
  `pasar-lista.store.ts` (store `@Injectable()` provisto por la página, signals privadas
  con selectores `asReadonly()`, servicios inyectados).
- Backend M4b completo: `/api/franjas`, `/api/horarios/*`, `/api/paralelos/{id}/agenda`,
  `/api/reportes/sesiones-tardias`, `/api/reportes/dias-sin-registrar`, 247 tests verdes.
- `reportes.page.ts` es un stub de 10 líneas ya ruteado en `/reportes` con
  `roleGuard` + `Roles.inspector`.

## 2. Dos huecos detectados al diseñar

### H1 — El inspector no puede obtener los `idAsignacion` de un paralelo (bloqueante)

`CrearCeldaDto` exige `idAsignacion`, pero ningún endpoint accesible al inspector lo
entrega:

- `ParaleloDto` (`ParalelosInspectorService.cs:8`) trae la 5-tupla, licencia, jornada,
  modalidad y **un conteo** `Asignaciones` — no los identificadores.
- `MiParaleloDto` (`DistributivoDtos.cs:39`) sí trae `IdAsignacion`, pero se sirve por
  `GET /api/mis-paralelos`, que es `[Authorize(Roles = "cplec_docente")]` y está acotado
  al `sub` del token.

Sin esto el panel lateral de asignación no se puede construir. Se resuelve con la
**tarea 0** de la sección 4.

### H2 — Las sesiones siguen naciendo libres

`pasar-lista.store.ts:106` llama a `crearSesion(idAsignacion, { fecha, tema })` sin
`idHorarioInicio`, pese a que `CrearSesionRequestDto` ya lo acepta desde M4b-6. Resultado:
toda sesión tiene `origen: "libre"`, y los reportes de tardías y días sin registrar
quedan vacíos de contenido real. **Este spec toca `pasar-lista` para cerrarlo** — decisión
tomada explícitamente, aun sabiendo que es código de M4 ya cerrado y verde.

## 3. Arquitectura

```
features/inspector/
├── models/horario.model.ts            espejo de HorariosDtos.cs + mapa de códigos de error
├── services/franjas.service.ts        GET/POST/PUT/DELETE /api/franjas
├── services/horarios.service.ts       grid · celda · validar-conflicto · 3 ops de rango
├── pages/franjas/franjas.page.ts      CRUD de franjas Z
├── pages/horarios/
│   ├── horarios.page.ts               orquesta selector + grid + panel
│   ├── horarios.store.ts              estado del grid; aquí van los tests
│   └── components/
│       ├── horario-grid.ts            presentacional puro
│       ├── asignacion-panel.ts
│       └── replicar-rango-dialog.ts
└── pages/reportes/
    ├── reportes.page.ts               stub → contenedor con mat-tab-group
    └── components/{tardias-tab,dias-sin-registrar-tab}.ts

features/docente/
├── models/agenda.model.ts
├── services/agenda.service.ts
└── pages/agenda/agenda.page.ts        agenda en lista + grid semanal de solo lectura

shared/paralelo-selector/paralelo-selector.ts
```

### Decisiones de frontera

**`horario-grid` es presentacional puro.** Recibe `franjas`, `dias`, `celdas`,
`modo: 'edicion' | 'lectura'` y `conflictos`; emite `celdaClick`. No inyecta servicios ni
conoce el store. Dos consecuencias que justifican la forma: el docente lo reusa en modo
lectura sin duplicar nada, y se testea sin HTTP.

**`paralelo-selector` vive en `shared/` y no importa de `features/`.** Recibe sus opciones
por input y emite la 5-tupla; quien las carga es la página. Es la regla del repo:
`core/` y `shared/` nunca importan de `features/`.

**El estado navegable vive en la URL.** La 5-tupla y el `lunes` del grid, y el rango
`desde`/`hasta` de los reportes, son query params. El grid queda enlazable y sobrevive a
un F5; el store los lee y los escribe, no los guarda en paralelo.

## 4. Trabajo por pieza

### Tarea 0 — Backend: asignaciones de un paralelo

`GET /api/paralelos/asignaciones?idPeriodo=&idNivel=&idSeccion=&idModalidad=&paralelo=`,
`[Authorize(Roles = "cplec_inspector")]`, en `DistributivoController`.

```csharp
public sealed record AsignacionParaleloDto(
    int IdAsignacion, string Asignatura, string? IdProfesor, string? NombreDocente,
    DateOnly? FechaInicial, DateOnly? FechaFin);
```

Sigue el patrón de `ParalelosInspectorService`: agrupa por la 5-tupla completa (con tres
columnas el join miente, `docs/10`), filtra `activo = 1` — nunca `<> 0` — y normaliza
`paralelo` con `TRIM()`. `fechaInicial`/`fechaFin` pueden ser NULL: 737 asignaciones
activas de carrera 6 no tienen ventana, y el panel debe mostrarlo en vez de ocultarlas.

### Franjas Z

Tabla de `FranjaDto` ordenada por `horaInicio`, con alta, edición y baja. La baja de una
franja con horario activo responde 409 `FRANJA_EN_USO` y se muestra como tal — no como
error genérico. Crear con `horaInicio >= horaFin` se valida en el formulario antes de
enviar.

### Grid del inspector

Filas = franjas Z; columnas = los 7 `DiaGridDto` de la semana. Una columna con
`habilitado: false` se pinta atenuada, no acepta click y expone su `motivo` en tooltip:
es el caso de una fecha ausente en `fechas_horarios`, que el spec de backend documenta
como riesgo abierto (la tabla termina el 2026-12-31). El grid degrada de forma legible;
no inventa el dato.

Navegación ±1 semana reescribiendo el query param `lunes`.

### Creación y edición de celda

1. Click en celda vacía habilitada → `asignacion-panel` con el resultado de la tarea 0.
2. Al elegir una asignación → `POST /api/horarios/validar-conflicto` con `idAsignacion`,
   `idFecha`, `idhora` y, al editar, `idHorarioExcluir` (si no, la fila choca consigo
   misma).
3. Tres desenlaces sobre `ResultadoConflictoDto`:
   - sin conflictos → guardar activo;
   - `bloqueantes.length > 0` → celda en rojo, guardar **deshabilitado**, mensajes
     listados;
   - solo `advertencias` → diálogo ámbar con `idProfesor`, `carrera`, `nivel`, `paralelo`
     y `franjaAjena`, y un botón explícito «Guardar igual» que envía
     `confirmarAdvertencias: true`.

La distinción no es cosmética: bloqueante es un choque que `cplec` puede resolver;
advertencia es contra el horario de otro sistema, que no puede ni debe editar (ADR-008
decisión 5).

Sin drag-and-drop en esta fase, igual que el spec de backend: es la parte más cara del
grid y no cambia lo que el inspector puede hacer.

### Operaciones por rango

Un diálogo con `desde`/`hasta` y las tres operaciones sobre `OperacionRangoDto`:
replicar (`POST /replicar-rango`), actualizar (`PUT /rango`), eliminar (`DELETE /rango`).

Los topes de **16 semanas** y **500 filas** se calculan en el cliente y bloquean el envío
con el mismo texto que devolvería el backend; aun así se maneja el 422
(`RANGO_EXCEDE_TOPE`, `LOTE_EXCEDE_TOPE`) por si el cálculo diverge. El cliente valida
para dar feedback, no para sustituir la validación del servidor.

El resultado se muestra como tabla de `DetalleOperacionDto`. Las filas con
`exitoso: false` — típicamente `FECHA_SIN_CALENDARIO` — se listan como detalle, **no**
como fracaso de la operación: el backend reporta por día y deliberadamente no aborta el
lote. `advertencia` (p. ej. `ASIGNACION_SIN_VENTANA`) se muestra sobre la tabla.

### Agenda del docente

`GET /api/paralelos/{idAsignacion}/agenda?desde=&hasta=`, una semana por vez. Cada
`BloqueAgendaDto` es una tarjeta con `horaInicio`–`horaFin`, `numeroBloque`,
`franjasPlanificadas`/`minutosPlanificados` y estado:

| Estado | Presentación | Acción |
|---|---|---|
| `Pendiente` | destacado | → pasar lista con `idHorarioInicio` |
| `Borrador` | con aviso de sin cerrar | → pasar lista con `idHorarioInicio` |
| `Cerrada` | atenuado, con `diasRetraso` si > 0 | consulta |
| `Futura` | atenuado | ninguna — el backend responde 422 `SESION_FUTURA` |

Debajo, el mismo `horario-grid` en `modo: 'lectura'` con la semana del paralelo. Es lo que
justifica que `DistributivoGuard.EnsureDocenteTieneParaleloAsync` ya esté probado.

### Cambio en `pasar-lista` (H2)

`PasarListaStore.cargar()` acepta `idHorarioInicio: number | null` y lo pasa a
`crearSesion`. La página lo lee del query param que pone la agenda. `mis-paralelos` deja
de enlazar directo a pasar-lista y enlaza a la agenda del paralelo.

Cuando `idHorarioInicio` viene en null y el paralelo **sí** tiene horario ese día, el
backend responde 422 `HORARIO_REQUERIDO`; la página lo traduce a un mensaje que devuelve
al usuario a la agenda. No se abre sesión libre por accidente.

Los tests existentes de `pasar-lista.store.spec.ts` se actualizan, no se borran.

### Reportes del inspector

`reportes.page.ts` pasa de stub a contenedor con `mat-tab-group` y un filtro de rango
`desde`/`hasta` compartido, en query params. Dos pestañas con tabla ordenable:

- **Sesiones tardías** — `SesionTardiaDto`, orden por `diasRetraso` desc.
- **Días sin registrar** — `DiaSinRegistrarDto`, orden por `diasVencido` desc.

M5 añadirá sus reportes como pestañas del mismo contenedor.

### Navegación

`MainLayout.NAV_ITEMS` suma «Horarios» (`/horarios`, `schedule`, inspector) y «Franjas»
(`/franjas`, `more_time`, inspector). Rutas nuevas en `app.routes.ts`, todas lazy y con
`roleGuard`:

| Ruta | Rol |
|---|---|
| `/horarios` | inspector |
| `/franjas` | inspector |
| `/paralelos/:idAsignacion/agenda` | docente |
| `/reportes` (existente, ampliada) | inspector |

## 5. Manejo de errores

Por `codigo`, nunca por `mensaje` (`docs/04` sección *Formato de error*, `docs/06`). Un
único mapa en `horario.model.ts`:

| Código | HTTP | Tratamiento |
|---|---|---|
| `FUERA_DE_ALCANCE` | 403 | mensaje de alcance; no reintentar |
| `FRANJA_NO_PROPIA` | 403 | la franja es del instituto (`tipo='X'`), no editable |
| `CONFLICTO_HORARIO` | 409 | resaltar la celda; no cerrar el panel |
| `FRANJA_EN_USO` | 409 | no se puede desactivar; ofrecer ver el horario |
| `SESION_FUTURA` | 422 | bloque no accionable |
| `HORARIO_REQUERIDO` | 422 | volver a la agenda a elegir bloque |
| `FECHA_SIN_CALENDARIO` | 422 | detalle por día en el resultado del rango |
| `RANGO_INVALIDO` | 422 | error de formulario en `desde`/`hasta` |
| `RANGO_EXCEDE_TOPE` | 422 | recordar el tope de 16 semanas |
| `LOTE_EXCEDE_TOPE` | 422 | recordar el tope de 500 filas |
| `FUERA_DE_VENTANA` | 422 | fuera de `fecha_inicial .. fecha_fin` |

Un código desconocido cae a un mensaje genérico. Nunca se muestra la cadena cruda del
backend: `sigafi_es` tiene datos personales reales y un mensaje de error no es el lugar
donde revisarlos.

## 6. Pruebas

TDD por tarea, patrón de `pasar-lista.store.spec.ts`: store aislado, servicios mockeados,
sin HTTP real. **~12 nuevos → ~36 totales.**

| Archivo | Tests | Casos que no pueden fallar |
|---|---:|---|
| `horarios.store.spec.ts` | 5 | conflicto bloqueante deja `puedeGuardar` en false; advertencia sí permite guardar con confirmación explícita; celda de día `habilitado: false` no abre el panel; cambiar de semana recarga el grid; editar envía su propio `idHorarioExcluir` |
| `horarios.service.spec.ts` | 2 | el query del grid lleva la 5-tupla **completa**; un rango de más de 500 filas se rechaza antes de la petición |
| `franjas.service.spec.ts` | 1 | `FRANJA_EN_USO` se propaga como código, no como texto |
| `horario-grid.spec.ts` | 2 | render bloqueante rojo vs. advertencia ámbar; `modo: 'lectura'` no emite `celdaClick` |
| `agenda.store.spec.ts` | 2 | estado por bloque (`Pendiente`/`Borrador`/`Cerrada`/`Futura`); bloque `Futura` no es navegable |

Se actualizan además los tests existentes de `pasar-lista.store.spec.ts` por el cambio de
firma de `cargar()`.

**DoD:** `cd client && npm test && npm run lint && npm run build` limpios, y
`cd src && dotnet test` verde para la tarea 0.

## 7. Riesgos

| Riesgo | Impacto | Mitigación |
|---|---|---|
| `fechas_horarios` termina el 2026-12-31 | El grid muestra semanas enteras deshabilitadas al cruzar a 2027 | El día deshabilitado explica su motivo. No lo resuelve el frontend; es un dato que hay que cargar |
| Tocar `pasar-lista`, código de M4 ya verde | Regresión en la pantalla más crítica del sistema | El cambio es aditivo (parámetro opcional); los tests existentes se actualizan y siguen corriendo |
| Paralelos sin horario cargado | El docente entra a la agenda y no ve bloques | Estado vacío explícito que lo diga, en vez de una lista en blanco |

## 8. Fuera de alcance

Drag-and-drop en el grid. Exportación XLSX/PDF (es M5). Edición de franjas `tipo='X'`
(son del instituto). Vista mensual del horario.
