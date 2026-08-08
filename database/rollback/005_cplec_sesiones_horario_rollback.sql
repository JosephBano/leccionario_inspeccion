-- =============================================================================
-- Rollback : 005_cplec_sesiones_horario
-- Devuelve `cplec_sesiones` al estado de la migración 002.
-- DESTRUCTIVO: se pierden idHorarioInicio, franjasPlanificadas,
-- minutosPlanificados, esTardia y diasRetraso de todas las sesiones.
-- =============================================================================

SET NAMES utf8mb4;

ALTER TABLE `cplec_sesiones` DROP FOREIGN KEY `fk_cplec_sesiones_horario`;
ALTER TABLE `cplec_sesiones` DROP INDEX `ix_cplec_sesiones_tardias`;
ALTER TABLE `cplec_sesiones`
  DROP COLUMN `idHorarioInicio`,
  DROP COLUMN `franjasPlanificadas`,
  DROP COLUMN `minutosPlanificados`,
  DROP COLUMN `esTardia`,
  DROP COLUMN `diasRetraso`;
