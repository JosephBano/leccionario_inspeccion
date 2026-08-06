-- =============================================================================
-- Rollback de : 003_cplec_asistencias.sql
-- Base        : sigafi_es  /  MySQL 5.7.21
--
-- ⚠️  DESTRUCTIVO: elimina todas las marcas de asistencia registradas.
--     Antes de correrlo en producción:
--
--       mysqldump -h <host> -P <port> -u <user> -p \
--         --single-transaction --no-create-info \
--         sigafi_es cplec_asistencias cplec_asistencias_historial \
--         > backup_cplec_asistencias_$(date +%Y%m%d_%H%M).sql
-- =============================================================================

SET FOREIGN_KEY_CHECKS = 0;

DROP TABLE IF EXISTS `cplec_asistencias_historial`;
DROP TABLE IF EXISTS `cplec_asistencias`;

SET FOREIGN_KEY_CHECKS = 1;

-- Verificación: ambas consultas deben devolver 0
-- SELECT COUNT(*) FROM information_schema.TABLES
--  WHERE TABLE_SCHEMA='sigafi_es' AND TABLE_NAME='cplec_asistencias';
-- SELECT COUNT(*) FROM information_schema.TABLES
--  WHERE TABLE_SCHEMA='sigafi_es' AND TABLE_NAME='cplec_asistencias_historial';
