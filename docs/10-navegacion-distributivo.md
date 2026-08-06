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

Casi todos los períodos tienen `activo = 1`, incluidos los de 2022. Para resolver el
período por defecto se usa la **fecha de hoy contra el rango de la asignación**:

```sql
WHERE CURDATE() BETWEEN ap.fecha_inicial AND ap.fecha_fin
```

y si eso no devuelve nada, se ofrece el selector de períodos ordenado por
`MAX(ap.fecha_fin)` descendente.

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
docente del token. Ver [`03-autenticacion-rbac.md`](03-autenticacion-rbac.md) § 5.
