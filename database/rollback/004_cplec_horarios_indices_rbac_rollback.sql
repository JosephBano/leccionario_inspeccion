-- =============================================================================
-- Rollback : 004_cplec_horarios_indices_rbac
--
-- NO borra los índices. `ix_horario_detalle_fecha_hora_activo` lo usa
-- gestion_academica para su query de conflictos; borrarlo degradaría otro
-- sistema en producción. Los índices son aditivos y sin efecto de negocio.
-- Este rollback revierte únicamente el RBAC.
-- =============================================================================

SET NAMES utf8mb4;
START TRANSACTION;

SET @idSistema := (SELECT `idSistema` FROM `rbac_sistema` WHERE `codigo` = 'cplec');

DELETE rmo FROM `rbac_rol_modulo_operacion` rmo
JOIN `rbac_modulos_operaciones` mo ON mo.`idModulosOperaciones` = rmo.`idModulosOperaciones`
JOIN `rbac_modulos` m ON m.`idModulos` = mo.`idModulos`
WHERE m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios';

DELETE mo FROM `rbac_modulos_operaciones` mo
JOIN `rbac_modulos` m ON m.`idModulos` = mo.`idModulos`
WHERE m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios';

DELETE FROM `rbac_modulos`
WHERE `id_sistema` = @idSistema AND `Nombre` = 'horarios';

COMMIT;
