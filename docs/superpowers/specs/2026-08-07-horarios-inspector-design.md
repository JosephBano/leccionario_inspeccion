---
title: Horarios del inspector y asistencia anclada al horario — diseño
doc: superpowers/specs/2026-08-07-horarios-inspector-design
status: aprobado
updated: 2026-08-07
hito: M4b
adr: adr/ADR-008-horarios-en-tabla-compartida
---

# Horarios del inspector y asistencia anclada al horario

> Diseño aprobado. Las decisiones y su justificación están en
> [ADR-008](../../adr/ADR-008-horarios-en-tabla-compartida.md); este documento es el
> **qué se construye**. El **cómo y en qué orden** va en el plan de implementación.

## 1. Objetivo

El inspector arma el horario de cada paralelo al inicio del período. La asistencia deja de
crearse en cualquier fecha del rango de la asignación y pasa a colgar de ese horario. El
docente puede registrar días pasados sin pedir permiso, y el sistema deja constancia de
cuánto se demoró.

Se porta el módulo de horarios de `gestion_academica_istpet` — mismo dato, misma base,
organización distinta — adaptándolo a las restricciones de `cplec`.

### Fuera de alcance

Generación automática de horarios, reasignación / recuperación pedagógica, feriados
(`fecha_config`), espacios/aulas y drag-and-drop en el grid. Todos aditivos si se piden.

## 2. Restricciones

1. **Sin `ALTER TABLE` sobre tablas de horarios.** `horario_detalle`, `horas_clases`,
   `fechas_horarios` y `espacios` solo reciben filas.
2. **`cplec` escribe únicamente filas de carrera 6** en `horario_detalle` y filas
   `tipo='Z'` en `horas_clases`.
3. **`idEspacio` siempre NULL.**
4. **`fechas_horarios` es de solo lectura**, se alimenta por fuera.
5. MySQL 5.7: sin CTEs, sin funciones de ventana, sin exclusion constraints.
6. `Domain/Entities/` es generado por EF Core Power Tools; no se edita a mano.
7. **`activo` se compara siempre con `= 1`.** Es `tinyint(4)` nullable en las tres tablas
   y hay datos sucios (H10). Nunca `<> 0`, nunca como booleano truthy.
8. **La replicación nunca toma un rango implícito**, y tiene topes duros (sección 5.6).

## 3. Hallazgos verificados contra `sigafi_es` (dev, 2026-08-07)

Consultado directamente contra la base de desarrollo. Las cuatro incógnitas que bloqueaban
M4b-0 (H1–H4) están resueltas, y la verificación del flujo destapó seis hallazgos más
(H5–H10). Tres cambian el diseño: **H1** deja la correctitud sin respaldo de la base,
**H7** obliga a acotar la replicación, y **H9** elimina cualquier validación por horas.

### H1 — `horario_detalle` **no tiene ningún índice único**

```
PRIMARY KEY (idHorario)
KEY fk_asignacion_horario_idx (idAsignacion)
KEY fk_horario_detalle_espacios1_idx (idEspacio)
KEY fk_horario_detalle_fechas_horarios1_idx (idFecha)
KEY fk_horario_detalle_horas_clases1_idx (idhora)
KEY ix_horario_detalle_fecha_hora_activo (idFecha, idhora, activo)
```

Todas son `KEY`, ninguna es `UNIQUE KEY`. **Las dos fuentes que consultamos estaban
equivocadas**: ADR-001 de este repo afirmaba un `UNIQUE (activo, idEspacio, idAsignacion,
idFecha, idhora)`; el ADR-0012 de gacad afirmaba `UNIQUE (idAsignacion, idFecha, idhora)`.
No existe ninguno de los dos.

Consecuencia: **la base no aporta absolutamente nada** ni a la unicidad ni a la detección
de conflictos. Todo —duplicados y solapamientos— vive en la transacción `Serializable` de
`HorarioService`. La discusión sobre `idEspacio` NULL y `NULL != NULL` queda sin objeto:
no hay índice único que evadir.

Cómo funciona entonces la protección: bajo `Serializable`, InnoDB toma next-key locks
sobre el rango leído de `ix_horario_detalle_fecha_hora_activo`. Dos escrituras
concurrentes de la misma celda se bloquean mutuamente y una muere con deadlock (1213);
el retry la reintenta y en el segundo pase ya ve la fila de la otra. **El retry no es una
mejora de robustez: es el mecanismo.** Sin él hay duplicados.

`ix_horario_detalle_fecha_hora_activo` **ya existe** — el script 004 de gacad ya corrió en
esta base.

### H2 — `horas_clases`: `'Z'` está libre, pero carrera 6 ya tiene franjas `'C'`

```sql
tipo | filas
C    | 54
I    |  7
X    | 11
```

`tipo` es `char(1) DEFAULT NULL`, sin `ENUM` ni `CHECK`: **`'Z'` se puede usar**.

Hallazgo no previsto: **la carrera 6 ya tiene franjas, de tipo `'C'`, segmentadas por
`idSeccion`** (que en esta carrera es la *jornada*):

```
idhora  tipo  hora_inicio  hora_fin  minutos  idCarrera  idSeccion
     3  C     10:00        11:30          90          6          1
    15  C     10:00        11:30          90          6          4
     4  C     11:30        13:00          90          6          1
    16  C     11:30        13:00          90          6          4
    65  C     14:00        15:30          90          6          3
    69  C     14:00        15:30          90          6          4
```

`'C'` no es exclusivo de la carrera 6 (aparece con `idCarrera` 1, 2, 4, 6…, 10), así que
es la convención de otro sistema, no un catálogo propio de la escuela de conducción. Se
mantiene la decisión de crear franjas `'Z'` propias y **no tocar las `'C'`**; se leen para
detectar conflictos como cualquier otro tipo. Ver la nota de la sección 4.3 sobre
`idSeccion`.

### H3 — formato de horas: `varchar(5)`, siempre `HH:MM`

`hora_inicio` y `hora_fin` son `varchar(5)`, y la muestra confirma zero-padding (`07:00`,
`10:00`). El ancho 5 hace imposible `'07:00:00'`, y `'7:00'` no aparece. La comparación
lexicográfica sería correcta, pero **igual se parsea a `TimeOnly` al leer**: ambas columnas
son `DEFAULT NULL` y una franja con hora nula debe descartarse explícitamente en vez de
comparar contra `null`.

**Nombres reales de columna**: `hora_inicio`, `hora_fin`, `numero_hora` — snake_case, no el
camelCase que usan los DTOs de gacad.

### H4 — el calendario está completo, pero se acaba el 2026-12-31

```
desde 2016-11-01 · hasta 2026-12-31 · 3713 filas
```

3 713 es exactamente el número de días del intervalo: **cero huecos**. La replicación por
rango no va a encontrar fechas faltantes dentro de ese período.

Pero el calendario **termina en 146 días**. Un período que se extienda a 2027 no se puede
planificar ni registrar. `fechas_horarios` se alimenta por fuera de `cplec`, así que esto
es una **dependencia operativa, no técnica**: alguien tiene que cargar 2027 antes de que
arranque el primer período que lo cruce. Se refleja en la sección 10 como riesgo del hito.

### H5 — carrera 6 sigue en cero

`horario_detalle` tiene 0 filas para la carrera 6. La suposición sobre la que descansa
toda la frontera de propiedad (ADR-008, sección 2) se mantiene.

Dato de refuerzo: **las 1 740 filas activas de `horario_detalle` usan exclusivamente
franjas `tipo='X'`**, repartidas en 4 carreras (Gastronomía, Educación Básica,
Electrónica, Redes) y 34 fechas distintas entre 2026-05-05 y 2026-09-12. La convención de
gacad es limpia y no colisiona con `'Z'`.

### H6 — el choque de docente entre carreras hoy es cero

Medición del supuesto que originalmente justificaba la tabla compartida:

| | |
|---|---:|
| Docentes en carrera 6 | 421 |
| …que dictan también en otra carrera | 33 |
| …que además tienen horario cargado en otra carrera | 2 |
| …con solapamiento real de fechas | **0** |

Los dos docentes (`…3904`, `…4793`) tienen su actividad de carrera 6 terminada
(2026-04-30 y 2025-05-13) mientras su horario del instituto arranca en 2026-05-09.
Verificado que el `0` no es un artefacto de la consulta: quitando el filtro de fechas el
join devuelve 5 filas, simplemente no hay traslape temporal.

**No cambia la decisión** —la tabla compartida se mantiene, ver ADR-008 decisión 1— pero
sí cambia su justificación: el beneficio de la detección cruzada es potencial, no actual.

### H7 — los módulos no son cortos, y la replicación tiene que acotarse

`CLAUDE.md` afirma "módulos cortos, de 2 a 6 semanas". Sobre los períodos recientes
(`OCC2025`, `JUC2026`, `JEC2026`, `ENE2026`, `MAC2026`, `MAE2026`, 531 asignaciones):

```
min 12 días · max 408 días · promedio 41,2
24 asignaciones > 90 días · 6 > 180 días
```

Los cuatro paralelos vigentes hoy corren **380–408 días** (`idAsignacion` 23634:
2025-11-06 → 2026-12-18). 2-6 semanas es la mediana, no la regla.

Impacto directo: replicar Lun-Vie × 4 franjas sobre las 7 asignaciones vigentes generaría
**6 940 filas** contra las **1 781** que tiene `horario_detalle` completa. De ahí el tope
de la decisión 11 del ADR: rango explícito, máximo 16 semanas y 500 filas por operación.

### H8 — 737 asignaciones activas sin ventana de fechas

`SUM(fecha_inicial IS NULL OR fecha_fin IS NULL)` sobre carrera 6: **2 859** en total,
**737** entre las activas (`activo = 1`). El diseño acotaba la replicación a
`fecha_inicial .. fecha_fin` y `SesionService` valida contra esa ventana; para esas 737 no
existe. Comportamiento definido en la sección 5.6.

### H9 — no hay carga horaria

`numeroHoras` es NULL en las **20 780** asignaciones de carrera 6.
`horasPracticoExperimental` es `0.00` en las 20 780. Ver ADR-008 decisión 12: no se valida
cobertura de horas y `minutosPlanificados` se calcula desde las franjas.

### H10 — `activo` no es un booleano confiable

`asignaciones_profesores.activo` es `tinyint(4)` y **existe una fila de carrera 6 con
valor `11`**. Regla de implementación: comparar siempre `activo = 1`, nunca `activo <> 0`
ni `activo` como truthy. Aplica igual a `horario_detalle.activo` y `horas_clases.activo`,
que también son `tinyint(4)` nullable.

## 4. Modelo de datos

### 4.1 Migración `004_cplec_horarios_indices_rbac.sql`

Sin cambios estructurales. Idempotente.

- `ix_horario_detalle_fecha_hora_activo (idFecha, idhora, activo)` — **ya existe** (H1), el
  script lo detecta y no hace nada. Se conserva en la migración para que la base quede
  reproducible desde cero sin depender de que `gestion_academica` haya corrido antes.
- `ix_fechas_horarios_fecha (fecha)` — verificar si existe; mismo tratamiento.
- Módulo RBAC `horarios` para el sistema `cplec`:
  - `cplec_inspector` → ver, crear, editar, eliminar
  - `cplec_docente` → **solo ver**

**No se crea ningún índice único** sobre `horario_detalle`, aunque H1 muestre que no hay
ninguno y que eso deja la unicidad sin respaldo. Crearlo cambiaría la estructura de una
tabla compartida con 1 658 filas de otros sistemas, y un `UNIQUE` sobre datos que nunca lo
tuvieron puede fallar por duplicados preexistentes ajenos. Si se quisiera, es una decisión
que le corresponde a quien gobierna la tabla, no a `cplec`.

Rollback en `database/rollback/004_..._rollback.sql`: **no borra los índices** (los usa
`gestion_academica`), solo revierte los grants RBAC.

### 4.2 Migración `005_cplec_sesiones_horario.sql`

`ALTER` sobre tabla propia, hoy vacía en desarrollo.

```sql
ALTER TABLE cplec_sesiones
  ADD COLUMN idHorarioInicio     INT(11)     NULL,
  ADD COLUMN franjasPlanificadas TINYINT(4)  NULL,
  ADD COLUMN minutosPlanificados SMALLINT(6) NULL,
  ADD COLUMN esTardia            TINYINT(1)  NOT NULL DEFAULT 0,
  ADD COLUMN diasRetraso         SMALLINT(6) NOT NULL DEFAULT 0,
  ADD CONSTRAINT fk_cplec_sesiones_horario
    FOREIGN KEY (idHorarioInicio) REFERENCES horario_detalle (idHorario)
    ON DELETE NO ACTION ON UPDATE NO ACTION,
  ADD INDEX ix_cplec_sesiones_tardias (esTardia, idFecha);
```

`idHorarioInicio` es NULL-able por el modo transición (sección 6.4). `minutosPlanificados` en vez
de "horas": con franjas de duración libre, contar franjas miente.

### 4.3 Filas que `cplec` escribe en tablas compartidas

`horas_clases` (franja Z):

| Columna | Valor |
|---|---|
| `tipo` | `'Z'` fijo |
| `idCarrera`, `idSeccion` | NULL fijo |
| `hora_inicio`, `hora_fin` | `varchar(5)`, `HH:MM` zero-padded, validado al escribir |
| `minutos` | derivado de `hora_fin - hora_inicio`, no lo teclea el inspector |
| `numero_hora` | orden dentro del catálogo Z |
| `activo` | 1; el borrado es lógico |

> **Nota sobre `idSeccion`.** H2 muestra que las franjas `'C'` de la carrera 6 sí están
> segmentadas por jornada (`idSeccion` 1, 3, 4) y que las horas difieren entre jornadas.
> Las franjas `'Z'` van con `idSeccion` NULL: el catálogo es plano y el inspector elige en
> el grid la franja que corresponde a cada paralelo. El costo es cosmético — el grid de un
> paralelo matutino muestra también las franjas nocturnas. Poblar `idSeccion` es aditivo y
> no rompe nada si más adelante se quiere filtrar.

`horario_detalle` (celda de horario):

| Columna | Valor |
|---|---|
| `idAsignacion` | de carrera 6, verificado por `HorarioCarreraGuard` |
| `idFecha` | de `fechas_horarios`, debe existir |
| `idhora` | franja `tipo='Z'`, verificado por `FranjaZGuard` |
| `idEspacio` | NULL fijo |
| `tipoBloque` | `enum('teorico','practico','taller')`; `cplec` usa `teorico` y `practico` |
| `activo` | 1; el borrado es lógico |
| `claseReasignacion`, `esRecuperacionPedagocia`, `idHorarioReasgincacion` | NULL — fuera de alcance |

## 5. Componentes de backend

```
Application/Horarios/
├── HorariosDtos.cs
└── Services/
    ├── HorarioCarreraGuard.cs
    ├── FranjaZGuard.cs
    ├── ConflictoHorarioService.cs
    ├── FranjaService.cs
    ├── HorarioService.cs
    └── HorarioRangoService.cs
Application/Asistencia/
└── AgendaService.cs
Controllers/Horarios/
├── FranjasController.cs
└── HorariosController.cs
```

Patrón del repo: interfaz + implementación en el mismo archivo, como `SesionService`.
Entidades `horario_detalle` y `horas_clases` se incorporan **re-scaffoldeando** con EF Core
Power Tools, en snake_case como el resto (`horario_detalle.cs`, `horas_clases.cs`).

### 5.1 `HorarioCarreraGuard`

```csharp
Task<bool> AsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct);
Task EnsureAsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct);
```

Camino: `asignaciones_profesores.idNivel → cursos.idCarrera = 6`. Se aplica a **todo INSERT
y UPDATE**, incluido el inspector — es frontera de datos, no de rol, y **no tiene bypass**.
Fallo → 403 `FUERA_DE_ALCANCE`.

### 5.2 `FranjaZGuard`

`Ensure` rechaza con 403 cualquier `idhora` cuyo `tipo != 'Z'`. Se aplica a la escritura de
franjas y a la escritura de celdas de horario. La lectura para conflictos **no** pasa por
el guard: ahí sí se ven todos los tipos.

### 5.3 `ConflictoHorarioService`

```csharp
Task<ResultadoConflictoDto> ValidarAsync(SolicitudConflictoDto s, CancellationToken ct);
```

Predicado, evaluado sobre `TimeOnly` normalizados:

```
solapan(a, b) ⇔ a.idFecha = b.idFecha ∧ a.inicio < b.fin ∧ b.inicio < a.fin
```

Se consultan las filas `activo=1` de `horario_detalle` de esa `idFecha`, con `JOIN` a
`horas_clases` (todos los tipos) y a `asignaciones_profesores` (para `idProfesor` y la
5-tupla del paralelo). Se excluye `idHorarioExcluir` al editar.

| Tipo | Regla | Severidad |
|---|---|---|
| `ASIGNACION_DUPLICADA` | misma `idAsignacion` solapada | bloqueante |
| `DOCENTE_OCUPADO` (franja Z) | mismo `idProfesor` solapado | bloqueante |
| `PARALELO_OCUPADO` (franja Z) | misma 5-tupla solapada, distinta asignación | bloqueante |
| `DOCENTE_OCUPADO` (otro tipo de franja) | mismo `idProfesor` solapado en otra carrera | **advertencia** |

Respuesta: `{ bloqueantes: [...], advertencias: [...] }`, cada ítem con docente, carrera,
nivel, paralelo, asignatura y la franja ajena, para que el inspector decida con la
información a la vista. Guardar con advertencias requiere `confirmarAdvertencias: true`.

### 5.4 `FranjaService` — CRUD de franjas Z

Crear valida `hora_inicio < hora_fin` y que no se solape con otra franja Z activa.
`DELETE` es `activo = 0`, y se rechaza (409) si tiene `horario_detalle` activo colgando.

### 5.5 `HorarioService` — grid y celda

- `ObtenerGridAsync(paralelo5tupla, fechaInicioSemana)` → franjas Z activas × días
  Lun-Dom de esa semana, con las celdas ocupadas y, por día, si existe la fila en
  `fechas_horarios`.
- `CrearAsync` / `ActualizarAsync` / `DesactivarAsync` sobre una celda.
- **Revive**: si existe una fila con la misma clave y `activo=0`, se hace `UPDATE`
  (`activo=1` + campos) en lugar de `INSERT`. Portado de gacad, ADR-0014.
- Escrituras dentro de transacción `Serializable` de alcance corto (validar + escribir) con
  retry 3× ante deadlock (MySQL 1213).

### 5.6 `HorarioRangoService` — operaciones sobre rango

Replicar / editar / eliminar todas las ocurrencias de `(idAsignacion, díaSemana, idhora)`
entre dos fechas.

**El rango siempre es explícito.** `desde` y `hasta` son obligatorios; no existe "replicar
todo el período". Escribimos en una tabla compartida de producción y los módulos llegan a
408 días (H7).

Validaciones del rango, en orden:

1. `desde <= hasta`, ambos presentes → si no, 422 `RANGO_INVALIDO`.
2. **Tope de 16 semanas** entre `desde` y `hasta` → 422 `RANGO_EXCEDE_TOPE`.
3. Si la asignación **tiene** ventana, el rango debe caer dentro de
   `fecha_inicial .. fecha_fin` → 422 `FUERA_DE_VENTANA`.
   Si **no** la tiene (737 asignaciones activas, H8), no hay contra qué acotar: se acepta
   el rango explícito tal cual y la respuesta incluye
   `advertencia: "ASIGNACION_SIN_VENTANA"`. No se inventa una ventana ni se bloquea al
   inspector por un dato que la carrera nunca cargó.
4. **Conteo previo**: se calcula cuántas filas generaría antes de escribir ninguna. Si
   supera **500**, se rechaza con 422 `LOTE_EXCEDE_TOPE` devolviendo el conteo estimado,
   para que el inspector reduzca el rango. Este chequeo va **antes** de abrir la
   transacción.

Los dos topes son constantes del código, no configuración: un tope que se sube desde la UI
no es un tope (ADR-008, decisión 11).

Superadas las validaciones, **un lote no falla entero**: devuelve `OperacionMasivaResultDto`
con una entrada por fecha (`exitoso`, `motivoFallo`), de modo que un día sin fila en
`fechas_horarios` o con conflicto se reporta y el resto se procesa.

### 5.7 `AgendaService`

`ObtenerAgendaAsync(idAsignacion, desde, hasta)` → los días con horario y su estado.

**Bloques contiguos**: para cada `(idAsignacion, idFecha)`, se toman las filas activas de
`horario_detalle` unidas a franjas Z, ordenadas por `hora_inicio`, y se agrupan mientras
`franja[i].hora_fin == franja[i+1].hora_inicio`. Cada corrida es un bloque:

- `numeroBloque` = 1, 2, … según orden en el día
- `idHorarioInicio` = `idHorario` de la primera franja de la corrida
- `franjasPlanificadas` = cantidad, `minutosPlanificados` = suma de `minutos`

Estado por bloque: `cerrada` | `borrador` | `pendiente` (pasado, sin sesión) | `futura`.

## 6. Cambios en la asistencia existente

### 6.1 Creación de sesión

`POST /api/paralelos/{idAsignacion}/sesiones` pasa de `{ fecha, numeroBloque, tema }` a:

```json
{ "idHorarioInicio": 1234, "tema": "...", "observacion": "..." }
```

El servicio deriva `idFecha`, `numeroBloque`, `franjasPlanificadas` y
`minutosPlanificados` recalculando el bloque, y verifica que `idHorarioInicio` pertenezca a
la `idAsignacion` de la ruta (403 si no). Sigue siendo **idempotente** por
`(idAsignacion, idFecha, numeroBloque)`.

Fecha futura → 422 `SESION_FUTURA`. La ventana
`fecha_inicial .. fecha_fin` se mantiene.

### 6.2 Tardanza

Al crear, y solo entonces:

```
diasRetraso = max(0, DATEDIFF(hoy, fechaClase))
esTardia    = diasRetraso > 0
```

**Congelados**: ninguna edición, cierre o reapertura posterior los recalcula. Se usa
`TimeProvider` (ya inyectado en `SesionService`) para que sea testeable.

### 6.3 `numeroBloque` no se recalcula

Una vez creada la sesión, `numeroBloque` e `idHorarioInicio` son inmutables. Si el
inspector edita el horario después, la sesión conserva los suyos. Detectar y mostrar esa
divergencia queda fuera de alcance de M4b.

### 6.4 Modo transición

Si la asignación no tiene ninguna fila de horario activa, se acepta el cuerpo anterior con
`fecha` y `numeroBloque` libres, e `idHorarioInicio` queda NULL. Toda respuesta de sesión
incluye `origen: "horario" | "libre"`.

Si la asignación **sí** tiene horario, `idHorarioInicio` es obligatorio: 422
`HORARIO_REQUERIDO`.

## 7. Contrato de API

Todo bajo `cplec_inspector` salvo lo marcado. Errores por `codigo`, según
`docs/04-contrato-api.md`.

| Verbo | Ruta | Rol | Nota |
|---|---|---|---|
| GET | `/api/paralelos` | inspector | **nuevo**; `mis-paralelos` es del docente |
| GET | `/api/franjas` | ambos | franjas Z activas |
| POST | `/api/franjas` | inspector | |
| PUT | `/api/franjas/{idhora}` | inspector | solo Z |
| DELETE | `/api/franjas/{idhora}` | inspector | `activo=0`; 409 si tiene horario activo |
| GET | `/api/horarios/grid` | ambos | 5-tupla + `fechaInicio` (lunes); el docente solo su distributivo |
| POST | `/api/horarios` | inspector | celda |
| PUT | `/api/horarios/{idHorario}` | inspector | |
| DELETE | `/api/horarios/{idHorario}` | inspector | soft delete |
| POST | `/api/horarios/validar-conflicto` | inspector | feedback previo |
| POST | `/api/horarios/replicar-rango` | inspector | |
| PUT | `/api/horarios/rango` | inspector | |
| DELETE | `/api/horarios/rango` | inspector | |
| GET | `/api/paralelos/{idAsignacion}/agenda` | ambos | `?desde=&hasta=` |
| GET | `/api/reportes/sesiones-tardias` | inspector | |
| GET | `/api/reportes/dias-sin-registrar` | inspector | |

Códigos de error nuevos: `FUERA_DE_ALCANCE` (403), `FRANJA_NO_PROPIA` (403),
`CONFLICTO_HORARIO` (409), `FRANJA_EN_USO` (409), `SESION_FUTURA` (422),
`HORARIO_REQUERIDO` (422), `FECHA_SIN_CALENDARIO` (422), `RANGO_INVALIDO` (422),
`RANGO_EXCEDE_TOPE` (422), `LOTE_EXCEDE_TOPE` (422), `FUERA_DE_VENTANA` (422).

## 8. Frontend

```
features/inspector/
├── models/horario.model.ts
├── services/{horarios,franjas}.service.ts
├── pages/franjas/franjas.page.ts
├── pages/horarios/
│   ├── horarios.page.ts + horarios.store.ts
│   └── components/{horario-grid,asignacion-panel,replicar-rango-dialog}.ts
└── pages/reportes/            ← existente, + tardías y días sin registrar
features/docente/pages/agenda/
```

Standalone + signals, Material, `roleGuard` con `cplec_inspector`. Se respeta la regla del
repo: `core/` y `shared/` nunca importan de `features/`.

**Grid**: franjas Z en filas, Lun-Dom en columnas. Click en celda vacía abre el panel
lateral con las asignaciones del paralelo; se elige una, se valida el conflicto y se crea.
Los días sin fila en `fechas_horarios` se muestran deshabilitados con su motivo.

**Conflictos**: bloqueante en rojo, celda no guardable. Advertencia en ámbar, con diálogo
que muestra docente, carrera y franja ajena, y un botón explícito para guardar igual.

Sin drag-and-drop en esta fase: es la parte más cara del grid de gacad y no cambia lo que
el inspector puede hacer.

## 9. Pruebas

Backend MSTest + Moq + FluentAssertions, **≈73 nuevos → ~213 totales**.

| Área | Tests | Casos que no pueden fallar |
|---|---:|---|
| `HorarioCarreraGuard` | 6 | escribir fila de otra carrera; asignación inexistente; nivel sin curso; sin bypass de inspector |
| `FranjaZGuard` | 5 | escribir sobre `tipo='X'`, `'I'`, tipo NULL, franja inexistente, franja inactiva |
| `ConflictoHorarioService` | 12 | bordes que se tocan **no** chocan; contención; solape parcial izq y der; Z↔X es advertencia y no bloqueo; filas `activo=0` ignoradas; `idHorarioExcluir` al editar |
| Bloques contiguos | 6 | una corrida; dos corridas (mañana/tarde); franja suelta; corte por hueco; orden sucio en BD; franjas de distinta duración |
| `FranjaService` | 6 | no desactivar franja con horario activo; no crear franja Z solapada; `hora_inicio >= hora_fin` |
| `HorarioService` | 8 | revive de fila soft-deleted; `idEspacio` siempre NULL; grid con día sin calendario |
| `HorarioRangoService` | 13 | fecha ausente en `fechas_horarios` se reporta y **no** aborta el lote; rango fuera de la ventana; rango invertido; lote vacío; **tope de 16 semanas**; **tope de 500 filas, rechazado antes de abrir la transacción**; asignación sin ventana acepta rango explícito con advertencia |
| `SesionService` (cambios) | 8 | `diasRetraso` congelado y no recalculado al editar; fecha futura 422; modo libre; `idHorarioInicio` de otra asignación 403; idempotencia |
| `AgendaService` + reportes | 9 | estados por bloque; días pasados sin sesión |

Frontend Vitest: ~12 nuevos → ~36 totales (store del grid, servicios, guardia de rol,
render de conflicto bloqueante vs. advertencia).

`TimeProvider` inyectado en todo lo que dependa de "hoy" — la tardanza es lo que más se
va a testear con relojes fijos.

## 10. Hitos

M4b, **bloqueante de M5**: los reportes del inspector deben incluir tardías y días sin
registrar, que dependen de esto.

| | Entregable | DoD |
|---|---|---|
| **M4b-0** | Verificación en BD + este spec + ADR-008 | ✅ **cerrado 2026-08-07** — H1–H5 en la sección 3, aplicados al spec y al ADR |
| M4b-1 | Re-scaffold entidades + migración 004 + los 2 guards | 11 tests |
| M4b-2 | `ConflictoHorarioService` | 18 tests |
| M4b-3 | `FranjaService` + `/api/franjas` | 6 tests |
| M4b-4 | `HorarioService` + grid + celda + `/api/paralelos` | 8 tests |
| M4b-5 | `HorarioRangoService` + replicar/editar/eliminar rango + topes | 13 tests |
| M4b-6 | Migración 005 + sesión anclada + agenda + tardanza | 17 tests |
| M4b-7 | Frontend inspector: franjas, grid, panel, replicar | `npm test && npm run lint && npm run build` limpios |
| M4b-8 | Frontend docente: agenda + reportes de tardías | 36 tests frontend |

### Riesgos abiertos del hito

| Riesgo | Impacto | Mitigación |
|---|---|---|
| **`fechas_horarios` termina el 2026-12-31** (H4), a 146 días | Un período que cruce a 2027 no se puede planificar ni registrar. No lo controla `cplec` | Confirmar con quien alimenta la tabla **antes** de M4b-5. `HorarioRangoService` reporta la fecha faltante por día en vez de fallar el lote, y el grid marca el día deshabilitado — el sistema degrada de forma legible, pero no resuelve la falta de datos |
| **Cero respaldo de la BD para unicidad y conflictos** (H1) | Un `INSERT` manual por SQL, o un bug en el retry, produce duplicados silenciosos | El retry ante deadlock **es** el mecanismo, no una mejora: tiene test propio. Además, un chequeo de duplicados en el reporte del inspector |
| `gestion_academica` empieza a cargar carrera 6 | Rompe la frontera de propiedad del ADR-008 | H5 la confirma en cero hoy. Revisar antes de producción |

Documentación a actualizar al cerrar: `docs/02-modelo-datos.md`,
`docs/04-contrato-api.md`, `docs/10-navegacion-distributivo.md`, `docs/00-roadmap.md` y
`CLAUDE.md`.
