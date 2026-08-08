-- =============================================================================
-- Migración : 005_cplec_sesiones_horario
-- Sistema   : cplec — Leccionario e Inspección
-- Base      : sigafi_es
-- Motor     : MySQL 5.7.21 / InnoDB
-- Fecha     : 2026-08-07
-- Rollback  : database/rollback/005_cplec_sesiones_horario_rollback.sql
--
-- Objetivo  : Anclar la sesión de clase a un bloque contiguo del horario y
--             registrar cuánto se demoró el docente en registrarla.
--
-- ALTER sobre tabla PROPIA (`cplec_sesiones`), vacía en desarrollo. Las tablas
-- de horarios NO se tocan.
--
-- idHorarioInicio es NULL-able por el modo transición: una asignación sin
-- horario cargado sigue aceptando sesiones con fecha libre. Ver ADR-008 dec. 9.
--
-- minutosPlanificados en vez de "horas": las franjas Z tienen duración libre
-- (las crea el inspector), así que contar franjas mentiría. Ver ADR-008 dec. 7.
--
-- diasRetraso se CONGELA al primer guardado. No se recalcula nunca.
-- =============================================================================

SET NAMES utf8mb4;
SET @OLD_SQL_MODE = @@SQL_MODE;
SET SQL_MODE = 'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION';

ALTER TABLE `cplec_sesiones`
  ADD COLUMN `idHorarioInicio`     INT(11)     NULL
      COMMENT 'FK horario_detalle.idHorario — identidad estable del bloque contiguo'
      AFTER `numeroBloque`,
  ADD COLUMN `franjasPlanificadas` TINYINT(4)  NULL
      COMMENT 'Cuantas franjas cubre el bloque'
      AFTER `idHorarioInicio`,
  ADD COLUMN `minutosPlanificados` SMALLINT(6) NULL
      COMMENT 'Suma de horas_clases.minutos del bloque'
      AFTER `franjasPlanificadas`,
  ADD COLUMN `esTardia`            TINYINT(1)  NOT NULL DEFAULT 0
      COMMENT 'Se registro despues del dia de clase. Congelado al crear.'
      AFTER `fechaCierre`,
  ADD COLUMN `diasRetraso`         SMALLINT(6) NOT NULL DEFAULT 0
      COMMENT 'Dias entre la clase y el primer guardado. Congelado al crear.'
      AFTER `esTardia`;

ALTER TABLE `cplec_sesiones`
  ADD CONSTRAINT `fk_cplec_sesiones_horario`
    FOREIGN KEY (`idHorarioInicio`) REFERENCES `horario_detalle` (`idHorario`)
    ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `cplec_sesiones`
  ADD INDEX `ix_cplec_sesiones_tardias` (`esTardia`, `idFecha`);

SET SQL_MODE = @OLD_SQL_MODE;

-- =============================================================================
-- VERIFICACIÓN
-- =============================================================================
-- SHOW CREATE TABLE cplec_sesiones;
-- SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
--  WHERE TABLE_SCHEMA='sigafi_es' AND TABLE_NAME='cplec_sesiones'
--    AND CONSTRAINT_TYPE='FOREIGN KEY';   -- esperado: 3
-- =============================================================================
