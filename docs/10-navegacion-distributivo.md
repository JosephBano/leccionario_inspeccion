# 10 — Navegación del distributivo: de la asignación a los alumnos

Este documento fija **el camino de consulta** del módulo inicial. Todo está verificado
contra datos reales de `sigafi_es` el 2026-08-06; las consultas de esta página se pueden
copiar y ejecutar.

---

## El eje es `asignaciones_profesores`

Todo parte de ahí. Una fila de esa tabla **es** la unidad de trabajo del docente:
"yo dicto *esta asignatura* a *este paralelo*".

```
asignaciones_profesores (idAsignacion)
        │
        ├── idProfesor    → profesores        ── quién dicta
        ├── idAsignatura  → asignaturas       ── qué dicta
        ├── idNivel       → cursos            ── TIPO de licencia  (idCarrera = 6)
        │                        └── idCarrera → carreras (6 = ESCUELA DE CONDUCCION)
        ├── idSeccion     → secciones         ── jornada
        ├── idModalidad   → modalidades       ── presencial / en línea / …
        ├── idPeriodo     → periodos
        ├── paralelo                          ── letra del paralelo
        └── fecha_inicial / fecha_fin         ── ventana en que se dicta
```

El filtro de alcance del sistema es **`cursos.idCarrera = 6`**, no
`periodos.esConduccion` (ese flag existe pero está en 0 en toda la tabla).

### Qué significan realmente esas tablas en la carrera 6

Los nombres genéricos engañan. Para la Escuela de Conducción:

| Tabla | Significado real | Valores |
|---|---|---|
| `cursos.nivel` | **Tipo de licencia** | `TIPO "C"`, `TIPO "D"`, `TIPO "E"`, `TIPO "D" CONVALIDADA`, `TIPO "E" CONVALIDADA`, `RECUPERACION PUNTOS` |
| `secciones.seccion` | **Jornada** | `MATUTINA`, `NOCTURNA`, `VESPERTINA`, `FIN SEMAN` |
| `modalidades.modalidad` | Modalidad | `PRESENCIAL`, `EN LINEA`, `HIBRIDA`, `SEMIPRESENCIAL` |
| `asignaturas.Asignatura` | Materia | `EDUCACIÓN VIAL`, `LEYES Y REGLAMENTOS`, `GEOGRAFÍA DEL ECUADOR`, `EDUCACION AMBIENTAL`, … |

Por eso un paralelo se rotula en la interfaz así, y no con IDs:

> **EDUCACIÓN VIAL** · TIPO "C" · NOCTURNA · Presencial · Paralelo D · 30 alumnos

---

## Paso 1 — Los paralelos de un docente

```sql
SELECT ap.idAsignacion,
       a.Asignatura,
       c.nivel      AS tipoLicencia,
       s.seccion    AS jornada,
       mo.modalidad,
       ap.paralelo,
       ap.fecha_inicial,
       ap.fecha_fin,
       COUNT(m.idMatricula) AS alumnos
FROM   asignaciones_profesores ap
JOIN   cursos      c  ON c.idNivel      = ap.idNivel AND c.idCarrera = 6
JOIN   asignaturas a  ON a.idAsignatura = ap.idAsignatura
JOIN   secciones   s  ON s.idSeccion    = ap.idSeccion
JOIN   modalidades mo ON mo.idModalidad = ap.idModalidad
LEFT   JOIN matriculas m
       ON  m.idPeriodo   = ap.idPeriodo
       AND m.idNivel     = ap.idNivel
       AND m.idSeccion   = ap.idSeccion
       AND m.idModalidad = ap.idModalidad
       AND m.paralelo    = ap.paralelo
       AND COALESCE(m.retirado, 0) = 0
WHERE  ap.idProfesor = ?          -- ← claim `sub` del JWT, NUNCA del request
  AND  ap.idPeriodo  = ?
  AND  ap.activo = 1
GROUP  BY ap.idAsignacion, a.Asignatura, c.nivel, s.seccion, mo.modalidad,
          ap.paralelo, ap.fecha_inicial, ap.fecha_fin
ORDER  BY ap.fecha_inicial, a.Asignatura, ap.paralelo;
```

Resultado real para el docente `1716547896` en `OCC2025`:

| idAsignacion | Asignatura | Tipo | Jornada | Par. | Desde | Hasta | Alumnos |
|---|---|---|---|---|---|---|---|
| 23229 | GEOGRAFÍA DEL ECUADOR | TIPO "C" | NOCTURNA | C | 2025-10-06 | 2025-11-05 | 29 |
| 23230 | GEOGRAFÍA DEL ECUADOR | TIPO "C" | NOCTURNA | D | 2025-10-06 | 2025-11-05 | 30 |
| 23246 | EDUCACION AMBIENTAL | TIPO "C" | NOCTURNA | A | 2025-11-06 | 2025-11-19 | 30 |
| 23247 | EDUCACION AMBIENTAL | TIPO "C" | NOCTURNA | B | 2025-11-06 | 2025-11-19 | 30 |

Esa tabla **es** la pantalla de inicio del docente.

> **Ventana de visibilidad (`MisParalelosService`).** El listado no muestra
> todo el distributivo activo: solo asignaciones donde
> `fecha_inicial <= hoy <= fecha_fin + 15 días`. El margen de 15 días permite
> que el docente siga viendo (y registrando/editando dentro de la ventana de
> edición de 72h) un paralelo recién cerrado. Pasados los 15 días, desaparece
> de `/api/mis-paralelos` aunque `ap.activo` siga en 1 — no confundir con la
> ventana de fechas de sesión del Paso 4 (esa es estricta, sin margen).

---

## Paso 2 — Los alumnos de un paralelo

El join con `matriculas` es por la **tupla de 5 columnas**:

```sql
SELECT m.idMatricula,
       m.idAlumno,
       TRIM(CONCAT_WS(' ', al.apellidoPaterno, al.apellidoMaterno)) AS apellidos,
       TRIM(CONCAT_WS(' ', al.primerNombre,    al.segundoNombre))   AS nombres,
       m.esOyente
FROM   asignaciones_profesores ap
JOIN   matriculas m
       ON  m.idPeriodo   = ap.idPeriodo
       AND m.idNivel     = ap.idNivel
       AND m.idSeccion   = ap.idSeccion
       AND m.idModalidad = ap.idModalidad
       AND TRIM(m.paralelo) = TRIM(ap.paralelo)
JOIN   alumnos al ON al.idAlumno = m.idAlumno
WHERE  ap.idAsignacion = ?
  AND  COALESCE(m.retirado, 0) = 0
  AND  COALESCE(m.valida,   1) = 1
ORDER  BY apellidos, nombres;
```

> **Cuidado con los nombres de columna.** `alumnos` **no** tiene `apellidos` /
> `nombres` (esas son de `profesores`). Usa
> `apellidoPaterno`, `apellidoMaterno`, `primerNombre`, `segundoNombre`, todas
> `varchar(30)`. Confundirlas es un error en tiempo de ejecución, no de compilación.

Salida real para `idAsignacion = 23229` (29 alumnos):

| idMatricula | idAlumno | apellidos | nombres |
|---|---|---|---|
| 55667 | 1724602071 | ANDRADE YUQUILEMA | SONIA CONCEPCION |
| 55711 | 1724596463 | ASADOBAY AYALA | JONATHAN ESTUARDO |
| 55679 | 1728735810 | BORJA PORTILLA | JOSETH NICOLAS |
| … | | | |

### Las 5 columnas son obligatorias — con evidencia

Se midió la diferencia entre el join completo y uno reducido a
`(idPeriodo, idNivel, paralelo)`:

| idAsignacion | Join de 5 columnas | Join de 3 columnas |
|---|---|---|
| 23229 | **29** | 89 |
| 23230 | **30** | 75 |
| 23219 | **30** | 90 |

Omitir `idSeccion` y `idModalidad` **triplica** la nómina: distintas jornadas reusan la
misma letra de paralelo. Un docente terminaría pasando lista a alumnos de otra jornada.
Las 5 columnas no son opcionales.

### Sobre el `TRIM`

`asignaciones_profesores.paralelo` es `char(1)` y `matriculas.paralelo` es
`varchar(10)`. Se verificó que **en la carrera 6 todos los valores tienen longitud 1**
(letras `A`–`H`, dígitos `0`–`9` y `@`), así que la comparación directa `=` funciona y
devuelve los 30 alumnos esperados.

Aun así, en el código se compara con `TRIM()` en ambos lados: la tolerancia cuesta nada
y protege de que alguien cargue `'A '` desde el sistema académico.

---

## Paso 3 — Una matrícula, muchas asignaturas

Este es el punto que define el modelo de asistencia:

```sql
SELECT m.idMatricula, COUNT(DISTINCT ap.idAsignacion) AS asignaciones
FROM   matriculas m
JOIN   cursos c ON c.idNivel = m.idNivel AND c.idCarrera = 6
JOIN   asignaciones_profesores ap
       ON  ap.idPeriodo   = m.idPeriodo
       AND ap.idNivel     = m.idNivel
       AND ap.idSeccion   = m.idSeccion
       AND ap.idModalidad = m.idModalidad
       AND ap.paralelo    = m.paralelo
       AND ap.activo = 1
WHERE  m.idPeriodo = 'OCC2025'
GROUP  BY m.idMatricula
ORDER  BY asignaciones DESC;
-- → 11 asignaciones por matrícula
```

Un alumno de `OCC2025` cursa **11 asignaturas** con el mismo `idMatricula`. Por eso la
asistencia se registra por **`(sesión, matrícula)`** y la sesión cuelga de
`idAsignacion`: una marca por asignatura y día, no una por día.

Es exactamente lo que `matriculas_asistencias` (legacy, PK `idMatricula` + `idFecha`)
no puede representar.

---

## Paso 4 — La ventana de fechas

`asignaciones_profesores` trae `fecha_inicial` y `fecha_fin` **por asignación**, y están
pobladas en todos los períodos recientes (solo faltan en `AGO2013`/`AGO2014`).

En la carrera 6 son ventanas cortas y reales — módulos intensivos de 2 a 6 semanas:

```
23229  GEOGRAFÍA DEL ECUADOR   2025-10-06 → 2025-11-05   (1 mes)
23246  EDUCACION AMBIENTAL     2025-11-06 → 2025-11-19   (2 semanas)
```

**Regla:** la fecha de una sesión debe caer dentro de esa ventana.

```
ap.fecha_inicial <= fechaSesion <= ap.fecha_fin
```

Si `fecha_inicial` o `fecha_fin` son `NULL` (períodos viejos), se cae al rango del
período (`periodos.fecha_inicial .. fecha_final`) y se registra un warning.

No se implementa con un `CHECK`: MySQL 5.7 los acepta sintácticamente pero **no los
aplica**, y además la regla cruza dos tablas. Se valida en `SesionService` y tiene test.

### `periodos.activo` no sirve para saber cuál es el período vigente

Casi todos los períodos tienen `activo = 1`, incluidos los de 2022. Es un flag inútil
para este propósito.

---

## Paso 5 — Resolución del período: **por nivel, con ventana de visibilidad**

Los niveles de la carrera 6 (tipos de licencia) corren en calendarios **independientes**:
TIPO "C" puede estar a mitad de período mientras TIPO "E" acaba de cerrar. No existe
"el período activo" del sistema — existe uno (o varios) **por nivel**.

Por eso se agrupa por `idNivel`: es la única forma de identificar correctamente qué
período le corresponde a cada tipo de licencia. Es lo que responde `PeriodosPorNivelService`
y lo que expone `GET /api/periodos/por-nivel`.

**Regla de visibilidad (qué se considera "vigencia" aquí):** para cada `idNivel` se
devuelve todo período que sea:

- **VIGENTE** — hoy cae dentro de `fecha_inicial..fecha_fin`.
- **FUTURO** — el período ya está cargado en el distributivo pero `fecha_inicial` todavía
  no llega. Se muestra para que el docente/inspector lo anticipe, no se oculta.
- **CERRADO**, pero solo si `fecha_fin` quedó dentro de los últimos
  `PeriodosPorNivelService.MesesVisibilidadCierre` (6) meses. Un cierre más viejo que eso
  ya no aporta al selector y solo agrega ruido.

Períodos con `fecha_inicial`/`fecha_fin` en `NULL` (rango sin datos, ver CLAUDE.md) se
tratan como `VIGENTE`: sin fechas no hay forma segura de excluirlos, así que se muestran
siempre.

Esto reemplaza la primera versión ("un período por nivel, el más reciente"): con esa
regla, un período FUTURO ya cargado quedaba oculto detrás de uno VIGENTE, y un CERRADO
de hace una semana desaparecía en cuanto había otro más nuevo. Ahora puede haber **más
de una fila por nivel** — típicamente el VIGENTE y el FUTURO conviviendo mientras se
carga el siguiente módulo.

### Consulta

Este SQL queda como referencia histórica del Paso 1 (agrupar por `idNivel`/`idPeriodo`
con `MIN`/`MAX` de fechas y conteos) y de la técnica de desempate — el `HAVING`
correlacionado ya **no** se usa para quedarse con un solo período por nivel; ese filtro
de "el más reciente" se reemplazó por la ventana de visibilidad de arriba, aplicada en
memoria (ver `PeriodosPorNivelService.EsVisible`). MySQL 5.7 no tiene funciones de
ventana, así que el "máximo por grupo" original se resolvía con una clave de
ordenamiento compuesta dentro de un `HAVING` correlacionado:

```sql
SELECT ap.idNivel,
       c.nivel      AS tipoLicencia,
       ap.idPeriodo,
       p.detalle,
       MIN(ap.fecha_inicial) AS fechaInicial,
       MAX(ap.fecha_fin)     AS fechaFin,
       CASE
         WHEN CURDATE() BETWEEN MIN(ap.fecha_inicial) AND MAX(ap.fecha_fin) THEN 'VIGENTE'
         WHEN CURDATE() <  MIN(ap.fecha_inicial)                            THEN 'FUTURO'
         ELSE                                                                    'CERRADO'
       END AS vigencia,
       COUNT(*)                      AS asignaciones,
       COUNT(DISTINCT ap.idProfesor) AS docentes
FROM   asignaciones_profesores ap
JOIN   cursos   c ON c.idNivel   = ap.idNivel AND c.idCarrera = 6
JOIN   periodos p ON p.idPeriodo = ap.idPeriodo
WHERE  COALESCE(ap.activo, 1) = 1
GROUP  BY ap.idNivel, c.nivel, ap.idPeriodo, p.detalle
HAVING CONCAT(DATE_FORMAT(MAX(ap.fecha_fin),     '%Y%m%d'),
              DATE_FORMAT(MIN(ap.fecha_inicial), '%Y%m%d'),
              ap.idPeriodo)
     = (SELECT MAX(CONCAT(DATE_FORMAT(x.fin, '%Y%m%d'),
                          DATE_FORMAT(x.ini, '%Y%m%d'),
                          x.idPeriodo))
        FROM  (SELECT ap2.idNivel,
                      ap2.idPeriodo,
                      MAX(ap2.fecha_fin)     AS fin,
                      MIN(ap2.fecha_inicial) AS ini
               FROM   asignaciones_profesores ap2
               WHERE  COALESCE(ap2.activo, 1) = 1
               GROUP  BY ap2.idNivel, ap2.idPeriodo) x
        WHERE x.idNivel = ap.idNivel)
ORDER  BY fechaFin DESC, c.idNivel;
```

### Resultado real bajo la regla anterior (ejecutado el 2026-08-06, "el más reciente")

| idNivel | Tipo de licencia | Período | Desde | Hasta | Vigencia | Asigs |
|---|---|---|---|---|---|---|
| 35 | TIPO "C" | `OCC2025` | 2025-10-04 | 2026-12-18 | **VIGENTE** | 166 |
| 37 | TIPO "E" | `JUE2026` | 2026-07-06 | 2026-07-31 | CERRADO | 9 |
| 39 | TIPO "E" CONVALIDADA | `JEC2026` | 2026-07-06 | 2026-07-31 | CERRADO | 4 |
| 36 | TIPO "D" | `NOD2025` | 2026-01-22 | 2026-05-19 | CERRADO | 6 |
| 38 | TIPO "D" CONVALIDADA | `NDC2025` | 2026-01-22 | 2026-02-20 | CERRADO | 2 |
| 64 | RECUPERACION PUNTOS | `ORP2025` | 2025-10-29 | 2025-10-30 | CERRADO | 1 |

Exactamente 6 filas, una por nivel — pero con la ventana de visibilidad todos estos
cierres caen dentro de los 6 meses desde 2026-08-06, así que hoy el resultado es el
mismo conjunto de filas (nada cambia salvo que, si apareciera un `OCT2026` cargado a
futuro para el nivel 35, se mostraría **junto** a `OCC2025` en vez de reemplazarlo).

### Detalles que no se pueden omitir

**1. El desempate por fila sigue siendo obligatorio.** El nivel 37 tenía **dos**
períodos con el mismo `fecha_fin` (`ENE2026` y `JUE2026`, ambos terminan 2026-07-31).
Sin un criterio de orden, dos ejecuciones podrían listar `JUE2026` antes o después de
`ENE2026` de forma inconsistente. El orden se fija por:

```
idNivel  →  fecha_fin (desc)  →  fecha_inicial (desc)  →  idPeriodo (desc)
```

de modo que, dentro de un nivel, el período más reciente aparece primero y el
resultado es determinista entre ejecuciones.

**2. "Vigente" no es lo mismo que "visible".** Antes de esta regla, un docente de
TIPO "E" que abriera `JUE2026` tenía que ver que ese período ya terminó (columna
`vigencia`), no suponer que pasaba lista sobre el período en curso — eso sigue igual.
Lo que cambió es que ya no hace falta elegir *entre* `JUE2026` y un eventual período
futuro del mismo nivel: ambos se listan, cada uno con su propia `vigencia`.

**3. El corte de 6 meses es una ventana de gracia para el selector, no una regla de
negocio del distributivo.** No borra ni oculta datos — un período cerrado hace más de
6 meses simplemente no aparece en `/api/periodos/por-nivel`; sigue existiendo y sigue
siendo consultable por otras rutas (reportes del inspector, por ejemplo) que no pasan
por este filtro.

### Implementación

El backend expone esto en `GET /api/periodos/por-nivel`. En `Application` conviene
resolverlo en dos pasos en vez de traducir el `HAVING` de arriba a LINQ:

1. Una consulta que agrupe por `(idNivel, idPeriodo)` con sus `MIN`/`MAX` y conteos.
2. La ventana de visibilidad (VIGENTE, FUTURO, o CERRADO ≤ 6 meses) se aplica en
   memoria — son ~6 niveles con unos pocos períodos cada uno, no hay costo.

Queda más legible y testeable que el `HAVING` correlacionado original. El SQL de arriba
se conserva en `database/queries/verificar-esquema-cplec.sql` (consulta 14) para
verificación directa contra la base, aunque ya no refleja el filtro final de "un
período por nivel" (ese `HAVING` quedó obsoleto con este cambio).

---

## Flags de matrícula a respetar

Medido sobre `OCC2025` (552 matrículas de la carrera 6): `retirado = 0`, `valida = 1`,
`esOyente = 0` en todas. Aun así el filtro va explícito, porque en períodos anteriores sí
hay variación:

```sql
AND COALESCE(m.retirado, 0) = 0     -- retirados fuera de la nómina
AND COALESCE(m.valida,   1) = 1     -- matrícula anulada fuera
-- m.esOyente = 1 → se muestra marcado como oyente, no se excluye
```

---

## Resumen del flujo del docente

```
1. Login                         → claim `sub` = idProfesor
2. GET /api/mis-paralelos        → paso 1  (solo asignaciones propias y activas)
3. Elige un paralelo             → idAsignacion
4. GET /api/paralelos/{id}/alumnos → paso 2  (nómina de ~30)
5. POST …/sesiones               → crea la sesión del día
                                   valida la ventana de fechas (paso 4)
                                   requiere `tema`, admite `observacion`
6. POST …/asistencias            → marca la lista (idempotente)
7. POST …/cerrar                 → congela la sesión
```

En los pasos 2 a 7 se revalida que `idAsignacion` pertenezca al distributivo del
docente del token. Ver [`03-autenticacion-rbac.md`](03-autenticacion-rbac.md) sección 5.

---

## Vigencia y anclaje de la asistencia (2026-08-08)

**Qué es "vigente".** Una asignación está vigente si su ventana
`fecha_inicial .. fecha_fin` cubre la fecha de referencia, con 15 días de gracia después
de `fecha_fin`; una ventana `NULL` cuenta como vigente (son 737 asignaciones activas de
carrera 6). **`periodos.activo` no sirve**: está en 1 en casi todos los períodos,
incluidos los de 2022. La regla vive en `MisParalelosService.ResolverAsync` y la
reutilizan `GET /api/mis-paralelos` y `GET /api/mi-horario`.

**Qué ancla la asistencia.** Una sesión cuelga de un bloque de `horario_detalle`, no de
una fecha. `idFecha` y `numeroBloque` se derivan del `idHorarioInicio`; el cliente no los
elige. Sin celdas de horario activas para la asignación, `POST .../sesiones` responde
`422 SIN_HORARIO`. El registro retroactivo sigue permitido sin tope, marcado con
`esTardia` y `diasRetraso` congelados en el primer guardado.

