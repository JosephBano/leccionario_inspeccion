-- =============================================================================
-- Migración : 001_cplec_rbac_seed
-- Sistema   : cplec — Leccionario e Inspección (Escuela de Conducción)
-- Base      : sigafi_es
-- Motor     : MySQL 5.7.21 / InnoDB
-- Autor     : dv_jb
-- Fecha     : 2026-08-06
-- Rollback  : database/rollback/001_cplec_rbac_seed_rollback.sql
--
-- Objetivo  : Registrar el sistema `cplec` en el RBAC compartido, con sus módulos,
--             operaciones y los dos roles iniciales:
--               - cplec_inspector : consulta transversal + descarga de reportes
--               - cplec_docente   : registra asistencia de SU distributivo
--
-- IMPORTANTE: este script es IDEMPOTENTE. Se puede correr varias veces sin duplicar.
--             No usa IDs literales: todo se resuelve por código/nombre.
-- =============================================================================

SET NAMES utf8mb4;
SET @OLD_SQL_MODE = @@SQL_MODE;
SET SQL_MODE = 'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION';

START TRANSACTION;

-- -----------------------------------------------------------------------------
-- 1. Sistema
-- -----------------------------------------------------------------------------
INSERT INTO `rbac_sistema` (`codigo`, `detalle`, `url`, `icono`)
SELECT 'cplec', 'Leccionario Escuela de Conduccion', 'http://localhost:4200', 'fact_check'
WHERE NOT EXISTS (SELECT 1 FROM `rbac_sistema` WHERE `codigo` = 'cplec');

SET @idSistema := (SELECT `idSistema` FROM `rbac_sistema` WHERE `codigo` = 'cplec');

-- -----------------------------------------------------------------------------
-- 2. Módulos del sistema
--    asistencia : pasar lista y gestionar sesiones de clase
--    reportes   : consultas y descargas
--    admin      : configuración del sistema
-- -----------------------------------------------------------------------------
INSERT INTO `rbac_modulos` (`id_sistema`, `Nombre`, `esActivo`)
SELECT @idSistema, 'asistencia', 1
WHERE NOT EXISTS (
    SELECT 1 FROM `rbac_modulos` WHERE `id_sistema` = @idSistema AND `Nombre` = 'asistencia');

INSERT INTO `rbac_modulos` (`id_sistema`, `Nombre`, `esActivo`)
SELECT @idSistema, 'reportes', 1
WHERE NOT EXISTS (
    SELECT 1 FROM `rbac_modulos` WHERE `id_sistema` = @idSistema AND `Nombre` = 'reportes');

INSERT INTO `rbac_modulos` (`id_sistema`, `Nombre`, `esActivo`)
SELECT @idSistema, 'admin', 1
WHERE NOT EXISTS (
    SELECT 1 FROM `rbac_modulos` WHERE `id_sistema` = @idSistema AND `Nombre` = 'admin');

-- -----------------------------------------------------------------------------
-- 3. Módulo × operación
--    Las operaciones (ver=1, editar=2, crear=3, eliminar=4) ya existen en
--    `rbac_operaciones`; no se crean nuevas.
--    Se genera el producto cartesiano módulos(cplec) × operaciones(1..4).
-- -----------------------------------------------------------------------------
INSERT INTO `rbac_modulos_operaciones`
       (`idModulos`, `idOperaciones`, `fecha_creacion`, `esActivo`)
SELECT m.`idModulos`, o.`idOperaciones`, CURDATE(), 1
FROM   `rbac_modulos` m
CROSS  JOIN `rbac_operaciones` o
WHERE  m.`id_sistema` = @idSistema
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_modulos_operaciones` mo
        WHERE mo.`idModulos` = m.`idModulos`
          AND mo.`idOperaciones` = o.`idOperaciones`);

-- -----------------------------------------------------------------------------
-- 4. Roles
--    `codigo_rol` es varchar(25) UNIQUE. Se respeta el prefijo por sistema
--    (grecuh_*, bien_*, gacad_*) que ya usa la institución.
-- -----------------------------------------------------------------------------
INSERT INTO `rbac_rol` (`Nombre`, `codigo_rol`, `esActivo`)
SELECT 'Inspector Leccionario', 'cplec_inspector', 1
WHERE NOT EXISTS (SELECT 1 FROM `rbac_rol` WHERE `codigo_rol` = 'cplec_inspector');

INSERT INTO `rbac_rol` (`Nombre`, `codigo_rol`, `esActivo`)
SELECT 'Docente Leccionario', 'cplec_docente', 1
WHERE NOT EXISTS (SELECT 1 FROM `rbac_rol` WHERE `codigo_rol` = 'cplec_docente');

SET @idRolInspector := (SELECT `idRol` FROM `rbac_rol` WHERE `codigo_rol` = 'cplec_inspector');
SET @idRolDocente   := (SELECT `idRol` FROM `rbac_rol` WHERE `codigo_rol` = 'cplec_docente');

-- -----------------------------------------------------------------------------
-- 5. Grants
-- -----------------------------------------------------------------------------

-- 5.1 INSPECTOR
--     asistencia -> ver, editar   (puede corregir/reabrir una sesión cerrada)
--     reportes   -> ver, crear    (crear = generar/descargar un reporte)
INSERT INTO `rbac_rol_modulo_operacion`
       (`idModulosOperaciones`, `idRol`, `fecha_asignacion`, `esActivo`)
SELECT mo.`idModulosOperaciones`, @idRolInspector, CURDATE(), 1
FROM   `rbac_modulos_operaciones` mo
JOIN   `rbac_modulos`   m ON m.`idModulos`     = mo.`idModulos`
JOIN   `rbac_operaciones` o ON o.`idOperaciones` = mo.`idOperaciones`
WHERE  m.`id_sistema` = @idSistema
  AND  (   (m.`Nombre` = 'asistencia' AND o.`NombreOperacion` IN ('ver','editar'))
        OR (m.`Nombre` = 'reportes'   AND o.`NombreOperacion` IN ('ver','crear')) )
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_rol_modulo_operacion` rmo
        WHERE rmo.`idModulosOperaciones` = mo.`idModulosOperaciones`
          AND rmo.`idRol` = @idRolInspector);

-- 5.2 DOCENTE
--     asistencia -> ver, crear, editar   (NO eliminar: la asistencia no se borra)
--     reportes   -> ver                  (solo sus propios paralelos)
INSERT INTO `rbac_rol_modulo_operacion`
       (`idModulosOperaciones`, `idRol`, `fecha_asignacion`, `esActivo`)
SELECT mo.`idModulosOperaciones`, @idRolDocente, CURDATE(), 1
FROM   `rbac_modulos_operaciones` mo
JOIN   `rbac_modulos`   m ON m.`idModulos`     = mo.`idModulos`
JOIN   `rbac_operaciones` o ON o.`idOperaciones` = mo.`idOperaciones`
WHERE  m.`id_sistema` = @idSistema
  AND  (   (m.`Nombre` = 'asistencia' AND o.`NombreOperacion` IN ('ver','crear','editar'))
        OR (m.`Nombre` = 'reportes'   AND o.`NombreOperacion` IN ('ver')) )
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_rol_modulo_operacion` rmo
        WHERE rmo.`idModulosOperaciones` = mo.`idModulosOperaciones`
          AND rmo.`idRol` = @idRolDocente);

COMMIT;

SET SQL_MODE = @OLD_SQL_MODE;

-- =============================================================================
-- VERIFICACIÓN (ejecutar después; debe devolver inspector=4 y docente=4)
-- =============================================================================
-- SELECT r.codigo_rol, COUNT(*) grants
-- FROM   rbac_rol r
-- JOIN   rbac_rol_modulo_operacion rmo ON rmo.idRol = r.idRol AND rmo.esActivo = 1
-- JOIN   rbac_modulos_operaciones  mo  ON mo.idModulosOperaciones = rmo.idModulosOperaciones
-- JOIN   rbac_modulos              m   ON m.idModulos = mo.idModulos
-- JOIN   rbac_sistema              s   ON s.idSistema = m.id_sistema
-- WHERE  s.codigo = 'cplec'
-- GROUP  BY r.codigo_rol;
-- =============================================================================
