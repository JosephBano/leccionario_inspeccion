# 00 — Visión y alcance

## Problema

La Escuela de Conducción del ISTPET (`carreras.idCarrera = 6`, "ESCUELA DE CONDUCCION")
opera dentro de la base `sigafi_es` con matrículas, cursos, paralelos y distributivo
docente ya cargados, pero **no tiene forma de registrar la asistencia por clase**.

Hallazgos verificados contra la BD de producción (2026-08-06):

| Hecho | Evidencia |
|---|---|
| La carrera 6 tiene volumen real de matrículas | `SEE2023` = 1000, `SEC2023` = 120, `SED2023` = 90, `SEE2019` = 240 |
| El distributivo docente existe para esa carrera | `asignaciones_profesores` × `cursos.idCarrera=6` → 549 asignaciones en `SEE2023`, 41 docentes |
| **No existe horario cargado para conducción** | `horario_detalle` ⋈ asignaciones de carrera 6 → **0 filas** |
| Existe una asistencia legacy, pero es de grano día | `matriculas_asistencias` (PK `idMatricula`,`idFecha`), 6 018 filas, sin asignatura ni hora |
| El flag `periodos.esConduccion` existe pero está sin usar | `SELECT ... WHERE esConduccion=1` → 0 filas |

**Consecuencia de diseño:** el leccionario no puede colgarse de `horario_detalle`
(no hay sesiones planificadas) ni de `matriculas_asistencias` (no distingue asignatura
ni hora). Necesita su propio concepto de *sesión de clase*, creado por el docente en el
momento de pasar lista. Ver [`02-modelo-datos.md`](02-modelo-datos.md).

---

## Alcance de la versión 1

### Dentro

- **Login** contra las credenciales existentes de `sigafi_es` (mismo mecanismo que
  `BienestarInstitucional.Api`: tabla `usuarios` + fallback legacy `profesores.clave`).
- **RBAC** por sistema `cplec` con dos roles:
  - `cplec_docente` — ve únicamente los paralelos de su propio distributivo y registra asistencia.
  - `cplec_inspector` — consulta transversal y descarga de reportes.
- **Registro de asistencia** por sesión de clase: presente / ausente / atraso / justificado,
  con observación opcional.
- **Consulta y reportes**: por docente, por paralelo y por estudiante, exportables.
- **Auditoría** de quién registró y modificó cada marca de asistencia.

### Fuera (v1)

- Prácticas de manejo y vehículos (`cond_alumnos_practicas`, `cond_alumnos_vehiculos`).
- Planificación de horarios (el docente elige fecha y hora al pasar lista; no hay
  módulo de armado de horario).
- Cálculo de notas o actas (`calificaciones`, `alumnos_acta_conduccion`).
- Migración o reescritura de `matriculas_asistencias` — se deja intacta.
- Notificaciones automáticas a estudiantes o representantes.

---

## Actores

| Actor | Rol RBAC | Qué hace | Cómo se identifica |
|---|---|---|---|
| Docente | `cplec_docente` | Abre un paralelo de su distributivo, crea la sesión del día y marca asistencia | `usuarios.idSigafi` = `profesores.idProfesor` |
| Inspector | `cplec_inspector` | Filtra por período/paralelo/docente/estudiante y descarga reportes | `usuarios.idSigafi`, sin restricción de distributivo |

El **aislamiento por distributivo** es la regla de autorización central: un docente
solo puede tocar sesiones cuyo `idAsignacion` pertenezca a una fila de
`asignaciones_profesores` con su `idProfesor` y `activo = 1`. Se valida **en el backend**,
en cada petición — nunca solo en el frontend.

---

## Definición de "listo" para la v1

- [ ] Un docente inicia sesión y ve solo sus paralelos del período activo.
- [ ] Un docente registra asistencia de una sesión y la puede corregir el mismo día.
- [ ] Un inspector descarga un reporte de asistencia por paralelo y por estudiante.
- [ ] Un docente **no puede** leer ni escribir asistencia de un paralelo ajeno (test que lo prueba).
- [ ] Los `.sql` de `database/migrations/` aplican limpio sobre una MySQL 5.7 vacía y sobre `sigafi_es`.
- [ ] `dotnet test` y `npm test` en verde en CI para todo PR hacia `develop`.

---

## Nota sobre el brief recibido

El pedido inicial incluía un párrafo sobre reorientar el proyecto "del área ganadera al
área porcina". Ese contenido **no corresponde a este sistema** (leccionario de una escuela
de conducción) y se asume traspapelado de otra conversación; no se incorporó a ningún
requerimiento. Si en efecto existe un requerimiento sectorial, hay que levantarlo aparte.
