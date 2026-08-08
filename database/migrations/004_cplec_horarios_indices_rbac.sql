-- =============================================================================
-- Migración : 004_cplec_horarios_indices_rbac
-- Sistema   : cplec — Leccionario e Inspección
-- Base      : sigafi_es
-- Motor     : MySQL 5.7.21 / InnoDB
-- Fecha     : 2026-08-07
-- Rollback  : database/rollback/004_cplec_horarios_indices_rbac_rollback.sql
--
-- Objetivo  : Soporte para el módulo de horarios del inspector (ADR-008).
--             SIN cambios estructurales: solo índices de performance y RBAC.
--
-- IDEMPOTENTE. Los índices probablemente YA existen: los creó el script 004 de
-- gestion_academica sobre esta misma base. Se incluyen para que el esquema sea
-- reproducible desde cero. MySQL 5.7 no soporta la cláusula IF NOT EXISTS al crear índices, así
-- que se usa el patrón PREPARE/EXECUTE sobre INFORMATION_SCHEMA.
--
-- NO se crea ningún índice UNIQUE sobre horario_detalle. Es una tabla
-- compartida con ~1781 filas de otros sistemas; un UNIQUE sobre datos que nunca
-- lo tuvieron puede fallar por duplicados ajenos, y la decisión le corresponde a
-- quien gobierna la tabla. La unicidad la garantiza la aplicación
-- (ADR-008 decisión 6).
-- =============================================================================

SET NAMES utf8mb4;
SET @OLD_SQL_MODE = @@SQL_MODE;
SET SQL_MODE = 'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION';

-- -----------------------------------------------------------------------------
-- 1. Índices de soporte (idempotentes)
-- -----------------------------------------------------------------------------
SET @ddl = (
  SELECT IF(COUNT(*) = 0,
    'ALTER TABLE `horario_detalle` ADD INDEX `ix_horario_detalle_fecha_hora_activo` (`idFecha`,`idhora`,`activo`)',
    'SELECT ''ix_horario_detalle_fecha_hora_activo ya existe'' AS info')
  FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'horario_detalle'
    AND INDEX_NAME = 'ix_horario_detalle_fecha_hora_activo');
PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;

SET @ddl = (
  SELECT IF(COUNT(*) = 0,
    'ALTER TABLE `fechas_horarios` ADD INDEX `ix_fechas_horarios_fecha` (`fecha`)',
    'SELECT ''ix_fechas_horarios_fecha ya existe'' AS info')
  FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fechas_horarios'
    AND INDEX_NAME = 'ix_fechas_horarios_fecha');
PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;

-- -----------------------------------------------------------------------------
-- 2. Módulo RBAC `horarios`
-- -----------------------------------------------------------------------------
START TRANSACTION;

SET @idSistema := (SELECT `idSistema` FROM `rbac_sistema` WHERE `codigo` = 'cplec');

INSERT INTO `rbac_modulos` (`id_sistema`, `Nombre`, `esActivo`)
SELECT @idSistema, 'horarios', 1
WHERE NOT EXISTS (
    SELECT 1 FROM `rbac_modulos` WHERE `id_sistema` = @idSistema AND `Nombre` = 'horarios');

INSERT INTO `rbac_modulos_operaciones`
       (`idModulos`, `idOperaciones`, `fecha_creacion`, `esActivo`)
SELECT m.`idModulos`, o.`idOperaciones`, CURDATE(), 1
FROM   `rbac_modulos` m
CROSS  JOIN `rbac_operaciones` o
WHERE  m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios'
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_modulos_operaciones` mo
        WHERE mo.`idModulos` = m.`idModulos` AND mo.`idOperaciones` = o.`idOperaciones`);

SET @idRolInspector := (SELECT `idRol` FROM `rbac_rol` WHERE `codigo_rol` = 'cplec_inspector');
SET @idRolDocente   := (SELECT `idRol` FROM `rbac_rol` WHERE `codigo_rol` = 'cplec_docente');

-- Inspector: ver, crear, editar, eliminar
INSERT INTO `rbac_rol_modulo_operacion`
       (`idModulosOperaciones`, `idRol`, `fecha_asignacion`, `esActivo`)
SELECT mo.`idModulosOperaciones`, @idRolInspector, CURDATE(), 1
FROM   `rbac_modulos_operaciones` mo
JOIN   `rbac_modulos`     m ON m.`idModulos`     = mo.`idModulos`
JOIN   `rbac_operaciones` o ON o.`idOperaciones` = mo.`idOperaciones`
WHERE  m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios'
  AND  o.`NombreOperacion` IN ('ver','crear','editar','eliminar')
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_rol_modulo_operacion` rmo
        WHERE rmo.`idModulosOperaciones` = mo.`idModulosOperaciones`
          AND rmo.`idRol` = @idRolInspector);

-- Docente: solo ver. NO crea ni edita horarios: eso es del inspector.
INSERT INTO `rbac_rol_modulo_operacion`
       (`idModulosOperaciones`, `idRol`, `fecha_asignacion`, `esActivo`)
SELECT mo.`idModulosOperaciones`, @idRolDocente, CURDATE(), 1
FROM   `rbac_modulos_operaciones` mo
JOIN   `rbac_modulos`     m ON m.`idModulos`     = mo.`idModulos`
JOIN   `rbac_operaciones` o ON o.`idOperaciones` = mo.`idOperaciones`
WHERE  m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios'
  AND  o.`NombreOperacion` = 'ver'
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_rol_modulo_operacion` rmo
        WHERE rmo.`idModulosOperaciones` = mo.`idModulosOperaciones`
          AND rmo.`idRol` = @idRolDocente);

COMMIT;

SET SQL_MODE = @OLD_SQL_MODE;

-- =============================================================================
-- VERIFICACIÓN (esperado: inspector=4, docente=1)
-- =============================================================================
-- SELECT r.codigo_rol, COUNT(*) grants
-- FROM   rbac_rol r
-- JOIN   rbac_rol_modulo_operacion rmo ON rmo.idRol = r.idRol AND rmo.esActivo = 1
-- JOIN   rbac_modulos_operaciones  mo  ON mo.idModulosOperaciones = rmo.idModulosOperaciones
-- JOIN   rbac_modulos              m   ON m.idModulos = mo.idModulos AND m.Nombre = 'horarios'
-- JOIN   rbac_sistema              s   ON s.idSistema = m.id_sistema AND s.codigo = 'cplec'
-- GROUP  BY r.codigo_rol;
-- =============================================================================
