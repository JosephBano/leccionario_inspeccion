-- =============================================================================
-- Plantilla operativa (NO es una migración)
-- Asignar roles `cplec` a usuarios concretos.
--
-- No va en database/migrations/ porque depende de personas, no del esquema:
-- cambia por institución y por período. Se ejecuta a mano y se deja registro
-- en el ticket correspondiente.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- A. ¿El usuario ya existe en el padrón `usuarios`?
--    Reemplazar '1234567890' por la cédula / idSigafi.
-- -----------------------------------------------------------------------------
SELECT u.idUsuario, u.idSigafi, u.tablaSigafi, u.nombre, u.activo,
       GROUP_CONCAT(r.codigo_rol) AS rolesActuales
FROM   usuarios u
LEFT   JOIN rbac_usuario_rol ur ON ur.idUsuario = u.idUsuario AND ur.esActivo = 1
LEFT   JOIN rbac_rol         r  ON r.idRol      = ur.idRol
WHERE  u.idSigafi = '1234567890'
GROUP  BY u.idUsuario, u.idSigafi, u.tablaSigafi, u.nombre, u.activo;

-- Si NO devuelve filas: el usuario todavía no está en `usuarios`. No hay que
-- insertarlo a mano — la API lo crea sola en el primer login exitoso contra la
-- credencial legacy (`profesores.clave`). Ver docs/03-autenticacion-rbac.md.
-- Después de ese primer login, volver a correr el bloque B.

-- -----------------------------------------------------------------------------
-- B. Asignar el rol DOCENTE a un usuario ya existente (idempotente)
-- -----------------------------------------------------------------------------
INSERT INTO rbac_usuario_rol (idUsuario, idRol, fecha_creacion, esActivo)
SELECT u.idUsuario, r.idRol, CURDATE(), 1
FROM   usuarios u
CROSS  JOIN rbac_rol r
WHERE  u.idSigafi   = '1234567890'      -- ← cédula del docente
  AND  r.codigo_rol = 'cplec_docente'
  AND  NOT EXISTS (SELECT 1 FROM rbac_usuario_rol ur
                   WHERE ur.idUsuario = u.idUsuario AND ur.idRol = r.idRol);

-- -----------------------------------------------------------------------------
-- C. Asignar el rol INSPECTOR
-- -----------------------------------------------------------------------------
INSERT INTO rbac_usuario_rol (idUsuario, idRol, fecha_creacion, esActivo)
SELECT u.idUsuario, r.idRol, CURDATE(), 1
FROM   usuarios u
CROSS  JOIN rbac_rol r
WHERE  u.idSigafi   = '0987654321'      -- ← cédula del inspector
  AND  r.codigo_rol = 'cplec_inspector'
  AND  NOT EXISTS (SELECT 1 FROM rbac_usuario_rol ur
                   WHERE ur.idUsuario = u.idUsuario AND ur.idRol = r.idRol);

-- -----------------------------------------------------------------------------
-- D. Alta masiva: rol DOCENTE a todo profesor con distributivo activo en la
--    Escuela de Conducción (idCarrera = 6) para un período dado.
--
--    ⚠️  Revisar el SELECT antes de correr el INSERT.
-- -----------------------------------------------------------------------------
-- Paso 1 — revisar a quién afectaría:
SELECT DISTINCT u.idUsuario, u.idSigafi, p.apellidos, p.nombres
FROM   asignaciones_profesores ap
JOIN   cursos     c ON c.idNivel    = ap.idNivel AND c.idCarrera = 6
JOIN   profesores p ON p.idProfesor = ap.idProfesor
JOIN   usuarios   u ON u.idSigafi   = ap.idProfesor
WHERE  ap.idPeriodo = 'SEE2023'         -- ← período objetivo
  AND  COALESCE(ap.activo, 1) = 1
  AND  u.activo = 1;

-- Paso 2 — aplicar (mismo filtro):
-- INSERT INTO rbac_usuario_rol (idUsuario, idRol, fecha_creacion, esActivo)
-- SELECT DISTINCT u.idUsuario, r.idRol, CURDATE(), 1
-- FROM   asignaciones_profesores ap
-- JOIN   cursos   c ON c.idNivel  = ap.idNivel AND c.idCarrera = 6
-- JOIN   usuarios u ON u.idSigafi = ap.idProfesor
-- CROSS  JOIN rbac_rol r
-- WHERE  ap.idPeriodo = 'SEE2023'
--   AND  COALESCE(ap.activo, 1) = 1
--   AND  u.activo = 1
--   AND  r.codigo_rol = 'cplec_docente'
--   AND  NOT EXISTS (SELECT 1 FROM rbac_usuario_rol ur
--                    WHERE ur.idUsuario = u.idUsuario AND ur.idRol = r.idRol);

-- -----------------------------------------------------------------------------
-- E. Revocar un rol (soft: no se borra la fila, se desactiva)
-- -----------------------------------------------------------------------------
-- UPDATE rbac_usuario_rol ur
-- JOIN   usuarios u ON u.idUsuario = ur.idUsuario
-- JOIN   rbac_rol r ON r.idRol     = ur.idRol
-- SET    ur.esActivo = 0, ur.fecha_modificacion = CURDATE()
-- WHERE  u.idSigafi = '1234567890' AND r.codigo_rol = 'cplec_docente';
