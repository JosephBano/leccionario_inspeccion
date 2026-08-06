-- =============================================================================
-- Verificación del esquema `cplec` — correr después de aplicar las migraciones.
-- Todas las consultas deben dar el valor esperado indicado en el comentario.
-- =============================================================================

-- 1. Motor y sql_mode del servidor (esperado: 5.7.x, InnoDB)
SELECT VERSION() AS version, @@default_storage_engine AS engine, @@sql_mode AS sql_mode;

-- 2. Las tres tablas cplec_* existen (esperado: 3)
SELECT COUNT(*) AS tablas_cplec
FROM   information_schema.TABLES
WHERE  TABLE_SCHEMA = 'sigafi_es'
  AND  TABLE_NAME LIKE 'cplec\_%';

-- 3. Foreign keys creadas (esperado: 4 → 2 en sesiones + 2 en asistencias)
SELECT TABLE_NAME, CONSTRAINT_NAME, COLUMN_NAME,
       REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
FROM   information_schema.KEY_COLUMN_USAGE
WHERE  TABLE_SCHEMA = 'sigafi_es'
  AND  TABLE_NAME LIKE 'cplec\_%'
  AND  REFERENCED_TABLE_NAME IS NOT NULL
ORDER  BY TABLE_NAME, CONSTRAINT_NAME;

-- 4. Índices únicos que garantizan idempotencia (esperado: 2)
SELECT TABLE_NAME, INDEX_NAME, GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS columnas
FROM   information_schema.STATISTICS
WHERE  TABLE_SCHEMA = 'sigafi_es'
  AND  TABLE_NAME LIKE 'cplec\_%'
  AND  NON_UNIQUE = 0
  AND  INDEX_NAME <> 'PRIMARY'
GROUP  BY TABLE_NAME, INDEX_NAME;

-- 5. Charset de las tablas nuevas (esperado: utf8mb4_unicode_ci)
SELECT TABLE_NAME, TABLE_COLLATION, ROW_FORMAT, ENGINE
FROM   information_schema.TABLES
WHERE  TABLE_SCHEMA = 'sigafi_es' AND TABLE_NAME LIKE 'cplec\_%';

-- 6. RBAC: sistema registrado (esperado: 1 fila, codigo = 'cplec')
SELECT * FROM rbac_sistema WHERE codigo = 'cplec';

-- 7. RBAC: módulos del sistema (esperado: asistencia, reportes, admin)
SELECT m.idModulos, m.Nombre, m.esActivo
FROM   rbac_modulos m
JOIN   rbac_sistema s ON s.idSistema = m.id_sistema
WHERE  s.codigo = 'cplec';

-- 8. RBAC: grants por rol (esperado: cplec_docente = 4, cplec_inspector = 4)
SELECT r.codigo_rol, r.Nombre, COUNT(*) AS grants
FROM   rbac_rol r
JOIN   rbac_rol_modulo_operacion rmo ON rmo.idRol = r.idRol AND rmo.esActivo = 1
JOIN   rbac_modulos_operaciones  mo  ON mo.idModulosOperaciones = rmo.idModulosOperaciones
JOIN   rbac_modulos              m   ON m.idModulos = mo.idModulos
JOIN   rbac_sistema              s   ON s.idSistema = m.id_sistema
WHERE  s.codigo = 'cplec'
GROUP  BY r.codigo_rol, r.Nombre;

-- 9. RBAC: detalle de permisos (para revisar módulo × operación por rol)
SELECT r.codigo_rol, m.Nombre AS modulo, o.NombreOperacion AS operacion
FROM   rbac_rol r
JOIN   rbac_rol_modulo_operacion rmo ON rmo.idRol = r.idRol AND rmo.esActivo = 1
JOIN   rbac_modulos_operaciones  mo  ON mo.idModulosOperaciones = rmo.idModulosOperaciones
JOIN   rbac_modulos              m   ON m.idModulos = mo.idModulos
JOIN   rbac_operaciones          o   ON o.idOperaciones = mo.idOperaciones
JOIN   rbac_sistema              s   ON s.idSistema = m.id_sistema
WHERE  s.codigo = 'cplec'
ORDER  BY r.codigo_rol, m.Nombre, o.NombreOperacion;

-- 10. Usuarios con acceso al sistema cplec
SELECT u.idSigafi, u.nombre, u.tablaSigafi, u.activo, r.codigo_rol
FROM   usuarios u
JOIN   rbac_usuario_rol ur ON ur.idUsuario = u.idUsuario AND ur.esActivo = 1
JOIN   rbac_rol         r  ON r.idRol      = ur.idRol
WHERE  r.codigo_rol LIKE 'cplec\_%'
ORDER  BY r.codigo_rol, u.idSigafi;


-- =============================================================================
-- Diagnóstico de datos de la Escuela de Conducción (idCarrera = 6)
-- =============================================================================

-- 11. Períodos con distributivo cargado para la carrera 6
SELECT ap.idPeriodo,
       p.detalle,
       COUNT(*)                       AS asignaciones,
       COUNT(DISTINCT ap.idProfesor)  AS docentes,
       COUNT(DISTINCT ap.paralelo)    AS paralelos
FROM   asignaciones_profesores ap
JOIN   cursos   c ON c.idNivel   = ap.idNivel AND c.idCarrera = 6
JOIN   periodos p ON p.idPeriodo = ap.idPeriodo
GROUP  BY ap.idPeriodo, p.detalle
ORDER  BY ap.idPeriodo DESC;

-- 12. Alumnos por paralelo de un período (unidad de trabajo del docente)
SELECT ap.idAsignacion, ap.idProfesor, a.Asignatura,
       c.nivel AS tipoLicencia, s.seccion AS jornada, mo.modalidad, ap.paralelo,
       ap.fecha_inicial, ap.fecha_fin,
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
       AND TRIM(m.paralelo) = TRIM(ap.paralelo)
       AND COALESCE(m.retirado, 0) = 0
WHERE  ap.idPeriodo = 'OCC2025'          -- ← período objetivo
  AND  COALESCE(ap.activo, 1) = 1
GROUP  BY ap.idAsignacion, ap.idProfesor, a.Asignatura, c.nivel, s.seccion,
          mo.modalidad, ap.paralelo, ap.fecha_inicial, ap.fecha_fin
ORDER  BY alumnos DESC;

-- 13. Confirmación de que la carrera 6 no usa el módulo de horarios
--     (esperado hoy: 0 — es el motivo de ADR-001)
SELECT COUNT(*) AS sesiones_horario_detalle_carrera6
FROM   horario_detalle hd
JOIN   asignaciones_profesores ap ON ap.idAsignacion = hd.idAsignacion
JOIN   cursos c ON c.idNivel = ap.idNivel
WHERE  c.idCarrera = 6;

-- 14. Resolución del período: UN período por nivel, el más reciente.
--     Los tipos de licencia corren en calendarios independientes; no existe
--     "el período activo" del sistema, existe uno por nivel.
--     `periodos.activo` NO sirve: está en 1 en casi todos, incluidos los de 2022.
--
--     MySQL 5.7 no tiene funciones de ventana → el máximo por grupo se resuelve
--     con una clave compuesta (fecha_fin, fecha_inicial, idPeriodo) en el HAVING.
--     El desempate es OBLIGATORIO: el nivel 37 tiene dos períodos que terminan el
--     mismo día y sin él la consulta devuelve 7 filas en vez de 6.
--
--     Esperado: exactamente una fila por cada nivel de la carrera 6.
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
