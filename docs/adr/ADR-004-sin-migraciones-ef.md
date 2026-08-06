# ADR-004 — Esquema por scripts `.sql`, sin migraciones de EF Core

- **Estado:** Aceptada
- **Fecha:** 2026-08-06
- **Autor:** dv_jb

## Contexto

`sigafi_es` es una base **legacy compartida** por al menos cinco sistemas:
`grecuh` (RRHH), `bien_proy062026` (Bienestar), `gacad` (Gestión Académica),
`gadmi` (Administración) y ahora `cplec`. Más de 300 tablas, MySQL **5.7.21**,
con datos de producción reales.

EF Core ofrece `dotnet ef migrations`, que genera el DDL a partir del modelo.

## Decisión

**El esquema se gestiona exclusivamente con scripts `.sql` versionados** en
`database/migrations/`, con su rollback correspondiente. EF Core se usa solo para
consultar y persistir datos. `dotnet ef migrations` está prohibido en este repositorio.

## Razones

1. **El modelo de EF no describe la base.** Se scaffoldean únicamente las ~15 tablas que
   este sistema usa. Una migración de EF compararía su modelo parcial contra la base
   real y propondría **eliminar las otras 285 tablas**. Un `database update` distraído
   sería catastrófico.
2. **Cinco sistemas, un esquema.** Si cada uno gestionara migraciones desde su propio
   modelo, se pisarían entre ellos. El `.sql` explícito es el único terreno común.
3. **Producción necesita un artefacto revisable.** El DBA aplica un archivo que puede
   leer, versionar y revertir. Un `dotnet ef database update` contra producción no es
   auditable ni reversible.
4. **EF genera DDL que 5.7 no siempre acepta.** El proveedor apunta a la versión
   configurada, y detalles como `ROW_FORMAT`, longitudes de índice con `utf8mb4` o
   defaults de `DATETIME` conviene controlarlos a mano.
5. **Las semillas de RBAC no son expresables como migración.** `001_cplec_rbac_seed.sql`
   resuelve IDs por código (`SET @idSistema := (SELECT …)`) porque los IDs de desarrollo
   y producción no coinciden. Eso es SQL, no un modelo.

## Consecuencias

- Cada cambio de esquema exige escribir el `.sql` a mano. Es más trabajo y es
  deliberado: obliga a pensar el cambio.
- **El modelo de EF y la base pueden desincronizarse.** Contramedida: tras aplicar una
  migración, se **regenera** el scaffolding con EF Core Power Tools y se commitea en el
  mismo PR. El CI compila y falla si el modelo no cuadra.
- Sin historial automático de migraciones. Lo reemplaza la tabla de estado de
  `database/README.md`, que se actualiza en el PR que agrega la migración.
- El hook `pre-commit` y el job `sql-lint` verifican que toda migración tenga rollback y
  que no se cuele sintaxis de MySQL 8.0.

## Alternativa descartada

**Migraciones EF con `--no-build` y revisión manual del SQL generado.** Aun revisando el
script, el `__EFMigrationsHistory` viviría en una base compartida donde otros cuatro
sistemas no lo esperan, y el modelo parcial seguiría proponiendo drops. El costo de
vigilarlo supera el ahorro.
