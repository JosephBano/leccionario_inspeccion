-- =============================================================================
-- Rollback de : 001_cplec_rbac_seed.sql
-- Base        : sigafi_es  /  MySQL 5.7.21
--
-- Quita el sistema `cplec` del RBAC compartido, en orden inverso al de creación.
--
-- ⚠️  `rbac_rol`, `rbac_operaciones` y `usuarios` son COMPARTIDAS con los demás
--     sistemas (grecuh, bien_*, gacad, gadmi). Este script toca EXCLUSIVAMENTE
--     las filas cuyo sistema es `cplec` y los dos roles con prefijo `cplec_`.
--     No borra operaciones (ver/editar/crear/eliminar): son de uso común.
-- =============================================================================

SET @OLD_SQL_MODE = @@SQL_MODE;
SET SQL_MODE = 'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION';

START TRANSACTION;

SET @idSistema := (SELECT `idSistema` FROM `rbac_sistema` WHERE `codigo` = 'cplec');

-- 1. Asignaciones usuario ↔ rol de este sistema
DELETE ur FROM `rbac_usuario_rol` ur
JOIN `rbac_rol` r ON r.`idRol` = ur.`idRol`
WHERE r.`codigo_rol` IN ('cplec_inspector', 'cplec_docente');

-- 2. Grants rol ↔ módulo/operación
DELETE rmo FROM `rbac_rol_modulo_operacion` rmo
JOIN `rbac_modulos_operaciones` mo ON mo.`idModulosOperaciones` = rmo.`idModulosOperaciones`
JOIN `rbac_modulos` m ON m.`idModulos` = mo.`idModulos`
WHERE m.`id_sistema` = @idSistema;

-- 3. Módulo × operación
DELETE mo FROM `rbac_modulos_operaciones` mo
JOIN `rbac_modulos` m ON m.`idModulos` = mo.`idModulos`
WHERE m.`id_sistema` = @idSistema;

-- 4. Roles del sistema
DELETE FROM `rbac_rol` WHERE `codigo_rol` IN ('cplec_inspector', 'cplec_docente');

-- 5. Módulos
DELETE FROM `rbac_modulos` WHERE `id_sistema` = @idSistema;

-- 6. Sistema
DELETE FROM `rbac_sistema` WHERE `codigo` = 'cplec';

COMMIT;

SET SQL_MODE = @OLD_SQL_MODE;

-- Verificación: debe devolver 0 filas
-- SELECT * FROM rbac_sistema WHERE codigo = 'cplec';
-- SELECT * FROM rbac_rol     WHERE codigo_rol LIKE 'cplec\_%';
