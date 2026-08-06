-- =============================================================================
-- Migración : 002_cplec_sesiones
-- Sistema   : cplec — Leccionario e Inspección
-- Base      : sigafi_es
-- Motor     : MySQL 5.7.21 / InnoDB
-- Autor     : dv_jb
-- Fecha     : 2026-08-06
-- Rollback  : database/rollback/002_cplec_sesiones_rollback.sql
--
-- Objetivo  : Sesión de clase del leccionario. Una fila = una clase dictada por
--             un docente, para una asignación (distributivo) suya, en una fecha
--             y un bloque del día.
--
-- Por qué una tabla propia y no `horario_detalle`:
--   `horario_detalle` es la sesión planificada, pero para la carrera 6
--   (ESCUELA DE CONDUCCION) tiene 0 filas — la escuela no carga horario.
--   Ver docs/adr/ADR-001-sesion-de-clase-propia.md
--
-- NOTA sobre `numeroBloque`:
--   La unicidad NO puede colgar de `idHora` porque es opcional y en MySQL un
--   índice UNIQUE con NULL admite filas duplicadas. `numeroBloque` es NOT NULL
--   y ordena las sesiones dentro del día (1 = primera clase, 2 = segunda…).
-- =============================================================================

SET NAMES utf8mb4;
SET @OLD_SQL_MODE = @@SQL_MODE;
SET SQL_MODE = 'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION';

CREATE TABLE IF NOT EXISTS `cplec_sesiones` (
  `idSesion`            INT(11)      NOT NULL AUTO_INCREMENT,

  -- Vínculo con el distributivo: define docente + asignatura + paralelo + período
  `idAsignacion`        INT(11)      NOT NULL
                        COMMENT 'FK asignaciones_profesores.idAsignacion (UNIQUE, no es la PK compuesta)',
  -- Calendario institucional ya precargado (1 fila por día hasta 2026-12-31)
  `idFecha`             INT(11)      NOT NULL
                        COMMENT 'FK fechas_horarios.idFecha',

  `numeroBloque`        TINYINT(4)   NOT NULL DEFAULT 1
                        COMMENT 'Orden de la clase dentro del dia. Forma la clave unica.',
  `idHora`              INT(11)      NULL
                        COMMENT 'FK opcional horas_clases.idhora cuando el bloque horario existe',
  `horaInicio`          TIME         NULL,
  `horaFin`             TIME         NULL,

  `tipoBloque`          ENUM('teorico','practico','taller')
                                     NOT NULL DEFAULT 'teorico',
  `tema`                VARCHAR(250) NULL
                        COMMENT 'Tema dictado — el leccionario propiamente dicho',
  `observacion`         VARCHAR(500) NULL,

  `estado`              ENUM('borrador','cerrada')
                                     NOT NULL DEFAULT 'borrador'
                        COMMENT 'cerrada = congelada; solo un inspector puede reabrirla',
  `fechaCierre`         DATETIME     NULL,
  `activo`              TINYINT(1)   NOT NULL DEFAULT 1,

  `usuarioCreacion`     VARCHAR(25)  NOT NULL COMMENT 'usuarios.idSigafi del autor',
  `fechaCreacion`       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `usuarioActualiza`    VARCHAR(25)  NULL,
  `fechaActualizacion`  DATETIME     NULL ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`idSesion`),

  UNIQUE KEY `uq_cplec_sesiones_asignacion_fecha_bloque`
         (`idAsignacion`, `idFecha`, `numeroBloque`),

  KEY `ix_cplec_sesiones_fecha`          (`idFecha`),
  KEY `ix_cplec_sesiones_asignacion`     (`idAsignacion`, `activo`),
  KEY `ix_cplec_sesiones_estado_fecha`   (`estado`, `idFecha`),
  KEY `fk_cplec_sesiones_hora_idx`       (`idHora`),

  CONSTRAINT `fk_cplec_sesiones_asignacion`
    FOREIGN KEY (`idAsignacion`) REFERENCES `asignaciones_profesores` (`idAsignacion`)
    ON DELETE NO ACTION ON UPDATE NO ACTION,

  CONSTRAINT `fk_cplec_sesiones_fecha`
    FOREIGN KEY (`idFecha`) REFERENCES `fechas_horarios` (`idFecha`)
    ON DELETE NO ACTION ON UPDATE NO ACTION,

  CONSTRAINT `fk_cplec_sesiones_hora`
    FOREIGN KEY (`idHora`) REFERENCES `horas_clases` (`idhora`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB
  ROW_FORMAT=DYNAMIC
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='cplec — Sesion de clase del leccionario';

SET SQL_MODE = @OLD_SQL_MODE;

-- =============================================================================
-- VERIFICACIÓN
-- =============================================================================
-- SHOW CREATE TABLE cplec_sesiones;
-- SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
--  WHERE TABLE_SCHEMA='sigafi_es' AND TABLE_NAME='cplec_sesiones'
--    AND CONSTRAINT_TYPE='FOREIGN KEY';   -- esperado: 3
-- =============================================================================
