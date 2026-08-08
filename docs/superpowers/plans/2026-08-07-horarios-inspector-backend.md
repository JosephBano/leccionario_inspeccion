# Horarios del inspector — backend (M4b-1 … M4b-6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que el inspector arme el horario de un paralelo escribiendo filas de carrera 6 en `horario_detalle`, y que la asistencia del docente cuelgue de ese horario registrando cuánto se demoró.

**Architecture:** Servicios en `Application/Horarios/`, patrón interfaz + implementación en el mismo archivo como `SesionService`. Dos guards de frontera de datos (`HorarioCarreraGuard`, `FranjaZGuard`) análogos a `DistributivoGuard`. Los conflictos se detectan por **solapamiento de rangos horarios**, no por igualdad de `idhora`. Como `horario_detalle` no tiene ningún índice único, la correctitud de la escritura vive en una transacción `Serializable` con reintento ante deadlock.

**Tech Stack:** ASP.NET Core net8.0 · EF Core 8 + Pomelo · MySQL 5.7.21 (`sigafi_es`) · MSTest + Moq + FluentAssertions + EF InMemory.

**Alcance:** solo backend. El frontend (M4b-7, M4b-8) va en un plan aparte; al terminar este, la API está completa y testeable.

**Documentos:** [spec](../specs/2026-08-07-horarios-inspector-design.md) · [ADR-008](../../adr/ADR-008-horarios-en-tabla-compartida.md)

## Global Constraints

- **Nunca `ALTER TABLE`** sobre `horario_detalle`, `horas_clases`, `fechas_horarios` ni `espacios`. Solo `INSERT`/`UPDATE` de filas. El único `ALTER` permitido es sobre `cplec_sesiones`, que es nuestra.
- **Nunca `dotnet ef migrations`.** El esquema se cambia con `.sql` en `database/migrations/` y su rollback en `database/rollback/`.
- **MySQL 5.7.21.** Prohibido: CTEs (`WITH`), funciones de ventana, `RENAME COLUMN`, `CREATE INDEX IF NOT EXISTS`, `0000-00-00`. `sql_mode` incluye `STRICT_TRANS_TABLES` y `NO_ZERO_DATE`.
- **`Domain/Entities/` y `Infrastructure/DbContexts/sigafi_esContext.cs` son código generado** por EF Core Power Tools. No se editan a mano.
- **`activo` se compara siempre con `== 1`.** Es `tinyint(4)` nullable y hay datos sucios (existe una fila con `11`). Nunca `!= 0`, nunca truthy. Excepción: `asignaciones_profesores` tolera NULL legacy con `(activo == null || activo == 1)`, como ya hace `DistributivoGuard`.
- **`cplec` escribe únicamente** filas de carrera 6 en `horario_detalle` y filas `tipo == "Z"` en `horas_clases`. Lee todas las carreras y todos los tipos.
- **`idEspacio` siempre `null`** al escribir.
- `paralelo` es `char(1)` en `asignaciones_profesores` y `varchar(10)` en `matriculas`: normalizar con `Trim()` al comparar.
- Sin secretos en el repo. `appsettings.Development.json` está en `.gitignore`.
- Trabajar en `dv_jb`. No usar `--no-verify`.
- Comandos: `cd src && dotnet test`.

---

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `database/migrations/004_cplec_horarios_indices_rbac.sql` | Índices de soporte (idempotentes) + módulo RBAC `horarios` |
| `database/rollback/004_cplec_horarios_indices_rbac_rollback.sql` | Revierte solo los grants RBAC, nunca los índices |
| `database/migrations/005_cplec_sesiones_horario.sql` | `ALTER` sobre `cplec_sesiones`: anclaje al horario + tardanza |
| `database/rollback/005_cplec_sesiones_horario_rollback.sql` | Quita las 5 columnas, la FK y el índice |
| `Application/Horarios/HorariosDtos.cs` | DTOs del módulo |
| `Application/Horarios/Services/HorarioCarreraGuard.cs` | Frontera: la asignación es de carrera 6 |
| `Application/Horarios/Services/FranjaZGuard.cs` | Frontera: la franja es `tipo='Z'` |
| `Application/Horarios/Services/PoliticaReintento.cs` | Política pura de reintento; sin dependencias de EF |
| `Application/Horarios/Services/EscrituraSerializable.cs` | Transacción `Serializable` + `PoliticaReintento` |
| `Application/Horarios/Services/BloqueHorarioCalculator.cs` | Agrupa franjas contiguas en bloques |
| `Application/Horarios/Services/ConflictoHorarioService.cs` | Solapamiento; bloqueante Z↔Z, advertencia Z↔otros |
| `Application/Horarios/Services/FranjaService.cs` | CRUD de franjas Z |
| `Application/Horarios/Services/HorarioService.cs` | Grid y celda |
| `Application/Horarios/Services/HorarioRangoService.cs` | Replicar / editar / eliminar por rango, con topes |
| `Application/Asistencia/AgendaService.cs` | Días con horario y su estado de registro |
| `Application/Distributivo/ParalelosInspectorService.cs` | Listado transversal de paralelos (inspector) |
| `Controllers/Horarios/FranjasController.cs` | `/api/franjas` |
| `Controllers/Horarios/HorariosController.cs` | `/api/horarios/*` |
| `Application/Common/Exceptions/FueraDeAlcanceException.cs` | 403 `FUERA_DE_ALCANCE` |
| `Application/Common/Exceptions/FranjaNoPropiaException.cs` | 403 `FRANJA_NO_PROPIA` |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `Domain/Entities/` + `Infrastructure/DbContexts/sigafi_esContext.cs` | Regenerar con Power Tools para incorporar `horario_detalle`, `horas_clases`, `espacios` y las columnas nuevas de `cplec_sesiones` |
| `Extensions/DependencyInjectionExtensions.cs:79-86` | Registrar los servicios nuevos |
| `Application/Asistencia/SesionService.cs` | Crear desde `idHorarioInicio`; congelar tardanza |
| `Controllers/Asistencia/SesionesController.cs` | Nuevo contrato de creación + `/agenda` |
| `Controllers/Distributivo/DistributivoController.cs` | `GET /api/paralelos` |

**Tests:** un archivo por servicio bajo `src/Leccionario.Tests/Horarios/`, más `Builders/HorarioDetalleBuilder.cs` y `Builders/FranjaBuilder.cs`.

**Total: 91 tests nuevos (+3 de humo del DbContext) → ~234.**

---

## Task 1: Entidades y DbSets de horarios

**Files:**
- Modify: `src/Leccionario.Api/Domain/Entities/` (generado — se agregan `horario_detalle.cs`, `horas_clases.cs`, `espacios.cs`)
- Modify: `src/Leccionario.Api/Infrastructure/DbContexts/sigafi_esContext.cs` (generado)
- Test: `src/Leccionario.Tests/DbContextSmokeTests.cs`

**Interfaces:**
- Produces: `DbSet<horario_detalle> horario_detalle`, `DbSet<horas_clases> horas_clases`, `DbSet<espacios> espacios` en `sigafi_esContext`. Propiedades con los nombres exactos de columna: `horario_detalle.{idHorario, idAsignacion, idFecha, idhora, idEspacio, tipoBloque, activo, claseReasignacion, esRecuperacionPedagocia, observacion, idHorarioReasgincacion}` y `horas_clases.{idhora, idSeccion, idCarrera, hora_inicio, hora_fin, minutos, numero_hora, tipo, activo}`.

> **Por qué `espacios`.** No lo usamos nunca y `idEspacio` va siempre NULL, pero `horario_detalle` tiene una FK a `espacios`; si no se scaffoldea, la entidad generada queda con una navegación colgando y el modelo no compila. Entra solo para satisfacer la FK.

> **Tipos esperados.** `activo` → `sbyte?` (es `tinyint(4)`, igual que en `asignaciones_profesores`), `esRecuperacionPedagocia` → `bool?` (`tinyint(1)`), `claseReasignacion` → `sbyte?`, `tipoBloque` / `hora_inicio` / `hora_fin` / `tipo` → `string?`, `minutos` / `numero_hora` / `idSeccion` / `idCarrera` / `idEspacio` → `int?`.

- [ ] **Step 1: Regenerar las entidades con EF Core Power Tools**

Ejecutar Power Tools contra `sigafi_es` seleccionando **las tablas ya presentes más** `horario_detalle`, `horas_clases` y `espacios`. Salida a `Domain/Entities/` (plano, sin subcarpetas) y el contexto a `Infrastructure/DbContexts/`.

Verificar que la cabecera `<auto-generated>` sigue presente y que no se perdió ninguna entidad existente:

```bash
ls src/Leccionario.Api/Domain/Entities/ | wc -l   # esperado: 28 (25 previas + 3 nuevas)
```

- [ ] **Step 2: Escribir el test de humo**

Agregar a `src/Leccionario.Tests/DbContextSmokeTests.cs`:

```csharp
[TestMethod]
public void DbContext_ExponeLasTablasDeHorarios()
{
    var opciones = new DbContextOptionsBuilder<sigafi_esContext>()
        .UseInMemoryDatabase(nameof(DbContext_ExponeLasTablasDeHorarios))
        .Options;
    using var db = new sigafi_esContext(opciones);

    db.horario_detalle.Should().NotBeNull();
    db.horas_clases.Should().NotBeNull();
}

[TestMethod]
public void HorasClases_UsaLosNombresDeColumnaRealesEnSnakeCase()
{
    // hora_inicio / hora_fin / numero_hora, NO horaInicio / horaFin / numeroHora.
    // gacad usa camelCase en sus DTOs; la columna real es snake_case.
    var props = typeof(horas_clases).GetProperties().Select(p => p.Name).ToArray();

    props.Should().Contain(new[] { "hora_inicio", "hora_fin", "numero_hora", "tipo" });
    props.Should().NotContain("horaInicio");
}

[TestMethod]
public void HorarioDetalle_TieneLasColumnasQueEscribimos()
{
    var props = typeof(horario_detalle).GetProperties().Select(p => p.Name).ToArray();

    props.Should().Contain(new[]
    {
        "idHorario", "idAsignacion", "idFecha", "idhora", "idEspacio", "tipoBloque", "activo"
    });
}
```

- [ ] **Step 3: Correr los tests**

```bash
cd src && dotnet test --filter "FullyQualifiedName~DbContextSmokeTests" -v q
```

Esperado: PASS. Si `hora_inicio` no aparece, Power Tools aplicó pluralización/renombrado — revisar su configuración y regenerar; **no** editar la entidad a mano.

- [ ] **Step 4: Commit**

```bash
git add src/Leccionario.Api/Domain/Entities src/Leccionario.Api/Infrastructure/DbContexts src/Leccionario.Tests/DbContextSmokeTests.cs
git commit -m "feat(horarios): scaffold de horario_detalle, horas_clases y espacios"
```

---

## Task 2: Migración 004 — índices y RBAC

**Files:**
- Create: `database/migrations/004_cplec_horarios_indices_rbac.sql`
- Create: `database/rollback/004_cplec_horarios_indices_rbac_rollback.sql`

**Interfaces:**
- Produces: módulo RBAC `horarios` del sistema `cplec`, con grants `ver/crear/editar/eliminar` para `cplec_inspector` y `ver` para `cplec_docente`. Los controllers de las tareas 8 y 9 dependen de que exista.

> **Los índices ya existen.** `ix_horario_detalle_fecha_hora_activo` está creado (lo puso el script 004 de `gestion_academica`). El script se escribe igual, idempotente, para que la base sea reproducible desde cero sin depender de que el otro sistema haya corrido antes. **No se crea ningún índice único**: sería cambio estructural sobre tabla compartida y podría fallar por duplicados ajenos preexistentes.

- [ ] **Step 1: Escribir la migración**

Crear `database/migrations/004_cplec_horarios_indices_rbac.sql`. Estructura idéntica a `001_cplec_rbac_seed.sql` (leerlo primero: mismo encabezado, `SET SQL_MODE`, `START TRANSACTION`, resolución por código y no por ID literal).

```sql
-- =============================================================================
-- Migración : 004_cplec_horarios_indices_rbac
-- Sistema   : cplec — Leccionario e Inspección
-- Base      : sigafi_es
-- Motor     : MySQL 5.7.21 / InnoDB
-- Fecha     : 2026-08-07
-- Rollback  : database/rollback/004_cplec_horarios_indices_rbac_rollback.sql
--
-- Objetivo  : Soporte para el módulo de horarios del inspector (ADR-008).
--             SIN cambios estructurales: solo índices de performance y RBAC.
--
-- IDEMPOTENTE. Los índices probablemente YA existen: los creó el script 004 de
-- gestion_academica sobre esta misma base. Se incluyen para que el esquema sea
-- reproducible desde cero. MySQL 5.7 no tiene CREATE INDEX IF NOT EXISTS, así
-- que se usa el patrón PREPARE/EXECUTE sobre INFORMATION_SCHEMA.
--
-- NO se crea ningún índice UNIQUE sobre horario_detalle. Es una tabla
-- compartida con ~1781 filas de otros sistemas; un UNIQUE sobre datos que nunca
-- lo tuvieron puede fallar por duplicados ajenos, y la decisión le corresponde a
-- quien gobierna la tabla. La unicidad la garantiza la aplicación
-- (ADR-008 decisión 6).
-- =============================================================================

SET NAMES utf8mb4;
SET @OLD_SQL_MODE = @@SQL_MODE;
SET SQL_MODE = 'ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION';

-- -----------------------------------------------------------------------------
-- 1. Índices de soporte (idempotentes)
-- -----------------------------------------------------------------------------
SET @ddl = (
  SELECT IF(COUNT(*) = 0,
    'ALTER TABLE `horario_detalle` ADD INDEX `ix_horario_detalle_fecha_hora_activo` (`idFecha`,`idhora`,`activo`)',
    'SELECT ''ix_horario_detalle_fecha_hora_activo ya existe'' AS info')
  FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'horario_detalle'
    AND INDEX_NAME = 'ix_horario_detalle_fecha_hora_activo');
PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;

SET @ddl = (
  SELECT IF(COUNT(*) = 0,
    'ALTER TABLE `fechas_horarios` ADD INDEX `ix_fechas_horarios_fecha` (`fecha`)',
    'SELECT ''ix_fechas_horarios_fecha ya existe'' AS info')
  FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fechas_horarios'
    AND INDEX_NAME = 'ix_fechas_horarios_fecha');
PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;

-- -----------------------------------------------------------------------------
-- 2. Módulo RBAC `horarios`
-- -----------------------------------------------------------------------------
START TRANSACTION;

SET @idSistema := (SELECT `idSistema` FROM `rbac_sistema` WHERE `codigo` = 'cplec');

INSERT INTO `rbac_modulos` (`id_sistema`, `Nombre`, `esActivo`)
SELECT @idSistema, 'horarios', 1
WHERE NOT EXISTS (
    SELECT 1 FROM `rbac_modulos` WHERE `id_sistema` = @idSistema AND `Nombre` = 'horarios');

INSERT INTO `rbac_modulos_operaciones`
       (`idModulos`, `idOperaciones`, `fecha_creacion`, `esActivo`)
SELECT m.`idModulos`, o.`idOperaciones`, CURDATE(), 1
FROM   `rbac_modulos` m
CROSS  JOIN `rbac_operaciones` o
WHERE  m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios'
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_modulos_operaciones` mo
        WHERE mo.`idModulos` = m.`idModulos` AND mo.`idOperaciones` = o.`idOperaciones`);

SET @idRolInspector := (SELECT `idRol` FROM `rbac_rol` WHERE `codigo_rol` = 'cplec_inspector');
SET @idRolDocente   := (SELECT `idRol` FROM `rbac_rol` WHERE `codigo_rol` = 'cplec_docente');

-- Inspector: ver, crear, editar, eliminar
INSERT INTO `rbac_rol_modulo_operacion`
       (`idModulosOperaciones`, `idRol`, `fecha_asignacion`, `esActivo`)
SELECT mo.`idModulosOperaciones`, @idRolInspector, CURDATE(), 1
FROM   `rbac_modulos_operaciones` mo
JOIN   `rbac_modulos`     m ON m.`idModulos`     = mo.`idModulos`
JOIN   `rbac_operaciones` o ON o.`idOperaciones` = mo.`idOperaciones`
WHERE  m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios'
  AND  o.`NombreOperacion` IN ('ver','crear','editar','eliminar')
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_rol_modulo_operacion` rmo
        WHERE rmo.`idModulosOperaciones` = mo.`idModulosOperaciones`
          AND rmo.`idRol` = @idRolInspector);

-- Docente: solo ver. NO crea ni edita horarios: eso es del inspector.
INSERT INTO `rbac_rol_modulo_operacion`
       (`idModulosOperaciones`, `idRol`, `fecha_asignacion`, `esActivo`)
SELECT mo.`idModulosOperaciones`, @idRolDocente, CURDATE(), 1
FROM   `rbac_modulos_operaciones` mo
JOIN   `rbac_modulos`     m ON m.`idModulos`     = mo.`idModulos`
JOIN   `rbac_operaciones` o ON o.`idOperaciones` = mo.`idOperaciones`
WHERE  m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios'
  AND  o.`NombreOperacion` = 'ver'
  AND  NOT EXISTS (
        SELECT 1 FROM `rbac_rol_modulo_operacion` rmo
        WHERE rmo.`idModulosOperaciones` = mo.`idModulosOperaciones`
          AND rmo.`idRol` = @idRolDocente);

COMMIT;

SET SQL_MODE = @OLD_SQL_MODE;

-- =============================================================================
-- VERIFICACIÓN (esperado: inspector=4, docente=1)
-- =============================================================================
-- SELECT r.codigo_rol, COUNT(*) grants
-- FROM   rbac_rol r
-- JOIN   rbac_rol_modulo_operacion rmo ON rmo.idRol = r.idRol AND rmo.esActivo = 1
-- JOIN   rbac_modulos_operaciones  mo  ON mo.idModulosOperaciones = rmo.idModulosOperaciones
-- JOIN   rbac_modulos              m   ON m.idModulos = mo.idModulos AND m.Nombre = 'horarios'
-- JOIN   rbac_sistema              s   ON s.idSistema = m.id_sistema AND s.codigo = 'cplec'
-- GROUP  BY r.codigo_rol;
-- =============================================================================
```

- [ ] **Step 2: Escribir el rollback**

Crear `database/rollback/004_cplec_horarios_indices_rbac_rollback.sql`:

```sql
-- =============================================================================
-- Rollback : 004_cplec_horarios_indices_rbac
--
-- NO borra los índices. `ix_horario_detalle_fecha_hora_activo` lo usa
-- gestion_academica para su query de conflictos; borrarlo degradaría otro
-- sistema en producción. Los índices son aditivos y sin efecto de negocio.
-- Este rollback revierte únicamente el RBAC.
-- =============================================================================

SET NAMES utf8mb4;
START TRANSACTION;

SET @idSistema := (SELECT `idSistema` FROM `rbac_sistema` WHERE `codigo` = 'cplec');

DELETE rmo FROM `rbac_rol_modulo_operacion` rmo
JOIN `rbac_modulos_operaciones` mo ON mo.`idModulosOperaciones` = rmo.`idModulosOperaciones`
JOIN `rbac_modulos` m ON m.`idModulos` = mo.`idModulos`
WHERE m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios';

DELETE mo FROM `rbac_modulos_operaciones` mo
JOIN `rbac_modulos` m ON m.`idModulos` = mo.`idModulos`
WHERE m.`id_sistema` = @idSistema AND m.`Nombre` = 'horarios';

DELETE FROM `rbac_modulos`
WHERE `id_sistema` = @idSistema AND `Nombre` = 'horarios';

COMMIT;
```

- [ ] **Step 3: Verificar idempotencia**

Correr la migración **dos veces seguidas** contra desarrollo, según `database/migrations/README.md`. La segunda corrida no debe insertar nada ni fallar. Luego correr la consulta de verificación del pie del script: `cplec_inspector` = 4, `cplec_docente` = 1.

- [ ] **Step 4: Verificar el rollback**

Correr el rollback, comprobar que la verificación devuelve 0 filas, y volver a correr la migración para dejar la base como estaba.

- [ ] **Step 5: Commit**

```bash
git add database/migrations/004_cplec_horarios_indices_rbac.sql database/rollback/004_cplec_horarios_indices_rbac_rollback.sql
git commit -m "feat(horarios): migración 004 — índices de soporte y módulo RBAC horarios"
```

---

## Task 3: `HorarioCarreraGuard` — la frontera de propiedad

**Files:**
- Create: `src/Leccionario.Api/Application/Common/Exceptions/FueraDeAlcanceException.cs`
- Create: `src/Leccionario.Api/Application/Horarios/Services/HorarioCarreraGuard.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs:86`
- Test: `src/Leccionario.Tests/Horarios/HorarioCarreraGuardTests.cs`

**Interfaces:**
- Consumes: `sigafi_esContext.asignaciones_profesores`, `.cursos` (Task 1).
- Produces:
  ```csharp
  public interface IHorarioCarreraGuard
  {
      Task<bool> AsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default);
      Task EnsureAsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default);
  }
  ```
  Usado por las tareas 8, 9 y 11 antes de todo INSERT/UPDATE.

> **Sin bypass de inspector.** A diferencia de `DistributivoGuard`, este guard **no** recibe `esInspector` y **no** tiene salida. Es una frontera de datos, no de rol: ni el inspector puede escribir una fila de otra carrera. Un test lo fija explícitamente.

- [ ] **Step 1: Escribir la excepción**

`src/Leccionario.Api/Application/Common/Exceptions/FueraDeAlcanceException.cs`:

```csharp
namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 403 FUERA_DE_ALCANCE — la asignación no pertenece a la carrera 6 (Escuela de
/// Conducción), así que cplec no puede escribir su horario.
/// </summary>
/// <remarks>
/// Frontera de datos, no de rol: aplica también al inspector. Ver
/// <c>docs/adr/ADR-008-horarios-en-tabla-compartida.md</c> decisión 2.
/// </remarks>
public sealed class FueraDeAlcanceException : ProhibidoException
{
    public FueraDeAlcanceException()
        : base("FUERA_DE_ALCANCE",
               "La asignación no pertenece a la Escuela de Conducción.") { }
}
```

- [ ] **Step 2: Escribir los tests (fallan)**

`src/Leccionario.Tests/Horarios/HorarioCarreraGuardTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Frontera de propiedad sobre `horario_detalle`: cplec solo escribe filas de la
/// carrera 6. Ver ADR-008 decisión 2. Sin bypass de inspector.
/// </summary>
[TestClass]
public sealed class HorarioCarreraGuardTests
{
    private const int IdAsignacion = 100;
    private const int IdNivelConduccion = 35;
    private const int IdNivelOtraCarrera = 77;

    private static sigafi_esContext CrearContexto(string nombreDb)
    {
        var opciones = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb)
            .Options;
        return new sigafi_esContext(opciones);
    }

    /// <summary>Siembra el camino asignación → curso → carrera.</summary>
    private static async Task SembrarAsync(
        sigafi_esContext db, int idNivel, int idCarrera, int idAsignacion = IdAsignacion)
    {
        db.cursos.Add(new cursos { idNivel = idNivel, idCarrera = idCarrera, Nivel = "TIPO \"C\"" });
        db.asignaciones_profesores.Add(
            new AsignacionBuilder().ConId(idAsignacion).ConNivel(idNivel).Build());
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Verificar_AsignacionDeCarrera6_True()
    {
        using var db = CrearContexto(nameof(Verificar_AsignacionDeCarrera6_True));
        await SembrarAsync(db, IdNivelConduccion, 6);

        var ok = await new HorarioCarreraGuard(db).AsignacionEsDeCarrera6Async(IdAsignacion);

        ok.Should().BeTrue();
    }

    [TestMethod]
    public async Task Verificar_AsignacionDeOtraCarrera_False()
    {
        using var db = CrearContexto(nameof(Verificar_AsignacionDeOtraCarrera_False));
        await SembrarAsync(db, IdNivelOtraCarrera, 19); // Gastronomía

        var ok = await new HorarioCarreraGuard(db).AsignacionEsDeCarrera6Async(IdAsignacion);

        ok.Should().BeFalse();
    }

    [TestMethod]
    public async Task Verificar_AsignacionInexistente_False()
    {
        using var db = CrearContexto(nameof(Verificar_AsignacionInexistente_False));
        await SembrarAsync(db, IdNivelConduccion, 6);

        var ok = await new HorarioCarreraGuard(db).AsignacionEsDeCarrera6Async(999);

        ok.Should().BeFalse();
    }

    [TestMethod]
    public async Task Verificar_NivelSinCursoRegistrado_False()
    {
        // La asignación existe pero su idNivel no tiene fila en `cursos`:
        // no se puede probar que sea de carrera 6, así que se niega.
        using var db = CrearContexto(nameof(Verificar_NivelSinCursoRegistrado_False));
        db.asignaciones_profesores.Add(
            new AsignacionBuilder().ConId(IdAsignacion).ConNivel(IdNivelConduccion).Build());
        await db.SaveChangesAsync();

        var ok = await new HorarioCarreraGuard(db).AsignacionEsDeCarrera6Async(IdAsignacion);

        ok.Should().BeFalse();
    }

    [TestMethod]
    public async Task Ensure_AsignacionDeCarrera6_NoLanza()
    {
        using var db = CrearContexto(nameof(Ensure_AsignacionDeCarrera6_NoLanza));
        await SembrarAsync(db, IdNivelConduccion, 6);

        var acto = async () =>
            await new HorarioCarreraGuard(db).EnsureAsignacionEsDeCarrera6Async(IdAsignacion);

        await acto.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Ensure_AsignacionDeOtraCarrera_LanzaFueraDeAlcance()
    {
        // Vale también para el inspector: el guard no recibe `esInspector` ni
        // tiene bypass. Es frontera de datos, no de rol (ADR-008 decisión 2).
        using var db = CrearContexto(nameof(Ensure_AsignacionDeOtraCarrera_LanzaFueraDeAlcance));
        await SembrarAsync(db, IdNivelOtraCarrera, 19);

        var acto = async () =>
            await new HorarioCarreraGuard(db).EnsureAsignacionEsDeCarrera6Async(IdAsignacion);

        (await acto.Should().ThrowAsync<FueraDeAlcanceException>())
            .Which.Codigo.Should().Be("FUERA_DE_ALCANCE");
    }
}
```

- [ ] **Step 3: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~HorarioCarreraGuardTests" -v q
```

Esperado: error de compilación — `HorarioCarreraGuard` no existe.

- [ ] **Step 4: Implementar el guard**

`src/Leccionario.Api/Application/Horarios/Services/HorarioCarreraGuard.cs`:

```csharp
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Frontera de propiedad de cplec sobre la tabla compartida `horario_detalle`:
/// solo se escriben filas cuya asignación pertenece a la carrera 6.
/// Ver <c>ADR-008</c> decisión 2 y <c>docs/10</c>.
/// </summary>
public interface IHorarioCarreraGuard
{
    /// <summary>¿La asignación pertenece a la Escuela de Conducción?</summary>
    Task<bool> AsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default);

    /// <summary>
    /// Igual que el anterior pero lanza <see cref="FueraDeAlcanceException"/>.
    /// Se invoca antes de TODO INSERT y UPDATE sobre `horario_detalle`.
    /// </summary>
    Task EnsureAsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default);
}

/// <inheritdoc cref="IHorarioCarreraGuard"/>
/// <remarks>
/// <para>Camino canónico, verificado en <c>docs/10</c>:</para>
/// <code>
/// asignaciones_profesores.idAsignacion → cursos.idNivel → cursos.idCarrera = 6
/// </code>
/// <para><b>No hay bypass de inspector.</b> Este guard no recibe <c>esInspector</c>:
/// es una frontera de datos, no de rol. Un inspector tampoco puede escribir el
/// horario de Gastronomía.</para>
/// <para>Si el <c>idNivel</c> de la asignación no tiene fila en <c>cursos</c>, se
/// niega: no se puede probar la pertenencia, y ante la duda no se escribe en una
/// tabla de otro sistema.</para>
/// </remarks>
public sealed class HorarioCarreraGuard : IHorarioCarreraGuard
{
    /// <summary>Escuela de Conducción. Ver <c>docs/10</c>.</summary>
    public const int IdCarreraConduccion = 6;

    private readonly sigafi_esContext _db;

    public HorarioCarreraGuard(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<bool> AsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default) =>
        _db.asignaciones_profesores
            .AsNoTracking()
            .Join(_db.cursos.AsNoTracking(),
                  ap => ap.idNivel,
                  c => c.idNivel,
                  (ap, c) => new { ap.idAsignacion, c.idCarrera })
            .AnyAsync(x => x.idAsignacion == idAsignacion
                        && x.idCarrera == IdCarreraConduccion, ct);

    public async Task EnsureAsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default)
    {
        if (!await AsignacionEsDeCarrera6Async(idAsignacion, ct))
            throw new FueraDeAlcanceException();
    }
}
```

- [ ] **Step 5: Registrar en DI**

En `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`, después de la línea `services.AddScoped<IAsistenciaService, AsistenciaService>();`:

```csharp
services.AddScoped<IHorarioCarreraGuard, HorarioCarreraGuard>();
```

Agregar el `using Leccionario.Api.Application.Horarios.Services;` en la cabecera del archivo.

- [ ] **Step 6: Correr los tests**

```bash
cd src && dotnet test --filter "FullyQualifiedName~HorarioCarreraGuardTests" -v q
```

Esperado: PASS, 6 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Leccionario.Api/Application/Horarios src/Leccionario.Api/Application/Common/Exceptions/FueraDeAlcanceException.cs src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs src/Leccionario.Tests/Horarios
git commit -m "feat(horarios): HorarioCarreraGuard — frontera de carrera 6 sin bypass"
```

---

## Task 4: `FranjaZGuard` — solo tocamos franjas propias

**Files:**
- Create: `src/Leccionario.Api/Application/Common/Exceptions/FranjaNoPropiaException.cs`
- Create: `src/Leccionario.Api/Application/Horarios/Services/FranjaZGuard.cs`
- Create: `src/Leccionario.Tests/Builders/FranjaBuilder.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Test: `src/Leccionario.Tests/Horarios/FranjaZGuardTests.cs`

**Interfaces:**
- Consumes: `sigafi_esContext.horas_clases` (Task 1).
- Produces:
  ```csharp
  public interface IFranjaZGuard
  {
      Task<bool> EsFranjaZAsync(int idhora, CancellationToken ct = default);
      Task EnsureEsFranjaZAsync(int idhora, CancellationToken ct = default);
  }
  public const string TipoCplec = "Z";   // en FranjaZGuard
  ```
  Usado por las tareas 8, 9 y 11 al escribir. **No** se usa al leer conflictos: ahí se ven todos los tipos.

> **Los tipos que existen.** `C` (54 filas, otras carreras y la 6), `I` (7, legacy), `X` (11, instituto — las usa `gestion_academica`). `Z` está libre. Verificado 2026-08-07, spec H2.

- [ ] **Step 1: Escribir la excepción**

`src/Leccionario.Api/Application/Common/Exceptions/FranjaNoPropiaException.cs`:

```csharp
namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 403 FRANJA_NO_PROPIA — la franja horaria no es de cplec (`tipo != 'Z'`).
/// </summary>
/// <remarks>
/// `X` es del instituto (gestion_academica), `I` es legacy y `C` es de otro
/// sistema. cplec las lee para detectar conflictos pero nunca las escribe.
/// Ver <c>ADR-008</c> decisión 3.
/// </remarks>
public sealed class FranjaNoPropiaException : ProhibidoException
{
    public FranjaNoPropiaException()
        : base("FRANJA_NO_PROPIA",
               "La franja horaria pertenece a otro sistema y no puede modificarse.") { }
}
```

- [ ] **Step 2: Escribir el builder de franjas**

`src/Leccionario.Tests/Builders/FranjaBuilder.cs`:

```csharp
using Leccionario.Api.Domain.Entities;

namespace Leccionario.Tests.Builders;

/// <summary>
/// Builder fluido para <see cref="horas_clases"/>. Patrón de docs/07 sección "Builders".
/// </summary>
/// <remarks>
/// Por defecto crea una franja de cplec: <c>tipo = "Z"</c>, <c>idCarrera</c> e
/// <c>idSeccion</c> en NULL (ADR-008 decisión 3). Las horas son
/// <c>varchar(5)</c> en formato <c>HH:MM</c> zero-padded, como en la base real.
/// </remarks>
public sealed class FranjaBuilder
{
    private int _idhora = 900;
    private string? _tipo = "Z";
    private string? _horaInicio = "07:00";
    private string? _horaFin = "08:00";
    private int? _minutos = 60;
    private int? _numeroHora = 1;
    private int? _idCarrera;
    private int? _idSeccion;
    private sbyte? _activo = 1;

    public FranjaBuilder ConId(int idhora) { _idhora = idhora; return this; }
    public FranjaBuilder DeTipo(string? tipo) { _tipo = tipo; return this; }
    public FranjaBuilder Activa(sbyte? activo) { _activo = activo; return this; }
    public FranjaBuilder ConCarrera(int? idCarrera) { _idCarrera = idCarrera; return this; }
    public FranjaBuilder ConSeccion(int? idSeccion) { _idSeccion = idSeccion; return this; }

    /// <summary>Fija el rango y recalcula `minutos` a partir de él.</summary>
    public FranjaBuilder DeRango(string? horaInicio, string? horaFin)
    {
        _horaInicio = horaInicio;
        _horaFin = horaFin;
        if (TimeOnly.TryParse(horaInicio, out var ini) && TimeOnly.TryParse(horaFin, out var fin))
            _minutos = (int)(fin - ini).TotalMinutes;
        return this;
    }

    public FranjaBuilder ConNumeroHora(int? numeroHora) { _numeroHora = numeroHora; return this; }

    public horas_clases Build() => new()
    {
        idhora = _idhora,
        tipo = _tipo,
        hora_inicio = _horaInicio,
        hora_fin = _horaFin,
        minutos = _minutos,
        numero_hora = _numeroHora,
        idCarrera = _idCarrera,
        idSeccion = _idSeccion,
        activo = _activo
    };
}
```

- [ ] **Step 3: Escribir los tests (fallan)**

`src/Leccionario.Tests/Horarios/FranjaZGuardTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// cplec solo escribe franjas `tipo='Z'`. `X` es del instituto, `I` legacy y `C`
/// de otro sistema: se leen para conflictos, nunca se modifican (ADR-008 dec. 3).
/// </summary>
[TestClass]
public sealed class FranjaZGuardTests
{
    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    [TestMethod]
    public async Task Ensure_FranjaZActiva_NoLanza()
    {
        using var db = CrearContexto(nameof(Ensure_FranjaZActiva_NoLanza));
        db.horas_clases.Add(new FranjaBuilder().ConId(900).DeTipo("Z").Build());
        await db.SaveChangesAsync();

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(900);

        await acto.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Ensure_FranjaX_DelInstituto_Lanza()
    {
        using var db = CrearContexto(nameof(Ensure_FranjaX_DelInstituto_Lanza));
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").Build());
        await db.SaveChangesAsync();

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(12);

        (await acto.Should().ThrowAsync<FranjaNoPropiaException>())
            .Which.Codigo.Should().Be("FRANJA_NO_PROPIA");
    }

    [TestMethod]
    public async Task Ensure_FranjaC_DeCarrera6_Lanza()
    {
        // La carrera 6 YA tiene franjas 'C' con idSeccion poblado (spec H2).
        // Son de otro sistema: cplec no las toca aunque sean "de su carrera".
        using var db = CrearContexto(nameof(Ensure_FranjaC_DeCarrera6_Lanza));
        db.horas_clases.Add(new FranjaBuilder().ConId(3).DeTipo("C").ConCarrera(6).ConSeccion(1).Build());
        await db.SaveChangesAsync();

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(3);

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }

    [TestMethod]
    public async Task Ensure_FranjaConTipoNull_Lanza()
    {
        // `tipo` es char(1) DEFAULT NULL: hay filas sin tipo. No son nuestras.
        using var db = CrearContexto(nameof(Ensure_FranjaConTipoNull_Lanza));
        db.horas_clases.Add(new FranjaBuilder().ConId(50).DeTipo(null).Build());
        await db.SaveChangesAsync();

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(50);

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }

    [TestMethod]
    public async Task Ensure_FranjaInexistente_Lanza()
    {
        using var db = CrearContexto(nameof(Ensure_FranjaInexistente_Lanza));

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(999);

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }
}
```

- [ ] **Step 4: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~FranjaZGuardTests" -v q
```

Esperado: error de compilación — `FranjaZGuard` no existe.

- [ ] **Step 5: Implementar el guard**

`src/Leccionario.Api/Application/Horarios/Services/FranjaZGuard.cs`:

```csharp
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Frontera de cplec sobre el catálogo compartido `horas_clases`: solo se crean,
/// editan o desactivan franjas <c>tipo = 'Z'</c>. Ver <c>ADR-008</c> decisión 3.
/// </summary>
public interface IFranjaZGuard
{
    Task<bool> EsFranjaZAsync(int idhora, CancellationToken ct = default);

    /// <summary>Lanza <see cref="FranjaNoPropiaException"/> si la franja no es de cplec.</summary>
    Task EnsureEsFranjaZAsync(int idhora, CancellationToken ct = default);
}

/// <inheritdoc cref="IFranjaZGuard"/>
/// <remarks>
/// <para>Tipos presentes en la base (verificado 2026-08-07): <c>C</c> 54 filas,
/// <c>I</c> 7, <c>X</c> 11. <c>Z</c> estaba libre y queda reservado para cplec.</para>
/// <para>Este guard aplica solo a la <b>escritura</b>. La detección de conflictos
/// lee todos los tipos a propósito: un docente ocupado en una franja <c>X</c>
/// sigue estando ocupado.</para>
/// <para>Franja inexistente o con <c>tipo</c> NULL: se niega. No se asume
/// propiedad sobre una fila que no podemos identificar como nuestra.</para>
/// </remarks>
public sealed class FranjaZGuard : IFranjaZGuard
{
    /// <summary>Tipo de franja reservado para cplec.</summary>
    public const string TipoCplec = "Z";

    private readonly sigafi_esContext _db;

    public FranjaZGuard(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<bool> EsFranjaZAsync(int idhora, CancellationToken ct = default) =>
        _db.horas_clases
            .AsNoTracking()
            .AnyAsync(h => h.idhora == idhora && h.tipo == TipoCplec, ct);

    public async Task EnsureEsFranjaZAsync(int idhora, CancellationToken ct = default)
    {
        if (!await EsFranjaZAsync(idhora, ct))
            throw new FranjaNoPropiaException();
    }
}
```

- [ ] **Step 6: Registrar en DI**

En `DependencyInjectionExtensions.cs`, junto al guard anterior:

```csharp
services.AddScoped<IFranjaZGuard, FranjaZGuard>();
```

- [ ] **Step 7: Correr los tests**

```bash
cd src && dotnet test --filter "FullyQualifiedName~FranjaZGuardTests" -v q
```

Esperado: PASS, 5 tests.

- [ ] **Step 8: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(horarios): FranjaZGuard — cplec solo escribe franjas tipo Z"
```

---

## Task 5: `PoliticaReintento` y `EscrituraSerializable`

**Files:**
- Create: `src/Leccionario.Api/Application/Horarios/Services/PoliticaReintento.cs`
- Create: `src/Leccionario.Api/Application/Horarios/Services/EscrituraSerializable.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Test: `src/Leccionario.Tests/Horarios/PoliticaReintentoTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class PoliticaReintento
  {
      public const int MaxIntentos = 3;
      public static Task<T> EjecutarAsync<T>(
          Func<int, CancellationToken, Task<T>> intento,
          Func<Exception, bool> esTransitorio,
          int maxIntentos = MaxIntentos,
          CancellationToken ct = default);
  }

  public interface IEscrituraSerializable
  {
      Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default);
  }
  ```
  `IEscrituraSerializable` lo consumen las tareas 8, 9 y 11 para envolver validar+escribir.

> **Por qué esta pieza existe y se testea aparte.** `horario_detalle` **no tiene ningún índice único** (spec H1). La única cosa que impide dos filas duplicadas es que la transacción `Serializable` haga que una de las dos escrituras concurrentes muera con deadlock 1213 y el reintento vea la fila de la otra. **El reintento no es robustez: es el mecanismo.** Merece sus propios tests.
>
> Se parte en dos porque EF InMemory no soporta transacciones ni niveles de aislamiento: `PoliticaReintento` es una función pura, testeable con excepciones cualquiera; `EscrituraSerializable` es el cableado con EF y MySQL, que se verifica en integración.

- [ ] **Step 1: Escribir los tests de la política (fallan)**

`src/Leccionario.Tests/Horarios/PoliticaReintentoTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Horarios.Services;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// La política de reintento ante deadlock. Con `horario_detalle` sin índice
/// único (spec H1), esto ES el mecanismo de unicidad, no una mejora.
/// </summary>
[TestClass]
public sealed class PoliticaReintentoTests
{
    private sealed class DeadlockSimulado : Exception { }
    private sealed class ErrorPermanente : Exception { }

    private static bool EsDeadlock(Exception ex) => ex is DeadlockSimulado;

    [TestMethod]
    public async Task Exito_AlPrimerIntento_NoReintenta()
    {
        var intentos = 0;

        var r = await PoliticaReintento.EjecutarAsync<int>(
            (_, _) => { intentos++; return Task.FromResult(42); },
            EsDeadlock);

        r.Should().Be(42);
        intentos.Should().Be(1);
    }

    [TestMethod]
    public async Task Deadlock_LuegoExito_DevuelveElResultadoDelSegundoIntento()
    {
        var intentos = 0;

        var r = await PoliticaReintento.EjecutarAsync<string>(
            (_, _) =>
            {
                intentos++;
                if (intentos == 1) throw new DeadlockSimulado();
                return Task.FromResult("ok");
            },
            EsDeadlock);

        r.Should().Be("ok");
        intentos.Should().Be(2);
    }

    [TestMethod]
    public async Task Deadlock_EnTodosLosIntentos_PropagaTrasAgotarlos()
    {
        var intentos = 0;

        var acto = async () => await PoliticaReintento.EjecutarAsync<int>(
            (_, _) => { intentos++; throw new DeadlockSimulado(); },
            EsDeadlock);

        await acto.Should().ThrowAsync<DeadlockSimulado>();
        intentos.Should().Be(PoliticaReintento.MaxIntentos);
    }

    [TestMethod]
    public async Task ErrorNoTransitorio_NoSeReintenta()
    {
        // Un conflicto de negocio o una violación de FK no se reintenta:
        // reintentarlo solo repetiría el mismo fallo y ocultaría la causa.
        var intentos = 0;

        var acto = async () => await PoliticaReintento.EjecutarAsync<int>(
            (_, _) => { intentos++; throw new ErrorPermanente(); },
            EsDeadlock);

        await acto.Should().ThrowAsync<ErrorPermanente>();
        intentos.Should().Be(1);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~PoliticaReintentoTests" -v q
```

Esperado: error de compilación — `PoliticaReintento` no existe.

- [ ] **Step 3: Implementar la política**

`src/Leccionario.Api/Application/Horarios/Services/PoliticaReintento.cs`:

```csharp
namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Reintento acotado ante fallos transitorios. Función pura: sin EF, sin MySQL,
/// sin reloj — para poder testearla exhaustivamente.
/// </summary>
/// <remarks>
/// <para>Existe porque <c>horario_detalle</c> no tiene ningún índice único
/// (spec H1): la unicidad la produce el deadlock bajo aislamiento
/// <c>Serializable</c> más este reintento. Si se elimina, aparecen duplicados.</para>
/// <para>Sin backoff: los deadlocks de InnoDB se resuelven abortando una de las
/// transacciones de inmediato, así que el reintento puede ser inmediato. Esperar
/// solo alargaría el lock de la otra.</para>
/// </remarks>
public static class PoliticaReintento
{
    /// <summary>Intentos totales, incluido el primero.</summary>
    public const int MaxIntentos = 3;

    /// <param name="intento">Recibe el número de intento (1-based) y el token.</param>
    /// <param name="esTransitorio">Decide si la excepción amerita reintento.</param>
    public static async Task<T> EjecutarAsync<T>(
        Func<int, CancellationToken, Task<T>> intento,
        Func<Exception, bool> esTransitorio,
        int maxIntentos = MaxIntentos,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intento);
        ArgumentNullException.ThrowIfNull(esTransitorio);

        for (var n = 1; ; n++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await intento(n, ct);
            }
            catch (Exception ex) when (n < maxIntentos && esTransitorio(ex))
            {
                // Se reintenta. La condición del `when` deja pasar la excepción
                // intacta cuando ya no quedan intentos o no es transitoria.
            }
        }
    }
}
```

- [ ] **Step 4: Correr los tests**

```bash
cd src && dotnet test --filter "FullyQualifiedName~PoliticaReintentoTests" -v q
```

Esperado: PASS, 4 tests.

- [ ] **Step 5: Implementar el cableado con EF**

`src/Leccionario.Api/Application/Horarios/Services/EscrituraSerializable.cs`:

```csharp
using System.Data;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Envuelve validar + escribir en una transacción <c>Serializable</c> de alcance
/// corto, con reintento ante deadlock. Ver <c>ADR-008</c> decisión 6.
/// </summary>
public interface IEscrituraSerializable
{
    Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default);
}

/// <inheritdoc cref="IEscrituraSerializable"/>
/// <remarks>
/// <para>Cómo protege, que conviene entender antes de tocar esto: bajo
/// <c>Serializable</c>, InnoDB toma next-key locks sobre el rango leído de
/// <c>ix_horario_detalle_fecha_hora_activo</c>. Dos escrituras concurrentes de la
/// misma celda se bloquean, una muere con el error 1213 y el reintento la repite
/// viendo ya la fila de la otra.</para>
/// <para>Los locks son del motor, así que esto también protege contra
/// <c>gestion_academica</c> escribiendo al mismo tiempo en la misma tabla.</para>
/// <para>El alcance debe ser <b>corto</b>: validar y escribir, nada más. Meter
/// lecturas de catálogo o llamadas lentas acá multiplica los deadlocks.</para>
/// <para>Con EF InMemory las transacciones son no-op, así que en tests unitarios
/// se sustituye por un doble que solo invoca la operación.</para>
/// </remarks>
public sealed class EscrituraSerializable : IEscrituraSerializable
{
    /// <summary>ER_LOCK_DEADLOCK.</summary>
    private const int MySqlDeadlock = 1213;
    /// <summary>ER_LOCK_WAIT_TIMEOUT.</summary>
    private const int MySqlLockWaitTimeout = 1205;

    private readonly sigafi_esContext _db;

    public EscrituraSerializable(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);

        return PoliticaReintento.EjecutarAsync(
            async (_, token) =>
            {
                await using var tx = await _db.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, token);

                var resultado = await operacion(token);

                await tx.CommitAsync(token);
                return resultado;
            },
            EsTransitorio,
            ct: ct);
    }

    /// <summary>
    /// Solo deadlock y timeout de lock se reintentan. Un conflicto de negocio o
    /// una violación de FK se propagan: repetirlos daría el mismo error.
    /// </summary>
    private static bool EsTransitorio(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is MySqlException { Number: MySqlDeadlock or MySqlLockWaitTimeout })
                return true;
        }
        return false;
    }
}
```

- [ ] **Step 6: Registrar en DI y compilar**

```csharp
services.AddScoped<IEscrituraSerializable, EscrituraSerializable>();
```

```bash
cd src && dotnet build -v q --nologo
```

Esperado: 0 errores. Si `MySqlConnector` no resuelve, verificar que `Leccionario.Api.csproj` referencia `Pomelo.EntityFrameworkCore.MySql` (que lo trae transitivamente).

- [ ] **Step 7: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(horarios): reintento ante deadlock y escritura Serializable

Sin indice unico en horario_detalle (spec H1), el reintento ES el mecanismo
de unicidad. Se parte en politica pura (testeable) y cableado con EF."
```

---

## Task 6: `BloqueHorarioCalculator` — franjas contiguas

**Files:**
- Create: `src/Leccionario.Api/Application/Horarios/Services/BloqueHorarioCalculator.cs`
- Test: `src/Leccionario.Tests/Horarios/BloqueHorarioCalculatorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record FranjaOrdenable(int IdHorario, int Idhora, TimeOnly Inicio, TimeOnly Fin, int Minutos);

  public sealed record BloqueHorario(
      int NumeroBloque, int IdHorarioInicio, TimeOnly Inicio, TimeOnly Fin,
      int FranjasPlanificadas, int MinutosPlanificados);

  public static class BloqueHorarioCalculator
  {
      public static IReadOnlyList<BloqueHorario> Agrupar(IEnumerable<FranjaOrdenable> franjas);
  }
  ```
  Lo consumen las tareas 12 (`SesionService`) y 13 (`AgendaService`).

> **Qué es un bloque.** Una corrida de franjas de la misma asignación en el mismo día donde `franja[i].Fin == franja[i+1].Inicio`. Un día partido mañana/tarde da dos bloques y por tanto dos sesiones. `IdHorarioInicio` es la **identidad estable** del bloque; `NumeroBloque` se calcula al crear la sesión y no se recalcula nunca (si el inspector agrega después una franja a las 06:00, el orden cambiaría y desalinearía sesiones ya guardadas).

- [ ] **Step 1: Escribir los tests (fallan)**

`src/Leccionario.Tests/Horarios/BloqueHorarioCalculatorTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Horarios.Services;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Agrupación de franjas contiguas en bloques. Las franjas Z tienen duración
/// libre (las crea el inspector), así que no se puede asumir una grilla regular.
/// </summary>
[TestClass]
public sealed class BloqueHorarioCalculatorTests
{
    private static FranjaOrdenable F(int idHorario, string ini, string fin) =>
        new(idHorario, idHorario + 1000, TimeOnly.Parse(ini), TimeOnly.Parse(fin),
            (int)(TimeOnly.Parse(fin) - TimeOnly.Parse(ini)).TotalMinutes);

    [TestMethod]
    public void CuatroFranjasContiguas_UnSoloBloque()
    {
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(1, "07:00", "08:00"), F(2, "08:00", "09:00"),
            F(3, "09:00", "10:00"), F(4, "10:00", "11:00"),
        });

        bloques.Should().HaveCount(1);
        bloques[0].NumeroBloque.Should().Be(1);
        bloques[0].IdHorarioInicio.Should().Be(1);
        bloques[0].FranjasPlanificadas.Should().Be(4);
        bloques[0].MinutosPlanificados.Should().Be(240);
        bloques[0].Fin.Should().Be(TimeOnly.Parse("11:00"));
    }

    [TestMethod]
    public void DiaPartidoMananaYTarde_DosBloquesNumeradosEnOrden()
    {
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(1, "07:00", "08:00"), F(2, "08:00", "09:00"),
            F(5, "15:00", "16:00"), F(6, "16:00", "17:00"),
        });

        bloques.Should().HaveCount(2);
        bloques[0].NumeroBloque.Should().Be(1);
        bloques[0].IdHorarioInicio.Should().Be(1);
        bloques[1].NumeroBloque.Should().Be(2);
        bloques[1].IdHorarioInicio.Should().Be(5);
        bloques[1].MinutosPlanificados.Should().Be(120);
    }

    [TestMethod]
    public void UnaSolaFranja_UnBloqueDeUna()
    {
        var bloques = BloqueHorarioCalculator.Agrupar(new[] { F(7, "19:00", "21:00") });

        bloques.Should().HaveCount(1);
        bloques[0].FranjasPlanificadas.Should().Be(1);
        bloques[0].MinutosPlanificados.Should().Be(120);
    }

    [TestMethod]
    public void HuecoDeUnMinuto_CortaElBloque()
    {
        // Contigüidad es igualdad exacta, no "casi". 08:00 != 08:01.
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(1, "07:00", "08:00"), F(2, "08:01", "09:00"),
        });

        bloques.Should().HaveCount(2);
    }

    [TestMethod]
    public void FranjasDesordenadas_SeOrdenanAntesDeAgrupar()
    {
        // La consulta no garantiza orden: `hora_inicio` es varchar y el ORDER BY
        // podría no aplicarse. El cálculo ordena por su cuenta.
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(3, "09:00", "10:00"), F(1, "07:00", "08:00"), F(2, "08:00", "09:00"),
        });

        bloques.Should().HaveCount(1);
        bloques[0].IdHorarioInicio.Should().Be(1);
        bloques[0].FranjasPlanificadas.Should().Be(3);
    }

    [TestMethod]
    public void FranjasDeDistintaDuracion_SeSumanLosMinutosReales()
    {
        // 90 + 45 + 60. Contar franjas daría 3 y mentiría sobre la duración:
        // por eso la sesión guarda `minutosPlanificados`, no "horas".
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(1, "07:00", "08:30"), F(2, "08:30", "09:15"), F(3, "09:15", "10:15"),
        });

        bloques.Should().HaveCount(1);
        bloques[0].FranjasPlanificadas.Should().Be(3);
        bloques[0].MinutosPlanificados.Should().Be(195);
    }

    [TestMethod]
    public void SinFranjas_ListaVacia()
    {
        BloqueHorarioCalculator.Agrupar(Array.Empty<FranjaOrdenable>()).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~BloqueHorarioCalculatorTests" -v q
```

Esperado: error de compilación.

- [ ] **Step 3: Implementar el cálculo**

`src/Leccionario.Api/Application/Horarios/Services/BloqueHorarioCalculator.cs`:

```csharp
namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>Una franja del horario de un día, ya parseada a <see cref="TimeOnly"/>.</summary>
public sealed record FranjaOrdenable(
    int IdHorario, int Idhora, TimeOnly Inicio, TimeOnly Fin, int Minutos);

/// <summary>
/// Una corrida de franjas contiguas. Es la unidad a la que se ancla una sesión
/// de clase.
/// </summary>
/// <param name="IdHorarioInicio">
/// Identidad estable del bloque: el <c>idHorario</c> de su primera franja.
/// </param>
public sealed record BloqueHorario(
    int NumeroBloque,
    int IdHorarioInicio,
    TimeOnly Inicio,
    TimeOnly Fin,
    int FranjasPlanificadas,
    int MinutosPlanificados);

/// <summary>
/// Agrupa las franjas de una asignación en un día en bloques contiguos.
/// Ver <c>ADR-008</c> decisión 7.
/// </summary>
/// <remarks>
/// <para>Contigüidad es <b>igualdad exacta</b> de <c>Fin</c> con el <c>Inicio</c>
/// siguiente. No hay tolerancia: un hueco de un minuto es un hueco.</para>
/// <para>Se ordena internamente porque el origen es una consulta sobre
/// <c>hora_inicio varchar(5)</c> y no conviene depender de su orden.</para>
/// <para><c>MinutosPlanificados</c> suma los minutos reales en vez de contar
/// franjas: con franjas Z de duración libre, contar mentiría.</para>
/// </remarks>
public static class BloqueHorarioCalculator
{
    public static IReadOnlyList<BloqueHorario> Agrupar(IEnumerable<FranjaOrdenable> franjas)
    {
        ArgumentNullException.ThrowIfNull(franjas);

        var ordenadas = franjas.OrderBy(f => f.Inicio).ThenBy(f => f.Fin).ToList();
        var bloques = new List<BloqueHorario>();

        var i = 0;
        while (i < ordenadas.Count)
        {
            var primera = ordenadas[i];
            var fin = primera.Fin;
            var cantidad = 1;
            var minutos = primera.Minutos;

            var j = i + 1;
            while (j < ordenadas.Count && ordenadas[j].Inicio == fin)
            {
                fin = ordenadas[j].Fin;
                minutos += ordenadas[j].Minutos;
                cantidad++;
                j++;
            }

            bloques.Add(new BloqueHorario(
                NumeroBloque: bloques.Count + 1,
                IdHorarioInicio: primera.IdHorario,
                Inicio: primera.Inicio,
                Fin: fin,
                FranjasPlanificadas: cantidad,
                MinutosPlanificados: minutos));

            i = j;
        }

        return bloques;
    }
}
```

- [ ] **Step 4: Correr los tests**

```bash
cd src && dotnet test --filter "FullyQualifiedName~BloqueHorarioCalculatorTests" -v q
```

Esperado: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(horarios): calculo de bloques contiguos con franjas de duracion libre"
```

---

## Task 7: `ConflictoHorarioService` — solapamiento de rangos

**Files:**
- Create: `src/Leccionario.Api/Application/Horarios/HorariosDtos.cs`
- Create: `src/Leccionario.Api/Application/Horarios/Services/ConflictoHorarioService.cs`
- Create: `src/Leccionario.Tests/Builders/HorarioDetalleBuilder.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Test: `src/Leccionario.Tests/Horarios/ConflictoHorarioServiceTests.cs`

**Interfaces:**
- Consumes: `sigafi_esContext.{horario_detalle, horas_clases, asignaciones_profesores, cursos}` (Task 1); `FranjaZGuard.TipoCplec` (Task 4).
- Produces:
  ```csharp
  public sealed record SolicitudConflictoDto(
      int IdAsignacion, int IdFecha, int Idhora, int? IdHorarioExcluir = null);

  public enum SeveridadConflicto { Bloqueante, Advertencia }

  public sealed record ConflictoDto(
      string Tipo, SeveridadConflicto Severidad, string Mensaje,
      int IdHorarioConflicto, string? IdProfesor, string? Carrera,
      string? Nivel, string? Paralelo, string? FranjaAjena);

  public sealed record ResultadoConflictoDto(IReadOnlyList<ConflictoDto> Bloqueantes,
                                             IReadOnlyList<ConflictoDto> Advertencias)
  {
      public bool HayBloqueantes => Bloqueantes.Count > 0;
  }

  public interface IConflictoHorarioService
  {
      Task<ResultadoConflictoDto> ValidarAsync(SolicitudConflictoDto s, CancellationToken ct = default);
  }
  ```
  Lo consumen las tareas 9 y 11.

> **Por qué solapamiento y no `idhora` igual.** Dos razones independientes, ambas verificadas: (a) las franjas Z y las X son filas distintas aunque cubran el mismo horario de reloj, así que por igualdad nunca colisionan entre sistemas; (b) las franjas Z las crea el inspector con duración libre, así que `Z-1 07:00–09:00` y `Z-7 08:00–08:45` son `idhora` distintos y se solapan de verdad. Por igualdad, el sistema diría "sin conflicto" mientras pone al mismo docente en dos clases a la vez.

**El predicado:**

```
solapan(a, b)  ⇔  a.inicio < b.fin  ∧  b.inicio < a.fin
```

Bordes que se tocan (`09:00–10:00` vs `10:00–11:00`) **no** solapan.

**Las cuatro reglas:**

| Tipo | Condición | Severidad |
|---|---|---|
| `ASIGNACION_DUPLICADA` | misma `idAsignacion`, solapada | bloqueante |
| `DOCENTE_OCUPADO` | mismo `idProfesor`, solapada, franja existente **es Z** | bloqueante |
| `PARALELO_OCUPADO` | misma 5-tupla, distinta asignación, solapada, franja existente **es Z** | bloqueante |
| `DOCENTE_OCUPADO` | mismo `idProfesor`, solapada, franja existente **no es Z** | **advertencia** |

Bloqueamos lo que podemos arreglar (franjas Z, carrera 6) y avisamos de lo que no podemos tocar (el horario de otra carrera).

- [ ] **Step 1: Escribir los DTOs**

`src/Leccionario.Api/Application/Horarios/HorariosDtos.cs`:

```csharp
namespace Leccionario.Api.Application.Horarios;

/// <summary>Celda que se quiere ocupar, para validar antes de escribir.</summary>
/// <param name="IdHorarioExcluir">Al editar, la propia fila no cuenta como conflicto.</param>
public sealed record SolicitudConflictoDto(
    int IdAsignacion,
    int IdFecha,
    int Idhora,
    int? IdHorarioExcluir = null);

/// <summary>
/// Bloqueante = está bajo nuestra autoridad y se puede resolver.
/// Advertencia = choque real contra el horario de otro sistema, que cplec no
/// puede ni debe editar. Ver <c>ADR-008</c> decisión 5.
/// </summary>
public enum SeveridadConflicto
{
    Bloqueante,
    Advertencia
}

/// <param name="FranjaAjena">Rango legible de la franja en conflicto, p. ej. "08:00–09:00".</param>
public sealed record ConflictoDto(
    string Tipo,
    SeveridadConflicto Severidad,
    string Mensaje,
    int IdHorarioConflicto,
    string? IdProfesor,
    string? Carrera,
    string? Nivel,
    string? Paralelo,
    string? FranjaAjena);

public sealed record ResultadoConflictoDto(
    IReadOnlyList<ConflictoDto> Bloqueantes,
    IReadOnlyList<ConflictoDto> Advertencias)
{
    public bool HayBloqueantes => Bloqueantes.Count > 0;

    public static ResultadoConflictoDto Vacio { get; } =
        new(Array.Empty<ConflictoDto>(), Array.Empty<ConflictoDto>());
}
```

- [ ] **Step 2: Escribir el builder de `horario_detalle`**

`src/Leccionario.Tests/Builders/HorarioDetalleBuilder.cs`:

```csharp
using Leccionario.Api.Domain.Entities;

namespace Leccionario.Tests.Builders;

/// <summary>
/// Builder fluido para <see cref="horario_detalle"/>. Patrón de docs/07.
/// </summary>
/// <remarks>
/// <c>idEspacio</c> queda siempre NULL: cplec no gestiona aulas (ADR-008 dec. 4).
/// </remarks>
public sealed class HorarioDetalleBuilder
{
    private int _idHorario = 1;
    private int _idAsignacion = 100;
    private int _idFecha = 500;
    private int _idhora = 900;
    private string? _tipoBloque = "teorico";
    private sbyte? _activo = 1;

    public HorarioDetalleBuilder ConId(int idHorario) { _idHorario = idHorario; return this; }
    public HorarioDetalleBuilder DeAsignacion(int id) { _idAsignacion = id; return this; }
    public HorarioDetalleBuilder EnFecha(int idFecha) { _idFecha = idFecha; return this; }
    public HorarioDetalleBuilder EnFranja(int idhora) { _idhora = idhora; return this; }
    public HorarioDetalleBuilder Activo(sbyte? activo) { _activo = activo; return this; }
    public HorarioDetalleBuilder DeTipoBloque(string? t) { _tipoBloque = t; return this; }

    public horario_detalle Build() => new()
    {
        idHorario = _idHorario,
        idAsignacion = _idAsignacion,
        idFecha = _idFecha,
        idhora = _idhora,
        idEspacio = null,
        tipoBloque = _tipoBloque,
        activo = _activo
    };
}
```

- [ ] **Step 3: Escribir los tests (fallan)**

`src/Leccionario.Tests/Horarios/ConflictoHorarioServiceTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Detección de conflictos por solapamiento de rangos. Ver ADR-008 decisión 5.
/// Escenario base: fecha 500, docente DUENIO con la asignación 100 (paralelo A),
/// docente DUENIO también con la asignación 101 (mismo paralelo).
/// </summary>
[TestClass]
public sealed class ConflictoHorarioServiceTests
{
    private const int IdFecha = 500;
    private const int Asig100 = 100;
    private const int Asig101 = 101;
    private const int AsigOtroDocente = 200;
    private const string Duenio = "0000000001";
    private const string Otro = "0000000002";
    private const int NivelC6 = 35;
    private const int NivelOtraCarrera = 77;

    // Franjas Z de cplec
    private const int Z_0700_0900 = 901;
    private const int Z_0800_0845 = 902;
    private const int Z_0900_1000 = 903;
    private const int Z_1000_1100 = 904;
    // Franja X del instituto, solapada con Z_0700_0900
    private const int X_0800_0900 = 12;

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    /// <summary>Siembra franjas, cursos y asignaciones comunes a todos los tests.</summary>
    private static async Task SembrarCatalogoAsync(sigafi_esContext db)
    {
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(Z_0700_0900).DeTipo("Z").DeRango("07:00", "09:00").Build(),
            new FranjaBuilder().ConId(Z_0800_0845).DeTipo("Z").DeRango("08:00", "08:45").Build(),
            new FranjaBuilder().ConId(Z_0900_1000).DeTipo("Z").DeRango("09:00", "10:00").Build(),
            new FranjaBuilder().ConId(Z_1000_1100).DeTipo("Z").DeRango("10:00", "11:00").Build(),
            new FranjaBuilder().ConId(X_0800_0900).DeTipo("X").DeRango("08:00", "09:00").Build());

        db.carreras.Add(new carreras { idCarrera = 6, Carrera = "ESCUELA DE CONDUCCION" });
        db.carreras.Add(new carreras { idCarrera = 19, Carrera = "GASTRONOMIA" });
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.cursos.Add(new cursos { idNivel = NivelOtraCarrera, idCarrera = 19, Nivel = "PRIMERO" });

        db.asignaciones_profesores.AddRange(
            new AsignacionBuilder().ConId(Asig100).DelProfesor(Duenio).ConNivel(NivelC6).ConParalelo("A").Build(),
            new AsignacionBuilder().ConId(Asig101).DelProfesor(Duenio).ConNivel(NivelC6).ConParalelo("A").Build(),
            new AsignacionBuilder().ConId(AsigOtroDocente).DelProfesor(Otro).ConNivel(NivelC6).ConParalelo("B").Build());

        db.fechas_horarios.Add(new fechas_horarios
        {
            idFecha = IdFecha, fecha = new DateOnly(2026, 8, 3), dia = "Lunes"
        });

        await db.SaveChangesAsync();
    }

    private static IConflictoHorarioService Crear(sigafi_esContext db) =>
        new ConflictoHorarioService(db);

    // -------------------------------------------------------------------------
    // Geometría del solapamiento
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task BordesQueSeTocan_NoChocan()
    {
        // 09:00–10:00 y 10:00–11:00 son consecutivas, no simultáneas.
        // Es el caso más importante: si esto falla, no se puede armar un bloque.
        using var db = CrearContexto(nameof(BordesQueSeTocan_NoChocan));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0900_1000).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_1000_1100));

        r.HayBloqueantes.Should().BeFalse();
        r.Advertencias.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SolapeParcial_PorLaIzquierda_Choca()
    {
        // Existente 08:00–08:45 dentro de la nueva 07:00–09:00.
        using var db = CrearContexto(nameof(SolapeParcial_PorLaIzquierda_Choca));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(Z_0800_0845).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeTrue();
    }

    [TestMethod]
    public async Task Contencion_FranjaLargaSobreCorta_Choca()
    {
        // Inversa del anterior: la existente es la larga.
        using var db = CrearContexto(nameof(Contencion_FranjaLargaSobreCorta_Choca));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0800_0845));

        r.HayBloqueantes.Should().BeTrue();
    }

    [TestMethod]
    public async Task OtraFecha_NoChoca()
    {
        using var db = CrearContexto(nameof(OtraFecha_NoChoca));
        await SembrarCatalogoAsync(db);
        db.fechas_horarios.Add(new fechas_horarios
        {
            idFecha = 501, fecha = new DateOnly(2026, 8, 4), dia = "Martes"
        });
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(501).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Las cuatro reglas
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task MismaAsignacionSolapada_AsignacionDuplicada_Bloqueante()
    {
        using var db = CrearContexto(nameof(MismaAsignacionSolapada_AsignacionDuplicada_Bloqueante));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.Bloqueantes.Should().ContainSingle()
            .Which.Tipo.Should().Be("ASIGNACION_DUPLICADA");
    }

    [TestMethod]
    public async Task MismoDocenteEnFranjaZ_DocenteOcupado_Bloqueante()
    {
        // El docente ya está en la asignación 101 a esa hora, en carrera 6.
        // Se puede arreglar desde acá, así que bloquea.
        using var db = CrearContexto(nameof(MismoDocenteEnFranjaZ_DocenteOcupado_Bloqueante));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0800_0845));

        r.Bloqueantes.Should().Contain(c => c.Tipo == "DOCENTE_OCUPADO");
        r.Bloqueantes.First(c => c.Tipo == "DOCENTE_OCUPADO").IdProfesor.Should().Be(Duenio);
    }

    [TestMethod]
    public async Task MismoParaleloDistintaAsignacion_ParaleloOcupado_Bloqueante()
    {
        // Asig 100 y 101 comparten la 5-tupla del paralelo pero tienen docentes
        // distintos: el grupo de alumnos no puede estar en dos clases a la vez.
        using var db = CrearContexto(nameof(MismoParaleloDistintaAsignacion_ParaleloOcupado_Bloqueante));
        await SembrarCatalogoAsync(db);
        // Reasignamos 101 a otro docente para aislar la regla de paralelo.
        var a101 = await db.asignaciones_profesores.FirstAsync(a => a.idAsignacion == Asig101);
        a101.idProfesor = Otro;
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0800_0845));

        r.Bloqueantes.Should().Contain(c => c.Tipo == "PARALELO_OCUPADO");
    }

    [TestMethod]
    public async Task MismoDocenteEnFranjaAjena_EsAdvertenciaYNoBloquea()
    {
        // El docente está ocupado en Gastronomía con una franja X. Es un choque
        // real, pero cplec no puede editar el horario ajeno: avisa, no bloquea.
        using var db = CrearContexto(nameof(MismoDocenteEnFranjaAjena_EsAdvertenciaYNoBloquea));
        await SembrarCatalogoAsync(db);
        db.asignaciones_profesores.Add(new AsignacionBuilder()
            .ConId(300).DelProfesor(Duenio).ConNivel(NivelOtraCarrera).ConParalelo("A").Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(300).EnFecha(IdFecha).EnFranja(X_0800_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
        var adv = r.Advertencias.Should().ContainSingle().Which;
        adv.Tipo.Should().Be("DOCENTE_OCUPADO");
        adv.Severidad.Should().Be(SeveridadConflicto.Advertencia);
        adv.Carrera.Should().Be("GASTRONOMIA");
        adv.FranjaAjena.Should().Be("08:00–09:00");
    }

    [TestMethod]
    public async Task OtroDocenteYOtroParalelo_NoChoca()
    {
        using var db = CrearContexto(nameof(OtroDocenteYOtroParalelo_NoChoca));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(AsigOtroDocente).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0800_0845));

        r.HayBloqueantes.Should().BeFalse();
        r.Advertencias.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Filtros
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task FilaConActivoCero_SeIgnora()
    {
        using var db = CrearContexto(nameof(FilaConActivoCero_SeIgnora));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0700_0900).Activo(0).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
    }

    [TestMethod]
    public async Task FilaConActivoSucio_NoCuentaComoActiva()
    {
        // `activo` es tinyint(4) y hay basura en la base (existe un 11).
        // Solo `= 1` cuenta como activo (spec H10).
        using var db = CrearContexto(nameof(FilaConActivoSucio_NoCuentaComoActiva));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0700_0900).Activo(11).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
    }

    [TestMethod]
    public async Task IdHorarioExcluir_NoSeCuentaContraSiMismo()
    {
        // Al editar una celda, su propia fila no es un conflicto.
        using var db = CrearContexto(nameof(IdHorarioExcluir_NoSeCuentaContraSiMismo));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(7)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(
            new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900, IdHorarioExcluir: 7));

        r.HayBloqueantes.Should().BeFalse();
    }

    [TestMethod]
    public async Task FranjaExistenteConHoraNula_SeDescarta()
    {
        // hora_inicio/hora_fin son DEFAULT NULL. Una franja sin horas no se puede
        // comparar: se descarta en vez de tratarla como 00:00 (spec H3).
        using var db = CrearContexto(nameof(FranjaExistenteConHoraNula_SeDescarta));
        await SembrarCatalogoAsync(db);
        db.horas_clases.Add(new FranjaBuilder().ConId(999).DeTipo("Z").DeRango(null, null).Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(999).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
    }
}
```

- [ ] **Step 4: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~ConflictoHorarioServiceTests" -v q
```

Esperado: error de compilación — `ConflictoHorarioService` no existe.

- [ ] **Step 5: Implementar el servicio**

`src/Leccionario.Api/Application/Horarios/Services/ConflictoHorarioService.cs`:

```csharp
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Detecta conflictos de horario por <b>solapamiento de rangos</b> en la misma
/// fecha. Ver <c>ADR-008</c> decisión 5.
/// </summary>
public interface IConflictoHorarioService
{
    Task<ResultadoConflictoDto> ValidarAsync(SolicitudConflictoDto s, CancellationToken ct = default);
}

/// <inheritdoc cref="IConflictoHorarioService"/>
/// <remarks>
/// <para>Predicado: <c>a.inicio &lt; b.fin ∧ b.inicio &lt; a.fin</c>. Bordes que se
/// tocan no solapan.</para>
/// <para><b>No se compara por <c>idhora</c></b>, que es lo que hace
/// <c>gestion_academica</c>, por dos razones independientes: las franjas Z y X
/// son filas distintas para el mismo horario de reloj, y las franjas Z tienen
/// duración libre, así que dos <c>idhora</c> distintos pueden solaparse de verdad.</para>
/// <para>La lectura cruza todas las carreras y todos los tipos de franja a
/// propósito. Lo que cambia es la severidad: bloqueamos contra franjas Z (nuestro
/// territorio, el inspector puede resolverlo) y avisamos contra franjas ajenas
/// (choque real, pero no podemos editar el horario de otra carrera).</para>
/// <para>El parseo de horas ocurre en memoria: <c>hora_inicio</c> es
/// <c>varchar(5)</c> nullable y una franja sin horas se descarta.</para>
/// </remarks>
public sealed class ConflictoHorarioService : IConflictoHorarioService
{
    private readonly sigafi_esContext _db;

    public ConflictoHorarioService(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>Proyección plana de una celda ocupada, con su contexto.</summary>
    private sealed record Ocupacion(
        int IdHorario, int IdAsignacion, string? Tipo, string? HoraInicio, string? HoraFin,
        string IdProfesor, string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad,
        string? Paralelo, string? Carrera, string? Nivel);

    public async Task<ResultadoConflictoDto> ValidarAsync(
        SolicitudConflictoDto s, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(s);

        var candidata = await LeerFranjaAsync(s.Idhora, ct)
            ?? throw new ValidacionException("La franja horaria no existe o no tiene horas definidas.");

        var propia = await _db.asignaciones_profesores
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.idAsignacion == s.IdAsignacion, ct)
            ?? throw new NoEncontradoException("La asignación solicitada no existe.");

        var ocupadas = await LeerOcupacionesDelDiaAsync(s.IdFecha, s.IdHorarioExcluir, ct);

        var bloqueantes = new List<ConflictoDto>();
        var advertencias = new List<ConflictoDto>();

        foreach (var o in ocupadas)
        {
            if (!TryRango(o.HoraInicio, o.HoraFin, out var ini, out var fin))
                continue; // franja sin horas: no se puede comparar

            if (!Solapan(candidata.Inicio, candidata.Fin, ini, fin))
                continue;

            var esFranjaPropia = o.Tipo == FranjaZGuard.TipoCplec;
            var rango = $"{ini:HH\\:mm}–{fin:HH\\:mm}";

            if (o.IdAsignacion == s.IdAsignacion)
            {
                bloqueantes.Add(Crear("ASIGNACION_DUPLICADA", SeveridadConflicto.Bloqueante,
                    "Esta asignación ya tiene una clase que se solapa con ese horario.", o, rango));
                continue;
            }

            if (o.IdProfesor == propia.idProfesor)
            {
                if (esFranjaPropia)
                {
                    bloqueantes.Add(Crear("DOCENTE_OCUPADO", SeveridadConflicto.Bloqueante,
                        "El docente ya tiene clase en ese horario.", o, rango));
                }
                else
                {
                    advertencias.Add(Crear("DOCENTE_OCUPADO", SeveridadConflicto.Advertencia,
                        $"El docente tiene clase en {o.Carrera ?? "otra carrera"} en ese horario. " +
                        "Ese horario pertenece a otro sistema y no se puede modificar desde acá.",
                        o, rango));
                }
                continue;
            }

            if (esFranjaPropia && EsMismoParalelo(propia, o))
            {
                bloqueantes.Add(Crear("PARALELO_OCUPADO", SeveridadConflicto.Bloqueante,
                    "El paralelo ya tiene otra clase en ese horario.", o, rango));
            }
        }

        return new ResultadoConflictoDto(bloqueantes, advertencias);
    }

    /// <summary>Solape estricto: los bordes que se tocan no cuentan.</summary>
    private static bool Solapan(TimeOnly aIni, TimeOnly aFin, TimeOnly bIni, TimeOnly bFin) =>
        aIni < bFin && bIni < aFin;

    /// <summary>El paralelo es la 5-tupla completa. Con menos, el join miente.</summary>
    private static bool EsMismoParalelo(Domain.Entities.asignaciones_profesores a, Ocupacion o) =>
        a.idPeriodo == o.IdPeriodo
        && a.idNivel == o.IdNivel
        && a.idSeccion == o.IdSeccion
        && a.idModalidad == o.IdModalidad
        && string.Equals(a.paralelo?.Trim(), o.Paralelo?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static ConflictoDto Crear(
        string tipo, SeveridadConflicto sev, string mensaje, Ocupacion o, string rango) =>
        new(tipo, sev, mensaje, o.IdHorario, o.IdProfesor, o.Carrera, o.Nivel,
            o.Paralelo?.Trim(), rango);

    private sealed record Rango(TimeOnly Inicio, TimeOnly Fin);

    private async Task<Rango?> LeerFranjaAsync(int idhora, CancellationToken ct)
    {
        var f = await _db.horas_clases
            .AsNoTracking()
            .Where(h => h.idhora == idhora)
            .Select(h => new { h.hora_inicio, h.hora_fin })
            .FirstOrDefaultAsync(ct);

        if (f is null || !TryRango(f.hora_inicio, f.hora_fin, out var ini, out var fin))
            return null;

        return new Rango(ini, fin);
    }

    /// <summary>
    /// `hora_inicio`/`hora_fin` son varchar(5) nullable. Se parsean en memoria;
    /// una franja sin horas o con basura se descarta en vez de asumir 00:00.
    /// </summary>
    private static bool TryRango(string? inicio, string? fin, out TimeOnly ini, out TimeOnly f)
    {
        ini = default;
        f = default;
        return TimeOnly.TryParse(inicio, out ini)
            && TimeOnly.TryParse(fin, out f)
            && ini < f;
    }

    /// <summary>
    /// Trae las celdas activas de la fecha con su contexto. Filtra por `idFecha`
    /// y `activo = 1` en la base (usa `ix_horario_detalle_fecha_hora_activo`) y
    /// compara los rangos en memoria: son decenas de filas por fecha.
    /// </summary>
    private Task<List<Ocupacion>> LeerOcupacionesDelDiaAsync(
        int idFecha, int? excluir, CancellationToken ct) =>
        (from hd in _db.horario_detalle.AsNoTracking()
         join hc in _db.horas_clases.AsNoTracking() on hd.idhora equals hc.idhora
         join ap in _db.asignaciones_profesores.AsNoTracking() on hd.idAsignacion equals ap.idAsignacion
         join cu in _db.cursos.AsNoTracking() on ap.idNivel equals cu.idNivel into cursoJoin
         from cu in cursoJoin.DefaultIfEmpty()
         join ca in _db.carreras.AsNoTracking() on cu.idCarrera equals ca.idCarrera into carreraJoin
         from ca in carreraJoin.DefaultIfEmpty()
         where hd.idFecha == idFecha
               && hd.activo == 1
               && (excluir == null || hd.idHorario != excluir)
         select new Ocupacion(
             hd.idHorario, hd.idAsignacion, hc.tipo, hc.hora_inicio, hc.hora_fin,
             ap.idProfesor, ap.idPeriodo, ap.idNivel, ap.idSeccion, ap.idModalidad,
             ap.paralelo, ca.Carrera, cu.Nivel))
        .ToListAsync(ct);
}
```

- [ ] **Step 6: Registrar en DI**

```csharp
services.AddScoped<IConflictoHorarioService, ConflictoHorarioService>();
```

- [ ] **Step 7: Correr los tests**

```bash
cd src && dotnet test --filter "FullyQualifiedName~ConflictoHorarioServiceTests" -v q
```

Esperado: PASS, 13 tests.

Si `EsMismoParalelo` falla en el test de `PARALELO_OCUPADO`, revisar que `AsignacionBuilder` use el mismo `idPeriodo`, `idSeccion` e `idModalidad` por defecto en ambas asignaciones — lo hace, pero es el punto donde una 5-tupla incompleta se nota.

- [ ] **Step 8: Correr la suite completa**

```bash
cd src && dotnet test -v q --nologo
```

Esperado: 0 fallos. Nota: en algunos entornos el host de pruebas aborta con "Test host process crashed" por límites de recursos; si pasa, correr por filtros y confirmar 0 fallos en cada uno.

- [ ] **Step 9: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(horarios): deteccion de conflictos por solapamiento de rangos

Bloqueante contra franjas Z, advertencia contra franjas de otros sistemas.
No se compara por idhora: con franjas Z de duracion libre la igualdad no
detecta al mismo docente en dos clases simultaneas."
```

---

## Task 8: `FranjaService` y `/api/franjas`

**Files:**
- Modify: `src/Leccionario.Api/Application/Horarios/HorariosDtos.cs`
- Create: `src/Leccionario.Api/Application/Horarios/Services/FranjaService.cs`
- Create: `src/Leccionario.Api/Controllers/Horarios/FranjasController.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Test: `src/Leccionario.Tests/Horarios/FranjaServiceTests.cs`

**Interfaces:**
- Consumes: `IFranjaZGuard` (Task 4).
- Produces:
  ```csharp
  public sealed record FranjaDto(int Idhora, string HoraInicio, string HoraFin, int Minutos, int? NumeroHora);
  public sealed record CrearFranjaDto(string HoraInicio, string HoraFin, int? NumeroHora);

  public interface IFranjaService
  {
      Task<IReadOnlyList<FranjaDto>> ListarAsync(CancellationToken ct = default);
      Task<FranjaDto> CrearAsync(CrearFranjaDto req, CancellationToken ct = default);
      Task<FranjaDto> ActualizarAsync(int idhora, CrearFranjaDto req, CancellationToken ct = default);
      Task DesactivarAsync(int idhora, CancellationToken ct = default);
  }
  ```
  `ListarAsync` la consume la tarea 9 para el grid.

> `minutos` se **deriva** de `hora_fin - hora_inicio`; no lo teclea el inspector. `idCarrera` e `idSeccion` van NULL fijos: el catálogo Z es plano y el inspector elige la franja correcta en el grid (ADR-008 decisión 3).

- [ ] **Step 1: Agregar los DTOs**

Al final de `HorariosDtos.cs`:

```csharp
/// <param name="HoraInicio">Formato <c>HH:mm</c>, como la columna varchar(5).</param>
public sealed record FranjaDto(
    int Idhora, string HoraInicio, string HoraFin, int Minutos, int? NumeroHora);

public sealed record CrearFranjaDto(string HoraInicio, string HoraFin, int? NumeroHora);
```

- [ ] **Step 2: Escribir los tests (fallan)**

`src/Leccionario.Tests/Horarios/FranjaServiceTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>CRUD del catálogo de franjas Z. Ver ADR-008 decisión 3.</summary>
[TestClass]
public sealed class FranjaServiceTests
{
    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static IFranjaService Crear(sigafi_esContext db) =>
        new FranjaService(db, new FranjaZGuard(db));

    [TestMethod]
    public async Task Listar_DevuelveSoloFranjasZActivas_OrdenadasPorHora()
    {
        using var db = CrearContexto(nameof(Listar_DevuelveSoloFranjasZActivas_OrdenadasPorHora));
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(902).DeTipo("Z").DeRango("09:00", "10:00").Build(),
            new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(903).DeTipo("Z").DeRango("11:00", "12:00").Activa(0).Build(),
            new FranjaBuilder().ConId(12).DeTipo("X").DeRango("08:00", "09:00").Build(),
            new FranjaBuilder().ConId(3).DeTipo("C").ConCarrera(6).DeRango("10:00", "11:30").Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ListarAsync();

        r.Select(f => f.Idhora).Should().Equal(901, 902);
    }

    [TestMethod]
    public async Task Crear_DerivaLosMinutosDelRango()
    {
        using var db = CrearContexto(nameof(Crear_DerivaLosMinutosDelRango));

        var f = await Crear(db).CrearAsync(new CrearFranjaDto("07:00", "08:30", 1));

        f.Minutos.Should().Be(90);
        var fila = await db.horas_clases.SingleAsync();
        fila.tipo.Should().Be("Z");
        fila.idCarrera.Should().BeNull();
        fila.idSeccion.Should().BeNull();
        fila.activo.Should().Be(1);
    }

    [TestMethod]
    public async Task Crear_ConInicioMayorOIgualAlFin_Rechaza()
    {
        using var db = CrearContexto(nameof(Crear_ConInicioMayorOIgualAlFin_Rechaza));

        var acto = async () => await Crear(db).CrearAsync(new CrearFranjaDto("10:00", "10:00", 1));

        await acto.Should().ThrowAsync<ValidacionException>();
    }

    [TestMethod]
    public async Task Crear_SolapadaConOtraFranjaZActiva_Rechaza()
    {
        // Dos franjas Z solapadas harían que el grid ofrezca horarios imposibles
        // y que el cálculo de bloques contiguos pierda sentido.
        using var db = CrearContexto(nameof(Crear_SolapadaConOtraFranjaZActiva_Rechaza));
        db.horas_clases.Add(new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "09:00").Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearFranjaDto("08:00", "08:45", 2));

        (await acto.Should().ThrowAsync<ConflictoException>())
            .Which.Codigo.Should().Be("CONFLICTO_HORARIO");
    }

    [TestMethod]
    public async Task Crear_PegadaAOtra_SeAcepta()
    {
        // 07:00–08:00 y 08:00–09:00 son consecutivas: es el caso normal.
        using var db = CrearContexto(nameof(Crear_PegadaAOtra_SeAcepta));
        db.horas_clases.Add(new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build());
        await db.SaveChangesAsync();

        var f = await Crear(db).CrearAsync(new CrearFranjaDto("08:00", "09:00", 2));

        f.Minutos.Should().Be(60);
    }

    [TestMethod]
    public async Task Desactivar_FranjaConHorarioActivo_Rechaza()
    {
        using var db = CrearContexto(nameof(Desactivar_FranjaConHorarioActivo_Rechaza));
        db.horas_clases.Add(new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1).EnFranja(901).Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).DesactivarAsync(901);

        (await acto.Should().ThrowAsync<ConflictoException>())
            .Which.Codigo.Should().Be("FRANJA_EN_USO");
    }

    [TestMethod]
    public async Task Desactivar_FranjaLibre_MarcaActivoCeroSinBorrar()
    {
        using var db = CrearContexto(nameof(Desactivar_FranjaLibre_MarcaActivoCeroSinBorrar));
        db.horas_clases.Add(new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build());
        await db.SaveChangesAsync();

        await Crear(db).DesactivarAsync(901);

        var fila = await db.horas_clases.SingleAsync();
        fila.activo.Should().Be(0);
    }

    [TestMethod]
    public async Task Actualizar_FranjaX_Rechaza()
    {
        using var db = CrearContexto(nameof(Actualizar_FranjaX_Rechaza));
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").DeRango("08:00", "09:00").Build());
        await db.SaveChangesAsync();

        var acto = async () =>
            await Crear(db).ActualizarAsync(12, new CrearFranjaDto("08:00", "10:00", 1));

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }
}
```

- [ ] **Step 3: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~FranjaServiceTests" -v q
```

- [ ] **Step 4: Implementar el servicio**

`src/Leccionario.Api/Application/Horarios/Services/FranjaService.cs`:

```csharp
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Catálogo de franjas horarias de cplec (<c>horas_clases</c> con
/// <c>tipo = 'Z'</c>). Ver <c>ADR-008</c> decisión 3.
/// </summary>
public interface IFranjaService
{
    /// <summary>Franjas Z activas, ordenadas por hora de inicio.</summary>
    Task<IReadOnlyList<FranjaDto>> ListarAsync(CancellationToken ct = default);

    Task<FranjaDto> CrearAsync(CrearFranjaDto req, CancellationToken ct = default);
    Task<FranjaDto> ActualizarAsync(int idhora, CrearFranjaDto req, CancellationToken ct = default);

    /// <summary>Borrado lógico. Rechaza si la franja tiene horario activo colgando.</summary>
    Task DesactivarAsync(int idhora, CancellationToken ct = default);
}

/// <inheritdoc cref="IFranjaService"/>
/// <remarks>
/// <para><c>minutos</c> se deriva del rango: es un dato calculable y dejar que el
/// usuario lo teclee solo produce inconsistencias.</para>
/// <para><c>idCarrera</c> e <c>idSeccion</c> quedan NULL. La carrera 6 tiene
/// franjas <c>'C'</c> segmentadas por jornada, pero son de otro sistema; el
/// catálogo Z es plano y el inspector elige en el grid.</para>
/// <para>No se permiten dos franjas Z solapadas: romperían el cálculo de bloques
/// contiguos y ofrecerían horarios imposibles en el grid.</para>
/// </remarks>
public sealed class FranjaService : IFranjaService
{
    private readonly sigafi_esContext _db;
    private readonly IFranjaZGuard _guard;

    public FranjaService(sigafi_esContext db, IFranjaZGuard guard)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public async Task<IReadOnlyList<FranjaDto>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await _db.horas_clases
            .AsNoTracking()
            .Where(h => h.tipo == FranjaZGuard.TipoCplec && h.activo == 1)
            .ToListAsync(ct);

        return filas
            .Select(Proyectar)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderBy(f => f.HoraInicio, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<FranjaDto> CrearAsync(CrearFranjaDto req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var (ini, fin) = ParsearRango(req);
        await EnsureNoSolapaAsync(ini, fin, excluir: null, ct);

        var fila = new horas_clases
        {
            tipo = FranjaZGuard.TipoCplec,
            idCarrera = null,
            idSeccion = null,
            hora_inicio = ini.ToString("HH\\:mm"),
            hora_fin = fin.ToString("HH\\:mm"),
            minutos = (int)(fin - ini).TotalMinutes,
            numero_hora = req.NumeroHora,
            activo = 1
        };

        _db.horas_clases.Add(fila);
        await _db.SaveChangesAsync(ct);

        return Proyectar(fila)!;
    }

    public async Task<FranjaDto> ActualizarAsync(int idhora, CrearFranjaDto req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        await _guard.EnsureEsFranjaZAsync(idhora, ct);

        var (ini, fin) = ParsearRango(req);
        await EnsureNoSolapaAsync(ini, fin, excluir: idhora, ct);

        var fila = await _db.horas_clases.FirstAsync(h => h.idhora == idhora, ct);
        fila.hora_inicio = ini.ToString("HH\\:mm");
        fila.hora_fin = fin.ToString("HH\\:mm");
        fila.minutos = (int)(fin - ini).TotalMinutes;
        fila.numero_hora = req.NumeroHora;
        await _db.SaveChangesAsync(ct);

        return Proyectar(fila)!;
    }

    public async Task DesactivarAsync(int idhora, CancellationToken ct = default)
    {
        await _guard.EnsureEsFranjaZAsync(idhora, ct);

        var enUso = await _db.horario_detalle
            .AsNoTracking()
            .AnyAsync(h => h.idhora == idhora && h.activo == 1, ct);

        if (enUso)
            throw new ConflictoException("FRANJA_EN_USO",
                "La franja tiene horarios activos asignados. Elimínalos antes de desactivarla.");

        var fila = await _db.horas_clases.FirstAsync(h => h.idhora == idhora, ct);
        fila.activo = 0;
        await _db.SaveChangesAsync(ct);
    }

    private static (TimeOnly Inicio, TimeOnly Fin) ParsearRango(CrearFranjaDto req)
    {
        if (!TimeOnly.TryParse(req.HoraInicio, out var ini) || !TimeOnly.TryParse(req.HoraFin, out var fin))
            throw new ValidacionException("Las horas deben tener formato HH:mm.");
        if (ini >= fin)
            throw new ValidacionException("La hora de inicio debe ser anterior a la de fin.");
        return (ini, fin);
    }

    private async Task EnsureNoSolapaAsync(TimeOnly ini, TimeOnly fin, int? excluir, CancellationToken ct)
    {
        var existentes = await _db.horas_clases
            .AsNoTracking()
            .Where(h => h.tipo == FranjaZGuard.TipoCplec && h.activo == 1
                        && (excluir == null || h.idhora != excluir))
            .Select(h => new { h.idhora, h.hora_inicio, h.hora_fin })
            .ToListAsync(ct);

        foreach (var e in existentes)
        {
            if (!TimeOnly.TryParse(e.hora_inicio, out var eIni) ||
                !TimeOnly.TryParse(e.hora_fin, out var eFin))
                continue;

            // Bordes que se tocan no solapan: 07:00–08:00 y 08:00–09:00 conviven.
            if (ini < eFin && eIni < fin)
                throw new ConflictoException("CONFLICTO_HORARIO",
                    $"La franja se solapa con la existente {e.hora_inicio}–{e.hora_fin}.");
        }
    }

    private static FranjaDto? Proyectar(horas_clases h)
    {
        if (!TimeOnly.TryParse(h.hora_inicio, out var ini) || !TimeOnly.TryParse(h.hora_fin, out var fin))
            return null;

        return new FranjaDto(
            h.idhora,
            ini.ToString("HH\\:mm"),
            fin.ToString("HH\\:mm"),
            h.minutos ?? (int)(fin - ini).TotalMinutes,
            h.numero_hora);
    }
}
```

- [ ] **Step 5: Escribir el controller**

`src/Leccionario.Api/Controllers/Horarios/FranjasController.cs`:

```csharp
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Horarios;

/// <summary>
/// Catálogo de franjas horarias de cplec. El docente solo lee; crear, editar y
/// desactivar es del inspector.
/// </summary>
[ApiController]
[Route("api/franjas")]
[Authorize(Roles = "cplec_docente,cplec_inspector")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class FranjasController : ControllerBase
{
    private readonly IFranjaService _franjas;

    public FranjasController(IFranjaService franjas) => _franjas = franjas;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FranjaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await _franjas.ListarAsync(ct));

    [HttpPost]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(FranjaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear([FromBody] CrearFranjaDto request, CancellationToken ct)
    {
        var f = await _franjas.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Listar), new { idhora = f.Idhora }, f);
    }

    [HttpPut("{idhora:int}")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(FranjaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Actualizar(int idhora, [FromBody] CrearFranjaDto request, CancellationToken ct) =>
        Ok(await _franjas.ActualizarAsync(idhora, request, ct));

    /// <summary>Borrado lógico (`activo = 0`). Rechaza si tiene horario activo.</summary>
    [HttpDelete("{idhora:int}")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Desactivar(int idhora, CancellationToken ct)
    {
        await _franjas.DesactivarAsync(idhora, ct);
        return NoContent();
    }
}
```

- [ ] **Step 6: Registrar en DI y correr los tests**

```csharp
services.AddScoped<IFranjaService, FranjaService>();
```

```bash
cd src && dotnet test --filter "FullyQualifiedName~FranjaServiceTests" -v q
```

Esperado: PASS, 8 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(horarios): catalogo de franjas Z con endpoints /api/franjas"
```

---

## Task 9: `GET /api/paralelos` para el inspector

**Files:**
- Create: `src/Leccionario.Api/Application/Distributivo/ParalelosInspectorService.cs`
- Modify: `src/Leccionario.Api/Controllers/Distributivo/DistributivoController.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Test: `src/Leccionario.Tests/Distributivo/ParalelosInspectorServiceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ParaleloDto(
      string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad, string Paralelo,
      string? Licencia, string? Jornada, string? Modalidad, int Asignaciones);

  public interface IParalelosInspectorService
  {
      Task<IReadOnlyList<ParaleloDto>> ListarAsync(string? idPeriodo, bool soloVigentes, CancellationToken ct = default);
  }
  ```
  El grid de la tarea 10 lo usa como selector.

> Hace falta porque `GET /api/mis-paralelos` está acotado al distributivo del docente y el inspector necesita ver todos los de la carrera 6. Un paralelo es la 5-tupla `(idPeriodo, idNivel, idSeccion, idModalidad, paralelo)`: con menos columnas el join devuelve alumnos de otras jornadas.

- [ ] **Step 1: Escribir los tests (fallan)**

`src/Leccionario.Tests/Distributivo/ParalelosInspectorServiceTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Distributivo;

/// <summary>
/// Listado transversal de paralelos para el inspector. Un paralelo es la 5-tupla
/// (idPeriodo, idNivel, idSeccion, idModalidad, paralelo).
/// </summary>
[TestClass]
public sealed class ParalelosInspectorServiceTests
{
    private const int NivelC6 = 35;
    private const int NivelOtra = 77;

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static async Task SembrarAsync(sigafi_esContext db)
    {
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.cursos.Add(new cursos { idNivel = NivelOtra, idCarrera = 19, Nivel = "PRIMERO" });
        db.secciones.Add(new secciones { idSeccion = 1, seccion = "VESPERTINA" });
        db.modalidades.Add(new modalidades { idModalidad = 1, modalidad = "PRESENCIAL" });
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Listar_AgrupaPorLa5TuplaYCuentaAsignaciones()
    {
        using var db = CrearContexto(nameof(Listar_AgrupaPorLa5TuplaYCuentaAsignaciones));
        await SembrarAsync(db);
        db.asignaciones_profesores.AddRange(
            new AsignacionBuilder().ConId(1).ConNivel(NivelC6).ConParalelo("C").EnPeriodo("OCC2025").Build(),
            new AsignacionBuilder().ConId(2).ConNivel(NivelC6).ConParalelo("C").EnPeriodo("OCC2025").Build(),
            new AsignacionBuilder().ConId(3).ConNivel(NivelC6).ConParalelo("D").EnPeriodo("OCC2025").Build());
        await db.SaveChangesAsync();

        var r = await new ParalelosInspectorService(db).ListarAsync(null, soloVigentes: false);

        r.Should().HaveCount(2);
        r.Single(p => p.Paralelo == "C").Asignaciones.Should().Be(2);
        r.Single(p => p.Paralelo == "C").Licencia.Should().Be("TIPO \"C\"");
        r.Single(p => p.Paralelo == "C").Jornada.Should().Be("VESPERTINA");
    }

    [TestMethod]
    public async Task Listar_ExcluyeOtrasCarreras()
    {
        using var db = CrearContexto(nameof(Listar_ExcluyeOtrasCarreras));
        await SembrarAsync(db);
        db.asignaciones_profesores.AddRange(
            new AsignacionBuilder().ConId(1).ConNivel(NivelC6).ConParalelo("A").Build(),
            new AsignacionBuilder().ConId(2).ConNivel(NivelOtra).ConParalelo("A").Build());
        await db.SaveChangesAsync();

        var r = await new ParalelosInspectorService(db).ListarAsync(null, soloVigentes: false);

        r.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Listar_ConParaleloConEspacios_LoNormalizaConTrim()
    {
        // `paralelo` es char(1) acá y varchar(10) en matriculas: se compara con Trim.
        using var db = CrearContexto(nameof(Listar_ConParaleloConEspacios_LoNormalizaConTrim));
        await SembrarAsync(db);
        var a = new AsignacionBuilder().ConId(1).ConNivel(NivelC6).ConParalelo("A").Build();
        a.paralelo = "A ";
        db.asignaciones_profesores.Add(a);
        await db.SaveChangesAsync();

        var r = await new ParalelosInspectorService(db).ListarAsync(null, soloVigentes: false);

        r.Single().Paralelo.Should().Be("A");
    }

    [TestMethod]
    public async Task Listar_SoloVigentes_FiltraPorFechaActual()
    {
        using var db = CrearContexto(nameof(Listar_SoloVigentes_FiltraPorFechaActual));
        await SembrarAsync(db);
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        db.asignaciones_profesores.AddRange(
            new AsignacionBuilder().ConId(1).ConNivel(NivelC6).ConParalelo("A")
                .ConRango(hoy.AddDays(-10), hoy.AddDays(10)).Build(),
            new AsignacionBuilder().ConId(2).ConNivel(NivelC6).ConParalelo("B")
                .ConRango(hoy.AddDays(-100), hoy.AddDays(-50)).Build());
        await db.SaveChangesAsync();

        var r = await new ParalelosInspectorService(db).ListarAsync(null, soloVigentes: true);

        r.Should().ContainSingle().Which.Paralelo.Should().Be("A");
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~ParalelosInspectorServiceTests" -v q
```

- [ ] **Step 3: Implementar el servicio**

`src/Leccionario.Api/Application/Distributivo/ParalelosInspectorService.cs`:

```csharp
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Distributivo;

/// <param name="Licencia">`cursos.Nivel` — en la carrera 6 es el tipo de licencia.</param>
/// <param name="Jornada">`secciones.seccion` — matutina, vespertina, nocturna.</param>
public sealed record ParaleloDto(
    string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad, string Paralelo,
    string? Licencia, string? Jornada, string? Modalidad, int Asignaciones);

/// <summary>
/// Listado transversal de paralelos de la carrera 6, para el inspector.
/// <c>/api/mis-paralelos</c> está acotado al distributivo del docente.
/// </summary>
public interface IParalelosInspectorService
{
    Task<IReadOnlyList<ParaleloDto>> ListarAsync(
        string? idPeriodo, bool soloVigentes, CancellationToken ct = default);
}

/// <inheritdoc cref="IParalelosInspectorService"/>
/// <remarks>
/// <para>Agrupa por la 5-tupla completa. Con 3 columnas el join devuelve el
/// triple de alumnos, de otras jornadas (ver <c>docs/10</c>).</para>
/// <para><c>soloVigentes</c> usa <c>fecha_inicial .. fecha_fin</c>. Las 737
/// asignaciones activas sin rango quedan fuera de ese filtro: aparecen solo con
/// <c>soloVigentes = false</c>. <c>periodos.activo</c> no sirve para saber el
/// período vigente: está en 1 en casi todos.</para>
/// </remarks>
public sealed class ParalelosInspectorService : IParalelosInspectorService
{
    private const int IdCarreraConduccion = 6;

    private readonly sigafi_esContext _db;
    private readonly TimeProvider _reloj;

    public ParalelosInspectorService(sigafi_esContext db, TimeProvider? reloj = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ParaleloDto>> ListarAsync(
        string? idPeriodo, bool soloVigentes, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);

        var filas = await (
            from ap in _db.asignaciones_profesores.AsNoTracking()
            join cu in _db.cursos.AsNoTracking() on ap.idNivel equals cu.idNivel
            where cu.idCarrera == IdCarreraConduccion
                  && (idPeriodo == null || ap.idPeriodo == idPeriodo)
                  && (!soloVigentes
                      || (ap.fecha_inicial != null && ap.fecha_fin != null
                          && ap.fecha_inicial <= hoy && hoy <= ap.fecha_fin))
            select new
            {
                ap.idPeriodo, ap.idNivel, ap.idSeccion, ap.idModalidad, ap.paralelo, cu.Nivel
            }).ToListAsync(ct);

        var secciones = await _db.secciones.AsNoTracking()
            .ToDictionaryAsync(s => s.idSeccion, s => s.seccion, ct);
        var modalidades = await _db.modalidades.AsNoTracking()
            .ToDictionaryAsync(m => m.idModalidad, m => m.modalidad, ct);

        return filas
            .GroupBy(f => new
            {
                f.idPeriodo, f.idNivel, f.idSeccion, f.idModalidad,
                Paralelo = f.paralelo?.Trim() ?? string.Empty
            })
            .Select(g => new ParaleloDto(
                g.Key.idPeriodo, g.Key.idNivel, g.Key.idSeccion, g.Key.idModalidad, g.Key.Paralelo,
                g.First().Nivel,
                secciones.GetValueOrDefault(g.Key.idSeccion),
                modalidades.GetValueOrDefault(g.Key.idModalidad),
                g.Count()))
            .OrderBy(p => p.IdPeriodo)
            .ThenBy(p => p.Licencia)
            .ThenBy(p => p.Jornada)
            .ThenBy(p => p.Paralelo)
            .ToList();
    }
}
```

- [ ] **Step 4: Agregar el endpoint**

En `src/Leccionario.Api/Controllers/Distributivo/DistributivoController.cs`, inyectar `IParalelosInspectorService _paralelos` en el constructor y agregar:

```csharp
/// <summary>Todos los paralelos de la carrera 6. Solo inspector.</summary>
[HttpGet("paralelos")]
[Authorize(Roles = "cplec_inspector")]
[ProducesResponseType(typeof(IReadOnlyList<ParaleloDto>), StatusCodes.Status200OK)]
public async Task<IActionResult> Paralelos(
    [FromQuery] string? idPeriodo,
    [FromQuery] bool soloVigentes,
    CancellationToken ct) =>
    Ok(await _paralelos.ListarAsync(idPeriodo, soloVigentes, ct));
```

- [ ] **Step 5: Registrar en DI y correr los tests**

```csharp
services.AddScoped<IParalelosInspectorService, ParalelosInspectorService>();
```

```bash
cd src && dotnet test --filter "FullyQualifiedName~ParalelosInspectorServiceTests" -v q
```

Esperado: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(horarios): GET /api/paralelos para el selector del inspector"
```

---

## Task 10: `HorarioService` — grid y celda

**Files:**
- Modify: `src/Leccionario.Api/Application/Horarios/HorariosDtos.cs`
- Create: `src/Leccionario.Api/Application/Horarios/Services/HorarioService.cs`
- Create: `src/Leccionario.Api/Controllers/Horarios/HorariosController.cs`
- Create: `src/Leccionario.Tests/Horarios/EscrituraDirecta.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Test: `src/Leccionario.Tests/Horarios/HorarioServiceTests.cs`

**Interfaces:**
- Consumes: `IHorarioCarreraGuard`, `IFranjaZGuard`, `IConflictoHorarioService`, `IEscrituraSerializable`, `IFranjaService.ListarAsync`.
- Produces:
  ```csharp
  public sealed record ParaleloClaveDto(string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad, string Paralelo);
  public sealed record DiaGridDto(string Dia, DateOnly Fecha, int? IdFecha, bool Habilitado, string? Motivo);
  public sealed record CeldaGridDto(int IdHorario, int IdAsignacion, int Idhora, string Dia,
      string? NombreDocente, string? TipoBloque);
  public sealed record GridDto(IReadOnlyList<FranjaDto> Franjas, IReadOnlyList<DiaGridDto> Dias,
      IReadOnlyList<CeldaGridDto> Celdas);
  public sealed record CrearCeldaDto(int IdAsignacion, int IdFecha, int Idhora, string? TipoBloque,
      bool ConfirmarAdvertencias = false);
  public sealed record CeldaCreadaDto(int IdHorario, IReadOnlyList<ConflictoDto> Advertencias);

  public interface IHorarioService
  {
      Task<GridDto> ObtenerGridAsync(ParaleloClaveDto p, DateOnly lunes, CancellationToken ct = default);
      Task<CeldaCreadaDto> CrearAsync(CrearCeldaDto req, CancellationToken ct = default);
      Task<CeldaCreadaDto> ActualizarAsync(int idHorario, CrearCeldaDto req, CancellationToken ct = default);
      Task DesactivarAsync(int idHorario, CancellationToken ct = default);
  }
  ```

> **Revive de soft-delete.** Si existe una fila con la misma `(idAsignacion, idFecha, idhora)` y `activo = 0`, se hace `UPDATE activo = 1` en vez de `INSERT`. Sin esto, borrar y volver a poner la misma celda acumula filas muertas en una tabla compartida. Portado de `gestion_academica` (su ADR-0014).

- [ ] **Step 1: Agregar los DTOs**

Al final de `HorariosDtos.cs`:

```csharp
/// <summary>Un paralelo es la 5-tupla completa. Con menos, el join miente.</summary>
public sealed record ParaleloClaveDto(
    string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad, string Paralelo);

/// <param name="IdFecha">NULL si `fechas_horarios` no tiene esa fecha.</param>
public sealed record DiaGridDto(
    string Dia, DateOnly Fecha, int? IdFecha, bool Habilitado, string? Motivo);

public sealed record CeldaGridDto(
    int IdHorario, int IdAsignacion, int Idhora, string Dia,
    string? NombreDocente, string? TipoBloque);

public sealed record GridDto(
    IReadOnlyList<FranjaDto> Franjas,
    IReadOnlyList<DiaGridDto> Dias,
    IReadOnlyList<CeldaGridDto> Celdas);

/// <param name="ConfirmarAdvertencias">
/// El inspector vio el choque contra el horario de otra carrera y decidió guardar igual.
/// </param>
public sealed record CrearCeldaDto(
    int IdAsignacion, int IdFecha, int Idhora, string? TipoBloque,
    bool ConfirmarAdvertencias = false);

public sealed record CeldaCreadaDto(int IdHorario, IReadOnlyList<ConflictoDto> Advertencias);
```

- [ ] **Step 2: Escribir el doble de `IEscrituraSerializable`**

`src/Leccionario.Tests/Horarios/EscrituraDirecta.cs`:

```csharp
using Leccionario.Api.Application.Horarios.Services;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Doble de <see cref="IEscrituraSerializable"/> para tests unitarios: ejecuta la
/// operación sin transacción.
/// </summary>
/// <remarks>
/// EF InMemory no soporta transacciones ni niveles de aislamiento, así que la
/// transacción real no se puede ejercitar acá. La lógica de reintento se prueba
/// aparte en <c>PoliticaReintentoTests</c>, y el cableado con MySQL en integración.
/// </remarks>
internal sealed class EscrituraDirecta : IEscrituraSerializable
{
    public Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default) =>
        operacion(ct);
}
```

- [ ] **Step 3: Escribir los tests (fallan)**

`src/Leccionario.Tests/Horarios/HorarioServiceTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>Grid semanal y CRUD de celda. Ver spec sección 5.5.</summary>
[TestClass]
public sealed class HorarioServiceTests
{
    private const int NivelC6 = 35;
    private const int Asig100 = 100;
    private const int Z_0700 = 901;
    private const int Z_0800 = 902;
    private static readonly DateOnly Lunes = new(2026, 8, 3);

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static IHorarioService Crear(sigafi_esContext db) =>
        new HorarioService(db,
            new HorarioCarreraGuard(db), new FranjaZGuard(db),
            new ConflictoHorarioService(db), new FranjaService(db, new FranjaZGuard(db)),
            new EscrituraDirecta());

    private static ParaleloClaveDto Clave => new("TEST0001", NivelC6, 1, 1, "A");

    private static async Task SembrarAsync(sigafi_esContext db, bool calendarioCompleto = true)
    {
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(Z_0700).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(Z_0800).DeTipo("Z").DeRango("08:00", "09:00").Build());
        db.asignaciones_profesores.Add(
            new AsignacionBuilder().ConId(Asig100).ConNivel(NivelC6).ConParalelo("A").Build());
        db.profesores.Add(new profesores
        {
            idProfesor = "0000000001", apellidos = "PEREZ", nombres = "JUAN"
        });

        // Lunes a domingo de la semana del 2026-08-03.
        var dias = new[] { "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo" };
        var total = calendarioCompleto ? 7 : 6;   // sin el domingo
        for (var i = 0; i < total; i++)
        {
            db.fechas_horarios.Add(new fechas_horarios
            {
                idFecha = 500 + i, fecha = Lunes.AddDays(i), dia = dias[i]
            });
        }

        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Grid_SinCeldas_DevuelveFranjasYSieteDias()
    {
        using var db = CrearContexto(nameof(Grid_SinCeldas_DevuelveFranjasYSieteDias));
        await SembrarAsync(db);

        var g = await Crear(db).ObtenerGridAsync(Clave, Lunes);

        g.Franjas.Should().HaveCount(2);
        g.Dias.Should().HaveCount(7);
        g.Dias.Should().OnlyContain(d => d.Habilitado);
        g.Celdas.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Grid_ConDiaAusenteDelCalendario_LoMarcaDeshabilitadoConMotivo()
    {
        using var db = CrearContexto(nameof(Grid_ConDiaAusenteDelCalendario_LoMarcaDeshabilitadoConMotivo));
        await SembrarAsync(db, calendarioCompleto: false);

        var g = await Crear(db).ObtenerGridAsync(Clave, Lunes);

        var domingo = g.Dias.Single(d => d.Dia == "Domingo");
        domingo.Habilitado.Should().BeFalse();
        domingo.IdFecha.Should().BeNull();
        domingo.Motivo.Should().Contain("calendario");
    }

    [TestMethod]
    public async Task Grid_DevuelveLasCeldasDelParalelo()
    {
        using var db = CrearContexto(nameof(Grid_DevuelveLasCeldasDelParalelo));
        await SembrarAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(Z_0700).Build());
        await db.SaveChangesAsync();

        var g = await Crear(db).ObtenerGridAsync(Clave, Lunes);

        var c = g.Celdas.Should().ContainSingle().Which;
        c.Idhora.Should().Be(Z_0700);
        c.Dia.Should().Be("Lunes");
        c.NombreDocente.Should().Contain("PEREZ");
    }

    [TestMethod]
    public async Task Crear_CeldaLibre_InsertaConIdEspacioNull()
    {
        using var db = CrearContexto(nameof(Crear_CeldaLibre_InsertaConIdEspacioNull));
        await SembrarAsync(db);

        var r = await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, Z_0700, "teorico"));

        r.IdHorario.Should().BeGreaterThan(0);
        var fila = await db.horario_detalle.SingleAsync();
        fila.idEspacio.Should().BeNull();
        fila.activo.Should().Be(1);
        fila.tipoBloque.Should().Be("teorico");
    }

    [TestMethod]
    public async Task Crear_SobreFilaSoftDeleted_ReviveEnVezDeInsertar()
    {
        using var db = CrearContexto(nameof(Crear_SobreFilaSoftDeleted_ReviveEnVezDeInsertar));
        await SembrarAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(9)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(Z_0700).Activo(0).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, Z_0700, "practico"));

        r.IdHorario.Should().Be(9);
        db.horario_detalle.Should().HaveCount(1);
        var fila = await db.horario_detalle.SingleAsync();
        fila.activo.Should().Be(1);
        fila.tipoBloque.Should().Be("practico");
    }

    [TestMethod]
    public async Task Crear_AsignacionDeOtraCarrera_Rechaza()
    {
        using var db = CrearContexto(nameof(Crear_AsignacionDeOtraCarrera_Rechaza));
        await SembrarAsync(db);
        db.cursos.Add(new cursos { idNivel = 77, idCarrera = 19, Nivel = "PRIMERO" });
        db.asignaciones_profesores.Add(new AsignacionBuilder().ConId(300).ConNivel(77).Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearCeldaDto(300, 500, Z_0700, "teorico"));

        await acto.Should().ThrowAsync<FueraDeAlcanceException>();
    }

    [TestMethod]
    public async Task Crear_ConFranjaX_Rechaza()
    {
        using var db = CrearContexto(nameof(Crear_ConFranjaX_Rechaza));
        await SembrarAsync(db);
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").DeRango("07:00", "08:00").Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, 12, "teorico"));

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }

    [TestMethod]
    public async Task Crear_ConConflictoBloqueante_LanzaConflictoHorario()
    {
        using var db = CrearContexto(nameof(Crear_ConConflictoBloqueante_LanzaConflictoHorario));
        await SembrarAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(Z_0700).Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, Z_0700, "teorico"));

        (await acto.Should().ThrowAsync<ConflictoException>())
            .Which.Codigo.Should().Be("CONFLICTO_HORARIO");
    }

    [TestMethod]
    public async Task Crear_ConAdvertenciaSinConfirmar_Rechaza()
    {
        using var db = CrearContexto(nameof(Crear_ConAdvertenciaSinConfirmar_Rechaza));
        await SembrarAsync(db);
        db.carreras.Add(new carreras { idCarrera = 19, Carrera = "GASTRONOMIA" });
        db.cursos.Add(new cursos { idNivel = 77, idCarrera = 19, Nivel = "PRIMERO" });
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").DeRango("07:00", "08:00").Build());
        db.asignaciones_profesores.Add(
            new AsignacionBuilder().ConId(300).DelProfesor("0000000001").ConNivel(77).Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(300).EnFecha(500).EnFranja(12).Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, Z_0700, "teorico"));

        (await acto.Should().ThrowAsync<ConflictoException>())
            .Which.Codigo.Should().Be("ADVERTENCIA_NO_CONFIRMADA");
    }

    [TestMethod]
    public async Task Crear_ConAdvertenciaConfirmada_GuardaYDevuelveLaAdvertencia()
    {
        using var db = CrearContexto(nameof(Crear_ConAdvertenciaConfirmada_GuardaYDevuelveLaAdvertencia));
        await SembrarAsync(db);
        db.carreras.Add(new carreras { idCarrera = 19, Carrera = "GASTRONOMIA" });
        db.cursos.Add(new cursos { idNivel = 77, idCarrera = 19, Nivel = "PRIMERO" });
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").DeRango("07:00", "08:00").Build());
        db.asignaciones_profesores.Add(
            new AsignacionBuilder().ConId(300).DelProfesor("0000000001").ConNivel(77).Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(300).EnFecha(500).EnFranja(12).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).CrearAsync(
            new CrearCeldaDto(Asig100, 500, Z_0700, "teorico", ConfirmarAdvertencias: true));

        r.IdHorario.Should().BeGreaterThan(0);
        r.Advertencias.Should().ContainSingle().Which.Carrera.Should().Be("GASTRONOMIA");
    }

    [TestMethod]
    public async Task Desactivar_MarcaActivoCeroSinBorrarLaFila()
    {
        using var db = CrearContexto(nameof(Desactivar_MarcaActivoCeroSinBorrarLaFila));
        await SembrarAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(Z_0700).Build());
        await db.SaveChangesAsync();

        await Crear(db).DesactivarAsync(1);

        var fila = await db.horario_detalle.SingleAsync();
        fila.activo.Should().Be(0);
    }
}
```

- [ ] **Step 4: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~HorarioServiceTests" -v q
```

- [ ] **Step 5: Implementar el servicio**

`src/Leccionario.Api/Application/Horarios/Services/HorarioService.cs`:

```csharp
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>Grid semanal y CRUD de celda del horario. Ver spec sección 5.5.</summary>
public interface IHorarioService
{
    /// <param name="lunes">Primer día de la semana a mostrar.</param>
    Task<GridDto> ObtenerGridAsync(ParaleloClaveDto p, DateOnly lunes, CancellationToken ct = default);

    Task<CeldaCreadaDto> CrearAsync(CrearCeldaDto req, CancellationToken ct = default);
    Task<CeldaCreadaDto> ActualizarAsync(int idHorario, CrearCeldaDto req, CancellationToken ct = default);

    /// <summary>Borrado lógico.</summary>
    Task DesactivarAsync(int idHorario, CancellationToken ct = default);
}

/// <inheritdoc cref="IHorarioService"/>
public sealed class HorarioService : IHorarioService
{
    private static readonly string[] DiasSemana =
        ["Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo"];

    private readonly sigafi_esContext _db;
    private readonly IHorarioCarreraGuard _carrera;
    private readonly IFranjaZGuard _franjaZ;
    private readonly IConflictoHorarioService _conflictos;
    private readonly IFranjaService _franjas;
    private readonly IEscrituraSerializable _escritura;

    public HorarioService(
        sigafi_esContext db,
        IHorarioCarreraGuard carrera,
        IFranjaZGuard franjaZ,
        IConflictoHorarioService conflictos,
        IFranjaService franjas,
        IEscrituraSerializable escritura)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _carrera = carrera ?? throw new ArgumentNullException(nameof(carrera));
        _franjaZ = franjaZ ?? throw new ArgumentNullException(nameof(franjaZ));
        _conflictos = conflictos ?? throw new ArgumentNullException(nameof(conflictos));
        _franjas = franjas ?? throw new ArgumentNullException(nameof(franjas));
        _escritura = escritura ?? throw new ArgumentNullException(nameof(escritura));
    }

    public async Task<GridDto> ObtenerGridAsync(
        ParaleloClaveDto p, DateOnly lunes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);

        var franjas = await _franjas.ListarAsync(ct);
        var fechas = Enumerable.Range(0, 7).Select(lunes.AddDays).ToList();

        var calendario = await _db.fechas_horarios
            .AsNoTracking()
            .Where(f => f.fecha != null && fechas.Contains(f.fecha.Value))
            .ToDictionaryAsync(f => f.fecha!.Value, f => f.idFecha, ct);

        var dias = fechas.Select((fecha, i) =>
        {
            var tiene = calendario.TryGetValue(fecha, out var idFecha);
            return new DiaGridDto(
                DiasSemana[i], fecha,
                tiene ? idFecha : null,
                tiene,
                // fechas_horarios se alimenta por fuera de cplec y termina el
                // 2026-12-31: sin fila no hay horario ni sesión posible.
                tiene ? null : "La fecha no existe en el calendario institucional.");
        }).ToList();

        var idsFecha = calendario.Values.ToList();
        var paraleloNorm = p.Paralelo.Trim();

        var celdas = await (
            from hd in _db.horario_detalle.AsNoTracking()
            join ap in _db.asignaciones_profesores.AsNoTracking() on hd.idAsignacion equals ap.idAsignacion
            join pr in _db.profesores.AsNoTracking() on ap.idProfesor equals pr.idProfesor into prJoin
            from pr in prJoin.DefaultIfEmpty()
            join fh in _db.fechas_horarios.AsNoTracking() on hd.idFecha equals fh.idFecha
            where hd.activo == 1
                  && idsFecha.Contains(hd.idFecha)
                  && ap.idPeriodo == p.IdPeriodo
                  && ap.idNivel == p.IdNivel
                  && ap.idSeccion == p.IdSeccion
                  && ap.idModalidad == p.IdModalidad
                  && ap.paralelo != null && ap.paralelo.Trim() == paraleloNorm
            select new CeldaGridDto(
                hd.idHorario, hd.idAsignacion, hd.idhora, fh.dia ?? string.Empty,
                pr == null ? null : (pr.apellidos + " " + pr.nombres).Trim(),
                hd.tipoBloque))
            .ToListAsync(ct);

        return new GridDto(franjas, dias, celdas);
    }

    public Task<CeldaCreadaDto> CrearAsync(CrearCeldaDto req, CancellationToken ct = default) =>
        EscribirAsync(req, idHorarioExistente: null, ct);

    public Task<CeldaCreadaDto> ActualizarAsync(int idHorario, CrearCeldaDto req, CancellationToken ct = default) =>
        EscribirAsync(req, idHorario, ct);

    public async Task DesactivarAsync(int idHorario, CancellationToken ct = default)
    {
        var fila = await _db.horario_detalle.FirstOrDefaultAsync(h => h.idHorario == idHorario, ct)
            ?? throw new NoEncontradoException("La celda de horario no existe.");

        await _carrera.EnsureAsignacionEsDeCarrera6Async(fila.idAsignacion, ct);

        fila.activo = 0;
        await _db.SaveChangesAsync(ct);
    }

    /// <remarks>
    /// Las validaciones baratas y de frontera van FUERA de la transacción; solo
    /// la revalidación de conflictos y el escribir van dentro, para que el
    /// alcance del lock sea corto (ADR-008 decisión 6).
    /// </remarks>
    private async Task<CeldaCreadaDto> EscribirAsync(
        CrearCeldaDto req, int? idHorarioExistente, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await _carrera.EnsureAsignacionEsDeCarrera6Async(req.IdAsignacion, ct);
        await _franjaZ.EnsureEsFranjaZAsync(req.Idhora, ct);

        if (!await _db.fechas_horarios.AsNoTracking().AnyAsync(f => f.idFecha == req.IdFecha, ct))
            throw new ValidacionException("La fecha no existe en el calendario institucional.");

        return await _escritura.EjecutarAsync(async token =>
        {
            var conflictos = await _conflictos.ValidarAsync(
                new SolicitudConflictoDto(req.IdAsignacion, req.IdFecha, req.Idhora, idHorarioExistente),
                token);

            if (conflictos.HayBloqueantes)
                throw new ConflictoException("CONFLICTO_HORARIO", conflictos.Bloqueantes[0].Mensaje);

            if (conflictos.Advertencias.Count > 0 && !req.ConfirmarAdvertencias)
                throw new ConflictoException("ADVERTENCIA_NO_CONFIRMADA",
                    conflictos.Advertencias[0].Mensaje);

            var fila = idHorarioExistente is null
                ? await CrearORevivirAsync(req, token)
                : await _db.horario_detalle.FirstAsync(h => h.idHorario == idHorarioExistente, token);

            fila.idhora = req.Idhora;
            fila.idFecha = req.IdFecha;
            fila.tipoBloque = req.TipoBloque;
            fila.idEspacio = null;   // cplec no gestiona aulas (ADR-008 decisión 4)
            fila.activo = 1;

            await _db.SaveChangesAsync(token);

            return new CeldaCreadaDto(fila.idHorario, conflictos.Advertencias);
        }, ct);
    }

    /// <summary>
    /// Si la misma celda existe con `activo = 0`, se revive. Sin esto, borrar y
    /// reponer acumula filas muertas en una tabla que es de otro sistema.
    /// </summary>
    private async Task<horario_detalle> CrearORevivirAsync(CrearCeldaDto req, CancellationToken ct)
    {
        var muerta = await _db.horario_detalle.FirstOrDefaultAsync(h =>
            h.idAsignacion == req.IdAsignacion
            && h.idFecha == req.IdFecha
            && h.idhora == req.Idhora
            && h.activo != 1, ct);

        if (muerta is not null)
            return muerta;

        var nueva = new horario_detalle
        {
            idAsignacion = req.IdAsignacion,
            idFecha = req.IdFecha,
            idhora = req.Idhora,
            idEspacio = null,
            tipoBloque = req.TipoBloque,
            activo = 1
        };
        _db.horario_detalle.Add(nueva);
        return nueva;
    }
}
```

- [ ] **Step 6: Escribir el controller**

`src/Leccionario.Api/Controllers/Horarios/HorariosController.cs`:

```csharp
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Horarios;

/// <summary>Horario del paralelo. El docente solo lee su grid.</summary>
[ApiController]
[Route("api/horarios")]
[Authorize(Roles = "cplec_docente,cplec_inspector")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class HorariosController : ControllerBase
{
    private readonly IHorarioService _horarios;
    private readonly IConflictoHorarioService _conflictos;

    public HorariosController(IHorarioService horarios, IConflictoHorarioService conflictos)
    {
        _horarios = horarios;
        _conflictos = conflictos;
    }

    /// <summary>Grid de una semana. El paralelo es la 5-tupla completa.</summary>
    [HttpGet("grid")]
    [ProducesResponseType(typeof(GridDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Grid(
        [FromQuery] string idPeriodo, [FromQuery] int idNivel, [FromQuery] int idSeccion,
        [FromQuery] int idModalidad, [FromQuery] string paralelo, [FromQuery] DateOnly lunes,
        CancellationToken ct) =>
        Ok(await _horarios.ObtenerGridAsync(
            new ParaleloClaveDto(idPeriodo, idNivel, idSeccion, idModalidad, paralelo), lunes, ct));

    [HttpPost("validar-conflicto")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(ResultadoConflictoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidarConflicto(
        [FromBody] SolicitudConflictoDto request, CancellationToken ct) =>
        Ok(await _conflictos.ValidarAsync(request, ct));

    [HttpPost]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(CeldaCreadaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear([FromBody] CrearCeldaDto request, CancellationToken ct)
    {
        var r = await _horarios.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Grid), new { idHorario = r.IdHorario }, r);
    }

    [HttpPut("{idHorario:int}")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(CeldaCreadaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Actualizar(
        int idHorario, [FromBody] CrearCeldaDto request, CancellationToken ct) =>
        Ok(await _horarios.ActualizarAsync(idHorario, request, ct));

    [HttpDelete("{idHorario:int}")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Desactivar(int idHorario, CancellationToken ct)
    {
        await _horarios.DesactivarAsync(idHorario, ct);
        return NoContent();
    }
}
```

- [ ] **Step 7: Registrar en DI y correr los tests**

```csharp
services.AddScoped<IHorarioService, HorarioService>();
```

```bash
cd src && dotnet test --filter "FullyQualifiedName~HorarioServiceTests" -v q
```

Esperado: PASS, 11 tests.

- [ ] **Step 8: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(horarios): grid semanal y CRUD de celda con revive de soft-delete"
```

---

## Task 11: `HorarioRangoService` — replicar, editar y eliminar con topes

**Files:**
- Modify: `src/Leccionario.Api/Application/Horarios/HorariosDtos.cs`
- Create: `src/Leccionario.Api/Application/Horarios/Services/HorarioRangoService.cs`
- Modify: `src/Leccionario.Api/Controllers/Horarios/HorariosController.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Test: `src/Leccionario.Tests/Horarios/HorarioRangoServiceTests.cs`

**Interfaces:**
- Consumes: `IHorarioService.CrearAsync/DesactivarAsync`, `IHorarioCarreraGuard`, `IFranjaZGuard`.
- Produces:
  ```csharp
  public sealed record OperacionRangoDto(int IdAsignacion, string Dia, int Idhora,
      DateOnly Desde, DateOnly Hasta, string? TipoBloque = null, bool ConfirmarAdvertencias = false);
  public sealed record DetalleOperacionDto(DateOnly Fecha, bool Exitoso, string? MotivoFallo, int? IdHorario);
  public sealed record ResultadoRangoDto(int TotalProcesados, int TotalExitosos, int TotalFallidos,
      IReadOnlyList<DetalleOperacionDto> Detalles, string? Advertencia);

  public interface IHorarioRangoService
  {
      // `maxFilas` existe SOLO para poder ejercitar el tope en tests sin sembrar
      // 500 fechas. El controller nunca lo pasa.
      Task<ResultadoRangoDto> ReplicarAsync(OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default);
      Task<ResultadoRangoDto> ActualizarAsync(OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default);
      Task<ResultadoRangoDto> EliminarAsync(OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default);
  }

  public const int MaxSemanas = 16;      // en HorarioRangoService
  public const int MaxFilasPorLote = 500;
  ```

> **Los topes no son opcionales.** Los módulos de carrera 6 llegan a **408 días** y los cuatro paralelos vigentes hoy corren 380-408. Replicar Lun-Vie × 4 franjas sobre las 7 asignaciones vigentes daría **6 940 filas** contra las **1 781** que tiene `horario_detalle` entera, con datos de otras cuatro carreras adentro. Sin tope, el primer uso del módulo infla la tabla de otro sistema en un orden de magnitud.
>
> Son **constantes del código**, no configuración: un tope que se sube desde la UI no es un tope (ADR-008 decisión 11).

**Orden de validación** (todo antes de escribir nada):

1. `Desde <= Hasta` → 422 `RANGO_INVALIDO`
2. ≤ 16 semanas → 422 `RANGO_EXCEDE_TOPE`
3. Si la asignación tiene ventana, el rango cae dentro → 422 `FUERA_DE_VENTANA`. Si **no** la tiene (737 activas), se acepta con `Advertencia = "ASIGNACION_SIN_VENTANA"`.
4. Conteo previo ≤ 500 → 422 `LOTE_EXCEDE_TOPE`, **antes de abrir transacción**.

- [ ] **Step 1: Agregar los DTOs**

Al final de `HorariosDtos.cs`:

```csharp
/// <param name="Dia">Día de la semana en español sin tilde: Lunes … Domingo.</param>
public sealed record OperacionRangoDto(
    int IdAsignacion, string Dia, int Idhora, DateOnly Desde, DateOnly Hasta,
    string? TipoBloque = null, bool ConfirmarAdvertencias = false);

public sealed record DetalleOperacionDto(
    DateOnly Fecha, bool Exitoso, string? MotivoFallo, int? IdHorario);

/// <param name="Advertencia">P. ej. ASIGNACION_SIN_VENTANA. No bloquea.</param>
public sealed record ResultadoRangoDto(
    int TotalProcesados, int TotalExitosos, int TotalFallidos,
    IReadOnlyList<DetalleOperacionDto> Detalles, string? Advertencia);
```

- [ ] **Step 2: Escribir los tests (fallan)**

`src/Leccionario.Tests/Horarios/HorarioRangoServiceTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Operaciones por rango. Los topes existen porque escribimos en una tabla
/// compartida de producción y los módulos llegan a 408 días (spec H7).
/// </summary>
[TestClass]
public sealed class HorarioRangoServiceTests
{
    private const int NivelC6 = 35;
    private const int Asig100 = 100;
    private const int Z_0700 = 901;
    private static readonly DateOnly Lunes = new(2026, 8, 3);

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static IHorarioRangoService Crear(sigafi_esContext db)
    {
        var franjaZ = new FranjaZGuard(db);
        var horarios = new HorarioService(db, new HorarioCarreraGuard(db), franjaZ,
            new ConflictoHorarioService(db), new FranjaService(db, franjaZ), new EscrituraDirecta());
        return new HorarioRangoService(db, horarios, new HorarioCarreraGuard(db), franjaZ);
    }

    /// <summary>Siembra el calendario de `semanas` semanas desde el lunes base.</summary>
    private static async Task SembrarAsync(
        sigafi_esContext db, int semanas = 4,
        DateOnly? ventanaIni = null, DateOnly? ventanaFin = null, bool sinVentana = false,
        int diasFaltantes = 0)
    {
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.horas_clases.Add(new FranjaBuilder().ConId(Z_0700).DeTipo("Z").DeRango("07:00", "08:00").Build());

        var asig = new AsignacionBuilder().ConId(Asig100).ConNivel(NivelC6).ConParalelo("A")
            .ConRango(sinVentana ? null : ventanaIni ?? Lunes.AddDays(-30),
                      sinVentana ? null : ventanaFin ?? Lunes.AddDays(365))
            .Build();
        db.asignaciones_profesores.Add(asig);

        var nombres = new[] { "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo" };
        var id = 500;
        var total = semanas * 7;
        for (var i = 0; i < total - diasFaltantes; i++)
        {
            db.fechas_horarios.Add(new fechas_horarios
            {
                idFecha = id++, fecha = Lunes.AddDays(i), dia = nombres[i % 7]
            });
        }

        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Camino feliz
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Replicar_CuatroSemanas_CreaUnaCeldaPorLunes()
    {
        using var db = CrearContexto(nameof(Replicar_CuatroSemanas_CreaUnaCeldaPorLunes));
        await SembrarAsync(db, semanas: 4);

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(27), "teorico"));

        r.TotalExitosos.Should().Be(4);
        r.TotalFallidos.Should().Be(0);
        r.Advertencia.Should().BeNull();
        (await db.horario_detalle.CountAsync(h => h.activo == 1)).Should().Be(4);
    }

    [TestMethod]
    public async Task Replicar_SoloTocaElDiaDeLaSemanaPedido()
    {
        using var db = CrearContexto(nameof(Replicar_SoloTocaElDiaDeLaSemanaPedido));
        await SembrarAsync(db, semanas: 2);

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Miercoles", Z_0700, Lunes, Lunes.AddDays(13), "teorico"));

        r.TotalExitosos.Should().Be(2);
        r.Detalles.Should().OnlyContain(d => d.Fecha.DayOfWeek == DayOfWeek.Wednesday);
    }

    [TestMethod]
    public async Task Eliminar_DesactivaLasOcurrenciasDelRango()
    {
        using var db = CrearContexto(nameof(Eliminar_DesactivaLasOcurrenciasDelRango));
        await SembrarAsync(db, semanas: 3);
        var svc = Crear(db);
        await svc.ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        var r = await svc.EliminarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20)));

        r.TotalExitosos.Should().Be(3);
        (await db.horario_detalle.CountAsync(h => h.activo == 1)).Should().Be(0);
        (await db.horario_detalle.CountAsync()).Should().Be(3); // borrado lógico
    }

    // -------------------------------------------------------------------------
    // Topes: la parte que protege la tabla compartida
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Replicar_RangoDeMasDe16Semanas_RechazaAntesDeEscribir()
    {
        using var db = CrearContexto(nameof(Replicar_RangoDeMasDe16Semanas_RechazaAntesDeEscribir));
        await SembrarAsync(db, semanas: 20);

        var acto = async () => await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(7 * 17), "teorico"));

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("RANGO_EXCEDE_TOPE");
        (await db.horario_detalle.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task Replicar_Exactamente16Semanas_SeAcepta()
    {
        // El tope es inclusivo: 16 semanas justas pasan.
        using var db = CrearContexto(nameof(Replicar_Exactamente16Semanas_SeAcepta));
        await SembrarAsync(db, semanas: 17);

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(7 * 16 - 1), "teorico"));

        r.TotalExitosos.Should().Be(16);
    }

    [TestMethod]
    public async Task Replicar_LoteQueSuperaLas500Filas_RechazaAntesDeEscribir()
    {
        // Con un solo día de la semana no se llega a 500 en 16 semanas, así que
        // se fuerza el tope bajándolo por parámetro para poder ejercitarlo.
        using var db = CrearContexto(nameof(Replicar_LoteQueSuperaLas500Filas_RechazaAntesDeEscribir));
        await SembrarAsync(db, semanas: 5);

        var acto = async () => await Crear(db).ReplicarAsync(
            new OperacionRangoDto(Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(34), "teorico"),
            maxFilas: 3);

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("LOTE_EXCEDE_TOPE");
        (await db.horario_detalle.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public void Constantes_TienenLosValoresDelAdr()
    {
        // Los topes son constantes, no configuración: un tope ajustable desde la
        // UI no es un tope (ADR-008 decisión 11).
        HorarioRangoService.MaxSemanas.Should().Be(16);
        HorarioRangoService.MaxFilasPorLote.Should().Be(500);
    }

    // -------------------------------------------------------------------------
    // Rango y ventana
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Replicar_RangoInvertido_Rechaza()
    {
        using var db = CrearContexto(nameof(Replicar_RangoInvertido_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes.AddDays(7), Lunes, "teorico"));

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("RANGO_INVALIDO");
    }

    [TestMethod]
    public async Task Replicar_FueraDeLaVentanaDeLaAsignacion_Rechaza()
    {
        using var db = CrearContexto(nameof(Replicar_FueraDeLaVentanaDeLaAsignacion_Rechaza));
        await SembrarAsync(db, semanas: 8,
            ventanaIni: Lunes, ventanaFin: Lunes.AddDays(13));

        var acto = async () => await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(27), "teorico"));

        (await acto.Should().ThrowAsync<FueraDeVentanaException>())
            .Which.Codigo.Should().Be("FUERA_DE_VENTANA");
    }

    [TestMethod]
    public async Task Replicar_AsignacionSinVentana_AceptaConAdvertencia()
    {
        // 737 asignaciones activas de carrera 6 no tienen fecha_inicial/fecha_fin
        // (spec H8). No hay contra qué acotar: se acepta el rango explícito y se
        // avisa, en vez de bloquear por un dato que la carrera nunca cargó.
        using var db = CrearContexto(nameof(Replicar_AsignacionSinVentana_AceptaConAdvertencia));
        await SembrarAsync(db, semanas: 3, sinVentana: true);

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        r.TotalExitosos.Should().Be(3);
        r.Advertencia.Should().Be("ASIGNACION_SIN_VENTANA");
    }

    // -------------------------------------------------------------------------
    // Resiliencia del lote
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Replicar_ConFechaAusenteDelCalendario_LaReportaYSigueConElResto()
    {
        // El lote NO falla entero: se reporta el día que no se pudo y el resto
        // se procesa. fechas_horarios se alimenta por fuera de cplec.
        using var db = CrearContexto(nameof(Replicar_ConFechaAusenteDelCalendario_LaReportaYSigueConElResto));
        await SembrarAsync(db, semanas: 3, diasFaltantes: 7); // falta la 3ª semana

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        r.TotalProcesados.Should().Be(3);
        r.TotalExitosos.Should().Be(2);
        r.TotalFallidos.Should().Be(1);
        r.Detalles.Single(d => !d.Exitoso).MotivoFallo.Should().Contain("calendario");
    }

    [TestMethod]
    public async Task Replicar_ConUnDiaEnConflicto_LoReportaYCreaLosDemas()
    {
        using var db = CrearContexto(nameof(Replicar_ConUnDiaEnConflicto_LoReportaYCreaLosDemas));
        await SembrarAsync(db, semanas: 3);
        // Ocupamos el segundo lunes con la misma asignación y franja.
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(507).EnFranja(Z_0700).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        r.TotalExitosos.Should().Be(2);
        r.TotalFallidos.Should().Be(1);
    }

    [TestMethod]
    public async Task Replicar_SinNingunaFechaQueCoincida_DevuelveLoteVacio()
    {
        using var db = CrearContexto(nameof(Replicar_SinNingunaFechaQueCoincida_DevuelveLoteVacio));
        await SembrarAsync(db, semanas: 1);

        // Rango de martes a miércoles: no contiene ningún lunes.
        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes.AddDays(1), Lunes.AddDays(2), "teorico"));

        r.TotalProcesados.Should().Be(0);
        r.Detalles.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Replicar_ConDiaInvalido_Rechaza()
    {
        using var db = CrearContexto(nameof(Replicar_ConDiaInvalido_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunez", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        await acto.Should().ThrowAsync<ValidacionException>();
    }
}
```

- [ ] **Step 3: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~HorarioRangoServiceTests" -v q
```

- [ ] **Step 4: Implementar el servicio**

`src/Leccionario.Api/Application/Horarios/Services/HorarioRangoService.cs`:

```csharp
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Replica, edita o elimina todas las ocurrencias de
/// <c>(idAsignacion, díaSemana, idhora)</c> en un rango de fechas.
/// Ver <c>ADR-008</c> decisión 11 y spec sección 5.6.
/// </summary>
public interface IHorarioRangoService
{
    Task<ResultadoRangoDto> ReplicarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default);

    Task<ResultadoRangoDto> EliminarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default);
}

/// <inheritdoc cref="IHorarioRangoService"/>
/// <remarks>
/// <para><b>El rango siempre es explícito.</b> No existe "replicar todo el
/// período": los módulos de la carrera 6 llegan a 408 días y replicar los siete
/// paralelos vigentes generaría ~6 940 filas contra las ~1 781 que tiene
/// <c>horario_detalle</c> entera, con datos de otras cuatro carreras adentro.</para>
/// <para>Los topes son constantes, no configuración. El parámetro
/// <c>maxFilas</c> existe únicamente para poder ejercitar el tope en tests sin
/// sembrar 500 fechas; el controller nunca lo pasa.</para>
/// <para>Superadas las validaciones, <b>el lote no falla entero</b>: cada fecha
/// se reporta por separado. Una fecha ausente del calendario o un conflicto no
/// deben tirar abajo las otras quince semanas de trabajo del inspector.</para>
/// </remarks>
public sealed class HorarioRangoService : IHorarioRangoService
{
    /// <summary>Semanas máximas por operación (inclusivo).</summary>
    public const int MaxSemanas = 16;

    /// <summary>Filas máximas que una sola operación puede crear.</summary>
    public const int MaxFilasPorLote = 500;

    private static readonly string[] DiasSemana =
        ["Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo"];

    private readonly sigafi_esContext _db;
    private readonly IHorarioService _horarios;
    private readonly IHorarioCarreraGuard _carrera;
    private readonly IFranjaZGuard _franjaZ;

    public HorarioRangoService(
        sigafi_esContext db, IHorarioService horarios,
        IHorarioCarreraGuard carrera, IFranjaZGuard franjaZ)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _horarios = horarios ?? throw new ArgumentNullException(nameof(horarios));
        _carrera = carrera ?? throw new ArgumentNullException(nameof(carrera));
        _franjaZ = franjaZ ?? throw new ArgumentNullException(nameof(franjaZ));
    }

    public Task<ResultadoRangoDto> ReplicarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default) =>
        EjecutarAsync(req, maxFilas, esEliminacion: false, ct);

    public Task<ResultadoRangoDto> EliminarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default) =>
        EjecutarAsync(req, maxFilas, esEliminacion: true, ct);

    private async Task<ResultadoRangoDto> EjecutarAsync(
        OperacionRangoDto req, int? maxFilas, bool esEliminacion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        var indiceDia = Array.FindIndex(DiasSemana,
            d => string.Equals(d, req.Dia, StringComparison.OrdinalIgnoreCase));
        if (indiceDia < 0)
            throw new ValidacionException(
                $"Día inválido: '{req.Dia}'. Valores válidos: {string.Join(", ", DiasSemana)}.");

        await _carrera.EnsureAsignacionEsDeCarrera6Async(req.IdAsignacion, ct);
        await _franjaZ.EnsureEsFranjaZAsync(req.Idhora, ct);

        var advertencia = await ValidarRangoAsync(req, ct);

        // Fechas del rango que caen en el día de semana pedido.
        var objetivo = (DayOfWeek)((indiceDia + 1) % 7);
        var fechas = new List<DateOnly>();
        for (var f = req.Desde; f <= req.Hasta; f = f.AddDays(1))
            if (f.DayOfWeek == objetivo)
                fechas.Add(f);

        // Conteo previo: se rechaza ANTES de escribir o abrir transacción alguna.
        var tope = maxFilas ?? MaxFilasPorLote;
        if (fechas.Count > tope)
            throw new ConflictoLoteException("LOTE_EXCEDE_TOPE",
                $"La operación generaría {fechas.Count} filas y el máximo es {tope}. " +
                "Reduce el rango y repite la operación por tramos.");

        var calendario = await _db.fechas_horarios
            .AsNoTracking()
            .Where(f => f.fecha != null && f.fecha >= req.Desde && f.fecha <= req.Hasta)
            .ToDictionaryAsync(f => f.fecha!.Value, f => f.idFecha, ct);

        var detalles = new List<DetalleOperacionDto>(fechas.Count);

        foreach (var fecha in fechas)
        {
            if (!calendario.TryGetValue(fecha, out var idFecha))
            {
                detalles.Add(new DetalleOperacionDto(fecha, false,
                    "La fecha no existe en el calendario institucional.", null));
                continue;
            }

            try
            {
                if (esEliminacion)
                {
                    var fila = await _db.horario_detalle.FirstOrDefaultAsync(h =>
                        h.idAsignacion == req.IdAsignacion && h.idFecha == idFecha
                        && h.idhora == req.Idhora && h.activo == 1, ct);

                    if (fila is null)
                    {
                        detalles.Add(new DetalleOperacionDto(fecha, false, "No había horario ese día.", null));
                        continue;
                    }

                    await _horarios.DesactivarAsync(fila.idHorario, ct);
                    detalles.Add(new DetalleOperacionDto(fecha, true, null, fila.idHorario));
                }
                else
                {
                    var r = await _horarios.CrearAsync(new CrearCeldaDto(
                        req.IdAsignacion, idFecha, req.Idhora, req.TipoBloque,
                        req.ConfirmarAdvertencias), ct);
                    detalles.Add(new DetalleOperacionDto(fecha, true, null, r.IdHorario));
                }
            }
            catch (AppException ex)
            {
                // Un conflicto o una advertencia no confirmada en un día concreto
                // se reporta y no aborta las demás semanas.
                detalles.Add(new DetalleOperacionDto(fecha, false, ex.Message, null));
            }
        }

        return new ResultadoRangoDto(
            detalles.Count,
            detalles.Count(d => d.Exitoso),
            detalles.Count(d => !d.Exitoso),
            detalles,
            advertencia);
    }

    /// <returns>La advertencia a propagar, o null.</returns>
    private async Task<string?> ValidarRangoAsync(OperacionRangoDto req, CancellationToken ct)
    {
        if (req.Desde > req.Hasta)
            throw new ConflictoLoteException("RANGO_INVALIDO",
                "La fecha inicial debe ser anterior o igual a la final.");

        var dias = req.Hasta.DayNumber - req.Desde.DayNumber + 1;
        if (dias > MaxSemanas * 7)
            throw new ConflictoLoteException("RANGO_EXCEDE_TOPE",
                $"El rango abarca {dias} días y el máximo por operación es {MaxSemanas} semanas. " +
                "Divide la carga en tramos.");

        var ap = await _db.asignaciones_profesores
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.idAsignacion == req.IdAsignacion, ct)
            ?? throw new NoEncontradoException("La asignación solicitada no existe.");

        if (ap.fecha_inicial is null || ap.fecha_fin is null)
            return "ASIGNACION_SIN_VENTANA";

        if (req.Desde < ap.fecha_inicial.Value || req.Hasta > ap.fecha_fin.Value)
            throw new FueraDeVentanaException(ap.fecha_inicial, ap.fecha_fin);

        return null;
    }
}

/// <summary>422 — la operación por rango no cumple sus topes o su rango.</summary>
public sealed class ConflictoLoteException : AppException
{
    public ConflictoLoteException(string codigo, string mensaje)
        : base(codigo, 422, mensaje) { }
}
```

> `ConflictoLoteException` vive en este archivo a propósito: sus tres códigos (`RANGO_INVALIDO`, `RANGO_EXCEDE_TOPE`, `LOTE_EXCEDE_TOPE`) solo los produce este servicio. Si más adelante otro los necesita, se mueve a `Application/Common/Exceptions/`.

- [ ] **Step 5: Agregar los endpoints**

En `HorariosController.cs`, inyectar `IHorarioRangoService _rangos` y agregar:

```csharp
/// <summary>
/// Replica una celda sobre un rango. Rango explícito obligatorio, máximo
/// 16 semanas y 500 filas por operación (ADR-008 decisión 11).
/// </summary>
[HttpPost("replicar-rango")]
[Authorize(Roles = "cplec_inspector")]
[ProducesResponseType(typeof(ResultadoRangoDto), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
public async Task<IActionResult> ReplicarRango(
    [FromBody] OperacionRangoDto request, CancellationToken ct) =>
    Ok(await _rangos.ReplicarAsync(request, ct: ct));

[HttpDelete("rango")]
[Authorize(Roles = "cplec_inspector")]
[ProducesResponseType(typeof(ResultadoRangoDto), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
public async Task<IActionResult> EliminarRango(
    [FromBody] OperacionRangoDto request, CancellationToken ct) =>
    Ok(await _rangos.EliminarAsync(request, ct: ct));
```

- [ ] **Step 6: Registrar en DI y correr los tests**

```csharp
services.AddScoped<IHorarioRangoService, HorarioRangoService>();
```

```bash
cd src && dotnet test --filter "FullyQualifiedName~HorarioRangoServiceTests" -v q
```

Esperado: PASS, 14 tests (16 al terminar el Step 7).

- [ ] **Step 7: Agregar `ActualizarAsync` por rango**

El spec lista `PUT /api/horarios/rango`. Con `idEspacio` siempre NULL, el único campo editable de una celda es `tipoBloque`, así que la operación es corta pero existe: sirve para pasar un módulo entero de teórico a práctico sin borrar y recrear.

Test, en el mismo archivo:

```csharp
[TestMethod]
public async Task Actualizar_CambiaElTipoDeBloqueEnTodoElRango()
{
    using var db = CrearContexto(nameof(Actualizar_CambiaElTipoDeBloqueEnTodoElRango));
    await SembrarAsync(db, semanas: 3);
    var svc = Crear(db);
    await svc.ReplicarAsync(new OperacionRangoDto(
        Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

    var r = await svc.ActualizarAsync(new OperacionRangoDto(
        Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "practico"));

    r.TotalExitosos.Should().Be(3);
    (await db.horario_detalle.Where(h => h.activo == 1).ToListAsync())
        .Should().OnlyContain(h => h.tipoBloque == "practico");
}

[TestMethod]
public async Task Actualizar_DiaSinHorario_LoReportaSinFallarElLote()
{
    using var db = CrearContexto(nameof(Actualizar_DiaSinHorario_LoReportaSinFallarElLote));
    await SembrarAsync(db, semanas: 2);

    var r = await Crear(db).ActualizarAsync(new OperacionRangoDto(
        Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(13), "practico"));

    r.TotalFallidos.Should().Be(2);
    r.Detalles.Should().OnlyContain(d => d.MotivoFallo!.Contains("No había horario"));
}
```

Implementación: agregar a la interfaz y delegar en el mismo `EjecutarAsync`, con un tercer modo.

```csharp
public Task<ResultadoRangoDto> ActualizarAsync(
    OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default) =>
    EjecutarAsync(req, maxFilas, ModoRango.Actualizar, ct);
```

Reemplazar el parámetro `bool esEliminacion` por `ModoRango modo` (`Replicar`, `Actualizar`, `Eliminar`) y, en el bucle, agregar la rama:

```csharp
case ModoRango.Actualizar:
{
    var fila = await _db.horario_detalle.FirstOrDefaultAsync(h =>
        h.idAsignacion == req.IdAsignacion && h.idFecha == idFecha
        && h.idhora == req.Idhora && h.activo == 1, ct);

    if (fila is null)
    {
        detalles.Add(new DetalleOperacionDto(fecha, false, "No había horario ese día.", null));
        break;
    }

    // Solo tipoBloque: mover día u hora es reasignación, fuera de alcance de M4b.
    fila.tipoBloque = req.TipoBloque;
    await _db.SaveChangesAsync(ct);
    detalles.Add(new DetalleOperacionDto(fecha, true, null, fila.idHorario));
    break;
}
```

Y el endpoint en `HorariosController.cs`:

```csharp
[HttpPut("rango")]
[Authorize(Roles = "cplec_inspector")]
[ProducesResponseType(typeof(ResultadoRangoDto), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
public async Task<IActionResult> ActualizarRango(
    [FromBody] OperacionRangoDto request, CancellationToken ct) =>
    Ok(await _rangos.ActualizarAsync(request, ct: ct));
```

Correr: `cd src && dotnet test --filter "FullyQualifiedName~HorarioRangoServiceTests" -v q` → PASS, 16 tests.

- [ ] **Step 8: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(horarios): operaciones por rango con topes de 16 semanas y 500 filas

Escribimos en tabla compartida y los modulos llegan a 408 dias: sin tope el
primer uso inflaria horario_detalle en un orden de magnitud (spec H7)."
```

---

## Task 12: Migración 005 y sesión anclada al horario

**Files:**
- Create: `database/migrations/005_cplec_sesiones_horario.sql`
- Create: `database/rollback/005_cplec_sesiones_horario_rollback.sql`
- Modify: `src/Leccionario.Api/Domain/Entities/cplec_sesiones.cs` (regenerado)
- Modify: `src/Leccionario.Api/Application/Asistencia/SesionService.cs`
- Modify: `src/Leccionario.Api/Application/Asistencia/AsistenciaDtos.cs` (donde vivan `CrearSesionRequestDto` y `SesionDto`)
- Modify: `src/Leccionario.Api/Controllers/Asistencia/SesionesController.cs`
- Test: `src/Leccionario.Tests/Asistencia/SesionServiceTests.cs`

**Interfaces:**
- Consumes: `BloqueHorarioCalculator.Agrupar` (Task 6).
- Produces: `CrearSesionRequestDto` con `IdHorarioInicio` (nullable) además de `Fecha`/`NumeroBloque`; `SesionDto` con `Origen`, `EsTardia`, `DiasRetraso`, `FranjasPlanificadas`, `MinutosPlanificados`. La tarea 13 los reutiliza.

> **Modo transición.** Si la asignación no tiene ninguna fila de horario activa, se acepta el flujo actual con `Fecha` libre e `IdHorarioInicio` NULL, y la respuesta trae `Origen = "libre"`. Sin esto, el día del despliegue todos los paralelos sin horario cargado quedan sin poder pasar lista. Si la asignación **sí** tiene horario, `IdHorarioInicio` es obligatorio.
>
> **La tardanza se congela.** `DiasRetraso` y `EsTardia` se calculan una sola vez, al crear. Ninguna edición, cierre o reapertura los recalcula: miden cuándo se registró por primera vez, no cuándo se tocó por última vez.

- [ ] **Step 1: Escribir la migración y su rollback**

`database/migrations/005_cplec_sesiones_horario.sql`:

```sql
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
```

`database/rollback/005_cplec_sesiones_horario_rollback.sql`:

```sql
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
```

- [ ] **Step 2: Aplicar la migración y regenerar la entidad**

Correr `005` contra desarrollo según `database/migrations/README.md`, verificar con `SHOW CREATE TABLE cplec_sesiones`, y regenerar `cplec_sesiones.cs` con EF Core Power Tools.

Verificar las propiedades nuevas:

```bash
grep -E "idHorarioInicio|franjasPlanificadas|minutosPlanificados|esTardia|diasRetraso" \
  src/Leccionario.Api/Domain/Entities/cplec_sesiones.cs
```

Esperado: 5 líneas. Tipos: `int?`, `sbyte?`, `short?`, `bool`, `short`.

- [ ] **Step 3: Ampliar los DTOs**

En el archivo de DTOs de asistencia, agregar a `CrearSesionRequestDto` la propiedad `int? IdHorarioInicio`, dejando `Fecha` y `NumeroBloque` como están (los usa el modo transición), y a `SesionDto`:

```csharp
/// <summary>"horario" si la sesión cuelga de una celda planificada; "libre" si no.</summary>
public string Origen { get; init; } = "libre";

/// <summary>Se registró después del día de clase. Congelado al crear.</summary>
public bool EsTardia { get; init; }

/// <summary>Días entre la clase y el primer guardado. Congelado al crear.</summary>
public int DiasRetraso { get; init; }

public int? FranjasPlanificadas { get; init; }
public int? MinutosPlanificados { get; init; }
```

- [ ] **Step 4: Escribir los tests (fallan)**

Agregar a `src/Leccionario.Tests/Asistencia/SesionServiceTests.cs`. Usar el patrón de contexto y siembra ya presente en ese archivo, más `FranjaBuilder` y `HorarioDetalleBuilder`. Fijar el reloj con `TimeProvider`:

```csharp
/// <summary>Reloj fijo, para poder afirmar sobre `diasRetraso`.</summary>
private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora;
}

[TestMethod]
public async Task Crear_DesdeIdHorarioInicio_DerivaFechaBloqueYMinutos()
{
    // Horario del lunes: 07:00–08:00 y 08:00–09:00 contiguas → un bloque de 2
    // franjas y 120 minutos, numeroBloque 1.
    using var db = CrearContexto(nameof(Crear_DesdeIdHorarioInicio_DerivaFechaBloqueYMinutos));
    await SembrarHorarioAsync(db);   // ver Step 5

    var sesion = await CrearServicio(db, new RelojFijo(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)))
        .CrearAsync(Asig100, Duenio, false,
            new CrearSesionRequestDto { IdHorarioInicio = 1, Tema = "Señalética" });

    sesion.Origen.Should().Be("horario");
    sesion.NumeroBloque.Should().Be(1);
    sesion.FranjasPlanificadas.Should().Be(2);
    sesion.MinutosPlanificados.Should().Be(120);
    sesion.EsTardia.Should().BeFalse();
    sesion.DiasRetraso.Should().Be(0);
}

[TestMethod]
public async Task Crear_TresDiasDespues_MarcaTardiaConElRetraso()
{
    using var db = CrearContexto(nameof(Crear_TresDiasDespues_MarcaTardiaConElRetraso));
    await SembrarHorarioAsync(db);   // clase el 2026-08-03

    var sesion = await CrearServicio(db, new RelojFijo(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero)))
        .CrearAsync(Asig100, Duenio, false,
            new CrearSesionRequestDto { IdHorarioInicio = 1, Tema = "Señalética" });

    sesion.EsTardia.Should().BeTrue();
    sesion.DiasRetraso.Should().Be(3);
}

[TestMethod]
public async Task Editar_NoRecalculaElRetraso()
{
    // El retraso mide cuándo se registró por primera vez, no cuándo se tocó por
    // última vez. Si se recalculara, editar una sesión tardía la "limpiaría".
    using var db = CrearContexto(nameof(Editar_NoRecalculaElRetraso));
    await SembrarHorarioAsync(db);
    var creada = await CrearServicio(db, new RelojFijo(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero)))
        .CrearAsync(Asig100, Duenio, false,
            new CrearSesionRequestDto { IdHorarioInicio = 1, Tema = "Señalética" });

    var editada = await CrearServicio(db, new RelojFijo(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero)))
        .EditarAsync(creada.IdSesion, Duenio, false,
            new EditarSesionRequestDto { Tema = "Señalética vertical" });

    editada.DiasRetraso.Should().Be(3);
    editada.EsTardia.Should().BeTrue();
}

[TestMethod]
public async Task Crear_ConFechaFutura_Rechaza()
{
    using var db = CrearContexto(nameof(Crear_ConFechaFutura_Rechaza));
    await SembrarHorarioAsync(db);

    var acto = async () => await CrearServicio(db,
            new RelojFijo(new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)))
        .CrearAsync(Asig100, Duenio, false,
            new CrearSesionRequestDto { IdHorarioInicio = 1, Tema = "Señalética" });

    (await acto.Should().ThrowAsync<AppException>())
        .Which.Codigo.Should().Be("SESION_FUTURA");
}

[TestMethod]
public async Task Crear_EsIdempotentePorBloque()
{
    using var db = CrearContexto(nameof(Crear_EsIdempotentePorBloque));
    await SembrarHorarioAsync(db);
    var svc = CrearServicio(db, new RelojFijo(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)));
    var req = new CrearSesionRequestDto { IdHorarioInicio = 1, Tema = "Señalética" };

    var a = await svc.CrearAsync(Asig100, Duenio, false, req);
    var b = await svc.CrearAsync(Asig100, Duenio, false, req);

    b.IdSesion.Should().Be(a.IdSesion);
    (await db.cplec_sesiones.CountAsync()).Should().Be(1);
}

[TestMethod]
public async Task Crear_ConIdHorarioDeOtraAsignacion_Rechaza()
{
    using var db = CrearContexto(nameof(Crear_ConIdHorarioDeOtraAsignacion_Rechaza));
    await SembrarHorarioAsync(db);
    db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(50)
        .DeAsignacion(999).EnFecha(500).EnFranja(903).Build());
    await db.SaveChangesAsync();

    var acto = async () => await CrearServicio(db,
            new RelojFijo(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)))
        .CrearAsync(Asig100, Duenio, false,
            new CrearSesionRequestDto { IdHorarioInicio = 50, Tema = "X" });

    await acto.Should().ThrowAsync<DistributivoAjenoException>();
}

[TestMethod]
public async Task Crear_SinHorarioEnLaAsignacion_AceptaFechaLibreYMarcaOrigenLibre()
{
    // Modo transición: sin esto, el día del despliegue todos los paralelos sin
    // horario cargado quedan sin poder pasar lista (ADR-008 decisión 9).
    using var db = CrearContexto(nameof(Crear_SinHorarioEnLaAsignacion_AceptaFechaLibreYMarcaOrigenLibre));
    await SembrarSinHorarioAsync(db);

    var sesion = await CrearServicio(db, new RelojFijo(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)))
        .CrearAsync(Asig100, Duenio, false,
            new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 3), NumeroBloque = 1, Tema = "Libre" });

    sesion.Origen.Should().Be("libre");
    sesion.MinutosPlanificados.Should().BeNull();
}

[TestMethod]
public async Task Crear_ConHorarioPeroSinIdHorarioInicio_Rechaza()
{
    using var db = CrearContexto(nameof(Crear_ConHorarioPeroSinIdHorarioInicio_Rechaza));
    await SembrarHorarioAsync(db);

    var acto = async () => await CrearServicio(db,
            new RelojFijo(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)))
        .CrearAsync(Asig100, Duenio, false,
            new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 3), NumeroBloque = 1, Tema = "X" });

    (await acto.Should().ThrowAsync<AppException>())
        .Which.Codigo.Should().Be("HORARIO_REQUERIDO");
}
```

- [ ] **Step 5: Escribir los helpers de siembra del test**

En la misma clase:

```csharp
/// <summary>Asignación de carrera 6 con dos franjas contiguas el lunes 2026-08-03.</summary>
private static async Task SembrarHorarioAsync(sigafi_esContext db)
{
    await SembrarSinHorarioAsync(db);
    db.horas_clases.AddRange(
        new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build(),
        new FranjaBuilder().ConId(902).DeTipo("Z").DeRango("08:00", "09:00").Build(),
        new FranjaBuilder().ConId(903).DeTipo("Z").DeRango("15:00", "16:00").Build());
    db.horario_detalle.AddRange(
        new HorarioDetalleBuilder().ConId(1).DeAsignacion(Asig100).EnFecha(500).EnFranja(901).Build(),
        new HorarioDetalleBuilder().ConId(2).DeAsignacion(Asig100).EnFecha(500).EnFranja(902).Build());
    await db.SaveChangesAsync();
}

private static async Task SembrarSinHorarioAsync(sigafi_esContext db)
{
    db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
    db.asignaciones_profesores.Add(new AsignacionBuilder()
        .ConId(Asig100).DelProfesor(Duenio).ConNivel(35)
        .ConRango(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).Build());
    db.fechas_horarios.Add(new fechas_horarios
    {
        idFecha = 500, fecha = new DateOnly(2026, 8, 3), dia = "Lunes"
    });
    await db.SaveChangesAsync();
}
```

- [ ] **Step 6: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~SesionServiceTests" -v q
```

- [ ] **Step 7: Modificar `SesionService.CrearAsync`**

Reemplazar la resolución de `idFecha` y `numeroBloque` por la derivación desde el horario. Insertar después del `EnsureDocenteTieneAsignacionAsync` existente:

```csharp
// ---- Anclaje al horario (ADR-008 decisiones 7 y 9) -----------------------
var bloques = await ResolverBloquesAsync(idAsignacion, request.IdHorarioInicio, ct);

int idFecha;
int numeroBloque;
BloqueHorario? bloque = null;

if (bloques is { Count: > 0 })
{
    if (request.IdHorarioInicio is null)
        throw new HorarioRequeridoException();

    bloque = bloques.FirstOrDefault(b => b.IdHorarioInicio == request.IdHorarioInicio)
        ?? throw new DistributivoAjenoException();

    idFecha = await IdFechaDelHorarioAsync(request.IdHorarioInicio.Value, ct);
    numeroBloque = bloque.NumeroBloque;
}
else
{
    // Modo transición: la asignación no tiene horario cargado.
    idFecha = await ResolverIdFechaLibreAsync(request.Fecha, ct);
    numeroBloque = request.NumeroBloque;
}

var fechaClase = await FechaDeAsync(idFecha, ct);
var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);

if (fechaClase > hoy)
    throw new SesionFuturaException();

// Congelado: mide el PRIMER guardado, no el último. Ver ADR-008 decisión 8.
var diasRetraso = Math.Max(0, fechaClase.DayNumber - fechaClase.DayNumber + (hoy.DayNumber - fechaClase.DayNumber));
```

> Simplificar esa última línea a `var diasRetraso = Math.Max(0, hoy.DayNumber - fechaClase.DayNumber);` — se deja la forma verbosa arriba solo para que se vea de dónde sale; escribir la corta.

Al construir la entidad `cplec_sesiones`:

```csharp
idHorarioInicio     = bloque?.IdHorarioInicio,
franjasPlanificadas = (sbyte?)bloque?.FranjasPlanificadas,
minutosPlanificados = (short?)bloque?.MinutosPlanificados,
esTardia            = diasRetraso > 0,
diasRetraso         = (short)diasRetraso,
```

La validación de ventana `fecha_inicial .. fecha_fin` existente **se mantiene** tal cual.

- [ ] **Step 8: Agregar los helpers privados a `SesionService`**

```csharp
/// <summary>
/// Bloques contiguos de la asignación en el día del `idHorarioInicio` pedido.
/// Devuelve lista vacía si la asignación no tiene ningún horario activo, que es
/// la señal del modo transición.
/// </summary>
private async Task<IReadOnlyList<BloqueHorario>> ResolverBloquesAsync(
    int idAsignacion, int? idHorarioInicio, CancellationToken ct)
{
    var tieneHorario = await _db.horario_detalle
        .AsNoTracking()
        .AnyAsync(h => h.idAsignacion == idAsignacion && h.activo == 1, ct);

    if (!tieneHorario)
        return Array.Empty<BloqueHorario>();

    if (idHorarioInicio is null)
        return new[] { default(BloqueHorario)! };   // no vacío: fuerza HORARIO_REQUERIDO

    var idFecha = await _db.horario_detalle
        .AsNoTracking()
        .Where(h => h.idHorario == idHorarioInicio)
        .Select(h => (int?)h.idFecha)
        .FirstOrDefaultAsync(ct)
        ?? throw new NoEncontradoException("La celda de horario no existe.");

    var filas = await (
        from hd in _db.horario_detalle.AsNoTracking()
        join hc in _db.horas_clases.AsNoTracking() on hd.idhora equals hc.idhora
        where hd.idAsignacion == idAsignacion && hd.idFecha == idFecha && hd.activo == 1
        select new { hd.idHorario, hd.idhora, hc.hora_inicio, hc.hora_fin, hc.minutos })
        .ToListAsync(ct);

    var franjas = filas
        .Select(f =>
        {
            if (!TimeOnly.TryParse(f.hora_inicio, out var ini) ||
                !TimeOnly.TryParse(f.hora_fin, out var fin) || ini >= fin)
                return null;
            return new FranjaOrdenable(f.idHorario, f.idhora, ini, fin,
                f.minutos ?? (int)(fin - ini).TotalMinutes);
        })
        .Where(f => f is not null)
        .Select(f => f!);

    return BloqueHorarioCalculator.Agrupar(franjas);
}

private Task<int> IdFechaDelHorarioAsync(int idHorario, CancellationToken ct) =>
    _db.horario_detalle.AsNoTracking()
        .Where(h => h.idHorario == idHorario)
        .Select(h => h.idFecha)
        .FirstAsync(ct);

private async Task<DateOnly> FechaDeAsync(int idFecha, CancellationToken ct) =>
    await _db.fechas_horarios.AsNoTracking()
        .Where(f => f.idFecha == idFecha)
        .Select(f => f.fecha)
        .FirstOrDefaultAsync(ct)
    ?? throw new ValidacionException("La fecha no existe en el calendario institucional.");
```

> El `new[] { default(BloqueHorario)! }` del caso "tiene horario pero no mandaron `idHorarioInicio`" es feo. Sustituirlo por una señal explícita al implementar: cambiar la firma a `Task<(bool TieneHorario, IReadOnlyList<BloqueHorario> Bloques)>` y decidir en el llamador. El test `Crear_ConHorarioPeroSinIdHorarioInicio_Rechaza` fija el comportamiento.

- [ ] **Step 9: Agregar las dos excepciones**

`src/Leccionario.Api/Application/Common/Exceptions/SesionFuturaException.cs`:

```csharp
namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>422 SESION_FUTURA — no se pasa lista de una clase que no ocurrió.</summary>
public sealed class SesionFuturaException : AppException
{
    public SesionFuturaException()
        : base("SESION_FUTURA", 422, "No se puede registrar una clase que aún no ocurrió.") { }
}
```

`src/Leccionario.Api/Application/Common/Exceptions/HorarioRequeridoException.cs`:

```csharp
namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 422 HORARIO_REQUERIDO — la asignación tiene horario planificado, así que la
/// sesión debe colgar de un bloque y no de una fecha suelta.
/// </summary>
public sealed class HorarioRequeridoException : AppException
{
    public HorarioRequeridoException()
        : base("HORARIO_REQUERIDO", 422,
               "Esta asignación tiene horario: indica el bloque al que corresponde la clase.") { }
}
```

- [ ] **Step 10: Correr los tests**

```bash
cd src && dotnet test --filter "FullyQualifiedName~SesionServiceTests" -v q
```

Esperado: PASS. Los tests previos de `SesionService` deben seguir verdes — usan asignaciones sin horario, así que caen en el modo transición.

- [ ] **Step 11: Commit**

```bash
git add database src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(asistencia): sesion anclada al bloque de horario y tardanza congelada

Migracion 005 sobre cplec_sesiones (tabla propia). Modo transicion para
asignaciones sin horario: sin el, el dia del despliegue nadie pasa lista."
```

---

## Task 13: `AgendaService` y reportes del inspector

**Files:**
- Create: `src/Leccionario.Api/Application/Asistencia/AgendaService.cs`
- Modify: `src/Leccionario.Api/Controllers/Asistencia/SesionesController.cs`
- Create: `src/Leccionario.Api/Controllers/Horarios/ReportesHorarioController.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Test: `src/Leccionario.Tests/Asistencia/AgendaServiceTests.cs`

**Interfaces:**
- Consumes: `BloqueHorarioCalculator` (Task 6).
- Produces:
  ```csharp
  public enum EstadoBloque { Pendiente, Borrador, Cerrada, Futura }

  public sealed record BloqueAgendaDto(DateOnly Fecha, string Dia, int IdHorarioInicio,
      int NumeroBloque, string HoraInicio, string HoraFin, int FranjasPlanificadas,
      int MinutosPlanificados, EstadoBloque Estado, int? IdSesion, int DiasRetraso);

  public sealed record SesionTardiaDto(int IdSesion, int IdAsignacion, DateOnly Fecha,
      string? NombreDocente, string Tema, int DiasRetraso);

  public sealed record DiaSinRegistrarDto(int IdAsignacion, DateOnly Fecha,
      string? NombreDocente, int NumeroBloque, int DiasVencido);

  public interface IAgendaService
  {
      Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(int idAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default);
      Task<IReadOnlyList<SesionTardiaDto>> SesionesTardiasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default);
      Task<IReadOnlyList<DiaSinRegistrarDto>> DiasSinRegistrarAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default);
  }
  ```

- [ ] **Step 1: Escribir los tests (fallan)**

`src/Leccionario.Tests/Asistencia/AgendaServiceTests.cs`. Reutiliza `RelojFijo`, `SembrarHorarioAsync` y los builders de la tarea 12.

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Asistencia;

/// <summary>
/// Agenda del docente: qué días toca clase y cuáles debe todavía.
/// Clase base sembrada: lunes 2026-08-03, franjas 07:00–08:00 y 08:00–09:00.
/// </summary>
[TestClass]
public sealed class AgendaServiceTests
{
    private const int Asig100 = 100;
    private const string Duenio = "0000000001";
    private static readonly DateOnly Lunes = new(2026, 8, 3);

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static DateTimeOffset El(int dia) => new(2026, 8, dia, 9, 0, 0, TimeSpan.Zero);

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static IAgendaService CrearAgenda(sigafi_esContext db, TimeProvider reloj) =>
        new AgendaService(db, reloj);

    /// <summary>Igual que en SesionServiceTests: horario del lunes con 2 franjas contiguas.</summary>
    private static async Task SembrarHorarioAsync(sigafi_esContext db)
    {
        db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.profesores.Add(new profesores { idProfesor = Duenio, apellidos = "PEREZ", nombres = "JUAN" });
        db.asignaciones_profesores.Add(new AsignacionBuilder()
            .ConId(Asig100).DelProfesor(Duenio).ConNivel(35)
            .ConRango(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).Build());
        db.fechas_horarios.AddRange(
            new fechas_horarios { idFecha = 500, fecha = Lunes, dia = "Lunes" },
            new fechas_horarios { idFecha = 507, fecha = Lunes.AddDays(7), dia = "Lunes" });
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(902).DeTipo("Z").DeRango("08:00", "09:00").Build(),
            new FranjaBuilder().ConId(903).DeTipo("Z").DeRango("15:00", "16:00").Build());
        db.horario_detalle.AddRange(
            new HorarioDetalleBuilder().ConId(1).DeAsignacion(Asig100).EnFecha(500).EnFranja(901).Build(),
            new HorarioDetalleBuilder().ConId(2).DeAsignacion(Asig100).EnFecha(500).EnFranja(902).Build());
        await db.SaveChangesAsync();
    }

    private static cplec_sesiones Sesion(int idSesion, int idFecha, sbyte bloque, string estado) => new()
    {
        idSesion = idSesion, idAsignacion = Asig100, idFecha = idFecha, numeroBloque = bloque,
        tema = "Señalética", estado = estado, activo = true,
        usuarioCreacion = Duenio, fechaCreacion = DateTime.UtcNow
    };

    [TestMethod]
    public async Task Agenda_DiaConHorarioSinSesionYaPasado_EsPendiente()
    {
        using var db = CrearContexto(nameof(Agenda_DiaConHorarioSinSesionYaPasado_EsPendiente));
        await SembrarHorarioAsync(db);

        var a = await CrearAgenda(db, new RelojFijo(El(5))).ObtenerAgendaAsync(Asig100, Lunes, Lunes);

        a.Should().ContainSingle().Which.Estado.Should().Be(EstadoBloque.Pendiente);
    }

    [TestMethod]
    public async Task Agenda_DiaConSesionBorrador_EsBorrador()
    {
        using var db = CrearContexto(nameof(Agenda_DiaConSesionBorrador_EsBorrador));
        await SembrarHorarioAsync(db);
        db.cplec_sesiones.Add(Sesion(1, 500, 1, "borrador"));
        await db.SaveChangesAsync();

        var a = await CrearAgenda(db, new RelojFijo(El(5))).ObtenerAgendaAsync(Asig100, Lunes, Lunes);

        a.Should().ContainSingle().Which.Estado.Should().Be(EstadoBloque.Borrador);
        a[0].IdSesion.Should().Be(1);
    }

    [TestMethod]
    public async Task Agenda_DiaConSesionCerrada_EsCerrada()
    {
        using var db = CrearContexto(nameof(Agenda_DiaConSesionCerrada_EsCerrada));
        await SembrarHorarioAsync(db);
        db.cplec_sesiones.Add(Sesion(1, 500, 1, "cerrada"));
        await db.SaveChangesAsync();

        var a = await CrearAgenda(db, new RelojFijo(El(5))).ObtenerAgendaAsync(Asig100, Lunes, Lunes);

        a.Should().ContainSingle().Which.Estado.Should().Be(EstadoBloque.Cerrada);
    }

    [TestMethod]
    public async Task Agenda_DiaFuturo_EsFuturaYNoRegistrable()
    {
        // El lunes siguiente todavía no ocurrió: no se pasa lista de una clase futura.
        using var db = CrearContexto(nameof(Agenda_DiaFuturo_EsFuturaYNoRegistrable));
        await SembrarHorarioAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(10)
            .DeAsignacion(Asig100).EnFecha(507).EnFranja(901).Build());
        await db.SaveChangesAsync();

        var a = await CrearAgenda(db, new RelojFijo(El(5)))
            .ObtenerAgendaAsync(Asig100, Lunes, Lunes.AddDays(7));

        a.Single(b => b.Fecha == Lunes.AddDays(7)).Estado.Should().Be(EstadoBloque.Futura);
        a.Single(b => b.Fecha == Lunes.AddDays(7)).DiasRetraso.Should().Be(0);
    }

    [TestMethod]
    public async Task Agenda_PendienteReportaLosDiasDeAtraso()
    {
        using var db = CrearContexto(nameof(Agenda_PendienteReportaLosDiasDeAtraso));
        await SembrarHorarioAsync(db);

        var a = await CrearAgenda(db, new RelojFijo(El(6))).ObtenerAgendaAsync(Asig100, Lunes, Lunes);

        a.Should().ContainSingle().Which.DiasRetraso.Should().Be(3);
    }

    [TestMethod]
    public async Task SesionesTardias_DevuelveSoloLasMarcadas_OrdenadasPorRetraso()
    {
        using var db = CrearContexto(nameof(SesionesTardias_DevuelveSoloLasMarcadas_OrdenadasPorRetraso));
        await SembrarHorarioAsync(db);
        var puntual = Sesion(1, 500, 1, "cerrada");
        var tardia2 = Sesion(2, 507, 1, "cerrada");
        tardia2.esTardia = true; tardia2.diasRetraso = 2;
        var tardia9 = Sesion(3, 507, 2, "cerrada");
        tardia9.esTardia = true; tardia9.diasRetraso = 9;
        db.cplec_sesiones.AddRange(puntual, tardia2, tardia9);
        await db.SaveChangesAsync();

        var r = await CrearAgenda(db, new RelojFijo(El(20)))
            .SesionesTardiasAsync(Lunes, Lunes.AddDays(30));

        r.Select(x => x.IdSesion).Should().Equal(3, 2);
        r[0].DiasRetraso.Should().Be(9);
        r[0].NombreDocente.Should().Contain("PEREZ");
    }

    [TestMethod]
    public async Task DiasSinRegistrar_ExcluyeLosFuturosYLosYaRegistrados()
    {
        using var db = CrearContexto(nameof(DiasSinRegistrar_ExcluyeLosFuturosYLosYaRegistrados));
        await SembrarHorarioAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(10)
            .DeAsignacion(Asig100).EnFecha(507).EnFranja(901).Build());
        db.cplec_sesiones.Add(Sesion(1, 500, 1, "cerrada"));   // el lunes 3 ya está
        await db.SaveChangesAsync();

        var r = await CrearAgenda(db, new RelojFijo(El(5)))
            .DiasSinRegistrarAsync(Lunes, Lunes.AddDays(7));

        r.Should().BeEmpty();   // el 3 registrado, el 10 aún es futuro
    }

    [TestMethod]
    public async Task DiasSinRegistrar_IgnoraCeldasConActivoDistintoDeUno()
    {
        // `activo` es tinyint(4) con basura en la base: solo `= 1` cuenta (spec H10).
        using var db = CrearContexto(nameof(DiasSinRegistrar_IgnoraCeldasConActivoDistintoDeUno));
        await SembrarHorarioAsync(db);
        foreach (var h in db.horario_detalle) h.activo = 11;
        await db.SaveChangesAsync();

        var r = await CrearAgenda(db, new RelojFijo(El(5))).DiasSinRegistrarAsync(Lunes, Lunes);

        r.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Agenda_DiaPartido_DevuelveDosBloquesConSusNumeros()
    {
    // Lunes 07:00–09:00 (dos franjas) y 15:00–16:00. Son dos clases distintas,
    // así que el docente pasa dos listas.
    using var db = CrearContexto(nameof(Agenda_DiaPartido_DevuelveDosBloquesConSusNumeros));
    await SembrarHorarioAsync(db);
    db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(3)
        .DeAsignacion(Asig100).EnFecha(500).EnFranja(903).Build());   // 15:00–16:00
    await db.SaveChangesAsync();

    var agenda = await CrearAgenda(db, new RelojFijo(new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero)))
        .ObtenerAgendaAsync(Asig100, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 3));

    agenda.Should().HaveCount(2);
    agenda[0].NumeroBloque.Should().Be(1);
    agenda[0].IdHorarioInicio.Should().Be(1);
    agenda[0].MinutosPlanificados.Should().Be(120);
    agenda[1].NumeroBloque.Should().Be(2);
    agenda[1].IdHorarioInicio.Should().Be(3);
        agenda.Should().OnlyContain(b => b.Estado == EstadoBloque.Pendiente);
        agenda[0].DiasRetraso.Should().Be(2);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~AgendaServiceTests" -v q
```

- [ ] **Step 3: Implementar `AgendaService`**

`src/Leccionario.Api/Application/Asistencia/AgendaService.cs`. La estructura:

```csharp
public async Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(
    int idAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
{
    var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);

    // 1. Celdas activas de la asignación en el rango, con su franja y su fecha.
    var filas = await (
        from hd in _db.horario_detalle.AsNoTracking()
        join hc in _db.horas_clases.AsNoTracking() on hd.idhora equals hc.idhora
        join fh in _db.fechas_horarios.AsNoTracking() on hd.idFecha equals fh.idFecha
        where hd.idAsignacion == idAsignacion
              && hd.activo == 1
              && fh.fecha != null && fh.fecha >= desde && fh.fecha <= hasta
        select new
        {
            hd.idHorario, hd.idhora, hd.idFecha,
            Fecha = fh.fecha!.Value, Dia = fh.dia,
            hc.hora_inicio, hc.hora_fin, hc.minutos
        }).ToListAsync(ct);

    // 2. Sesiones existentes, indexadas por (idFecha, numeroBloque).
    var sesiones = await _db.cplec_sesiones.AsNoTracking()
        .Where(s => s.idAsignacion == idAsignacion && s.activo == true)
        .Select(s => new { s.idSesion, s.idFecha, s.numeroBloque, s.estado })
        .ToListAsync(ct);

    // 3. Un grupo por fecha; dentro, agrupar en bloques contiguos.
    var resultado = new List<BloqueAgendaDto>();
    foreach (var grupo in filas.GroupBy(f => new { f.idFecha, f.Fecha, f.Dia }).OrderBy(g => g.Key.Fecha))
    {
        var franjas = grupo
            .Select(f => TimeOnly.TryParse(f.hora_inicio, out var i)
                      && TimeOnly.TryParse(f.hora_fin, out var fin) && i < fin
                ? new FranjaOrdenable(f.idHorario, f.idhora, i, fin,
                    f.minutos ?? (int)(fin - i).TotalMinutes)
                : null)
            .Where(f => f is not null).Select(f => f!);

        foreach (var b in BloqueHorarioCalculator.Agrupar(franjas))
        {
            var s = sesiones.FirstOrDefault(x =>
                x.idFecha == grupo.Key.idFecha && x.numeroBloque == b.NumeroBloque);

            var estado = grupo.Key.Fecha > hoy ? EstadoBloque.Futura
                : s is null                    ? EstadoBloque.Pendiente
                : s.estado == "cerrada"        ? EstadoBloque.Cerrada
                                               : EstadoBloque.Borrador;

            resultado.Add(new BloqueAgendaDto(
                grupo.Key.Fecha, grupo.Key.Dia ?? string.Empty,
                b.IdHorarioInicio, b.NumeroBloque,
                b.Inicio.ToString("HH\\:mm"), b.Fin.ToString("HH\\:mm"),
                b.FranjasPlanificadas, b.MinutosPlanificados,
                estado, s?.idSesion,
                estado == EstadoBloque.Pendiente
                    ? Math.Max(0, hoy.DayNumber - grupo.Key.Fecha.DayNumber)
                    : 0));
        }
    }

    return resultado;
}
```

Y los dos reportes del inspector:

```csharp
public async Task<IReadOnlyList<SesionTardiaDto>> SesionesTardiasAsync(
    DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
    await (from s in _db.cplec_sesiones.AsNoTracking()
           join f in _db.fechas_horarios.AsNoTracking() on s.idFecha equals f.idFecha
           join ap in _db.asignaciones_profesores.AsNoTracking() on s.idAsignacion equals ap.idAsignacion
           join pr in _db.profesores.AsNoTracking() on ap.idProfesor equals pr.idProfesor into prJoin
           from pr in prJoin.DefaultIfEmpty()
           where s.activo == true && s.esTardia
                 && f.fecha != null && f.fecha >= desde && f.fecha <= hasta
           orderby s.diasRetraso descending, f.fecha
           select new SesionTardiaDto(
               s.idSesion, s.idAsignacion, f.fecha!.Value,
               pr == null ? null : (pr.apellidos + " " + pr.nombres).Trim(),
               s.tema, s.diasRetraso))
        .ToListAsync(ct);

/// <summary>
/// Días con horario, ya pasados, sin sesión. Reutiliza <see cref="ObtenerAgendaAsync"/>
/// por asignación en vez de duplicar el cálculo de bloques.
/// </summary>
public async Task<IReadOnlyList<DiaSinRegistrarDto>> DiasSinRegistrarAsync(
    DateOnly desde, DateOnly hasta, CancellationToken ct = default)
{
    // Solo asignaciones de carrera 6 que tengan al menos una celda en el rango.
    var asignaciones = await (
        from hd in _db.horario_detalle.AsNoTracking()
        join fh in _db.fechas_horarios.AsNoTracking() on hd.idFecha equals fh.idFecha
        join ap in _db.asignaciones_profesores.AsNoTracking() on hd.idAsignacion equals ap.idAsignacion
        join cu in _db.cursos.AsNoTracking() on ap.idNivel equals cu.idNivel
        join pr in _db.profesores.AsNoTracking() on ap.idProfesor equals pr.idProfesor into prJoin
        from pr in prJoin.DefaultIfEmpty()
        where hd.activo == 1 && cu.idCarrera == 6
              && fh.fecha != null && fh.fecha >= desde && fh.fecha <= hasta
        select new
        {
            hd.idAsignacion,
            Docente = pr == null ? null : (pr.apellidos + " " + pr.nombres).Trim()
        })
        .Distinct()
        .ToListAsync(ct);

    var salida = new List<DiaSinRegistrarDto>();
    foreach (var a in asignaciones)
    {
        var agenda = await ObtenerAgendaAsync(a.idAsignacion, desde, hasta, ct);
        salida.AddRange(agenda
            .Where(b => b.Estado == EstadoBloque.Pendiente)
            .Select(b => new DiaSinRegistrarDto(
                a.idAsignacion, b.Fecha, a.Docente, b.NumeroBloque, b.DiasRetraso)));
    }

    return salida.OrderByDescending(d => d.DiasVencido).ThenBy(d => d.Fecha).ToList();
}
```

> `DiasSinRegistrarAsync` hace una consulta por asignación. Con los cuatro paralelos vigentes de la carrera 6 eso es trivial. Si algún día el rango abarcara cientos de asignaciones, conviene reescribirlo como una sola consulta — pero no se optimiza antes de que duela.

- [ ] **Step 4: Agregar los endpoints**

En `SesionesController.cs`:

```csharp
/// <summary>Días con horario y su estado de registro.</summary>
[HttpGet("paralelos/{idAsignacion:int}/agenda")]
[ProducesResponseType(typeof(IReadOnlyList<BloqueAgendaDto>), StatusCodes.Status200OK)]
public async Task<IActionResult> Agenda(
    int idAsignacion, [FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
{
    await _guard.EnsureDocenteTieneAsignacionAsync(IdProfesorSub ?? string.Empty, idAsignacion, EsInspector, ct);
    return Ok(await _agenda.ObtenerAgendaAsync(idAsignacion, desde, hasta, ct));
}
```

Nuevo `Controllers/Horarios/ReportesHorarioController.cs` con `[Route("api/reportes")]`, `[Authorize(Roles = "cplec_inspector")]` y dos `GET`: `sesiones-tardias` y `dias-sin-registrar`, ambos con `?desde=&hasta=`.

- [ ] **Step 5: Registrar en DI y correr todo**

```csharp
services.AddScoped<IAgendaService, AgendaService>();
```

```bash
cd src && dotnet test -v q --nologo
```

Esperado: 0 fallos, ~220 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Leccionario.Api src/Leccionario.Tests
git commit -m "feat(asistencia): agenda del docente y reportes de tardias del inspector"
```

---

## Cierre del hito

- [ ] **Actualizar la documentación**

- `docs/02-modelo-datos.md`: `horario_detalle`, `horas_clases` tipo Z y las columnas nuevas de `cplec_sesiones`.
- `docs/04-contrato-api.md`: los endpoints de la tabla del spec sección 7 y los códigos de error nuevos.
- `docs/10-navegacion-distributivo.md`: el camino `asignación → curso → carrera 6` que usa `HorarioCarreraGuard`.
- `docs/00-roadmap.md`: M4b en ✅ con el commit de referencia, y M5 desbloqueado.
- `CLAUDE.md`: comandos y conteo de tests.

- [ ] **Verificación final**

```bash
cd src && dotnet test -v q --nologo
```

No declarar el hito cerrado sin ver la salida en verde. Si el host de pruebas aborta por límites del entorno, correr por filtros y confirmar 0 fallos en cada bloque.

- [ ] **Commit de documentación**

```bash
git add docs CLAUDE.md
git commit -m "docs(horarios): cerrar M4b backend y desbloquear M5"
```

---

## Notas para quien ejecute

**Lo que más fácil se rompe, por orden:**

1. **Comparar `activo` con `!= 0`.** Hay una fila con `activo = 11` en `asignaciones_profesores`. Siempre `== 1`.
2. **Usar `horaInicio` en vez de `hora_inicio`.** Los DTOs de `gestion_academica` usan camelCase; las columnas reales son snake_case.
3. **Comparar conflictos por `idhora`.** Es lo que hace el otro sistema y acá no sirve: no detecta al mismo docente en dos clases simultáneas cuando las franjas tienen duración distinta.
4. **Quitar el reintento por parecer defensivo.** Es el mecanismo de unicidad, no una precaución.
5. **Dejar que la replicación tome el rango completo de la asignación.** Con módulos de 408 días eso son miles de filas en una tabla de otro sistema.
6. **Comparar el paralelo con menos de 5 columnas.** Devuelve el triple de filas, de otras jornadas.

**Lo que no se toca:** `matriculas_asistencias`, las franjas `X`/`I`/`C`, el esquema de cualquier tabla que no empiece con `cplec_`.
