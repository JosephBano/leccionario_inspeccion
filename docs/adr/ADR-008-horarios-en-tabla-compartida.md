---
title: ADR-008 — Horarios del inspector sobre `horario_detalle` compartida
doc: adr/ADR-008-horarios-en-tabla-compartida
status: aceptado
updated: 2026-08-07
enmienda: adr/ADR-001-sesion-de-clase-propia
---

# ADR-008 — Horarios del inspector sobre `horario_detalle` compartida

## Contexto

[ADR-001](ADR-001-sesion-de-clase-propia.md) decidió que la sesión de clase fuera una
tabla propia (`cplec_sesiones`), con **grano día**, sin FK hacia `horas_clases`, y
descartó explícitamente escribir en `horario_detalle`. El argumento era correcto en su
momento: la tabla estaba vacía para la carrera 6, la escuela de conducción no planificaba
sus clases en el sistema, y construir un módulo de armado de horarios era fuera de alcance.

Dos hechos cambian esa premisa:

1. **Ahora sí se quiere planificación.** El inspector debe armar el horario del paralelo
   al inicio del período, y la asistencia debe colgar de ese horario en vez de crearse a
   mano en cualquier fecha del rango de la asignación.
2. **Ese módulo ya existe y está probado.** `gestion_academica_istpet`
   (`/home/joeman/Documents/istpet-dev/digitalizacion-istpet/gestion_academica_istpet`)
   tiene un módulo de horarios en producción sobre la misma base `sigafi_es`:
   `HorarioService` (2 796 líneas), `BulkHorarioService` (1 096), controller y grid
   Angular. Sus decisiones están documentadas en sus `ADR-007`, `ADR-008`, `ADR-0014` y
   `ADR-0015`.

No es código duplicado en el sentido problemático del término: son dos organizaciones
distintas, con roles, RBAC y ciclos de vida propios, que comparten una base de datos
porque la institución la gestiona así. Lo que se comparte es el **dato**, no el código.

### Restricción dura

Las tablas de horarios (`horario_detalle`, `horas_clases`, `fechas_horarios`, `espacios`)
**no se alteran**: ni columnas nuevas, ni triggers, ni cambios de constraints. `cplec`
solo inserta y actualiza **filas** que le pertenecen.

## Decisión

### 1. El horario vive en `horario_detalle`, no en una tabla propia

`cplec` escribe filas en `horario_detalle`. Se descarta `cplec_horarios` propia.

La razón decisiva es que un docente de la escuela de conducción puede dictar también en
otra carrera. Con tabla propia, ese choque de agenda es indetectable por construcción.
Con la tabla compartida, la validación de conflictos ve todas las carreras y el horario
de `cplec` es visible para `gestion_academica`.

Esto **enmienda** la alternativa descartada de ADR-001 ("poblar `horario_detalle` con
sesiones sintéticas"). La diferencia no es cosmética: ADR-001 rechazaba meter ahí
**sesiones dictadas**, que no es lo que la tabla modela. Acá se escribe **planificación**,
que es exactamente para lo que la tabla existe. `cplec_sesiones` sigue siendo la sesión
real y sigue siendo tabla propia.

### 2. Frontera de propiedad: carrera 6

`cplec` escribe **únicamente** filas cuya `idAsignacion` pertenece a la carrera 6:

```
asignaciones_profesores.idAsignacion → cursos.idNivel → carreras.idCarrera = 6
```

Se implementa como `HorarioCarreraGuard`, análogo a `DistributivoGuard`, y se aplica a
todo INSERT y UPDATE sin excepción — incluido el inspector, porque es una frontera de
**datos**, no de rol.

Las lecturas sí cruzan carreras: es lo que hace útil la detección de conflictos.

| | lee | escribe |
|---|:---:|:---:|
| `horario_detalle` carrera 6 | sí | sí |
| `horario_detalle` otras carreras | sí (solo validación de conflictos) | **nunca** |
| `horas_clases` `tipo='Z'` | sí | sí |
| `horas_clases` otros tipos | sí (solo validación de conflictos) | **nunca** |
| `fechas_horarios` | sí | nunca |
| `espacios` | no | nunca |

La única marca disponible para saber qué fila es nuestra es la carrera: `horario_detalle`
no tiene `usuarioCreacion` y no podemos agregarlo. Hoy es fiable porque la carrera 6 tiene
0 filas y `gestion_academica` no la gestiona. **Es una suposición explícita**: si algún
día ese sistema empieza a cargar horarios de la carrera 6, hay que revisar este ADR.

### 3. Franjas horarias: `tipo = 'Z'`

`cplec` crea sus propias franjas en `horas_clases` con:

- `tipo = 'Z'` — reservado para este sistema
- `idCarrera` NULL, `idSeccion` NULL — las franjas no se segmentan

`tipo = 'X'` es del instituto y lo gestiona `gestion_academica`
([su ADR-0015](../../../../digitalizacion-istpet/gestion_academica_istpet/docs/decisions/ADR-0015-tipo-franja-x-legado-i.md)).
`tipo = 'I'` es legacy. `cplec` **no crea, no edita ni desactiva** ninguna franja que no
sea `'Z'`; sí las lee todas para detectar conflictos. Se implementa como `FranjaZGuard`.

La verificación del 2026-08-07 confirmó que `tipo` es `char(1)` sin `ENUM` ni `CHECK` —
`'Z'` se puede usar— y que los tipos existentes son `C` (54 filas), `I` (7) y `X` (11).

Hallazgo no previsto: **la carrera 6 ya tiene franjas de tipo `'C'`**, segmentadas por
`idSeccion`, que en esta carrera es la jornada — y con horas distintas por jornada. `'C'`
aparece también en las carreras 1, 2, 4, 7, 8, 9 y 10, así que es la convención de otro
sistema y no un catálogo propio de la escuela de conducción. Se mantiene crear franjas
`'Z'` con `idSeccion` NULL: el catálogo queda plano y el inspector elige la franja
correcta en el grid. El costo es cosmético (un paralelo matutino ve también las franjas
nocturnas) y poblar `idSeccion` después es aditivo.

### 4. Sin espacios

`idEspacio` queda **siempre NULL**. La escuela de conducción no gestiona aulas en este
sistema. No es un pendiente de implementación: es el comportamiento definido. Si algún día
lo piden, es aditivo.

Se había anotado acá una preocupación por `NULL != NULL` en índices únicos de MySQL. La
verificación contra la base (2026-08-07) la dejó sin objeto por una razón peor: ver la
sección 6.

### 5. Conflictos por solapamiento de horas, no por `idhora`

Un conflicto es un **solape de rangos horarios** en la misma fecha, no una igualdad de
`idhora`:

```
choque(a, b) ⇔ a.idFecha = b.idFecha
              ∧ a.hora_inicio < b.hora_fin
              ∧ b.hora_inicio < a.hora_fin
```

Bordes que se tocan (`09:00–10:00` vs `10:00–11:00`) **no** chocan.

Comparar por `idhora` —lo que hace `gestion_academica`— no sirve acá por dos razones
independientes:

- **Entre sistemas**: las franjas Z y las X son filas distintas aunque cubran el mismo
  horario de reloj. El lunes 07:00 tiene un `idhora` en `cplec` y otro en
  `gestion_academica`; por igualdad nunca colisionan.
- **Dentro de `cplec`**: las franjas Z las crea el inspector desde la UI y pueden tener
  duraciones libres. `Z-1 07:00–09:00` y `Z-7 08:00–08:45` son `idhora` distintos y se
  solapan de verdad. Por igualdad, el sistema reportaría "sin conflicto" mientras pone al
  mismo docente en dos clases a la vez.

Se validan tres conflictos: **docente** (mismo `idProfesor` vía join a
`asignaciones_profesores`), **paralelo** (misma 5-tupla) y **asignación duplicada**.
Espacio no aplica (sección 4).

#### Bloqueante vs. advertencia

| Contra | Trato | Por qué |
|---|---|---|
| Franja `Z` (carrera 6) | **Bloqueante**, 409 | Está bajo nuestra autoridad: el inspector puede resolverlo |
| Franja de otro tipo (otra carrera) | **Advertencia**, se guarda | Es un choque real, pero `cplec` no puede ni debe editar el horario ajeno |

La advertencia se devuelve con el detalle (docente, carrera, franja ajena), el inspector
confirma, y la fila queda listada en su reporte.

### 6. TOCTOU: `Serializable` + retry, sin cambios de esquema

**Verificado contra la base el 2026-08-07: `horario_detalle` no tiene ningún índice
único.** Solo `PRIMARY KEY (idHorario)` y cinco `KEY` no únicas. Las dos fuentes
documentales que teníamos estaban equivocadas: ADR-001 de este repo afirmaba un `UNIQUE
(activo, idEspacio, idAsignacion, idFecha, idhora)` y el ADR-0012 de `gestion_academica`
afirmaba `UNIQUE (idAsignacion, idFecha, idhora)`. No existe ninguno de los dos.

Esto agrava el problema en vez de simplificarlo. No es solo que el solapamiento no se
pueda expresar como constraint en MySQL 5.7 (no hay exclusion constraints): **tampoco hay
respaldo para el duplicado exacto**. La base no aporta nada. Toda la correctitud vive en
la aplicación.

Se adopta la misma solución que `gestion_academica` en su ADR-0014: transacción con
`IsolationLevel.Serializable` de alcance corto (validar + escribir) y retry de 3 intentos
ante deadlock (error 1213).

Cómo protege realmente, que conviene entender antes de tocar ese código: bajo
`Serializable`, InnoDB toma next-key locks sobre el rango leído de
`ix_horario_detalle_fecha_hora_activo`. Dos escrituras concurrentes de la misma celda se
bloquean mutuamente, una muere con 1213, y el retry la reintenta viendo ya la fila de la
otra. **El retry no es una mejora de robustez: es el mecanismo.** Sin él hay duplicados.
Los locks son del motor, así que esto también protege contra `gestion_academica`
escribiendo de forma concurrente.

No se crea un índice único sobre `horario_detalle`. Sería un cambio estructural sobre una
tabla compartida con ~1 658 filas de otros sistemas, podría fallar por duplicados
preexistentes ajenos, y la decisión le corresponde a quien gobierna la tabla, no a `cplec`.

Se descarta desnormalizar `idProfesor` en `horario_detalle` con triggers (lo que su
ADR-0012 propuso y su ADR-0014 revirtió): exige `ALTER TABLE` y un trigger sobre
`asignaciones_profesores`, ambos prohibidos acá.

### 7. La sesión sigue con grano día, anclada a un bloque contiguo

`cplec_sesiones` **no cambia de grano**. Lo que cambia es que se ancla al horario. Un
bloque es una corrida de franjas contiguas de la misma asignación en el mismo día:
`franja[i].hora_fin == franja[i+1].hora_inicio`. Si el horario del día está partido en
mañana y tarde, salen dos bloques y dos sesiones.

La migración 005 agrega a `cplec_sesiones`:

| Columna | Para qué |
|---|---|
| `idHorarioInicio` INT NULL, FK `horario_detalle` | **Identidad real del bloque** |
| `franjasPlanificadas` TINYINT NULL | Cuántas franjas cubre |
| `minutosPlanificados` SMALLINT NULL | Suma de `horas_clases.minutos` — con franjas de duración libre, contar franjas miente |
| `esTardia` TINYINT(1) NOT NULL DEFAULT 0 | sección 8 |
| `diasRetraso` SMALLINT NOT NULL DEFAULT 0 | sección 8 |

`numeroBloque` se calcula al crear y **no se recalcula nunca**. Si el inspector agrega
después una franja a las 06:00, el orden de los bloques cambiaría y desalinearía sesiones
ya guardadas. La identidad estable es `idHorarioInicio`; `numeroBloque` se conserva porque
forma parte del `UNIQUE` existente.

`idHorarioInicio` es NULL-able a propósito: ver sección 9.

### 8. Registro tardío: sin límite, marcado y visible

El docente puede registrar cualquier día pasado que tenga horario dentro del rango de su
asignación. No hay tope de días ni habilitación previa del inspector.

Al **primer guardado** se congela `diasRetraso = DATEDIFF(hoy, fechaClase)` (mínimo 0) y
`esTardia = diasRetraso > 0`. Congelado significa que ediciones posteriores no lo
recalculan: mide cuándo se registró por primera vez, no cuándo se tocó por última vez.

Fecha futura: 422. No se pasa lista de una clase que no ocurrió.

El inspector tiene dos reportes: sesiones tardías y días con horario ya pasados sin sesión.
La política es **no bloquear a nadie y dejar la evidencia** — el control es del inspector,
no del sistema.

### 9. Modo transición para asignaciones sin horario

Si una asignación no tiene ninguna fila de horario activa, se acepta el flujo actual: el
docente crea la sesión con `fecha` libre e `idHorarioInicio` NULL. La respuesta trae
`origen: "horario" | "libre"`.

Sin esto, el día del despliegue todos los paralelos sin horario cargado quedan sin poder
pasar lista. No es una vía de escape permanente: el inspector ve qué sesiones se crearon
sin planificación.

### 10. Alcance de lo que se porta

Del módulo de `gestion_academica` entran: grid semanal, panel de asignación, replicación
sobre rango de semanas (con editar y eliminar por rango) y validación de conflictos.

Quedan fuera, como hitos posteriores si se piden: generación automática
(`ScheduleGeneratorService`, 924 líneas — rinde poco con pocos paralelos), reasignación y
recuperación pedagógica, feriados / `fecha_config`, espacios y drag-and-drop en el grid.

## Consecuencias

**A favor**

- El choque de agenda de un docente que dicta en varias carreras se detecta de verdad.
- Cero cambios de esquema sobre tablas compartidas: solo filas.
- `cplec_sesiones` conserva su grano y sus 140 tests siguen siendo válidos en su mayoría.
- El inspector obtiene una vista real de cumplimiento: quién registró tarde, qué días
  faltan.
- Las decisiones difíciles (TOCTOU, revive de soft-delete, replicación con detalle por día)
  ya están resueltas y probadas del otro lado; se portan con su justificación.

**En contra**

- **Dos aplicaciones escriben la misma tabla legacy.** Mitigado por la frontera de
  carrera 6, la de `tipo='Z'` y el aislamiento `Serializable`, pero el riesgo no es cero
  y no lo controlamos por completo.
- **Ni la unicidad ni el solapamiento tienen respaldo de la BD** (sección 6): `horario_detalle`
  no tiene ningún índice único. Todo vive en la aplicación, así que un `INSERT` manual por
  SQL se lo salta por completo, y un bug en el retry produce duplicados silenciosos.
  Es la consecuencia más seria de este ADR.
- **Dependemos del soft-delete ajeno.** La FK `cplec_sesiones.idHorarioInicio` asume que
  nadie borra físicamente filas de `horario_detalle`. `gestion_academica` usa soft delete
  (su ADR-008) y `cplec` también, pero es una política, no una garantía del motor.
- La advertencia Z↔otro tipo no bloquea: un conflicto real puede persistirse. Es
  deliberado, y por eso queda en el reporte.
- `fechas_horarios` se alimenta por fuera. Hoy está completo y sin huecos, pero **termina
  el 2026-12-31**: un período que cruce a 2027 no se puede planificar ni registrar hasta
  que alguien cargue esas fechas. Es una dependencia operativa que `cplec` no controla. La
  replicación reporta el día faltante en vez de fallar el lote entero, pero degradar de
  forma legible no es lo mismo que resolverlo.

## Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| Tabla propia `cplec_horarios` | Pierde por construcción el choque de docente contra otras carreras, que es la razón principal de hacer esto |
| Leer la compartida, escribir en propia | Conserva la detección pero deja el horario de `cplec` invisible para el resto, y duplica el modelo sin ganar nada |
| `ALTER TABLE horario_detalle` (columnas de `cplec`) | Prohibido: tabla compartida en producción con otros sistemas |
| Conflicto por `idhora` igual | No detecta nada fuera de las franjas Z, y **ni siquiera protege dentro de `cplec`** con franjas de duración libre (sección 5) |
| Reutilizar franjas `tipo='X'` | Son del instituto; editarlas afecta a `gestion_academica` |
| Sesión por franja (una lista por hora) | El docente pasaría 4 listas en un día de 4 horas; obliga a rehacer `SesionService` y su UI sin beneficio operativo |
| Horario que solo habilita el día, sin anclaje | El inspector no podría auditar horas dictadas ni saber a qué bloque corresponde la lista |
| Límite de N días para el registro tardío | Es lo contrario de lo pedido: bloquea al docente en el caso normal |

## Referencias

- [ADR-001 — Sesión de clase propia](ADR-001-sesion-de-clase-propia.md) — enmendada
  parcialmente por este ADR
- [ADR-004 — Sin migraciones EF](ADR-004-sin-migraciones-ef.md)
- [`docs/10-navegacion-distributivo.md`](../10-navegacion-distributivo.md)
- `gestion_academica_istpet`: `docs/decisions/ADR-007-horarios-db-design.md`,
  `ADR-008-horarios-module-architecture.md`, `ADR-0014-toctou-serializable-retry.md`,
  `ADR-0015-tipo-franja-x-legado-i.md`
