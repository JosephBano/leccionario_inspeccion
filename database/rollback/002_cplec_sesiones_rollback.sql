-- =============================================================================
-- Rollback de : 002_cplec_sesiones.sql
-- Base        : sigafi_es  /  MySQL 5.7.21
--
-- ⚠️  Correr PRIMERO el rollback 003 (cplec_asistencias tiene FK hacia esta tabla).
--     Si 003 sigue aplicado, este script falla con error 1217 — y está bien que falle.
--
--     Backup previo:
--       mysqldump -h <host> -P <port> -u <user> -p \
--         --single-transaction sigafi_es cplec_sesiones \
--         > backup_cplec_sesiones_$(date +%Y%m%d_%H%M).sql
-- =============================================================================

DROP TABLE IF EXISTS `cplec_sesiones`;

-- Verificación: debe devolver 0
-- SELECT COUNT(*) FROM information_schema.TABLES
--  WHERE TABLE_SCHEMA='sigafi_es' AND TABLE_NAME='cplec_sesiones';
