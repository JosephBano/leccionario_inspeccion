# 02 — Modelo de datos

Base: **`sigafi_es`** en **MySQL 5.7.21**, InnoDB, `sql_mode` de producción:

```
ONLY_FULL_GROUP_BY, STRICT_TRANS_TABLES, NO_ZERO_IN_DATE, NO_ZERO_DATE,
ERROR_FOR_DIVISION_BY_ZERO, NO_AUTO_CREATE_USER, NO_ENGINE_SUBSTITUTION
```

Todo DDL que se escriba debe aplicar limpio con ese `sql_mode`. En particular:
**no usar `DEFAULT '0000-00-00 00:00:00'`** (lo hace la tabla legacy
`matriculas_asistencias`, pero fallaría hoy en un `CREATE TABLE`).

---

## Tablas legacy que se reutilizan (solo lectura)

Ninguna de estas se modifica. El leccionario las consulta.

| Tabla | PK | Para qué la usamos |
|---|---|---|
| `usuarios` | `idUsuario` | Padrón de login. `idSigafi` es la cédula/código; `tablaSigafi` ∈ (`alumno`,`profesor`,`otros`) |
| `profesores` | `idProfesor` varchar(14) | Datos del docente. `clave` es la credencial legacy; `esReal` distingue docentes reales |
| `alumnos` | `idAlumno` varchar(14) | Datos del estudiante |
| `matriculas` | `idMatricula` int | **Un alumno en un paralelo/período.** Es la unidad de asistencia |
| `asignaciones_profesores` | PK compuesta + `idAsignacion` UNIQUE | **El distributivo.** Un docente ↔ asignatura ↔ período ↔ modalidad ↔ sección ↔ nivel ↔ paralelo |
| `cursos` | `idNivel` | Nivel/curso. `idCarrera = 6` ⇒ Escuela de Conducción |
| `carreras` | `idCarrera` | `6` = "ESCUELA DE CONDUCCION" |
| `asignaturas` | `idAsignatura` | Nombre de la materia |
| `periodos` | `idPeriodo` char(7) | Período académico. Flag `esConduccion` (hoy sin usar) |
| `fechas_horarios` | `idFecha` | **Calendario precargado**, una fila por día hasta 2026-12-31 (`idFecha` 1..3713) |
| `secciones` | `idSeccion` | Jornada: matutina / nocturna / vespertina / fin de semana |
| `modalidades` | `idModalidad` | Presencial / en línea / híbrida / semipresencial |
| `rbac_*` (7 tablas) | — | Sistema, módulos, operaciones, roles, asignaciones |

### Qué significan estas tablas en la carrera 6

Los nombres genéricos engañan. Para la Escuela de Conducción:

| Tabla | Significado real | Valores |
|---|---|---|
| `cursos.nivel` | **Tipo de licencia** | `TIPO "C"`, `TIPO "D"`, `TIPO "E"`, sus convalidadas y `RECUPERACION PUNTOS` |
| `secciones.seccion` | **Jornada** | `MATUTINA`, `NOCTURNA`, `VESPERTINA`, `FIN SEMAN` |
| `modalidades.modalidad` | Modalidad | `PRESENCIAL`, `EN LINEA`, `HIBRIDA`, `SEMIPRESENCIAL` |

### Cómo se identifica un "paralelo"

No hay una tabla `paralelos`. Un paralelo es la **tupla**:

```
(idPeriodo, idNivel, idSeccion, idModalidad, paralelo)
```

Tanto `matriculas` como `asignaciones_profesores` la llevan. Por eso el join
"alumnos de la clase que dicta este docente" es:

```sql
SELECT m.idMatricula, m.idAlumno
FROM asignaciones_profesores ap
JOIN matriculas m
  ON  m.idPeriodo   = ap.idPeriodo
  AND m.idNivel     = ap.idNivel
  AND m.idSeccion   = ap.idSeccion
  AND m.idModalidad = ap.idModalidad
  AND m.paralelo    = ap.paralelo
WHERE ap.idAsignacion = ?
  AND COALESCE(m.retirado, 0) = 0
  AND COALESCE(m.valida,   1) = 1;
```

**Las 5 columnas son obligatorias.** Medido sobre datos reales: el join completo
devuelve 29–30 alumnos por paralelo; omitiendo `idSeccion` y `idModalidad` devuelve
75–90. Distintas jornadas reusan la misma letra de paralelo, así que un join reducido
haría que el docente pasara lista a alumnos de otra jornada. Evidencia en
[`10-navegacion-distributivo.md`](10-navegacion-distributivo.md) sección Paso 2.

> Sobre los tipos: `asignaciones_profesores.paralelo` es `char(1)` y
> `matriculas.paralelo` es `varchar(10)`. Se verificó que en la carrera 6 todos los
> valores tienen longitud 1, así que `=` funciona. Aun así se compara con `TRIM()` en
> ambos lados: cuesta nada y protege de que el sistema académico cargue `'A '`.

---

## Qué existe ya sobre asistencia y por qué no alcanza

| Tabla existente | Grano | Por qué no sirve |
|---|---|---|
| `matriculas_asistencias` (6 018 filas) | `(idMatricula, idFecha)` — un día completo | No distingue asignatura ni hora. Un alumno con dos materias el mismo día tendría una sola marca |
| `horario_detalle` | `(idAsignacion, idFecha, idhora)` — la sesión ideal | **0 filas para la carrera 6.** La escuela de conducción no carga horario |
| `horario_profesores` | asistencia **del docente** | Otro sujeto, otro caso de uso |
| `calificaciones.faltasi1..4` | agregados por parcial | Es un resumen, no el registro de origen |

**Decisión:** el leccionario define su propia sesión de clase. Ver
[ADR-001](adr/ADR-001-sesion-de-clase-propia.md). `matriculas_asistencias` queda
intacta; no se lee ni se escribe.

---

## Tablas nuevas (`cplec_*`)

Prefijo `cplec_` para que sean rastreables dentro de una base con 300+ tablas
compartidas entre sistemas.

### `cplec_sesiones` — una clase dictada

**Grano: día.** Sin horas ni bloques horarios — la Escuela de Conducción no usa el
módulo de horarios. Si más adelante se necesita planificación, se agrega por migración.

```
idSesion        INT PK AUTO_INCREMENT
idAsignacion    INT      NOT NULL  → asignaciones_profesores(idAsignacion)
idFecha         INT      NOT NULL  → fechas_horarios(idFecha)
numeroBloque    TINYINT  NOT NULL DEFAULT 1     -- normalmente 1; la UI no lo muestra
tema            VARCHAR(250) NOT NULL  -- tema general de la clase (el leccionario)
observacion     VARCHAR(500) NULL
estado          ENUM('borrador','cerrada') NOT NULL DEFAULT 'borrador'
fechaCierre     DATETIME NULL
activo          TINYINT(1) NOT NULL DEFAULT 1
usuarioCreacion / fechaCreacion / usuarioActualiza / fechaActualizacion

UNIQUE (idAsignacion, idFecha, numeroBloque)
```

- `tema` es **obligatorio**: es el propósito del leccionario. Una sesión sin tema no es
  un registro de clase.
- `numeroBloque` cubre el caso de dos clases de la misma asignación el mismo día. Va
  desde el inicio porque forma parte de la clave única: agregarlo después obligaría a
  recrear el índice sobre una tabla con datos.
- `estado = 'cerrada'` congela la sesión: a partir de ahí solo un inspector puede
  reabrirla. Es lo que da validez al reporte.
- **La fecha debe caer dentro de `asignaciones_profesores.fecha_inicial .. fecha_fin`**.
  No se implementa con `CHECK` (MySQL 5.7 los parsea pero no los aplica, y la regla
  cruza tablas): se valida en `SesionService`. Ver
  [`10-navegacion-distributivo.md`](10-navegacion-distributivo.md) sección Paso 4.

### `cplec_asistencias` — la marca por estudiante

```
idAsistencia    INT PK AUTO_INCREMENT
idSesion        INT NOT NULL  → cplec_sesiones(idSesion) ON DELETE CASCADE
idMatricula     INT NOT NULL  → matriculas(idMatricula)
estado          ENUM('presente','ausente','atraso','justificado') NOT NULL DEFAULT 'presente'
minutosAtraso   SMALLINT UNSIGNED NULL
observacion     VARCHAR(200) NULL
usuarioCreacion / fechaCreacion / usuarioActualiza / fechaActualizacion

UNIQUE (idSesion, idMatricula)
```

`UNIQUE (idSesion, idMatricula)` es la garantía de idempotencia: el endpoint de
registro hace `INSERT … ON DUPLICATE KEY UPDATE`, así que reenviar la misma lista no
duplica filas.

### `cplec_asistencias_historial` — auditoría de cambios

Cada modificación de un `estado` deja rastro. Un inspector debe poder responder
"¿quién cambió esta falta a justificada y cuándo?".

```
idHistorial     BIGINT PK AUTO_INCREMENT
idAsistencia    INT NOT NULL
estadoAnterior  VARCHAR(15) NULL
estadoNuevo     VARCHAR(15) NOT NULL
motivo          VARCHAR(250) NULL
usuario         VARCHAR(25) NOT NULL
fecha           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
```

Se escribe desde la aplicación (no por trigger), para que el usuario responsable venga
del JWT y no de `CURRENT_USER()`.

---

## Diagrama de relaciones

```
carreras(6) ──< cursos ──< matriculas >── periodos
                   │           │
                   │           └──────────────< cplec_asistencias >── cplec_sesiones
                   │                                   │                    │
profesores ──< asignaciones_profesores ────────────────┼────────────────────┘
                                                       │
                            cplec_asistencias_historial ┘

usuarios ──< rbac_usuario_rol >── rbac_rol ──< rbac_rol_modulo_operacion
                                                      │
                             rbac_modulos_operaciones ┴──< rbac_modulos >── rbac_sistema('cplec')
```

---

## Charset

Las tablas legacy son `latin1`. Las nuevas `cplec_*` se crean en
**`utf8mb4` / `utf8mb4_unicode_ci`** con `ROW_FORMAT=DYNAMIC`.

Es seguro porque **todas las FK son sobre columnas `INT`** — no hay join por texto entre
una tabla nueva y una legacy, que es el único caso donde la mezcla de collations rompe
(`Illegal mix of collations`). Si en el futuro alguien agrega una FK sobre `varchar`
hacia una tabla legacy, esa columna concreta debe declararse
`CHARACTER SET latin1 COLLATE latin1_swedish_ci`.

---

## Convenciones de esquema

1. **Prefijo `cplec_`** en toda tabla nueva de este sistema.
2. **Nombres de columna en `camelCase`** para PK/FK (`idSesion`, `idMatricula`) siguiendo
   el estilo dominante de `sigafi_es`. Se acepta `snake_case` solo si se replica una
   columna legacy existente.
3. **Cuatro columnas de auditoría en toda tabla transaccional**:
   `usuarioCreacion`, `fechaCreacion`, `usuarioActualiza`, `fechaActualizacion`.
4. **Nunca `DELETE` físico** de asistencia. Se usa `activo` en la sesión.
5. **Todo índice y constraint lleva nombre explícito** (`ix_…`, `uq_…`, `fk_…`), para que
   el rollback pueda referenciarlos.
