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
--             un docente, para una asignación suya del distributivo, en un DÍA.
--
-- GRANO: día, no hora. La Escuela de Conducción no usa el módulo de horarios
--        (`horario_detalle` tiene 0 filas para idCarrera = 6). No hay FK hacia
--        `horas_clases` ni columnas de hora: si más adelante se necesita
--        planificación por día, se agrega en una migración aparte.
--        Ver docs/adr/ADR-001-sesion-de-clase-propia.md
--
-- VENTANA VÁLIDA: la fecha de la sesión debe caer dentro de
--        `asignaciones_profesores.fecha_inicial .. fecha_fin` de esa asignación.
--        En la carrera 6 esos rangos son cortos y reales (módulos de 2 a 6
--        semanas: p. ej. la asignación 23229 va del 2025-10-06 al 2025-11-05).
--        NO se puede expresar con un CHECK: MySQL 5.7 los parsea pero NO los
--        aplica, y además la regla cruza tablas. Se valida en la aplicación
--        (`SesionService`) y está cubierta por tests.
--
-- `numeroBloque`: permite más de una clase de la misma asignación el mismo día.
--        El caso normal es 1 y la UI ni lo muestra. Se incluye desde el inicio
--        porque forma parte de la clave única: agregarlo después obligaría a
--        recrear el índice sobre una tabla con datos.
-- =============================================================================

SET NAMES utf8mb4;
SET @OLD_SQL_MODE = @@SQL_MODE;
SET SQL_MODE = 'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION';

CREATE TABLE IF NOT EXISTS `cplec_sesiones` (
  `idSesion`            INT(11)      NOT NULL AUTO_INCREMENT,

  -- Define docente + asignatura + tipo de licencia + jornada + modalidad + paralelo
  `idAsignacion`        INT(11)      NOT NULL
                        COMMENT 'FK asignaciones_profesores.idAsignacion (columna UNIQUE, no la PK compuesta)',
  -- Calendario institucional ya precargado (1 fila por día hasta 2026-12-31)
  `idFecha`             INT(11)      NOT NULL
                        COMMENT 'FK fechas_horarios.idFecha — el dia de la clase',

  `numeroBloque`        TINYINT(4)   NOT NULL DEFAULT 1
                        COMMENT 'Orden de la clase dentro del dia. Normalmente 1.',

  `tema`                VARCHAR(250) NOT NULL
                        COMMENT 'Tema general de la clase — el leccionario propiamente dicho',
  `observacion`         VARCHAR(500) NULL
                        COMMENT 'Observaciones generales de la sesion',

  `estado`              ENUM('borrador','cerrada')
                                     NOT NULL DEFAULT 'borrador'
                        COMMENT 'cerrada = congelada; solo un inspector puede reabrirla',
  `fechaCierre`         DATETIME     NULL,
  `activo`              TINYINT(1)   NOT NULL DEFAULT 1,

  `usuarioCreacion`     VARCHAR(25)  NOT NULL COMMENT 'usuarios.idSigafi del docente',
  `fechaCreacion`       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `usuarioActualiza`    VARCHAR(25)  NULL,
  `fechaActualizacion`  DATETIME     NULL ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`idSesion`),

  UNIQUE KEY `uq_cplec_sesiones_asignacion_fecha_bloque`
         (`idAsignacion`, `idFecha`, `numeroBloque`),

  KEY `ix_cplec_sesiones_fecha`        (`idFecha`),
  KEY `ix_cplec_sesiones_asignacion`   (`idAsignacion`, `activo`),
  KEY `ix_cplec_sesiones_estado_fecha` (`estado`, `idFecha`),

  CONSTRAINT `fk_cplec_sesiones_asignacion`
    FOREIGN KEY (`idAsignacion`) REFERENCES `asignaciones_profesores` (`idAsignacion`)
    ON DELETE NO ACTION ON UPDATE NO ACTION,

  CONSTRAINT `fk_cplec_sesiones_fecha`
    FOREIGN KEY (`idFecha`) REFERENCES `fechas_horarios` (`idFecha`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB
  ROW_FORMAT=DYNAMIC
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='cplec — Sesion de clase del leccionario (grano: dia)';

SET SQL_MODE = @OLD_SQL_MODE;

-- =============================================================================
-- VERIFICACIÓN
-- =============================================================================
-- SHOW CREATE TABLE cplec_sesiones;
-- SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
--  WHERE TABLE_SCHEMA='sigafi_es' AND TABLE_NAME='cplec_sesiones'
--    AND CONSTRAINT_TYPE='FOREIGN KEY';   -- esperado: 2
-- =============================================================================
