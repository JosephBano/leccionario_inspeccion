# Base de datos — `sigafi_es` (MySQL 5.7.21)

Esta carpeta es **la única fuente de verdad del esquema**. Lo que no está acá, no
existe en producción.

```
database/
├── migrations/   Cambios de esquema, numerados y ordenados. Se aplican en orden.
│   └── README.md ← PASO A PASO para ejecutarlos y usar el rollback
├── rollback/     Un archivo por migración, mismo número. Deshace el cambio.
└── queries/      Consultas operativas y de verificación. No modifican el esquema.
```

> **¿Vas a correr un `.sql`?** Empezá por
> [`migrations/README.md`](migrations/README.md): tiene el procedimiento completo,
> tres métodos de ejecución, la verificación esperada y cómo usar los rollbacks.

---

## Reglas

1. **Numeración correlativa e inmutable.** `NNN_descripcion.sql`. Una vez que un
   archivo se mergeó a `develop`, **no se edita**: se crea el siguiente número.
2. **Cada migración tiene su rollback**, con el mismo número, en `rollback/`.
   Un PR que agrega `004_x.sql` sin `004_x_rollback.sql` no se aprueba.
3. **MySQL 5.7, no 8.0.** Prohibido: `CHECK` constraints con efecto, columnas
   `JSON` con índices funcionales, CTEs (`WITH`), funciones de ventana,
   `ALTER TABLE ... RENAME COLUMN`, `CREATE INDEX IF NOT EXISTS`.
4. **`sql_mode` de producción** (incluye `STRICT_TRANS_TABLES` y `NO_ZERO_DATE`).
   Cada script lo fija explícitamente al inicio. No usar `DEFAULT '0000-00-00'`.
5. **Idempotencia**: `CREATE TABLE IF NOT EXISTS`, e `INSERT ... WHERE NOT EXISTS`
   para semillas. Correr dos veces la misma migración no debe romper nada.
6. **Sin IDs literales en las semillas.** Los `idSistema`, `idRol`, `idModulos` se
   resuelven por código/nombre en variables (`SET @x := (SELECT …)`). Los IDs de
   desarrollo y producción no coinciden.
7. **Nunca `dotnet ef migrations`.** `sigafi_es` es una base legacy compartida entre
   cinco sistemas. EF Core se usa **solo para leer/escribir**, jamás para migrar.
   Ver [ADR-004](../docs/adr/ADR-004-sin-migraciones-ef.md).
8. **Tablas nuevas con prefijo `cplec_`.**
9. **No se modifican tablas de otros sistemas.** Si hace falta una columna en una
   tabla compartida (`matriculas`, `asignaciones_profesores`, …), va en un PR
   aparte, con aviso al responsable de ese sistema y justificación en el ADR.

---

## Estado actual

| # | Script | Qué hace | Aplicado en dev | Aplicado en prod |
|---|---|---|---|---|
| 001 | `001_cplec_rbac_seed.sql` | Registra el sistema `cplec`, 3 módulos, grants y los roles `cplec_inspector` / `cplec_docente` | ✅ 2026-08-06 | ☐ |
| 002 | `002_cplec_sesiones.sql` | Tabla `cplec_sesiones` (sesión de clase) | ✅ 2026-08-06 | ☐ |
| 003 | `003_cplec_asistencias.sql` | Tablas `cplec_asistencias` y `cplec_asistencias_historial` | ✅ 2026-08-06 | ☐ |

> Mantener esta tabla al día es parte del PR que agrega la migración.

Detalle de la primera aplicación (deltas medidos y pruebas de constraints) en
[`migrations/README.md`](migrations/README.md) sección Registro de la primera aplicación.

**Pendiente antes de usar el sistema:** asignar los roles `cplec_docente` y
`cplec_inspector` a personas reales con
[`queries/asignar-roles-cplec.sql`](queries/asignar-roles-cplec.sql). La migración 001
crea los roles pero **no** se los da a nadie.

---

## Cómo aplicar

Resumen. El procedimiento completo está en
[`migrations/README.md`](migrations/README.md).

```bash
# 1. Backup SIEMPRE (producción y desarrollo)
mysqldump -h <host> -P 3307 -u <user> -p --single-transaction \
  --routines --triggers sigafi_es > backup_sigafi_es_$(date +%Y%m%d_%H%M).sql

# 2. Aplicar en orden
for f in database/migrations/*.sql; do
  echo ">>> $f"
  mysql -h <host> -P 3307 -u <user> -p sigafi_es < "$f" || break
done

# 3. Verificar (las consultas de verificación están comentadas al pie de cada script)
mysql -h <host> -P 3307 -u <user> -p sigafi_es < database/queries/verificar-esquema-cplec.sql
```

Si no hay cliente `mysql` instalado:

```bash
docker run --rm -i mysql:5.7 mysql -h <host> -P 3307 -u <user> -p<pass> sigafi_es < archivo.sql
```

---

## Cómo replicar el esquema en un entorno limpio

Los `cplec_*` tienen FK hacia tablas legacy (`asignaciones_profesores`,
`fechas_horarios`, `matriculas`). Una base vacía **no** las tiene, así
que las migraciones fallarán con error 1215. Para levantar un entorno de pruebas:

```bash
# Estructura + datos de las tablas legacy necesarias
mysqldump -h <host> -P 3307 -u <user> -p sigafi_es \
  usuarios profesores alumnos matriculas asignaciones_profesores \
  cursos carreras asignaturas periodos fechas_horarios secciones modalidades \
  rbac_sistema rbac_modulos rbac_operaciones rbac_modulos_operaciones \
  rbac_rol rbac_rol_modulo_operacion rbac_usuario_rol rbac_refresh_tokens \
  > seed_legacy_minimo.sql

# Luego: seed_legacy_minimo.sql, después migrations/001..003
```

`seed_legacy_minimo.sql` contiene datos personales reales — **no se commitea**
(está en `.gitignore`).

---

## Tablas legacy que NO se tocan

`matriculas_asistencias`, `horario_detalle`, `horario_profesores`, `calificaciones`,
`cond_*`, y todo lo demás. El leccionario convive con ellas sin modificarlas.
El motivo está en [`docs/02-modelo-datos.md`](../docs/02-modelo-datos.md).
