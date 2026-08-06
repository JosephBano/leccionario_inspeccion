-- =============================================================================
-- Migración : 003_cplec_asistencias
-- Sistema   : cplec — Leccionario e Inspección
-- Base      : sigafi_es
-- Motor     : MySQL 5.7.21 / InnoDB
-- Autor     : dv_jb
-- Fecha     : 2026-08-06
-- Depende de: 002_cplec_sesiones.sql
-- Rollback  : database/rollback/003_cplec_asistencias_rollback.sql
--
-- Objetivo  : Marca de asistencia por estudiante y sesión, más su historial de
--             cambios para auditoría de inspección.
--
-- La unidad del estudiante es `matriculas.idMatricula` (un alumno en un paralelo
-- y período concretos), NO `alumnos.idAlumno`: un mismo alumno puede estar
-- matriculado en varios paralelos/períodos.
--
-- IDEMPOTENCIA: `uq_cplec_asistencias_sesion_matricula` permite que el endpoint
-- de registro use INSERT ... ON DUPLICATE KEY UPDATE. Reenviar la misma lista no
-- duplica filas.
-- =============================================================================

SET NAMES utf8mb4;
SET @OLD_SQL_MODE = @@SQL_MODE;
SET SQL_MODE = 'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION';

-- -----------------------------------------------------------------------------
-- 1. Marca de asistencia
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `cplec_asistencias` (
  `idAsistencia`        INT(11)      NOT NULL AUTO_INCREMENT,
  `idSesion`            INT(11)      NOT NULL COMMENT 'FK cplec_sesiones.idSesion',
  `idMatricula`         INT(11)      NOT NULL COMMENT 'FK matriculas.idMatricula',

  `estado`              ENUM('presente','ausente','atraso','justificado')
                                     NOT NULL DEFAULT 'presente',
  `minutosAtraso`       SMALLINT(5) UNSIGNED NULL
                        COMMENT 'Solo aplica cuando estado = atraso',
  `observacion`         VARCHAR(200) NULL,

  `usuarioCreacion`     VARCHAR(25)  NOT NULL COMMENT 'usuarios.idSigafi del autor',
  `fechaCreacion`       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `usuarioActualiza`    VARCHAR(25)  NULL,
  `fechaActualizacion`  DATETIME     NULL ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`idAsistencia`),

  UNIQUE KEY `uq_cplec_asistencias_sesion_matricula` (`idSesion`, `idMatricula`),

  -- Reporte "historial de un estudiante": filtra por matrícula y ordena por estado
  KEY `ix_cplec_asistencias_matricula_estado` (`idMatricula`, `estado`),

  CONSTRAINT `fk_cplec_asistencias_sesion`
    FOREIGN KEY (`idSesion`) REFERENCES `cplec_sesiones` (`idSesion`)
    ON DELETE CASCADE ON UPDATE NO ACTION,

  CONSTRAINT `fk_cplec_asistencias_matricula`
    FOREIGN KEY (`idMatricula`) REFERENCES `matriculas` (`idMatricula`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB
  ROW_FORMAT=DYNAMIC
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='cplec — Asistencia por estudiante y sesion';

-- -----------------------------------------------------------------------------
-- 2. Historial de cambios
--
--    Se escribe DESDE LA APLICACIÓN, no por trigger: el usuario responsable sale
--    del claim `sub` del JWT, no de CURRENT_USER() (la app se conecta con un solo
--    usuario de base). Un trigger registraría siempre el mismo responsable y la
--    auditoría no serviría para nada.
--
--    Sin FK hacia cplec_asistencias: si una sesión se elimina en cascada, el
--    rastro de auditoría debe sobrevivir.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `cplec_asistencias_historial` (
  `idHistorial`         BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT,
  `idAsistencia`        INT(11)      NOT NULL,
  `idSesion`            INT(11)      NOT NULL COMMENT 'Desnormalizado: sobrevive al borrado en cascada',
  `idMatricula`         INT(11)      NOT NULL COMMENT 'Desnormalizado por el mismo motivo',

  `estadoAnterior`      VARCHAR(15)  NULL COMMENT 'NULL = alta inicial',
  `estadoNuevo`         VARCHAR(15)  NOT NULL,
  `motivo`              VARCHAR(250) NULL,

  `usuario`             VARCHAR(25)  NOT NULL COMMENT 'usuarios.idSigafi tomado del JWT',
  `rol`                 VARCHAR(25)  NULL     COMMENT 'cplec_docente | cplec_inspector',
  `ipAddress`           VARCHAR(45)  NULL,
  `fecha`               DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,

  PRIMARY KEY (`idHistorial`),
  KEY `ix_cplec_hist_asistencia` (`idAsistencia`),
  KEY `ix_cplec_hist_sesion`     (`idSesion`),
  KEY `ix_cplec_hist_fecha`      (`fecha`)
) ENGINE=InnoDB
  ROW_FORMAT=DYNAMIC
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='cplec — Auditoria de cambios de asistencia';

SET SQL_MODE = @OLD_SQL_MODE;

-- =============================================================================
-- VERIFICACIÓN
-- =============================================================================
-- SHOW CREATE TABLE cplec_asistencias;
-- SHOW CREATE TABLE cplec_asistencias_historial;
--
-- -- No debe permitir dos marcas del mismo alumno en la misma sesión:
-- -- INSERT INTO cplec_asistencias (idSesion,idMatricula,estado,usuarioCreacion)
-- --   VALUES (1,1,'presente','test'), (1,1,'ausente','test');   -- error 1062 esperado
-- =============================================================================
