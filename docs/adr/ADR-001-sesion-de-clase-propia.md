# ADR-001 — Sesión de clase propia en vez de reutilizar `horario_detalle`

- **Estado:** Aceptada — **enmendada parcialmente por
  [ADR-008](ADR-008-horarios-en-tabla-compartida.md)** (2026-08-07)
- **Fecha:** 2026-08-06
- **Autor:** dv_jb

> **Qué sigue vigente:** `cplec_sesiones` y `cplec_asistencias` son tablas propias, la
> sesión conserva el **grano día** y `matriculas_asistencias` sigue intacta.
>
> **Qué cambió:** ADR-008 revierte el punto "`horario_detalle` queda intacta: no se lee
> ni se escribe". El inspector ahora arma el horario del paralelo **escribiendo filas de
> carrera 6 en `horario_detalle`** (sin `ALTER TABLE`), y la sesión se ancla a un bloque
> contiguo de ese horario vía `cplec_sesiones.idHorarioInicio`. La alternativa descartada
> más abajo —"poblar `horario_detalle` con sesiones sintéticas"— sigue descartada: lo que
> se escribe ahí es **planificación**, no sesiones dictadas.
>
> **Corrección de un dato.** Más abajo este ADR afirma que `horario_detalle` tiene un
> `UNIQUE (activo, idEspacio, idAsignacion, idFecha, idhora)`. **Es falso.** El
> `SHOW CREATE TABLE` del 2026-08-07 muestra solo `PRIMARY KEY (idHorario)` y cinco `KEY`
> no únicas. La tabla no tiene ningún índice único. Esto no cambia la conclusión de
> ADR-001 —seguía siendo correcto no colgar la asistencia de una tabla vacía para la
> carrera 6— pero el argumento del `UNIQUE` no se sostiene, y quien lea el documento no
> debe apoyarse en él.

## Contexto

`sigafi_es` ya tiene dos mecanismos que parecen servir para registrar asistencia:

**`horario_detalle`** — la sesión de clase planificada. Estructura ideal:
`(idAsignacion, idFecha, idhora, idEspacio, tipoBloque)`, con FK a
`asignaciones_profesores`, `fechas_horarios`, `horas_clases` y `espacios`.
9 078 filas en total.

**`matriculas_asistencias`** — asistencia legacy, PK `(idMatricula, idFecha)`,
6 018 filas.

Antes de diseñar nada se midió qué hay realmente para la Escuela de Conducción
(`carreras.idCarrera = 6`):

```sql
SELECT COUNT(*) FROM horario_detalle hd
JOIN asignaciones_profesores ap ON ap.idAsignacion = hd.idAsignacion
JOIN cursos c ON c.idNivel = ap.idNivel
WHERE c.idCarrera = 6;
-- → 0
```

Cero. En cambio sí hay distributivo y matrículas en volumen:
549 asignaciones y 1 000 matrículas solo en el período `SEE2023`, con 41 docentes.

## Problema

- **`horario_detalle` está vacío para esta carrera.** La escuela de conducción no usa
  el módulo de horarios: no planifica sus clases en el sistema. Colgar la asistencia de
  ahí obligaría a construir primero un módulo de armado de horarios —fuera de alcance—
  o a insertar filas sintéticas en una tabla que es de otro sistema y tiene un
  `UNIQUE (activo, idEspacio, idAsignacion, idFecha, idhora)` pensado para otro flujo.
- **`matriculas_asistencias` tiene el grano equivocado.** Su PK es
  `(idMatricula, idFecha)`: un día, un alumno, una marca. Un alumno con dos asignaturas
  el mismo día no puede estar presente en una y ausente en otra. Tampoco distingue
  bloques horarios. Extenderla significaría cambiar su clave primaria —una tabla con
  6 018 filas usada por el sistema académico— rompiendo a quien la lea hoy.

## Decisión

Crear **`cplec_sesiones`** y **`cplec_asistencias`**, propias de este sistema.

- `cplec_sesiones` referencia `asignaciones_profesores(idAsignacion)` y
  `fechas_horarios(idFecha)` — reutiliza el distributivo y el calendario, que sí están
  poblados — pero **el docente crea la sesión en el momento de pasar lista**. No
  requiere planificación previa.
- **El grano es el día, no la hora.** No hay FK hacia `horas_clases` ni columnas de
  hora: el módulo de horarios queda fuera de alcance. La unicidad es
  `(idAsignacion, idFecha, numeroBloque)`, con `numeroBloque` por defecto en 1.
- El rango válido de fechas sale de `asignaciones_profesores.fecha_inicial .. fecha_fin`,
  que está poblado y en la carrera 6 son módulos cortos (2 a 6 semanas).
- `matriculas_asistencias` y `horario_detalle` quedan **intactas**: no se leen ni se
  escriben.

## Consecuencias

**A favor**

- El leccionario funciona desde el día uno, sin depender de que alguien cargue horarios.
- Grano correcto: asignatura + fecha + bloque + alumno.
- Ningún sistema existente cambia de comportamiento; riesgo de regresión ≈ 0.
- Auditoría propia (`cplec_asistencias_historial`) sin ensuciar tablas compartidas.

**En contra**

- Un tercer modelo de asistencia en la misma base. Se mitiga con el prefijo `cplec_`,
  los comentarios de tabla y este ADR.
- Si en el futuro la escuela adopta planificación (a nivel de día, no de hora), habrá
  que decidir si `cplec_sesiones` se prellena desde ahí. Sería una migración aditiva:
  la clave única ya contempla varias sesiones por día.
- Los reportes institucionales que hoy leen `matriculas_asistencias` no verán estos
  datos. Es intencional en la v1; si se pide consolidar, se hace con una vista.

## Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| Poblar `horario_detalle` con sesiones sintéticas | Escribir en una tabla de otro sistema, con un `UNIQUE` que incluye `idEspacio` y `activo`; alto riesgo de romper el módulo académico |
| Ampliar la PK de `matriculas_asistencias` | Cambio destructivo sobre 6 018 filas en uso por terceros |
| Agregar `idAsignacion` como columna nullable a `matriculas_asistencias` | Deja la tabla con dos semánticas conviviendo; ninguna consulta existente podría confiar en ella |
| Base de datos separada para `cplec` | Impide las FK hacia `matriculas` y `asignaciones_profesores`, que son la razón de reutilizar `sigafi_es` |
