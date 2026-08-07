# Cómo ejecutar las migraciones — paso a paso

Guía operativa. Las **reglas** para escribir migraciones están en
[`../README.md`](../README.md); esto es el procedimiento para **correrlas**.

> **Antes de empezar:** confirmá contra qué base vas a ejecutar. `sigafi_es` es una base
> **compartida por cinco sistemas** (`grecuh`, `bien_proy062026`, `gacad`, `gadmi`,
> `cplec`) y hoy desarrollo apunta a la misma instancia con datos reales de personas.

---

## Orden de aplicación

Las migraciones se aplican **en orden numérico** y son **dependientes**:

```
001_cplec_rbac_seed.sql     Sistema cplec, módulos, roles y permisos en el RBAC
002_cplec_sesiones.sql      Tabla cplec_sesiones
003_cplec_asistencias.sql   Tablas cplec_asistencias y cplec_asistencias_historial
                            ↳ tiene FK hacia cplec_sesiones: 002 va SÍ o SÍ antes
```

Los rollbacks se aplican **en orden inverso** (003 → 002 → 001).

---

## Paso 0 — Verificar a qué base te estás conectando

```sql
SELECT DATABASE() AS base, VERSION() AS version, @@hostname AS host, @@sql_mode;
```

Esperado: `sigafi_es`, `5.7.21`, y el `sql_mode` con
`STRICT_TRANS_TABLES` y `NO_ZERO_DATE`.

Si la versión no es 5.7.x, **pará**: las migraciones están escritas para 5.7 y algunas
validaciones del CI dependen de eso.

---

## Paso 1 — Backup (siempre, también en desarrollo)

```bash
mysqldump -h <host> -P 3307 -u <user> -p \
  --single-transaction --routines --triggers \
  sigafi_es > backup_sigafi_es_$(date +%Y%m%d_%H%M).sql
```

Si solo querés respaldar lo que estas migraciones tocan (más rápido, suficiente para
un rollback de `cplec`):

```bash
mysqldump -h <host> -P 3307 -u <user> -p --single-transaction sigafi_es \
  rbac_sistema rbac_modulos rbac_modulos_operaciones rbac_rol \
  rbac_rol_modulo_operacion rbac_usuario_rol \
  > backup_rbac_$(date +%Y%m%d_%H%M).sql
```

Los `.sql` de backup están en `.gitignore`: **no se commitean** (contienen datos reales).

---

## Paso 2 — Aplicar

Elegí el método según lo que tengas instalado.

### Opción A — cliente `mysql` (lo normal)

```bash
cd <raíz del repo>

for f in database/migrations/*.sql; do
  echo ">>> $f"
  mysql -h <host> -P 3307 -u <user> -p sigafi_es < "$f" || { echo "FALLÓ en $f"; break; }
done
```

El `|| break` importa: si 002 falla, **no** hay que seguir con 003.

### Opción B — Docker (si no tenés el cliente instalado)

```bash
docker run --rm -i mysql:5.7 \
  mysql -h <host> -P 3307 -u <user> -p<pass> sigafi_es \
  < database/migrations/001_cplec_rbac_seed.sql
```

> En el equipo de desarrollo actual esto **no funciona**: el daemon de Docker tiene la
> red rota (`failed to create endpoint ... operation not supported` al crear el par
> veth). Si te pasa lo mismo, usá la opción C.

### Opción C — runner .NET (sin cliente `mysql` ni Docker)

Un programa de ~20 líneas con `MySqlConnector`. Es el método con el que se aplicaron
estas migraciones por primera vez.

```bash
mkdir -p /tmp/runner && cd /tmp/runner
dotnet new console -o . --force
dotnet add package MySqlConnector
```

`Program.cs`:

```csharp
using MySqlConnector;

// AllowUserVariables=true es OBLIGATORIO: las semillas usan `SET @idSistema := (...)`.
var cs = "Server=<host>;Port=3307;Database=sigafi_es;User=<user>;Password=<pass>"
       + ";AllowPublicKeyRetrieval=true;SslMode=None;ConnectionTimeout=20;AllowUserVariables=true";

if (args.Length == 0) { Console.Error.WriteLine("uso: runner <archivo.sql>"); return 2; }

var sql = File.ReadAllText(args[0]);
Console.WriteLine($"=== Aplicando {Path.GetFileName(args[0])} ===");

await using var conn = new MySqlConnection(cs);
await conn.OpenAsync();
try
{
    await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 120 };
    Console.WriteLine($"OK — filas afectadas: {await cmd.ExecuteNonQueryAsync()}");
    return 0;
}
catch (MySqlException ex)
{
    Console.Error.WriteLine($"ERROR {ex.Number}: {ex.Message}");
    return 1;
}
```

```bash
dotnet run -- <repo>/database/migrations/001_cplec_rbac_seed.sql
dotnet run -- <repo>/database/migrations/002_cplec_sesiones.sql
dotnet run -- <repo>/database/migrations/003_cplec_asistencias.sql
```

**No dejes la contraseña en ese archivo si lo guardás.** El runner es una herramienta
descartable: vive en `/tmp`, no en el repositorio.

---

## Paso 3 — Verificar

```bash
mysql -h <host> -P 3307 -u <user> -p sigafi_es \
  < database/queries/verificar-esquema-cplec.sql
```

Valores esperados:

| # | Consulta | Esperado |
|---|---|---|
| 2 | Tablas `cplec_*` | **3** |
| 3 | Foreign keys | **4** (2 en sesiones + 2 en asistencias) |
| 4 | Índices únicos | **2** |
| 5 | Charset | `utf8mb4_unicode_ci`, `Dynamic`, `InnoDB` |
| 6 | Sistema RBAC | 1 fila, `codigo = 'cplec'` |
| 7 | Módulos | `asistencia`, `reportes`, `admin` |
| 8 | Grants | `cplec_docente = 4`, `cplec_inspector = 4` |
| 14 | Período por nivel | 1 fila por nivel de la carrera 6 |

---

## Paso 4 — Dejar constancia

Marcá la migración como aplicada en la tabla **Estado actual** de
[`../README.md`](../README.md). Esa tabla reemplaza al `__EFMigrationsHistory` que no
tenemos (ver [ADR-004](../../docs/adr/ADR-004-sin-migraciones-ef.md)): es la única forma
de saber qué está aplicado en cada entorno.

---

## Repetir una migración es seguro

Las tres son **idempotentes** — verificado ejecutándolas dos veces seguidas:
la segunda corrida afectó **0 filas**.

- Las tablas usan `CREATE TABLE IF NOT EXISTS`.
- Las semillas usan `INSERT ... SELECT ... WHERE NOT EXISTS`.
- Ningún ID está escrito a mano: `idSistema`, `idRol` y `idModulos` se resuelven por
  código con `SET @x := (SELECT ...)`. Por eso el mismo script sirve en desarrollo y en
  producción, donde los IDs **no** coinciden.

Si dudás de si una migración ya se aplicó, **volvé a correrla**. Es más seguro que
adivinar.

---

# Rollback

## Qué es y cuándo usarlo

Cada migración tiene un archivo espejo en [`../rollback/`](../rollback/) con el mismo
número, que **deshace** ese cambio:

```
migrations/002_cplec_sesiones.sql  ←→  rollback/002_cplec_sesiones_rollback.sql
```

Se usa cuando:

- Una migración se aplicó por error en el entorno equivocado.
- Hay que volver a una versión anterior del backend y el esquema nuevo estorba.
- Se está reconstruyendo un entorno de pruebas desde cero.

**No** se usa para "limpiar datos": para eso está el `DELETE` correspondiente. Un
rollback tira la estructura completa.

## Orden inverso, sin excepciones

```
003_cplec_asistencias_rollback.sql   ← primero
002_cplec_sesiones_rollback.sql
001_cplec_rbac_seed_rollback.sql     ← último
```

`cplec_asistencias` tiene una FK hacia `cplec_sesiones`. Si intentás correr el rollback
de 002 antes que el de 003, MySQL falla con **error 1217**
(`Cannot delete or update a parent row`). **Que falle está bien**: es la base
protegiéndote de dejar una FK huérfana. Corré el 003 y volvé a intentar.

## Qué tan destructivo es cada uno

| Rollback | Qué borra | Riesgo |
|---|---|---|
| `003` | `cplec_asistencias` y `cplec_asistencias_historial` | **Alto** — se pierde toda la asistencia registrada y su auditoría |
| `002` | `cplec_sesiones` | **Alto** — se pierden las clases y sus temas |
| `001` | Sistema, módulos, roles y grants de `cplec` en el RBAC | **Medio** — los usuarios pierden el acceso; los datos no se tocan |

Antes de correr `003` o `002` en producción, **extraé los datos**. El comando exacto
está comentado al inicio de cada archivo de rollback:

```bash
mysqldump -h <host> -P 3307 -u <user> -p \
  --single-transaction --no-create-info \
  sigafi_es cplec_asistencias cplec_asistencias_historial \
  > backup_cplec_asistencias_$(date +%Y%m%d_%H%M).sql
```

## El rollback de 001 toca tablas compartidas

`rbac_rol`, `rbac_operaciones` y `usuarios` las usan los otros cuatro sistemas. El
script está acotado para borrar **solo**:

- las filas cuyo sistema es `cplec`,
- los dos roles con prefijo `cplec_` (`cplec_inspector`, `cplec_docente`),
- y las asignaciones usuario↔rol de esos dos roles.

**No** borra `rbac_operaciones` (`ver`, `editar`, `crear`, `eliminar`): son de uso común.
Si borrás esas cuatro filas, rompés Bienestar, GACAD, GADMI y RRHH a la vez.

Efecto colateral esperado: los docentes e inspectores que tuvieran el rol asignado lo
**pierden**. Volver a aplicar `001` recrea el sistema y los roles, pero **no** reasigna
las personas — eso se rehace con
[`../queries/asignar-roles-cplec.sql`](../queries/asignar-roles-cplec.sql).

## Cómo correr un rollback

Mismo método que una migración, en orden inverso:

```bash
mysql -h <host> -P 3307 -u <user> -p sigafi_es < database/rollback/003_cplec_asistencias_rollback.sql
mysql -h <host> -P 3307 -u <user> -p sigafi_es < database/rollback/002_cplec_sesiones_rollback.sql
mysql -h <host> -P 3307 -u <user> -p sigafi_es < database/rollback/001_cplec_rbac_seed_rollback.sql
```

Verificación (las tres deben devolver 0):

```sql
SELECT COUNT(*) FROM information_schema.TABLES
 WHERE TABLE_SCHEMA='sigafi_es' AND TABLE_NAME LIKE 'cplec\_%';
SELECT COUNT(*) FROM rbac_sistema WHERE codigo = 'cplec';
SELECT COUNT(*) FROM rbac_rol     WHERE codigo_rol LIKE 'cplec\_%';
```

Después, actualizá la tabla **Estado actual** de [`../README.md`](../README.md).

---

## Errores frecuentes

| Error | Causa | Solución |
|---|---|---|
| **1215** `Cannot add foreign key constraint` | Falta una tabla legacy (`asignaciones_profesores`, `fechas_horarios`, `matriculas`) | Estás sobre una base vacía. Ver [`../README.md`](../README.md) sección Cómo replicar el esquema |
| **1217** `Cannot delete or update a parent row` | Rollback en orden equivocado | Corré primero el rollback de 003 |
| **1062** `Duplicate entry` | Correcto: la clave única está haciendo su trabajo | Si aparece al aplicar una migración, revisá que no hayas editado la semilla |
| **1064** `You have an error in your SQL syntax` cerca de `WITH` / `OVER` | Sintaxis de MySQL 8.0 | Producción es 5.7.21. Sin CTEs ni funciones de ventana |
| `Parameter '@OLD_SQL_MODE' must be defined` | El driver no tiene variables de usuario habilitadas | Agregá `AllowUserVariables=true` a la cadena de conexión (opción C) |
| **1045** `Access denied` | Credenciales o host equivocados | Verificá la cadena en `appsettings.Development.json` |

---

## Registro de la primera aplicación

Aplicadas por primera vez el **2026-08-06** sobre `sigafi_es` (`100.121.210.68:3307`,
MySQL 5.7.21), con el runner de la opción C.

Deltas medidos contra la foto previa:

| Tabla | Antes | Después | Δ |
|---|---|---|---|
| `rbac_sistema` | 4 | 5 | +1 |
| `rbac_modulos` | 19 | 22 | +3 |
| `rbac_modulos_operaciones` | 76 | 88 | +12 (3 módulos × 4 operaciones) |
| `rbac_rol` | 21 | 23 | +2 |
| `rbac_rol_modulo_operacion` | 129 | 137 | +8 (4 grants × 2 roles) |
| `rbac_usuario_rol` | 77 | 77 | **0** — 001 no asigna personas |

Pruebas ejecutadas contra el esquema recién creado, con datos reales de la asignación
`23229` (GEOGRAFÍA DEL ECUADOR, TIPO "C", nocturna, paralelo C):

- Idempotencia: segunda corrida de las tres migraciones → 0 filas afectadas.
- Carga de la nómina real → 29 marcas `presente`, coincide con el conteo del paralelo.
- Sesión duplicada → `1062` en `uq_cplec_sesiones_asignacion_fecha_bloque`.
- Asistencia duplicada → `1062` en `uq_cplec_asistencias_sesion_matricula`.
- `idMatricula` inexistente → `1452` en `fk_cplec_asistencias_matricula`.
- `idAsignacion` inexistente → `1452` en `fk_cplec_sesiones_asignacion`.
- `DELETE` de la sesión → las 29 asistencias se borraron en cascada.

Las filas de prueba se eliminaron y los `AUTO_INCREMENT` se reiniciaron en 1. Las tres
tablas quedaron vacías.
