# Asistencia anclada al horario — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que el docente vea su horario del periodo vigente y que la asistencia solo pueda registrarse desde un bloque planificado, retirando el "modo transición" que permite crear sesiones por fecha suelta.

**Architecture:** Backend .NET 8: se cierra la rama libre de `SesionService.CrearAsync` con una excepción nueva `SinHorarioException`, y se agrega `MiHorarioService` que compone la vigencia ya implementada en `MisParalelosService` con una variante batch de `AgendaService.ObtenerAgendaAsync`. Frontend Angular 21: página `mi-horario` con grid semanal, y `pasar-lista` pasa a exigir `idHorarioInicio` por query param.

**Tech Stack:** ASP.NET Core net8.0 · EF Core 8 + Pomelo · MySQL 5.7 · MSTest + Moq + FluentAssertions · Angular 21 (standalone, signals, Material) · Vitest.

**Spec:** [`docs/superpowers/specs/2026-08-08-asistencia-anclada-horario-design.md`](../specs/2026-08-08-asistencia-anclada-horario-design.md)

## Global Constraints

- Rama de trabajo: **`dv_jb`**. `main` y `develop` están bloqueadas por hooks. No usar `--no-verify`.
- **No se toca `Domain/Entities/`** — es código generado por EF Core Power Tools.
- **No hay cambios de esquema** en este plan: ningún `.sql` nuevo en `database/migrations/` ni `database/rollback/`, y `scripts/` no se modifica. Si algún paso parece necesitar una columna nueva, es señal de error: parar y consultar.
- **El `idProfesor` sale siempre del claim `sub`**, nunca del body ni del query string.
- Todo endpoint que recibe `idAsignacion` o `idSesion` pasa por `DistributivoGuard`.
- MySQL 5.7: prohibidos CTEs, funciones de ventana, `RENAME COLUMN`, `0000-00-00`.
- Backend verde: `cd src && dotnet test` (247 tests al empezar). Frontend verde: `cd client && npm test && npm run lint && npm run build` (24 tests al empezar).
- Comparar siempre `activo = 1`, nunca `<> 0` (`activo` es `tinyint(4)` con datos sucios).
- Códigos de error existentes que se reutilizan tal cual: `HORARIO_REQUERIDO` (422), `DISTRIBUTIVO_AJENO` (403), `FUERA_DE_VENTANA` (422), `RANGO_EXCEDE_TOPE` (422, emitido por `ConflictoLoteException`).

## File Structure

**Backend — crear:**

| Archivo | Responsabilidad |
|---|---|
| `src/Leccionario.Api/Application/Common/Exceptions/SinHorarioException.cs` | 422 `SIN_HORARIO` |
| `src/Leccionario.Api/Application/Horarios/Services/MiHorarioService.cs` | `IMiHorarioService` + DTO + composición vigencia × bloques |
| `src/Leccionario.Api/Controllers/Horarios/MiHorarioController.cs` | `GET /api/mi-horario` |
| `src/Leccionario.Tests/Horarios/MiHorarioServiceTests.cs` | Tests del servicio nuevo |

**Backend — modificar:**

| Archivo | Cambio |
|---|---|
| `src/Leccionario.Api/Application/Asistencia/SesionService.cs:112-117` | Retirar la rama de modo transición |
| `src/Leccionario.Api/Application/Asistencia/AgendaService.cs` | `BloqueAgendaDto.IdAsignacion`; sobrecarga batch; `DiasSinRegistrarAsync` sin N+1 |
| `src/Leccionario.Api/Application/Asistencia/AsistenciaDtos.cs:76-83` | Marcar `Fecha` y `NumeroBloque` como obsoletos |
| `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs:96` | Registrar `IMiHorarioService` |
| `src/Leccionario.Tests/Asistencia/SesionServiceTests.cs` | Reescribir los tests que dependen del modo transición |
| `src/Leccionario.Tests/Asistencia/AgendaServiceTests.cs` | Test de equivalencia batch |

**Frontend — crear:**

| Archivo | Responsabilidad |
|---|---|
| `client/src/app/features/docente/models/mi-horario.model.ts` | Espejo del DTO |
| `client/src/app/features/docente/services/mi-horario.service.ts` | `GET /api/mi-horario` |
| `client/src/app/features/docente/pages/mi-horario/mi-horario.store.ts` | Estado: semana, bloques, grid derivado |
| `client/src/app/features/docente/pages/mi-horario/mi-horario.store.spec.ts` | Tests del store |
| `client/src/app/features/docente/pages/mi-horario/mi-horario.page.ts` | Grid semanal |

**Frontend — modificar:**

| Archivo | Cambio |
|---|---|
| `client/src/app/app.routes.ts` | Ruta `/mi-horario`; redirección del docente |
| `client/src/app/layout/main-layout/main-layout.ts:21` | Entrada "Mi horario" |
| `client/src/app/features/docente/pages/pasar-lista/pasar-lista.page.ts:236-256,296-302` | Exigir `idHorarioInicio`, quitar `hoyISO()` |
| `client/src/app/features/docente/pages/pasar-lista/pasar-lista.store.ts:83-118` | Dejar de mandar `fecha`; mensaje `SIN_HORARIO` |
| `client/src/app/features/docente/pages/pasar-lista/pasar-lista.store.spec.ts` | Ajustar a la firma nueva |

**Docs — modificar:** `docs/04-contrato-api.md`, `docs/adr/ADR-008-horarios-en-tabla-compartida.md`, `docs/10-navegacion-distributivo.md`, `docs/00-roadmap.md`, `CLAUDE.md`.

---

### Task 1: `SinHorarioException` y bloqueo duro en `SesionService`

**Files:**
- Create: `src/Leccionario.Api/Application/Common/Exceptions/SinHorarioException.cs`
- Modify: `src/Leccionario.Api/Application/Asistencia/SesionService.cs:96-117`
- Modify: `src/Leccionario.Api/Application/Asistencia/AsistenciaDtos.cs:76-83`
- Test: `src/Leccionario.Tests/Asistencia/SesionServiceTests.cs`

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: `SinHorarioException` (código `"SIN_HORARIO"`, HTTP 422), consumido por el frontend en la Task 7. `SesionService.CrearAsync` conserva su firma exacta: `Task<SesionDto> CrearAsync(int idAsignacion, string idProfesorDocente, bool esInspector, CrearSesionRequestDto request, CancellationToken ct = default)`.

- [x] **Step 1: Escribir el test que falla**

En `src/Leccionario.Tests/Asistencia/SesionServiceTests.cs`, **reemplazar completo** el método `Crear_SinHorarioEnLaAsignacion_AceptaFechaLibreYMarcaOrigenLibre` (líneas ~328-341) por:

```csharp
    [TestMethod]
    public async Task Crear_SinHorarioEnLaAsignacion_LanzaSinHorario()
    {
        using var db = CrearContexto(nameof(Crear_SinHorarioEnLaAsignacion_LanzaSinHorario));
        await SembrarSinHorarioAsync(db);

        var acto = async () => await CrearServicio(db,
                new RelojFijo(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)))
            .CrearAsync(Asig100, Duenio, false,
                new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 3), NumeroBloque = 1, Tema = "Libre" });

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("SIN_HORARIO");
    }

    [TestMethod]
    public async Task Crear_ConBloqueValido_IgnoraLaFechaDelBody()
    {
        using var db = CrearContexto(nameof(Crear_ConBloqueValido_IgnoraLaFechaDelBody));
        await SembrarHorarioAsync(db);

        // El body miente: dice 2026-08-07, pero el bloque 1 está en fechas_horarios 500 = 2026-08-03.
        var sesion = await CrearServicio(db,
                new RelojFijo(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)))
            .CrearAsync(Asig100, Duenio, false,
                new CrearSesionRequestDto
                {
                    IdHorarioInicio = 1,
                    Fecha = new DateOnly(2026, 8, 7),
                    NumeroBloque = 9,
                    Tema = "Señalética"
                });

        sesion.Fecha.Should().Be(new DateOnly(2026, 8, 3));
        sesion.NumeroBloque.Should().Be(1);
        sesion.Origen.Should().Be("horario");
    }
```

- [x] **Step 2: Correr los tests y verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~SesionServiceTests"
```

Esperado: FALLAN. `Crear_SinHorarioEnLaAsignacion_LanzaSinHorario` no lanza nada (crea la sesión libre); `Crear_ConBloqueValido_IgnoraLaFechaDelBody` debería pasar ya (la fecha ya se deriva del bloque) — si pasa, es un test de regresión legítimo, déjalo.

También fallarán otros tests que crean sesiones sin horario (`Crear_FechaValida_CreaSesionConNomina`, `Crear_FechaFueraDeVentana_LanzaFueraDeVentana`, `Crear_TemaVacio_LanzaValidacion` y los que usen `SembrarAsignacion`). Se arreglan en el Step 5.

- [x] **Step 3: Crear la excepción**

`src/Leccionario.Api/Application/Common/Exceptions/SinHorarioException.cs`:

```csharp
namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 422 SIN_HORARIO — la asignación no tiene ninguna celda de horario activa, así
/// que no puede registrarse asistencia hasta que el inspector la cargue. Retira
/// el "modo transición" de ADR-008 decisión 9. Ver
/// <c>docs/superpowers/specs/2026-08-08-asistencia-anclada-horario-design.md</c> sección 4.1.
/// </summary>
public sealed class SinHorarioException : AppException
{
    public SinHorarioException()
        : base("SIN_HORARIO", 422,
               "Este paralelo no tiene horario planificado. Pide al inspector que lo cargue.") { }
}
```

- [x] **Step 4: Retirar la rama de modo transición**

En `src/Leccionario.Api/Application/Asistencia/SesionService.cs`, reemplazar el bloque de líneas 94-117 (desde el comentario `// ---- Anclaje al horario` hasta el cierre del `else`) por:

```csharp
        // ---- Anclaje al horario (ADR-008 decisiones 7 y 9, modificada 2026-08-08) ----
        // Sin horario planificado no hay asistencia: el modo transición se retiró.
        var (tieneHorario, bloques) = await ResolverBloquesAsync(idAsignacion, request.IdHorarioInicio, ct);

        if (!tieneHorario)
            throw new SinHorarioException();

        if (request.IdHorarioInicio is null)
            throw new HorarioRequeridoException();

        var bloque = bloques.FirstOrDefault(b => b.IdHorarioInicio == request.IdHorarioInicio)
            ?? throw new DistributivoAjenoException();

        var idFecha = await IdFechaDelHorarioAsync(request.IdHorarioInicio.Value, ct);
        var numeroBloque = bloque.NumeroBloque;
```

Nota: `bloque` deja de ser `BloqueHorario?` y pasa a ser no-nulable, así que las líneas 156-158 (`idHorarioInicio = bloque?.IdHorarioInicio`, etc.) pueden dejar el `?.` o quitarlo; quitarlo es más claro:

```csharp
            idHorarioInicio = bloque.IdHorarioInicio,
            franjasPlanificadas = (sbyte?)bloque.FranjasPlanificadas,
            minutosPlanificados = (short?)bloque.MinutosPlanificados,
```

El método privado `ResolverIdFechaLibreAsync` (líneas 382-393) queda sin uso: **eliminarlo**.

En `src/Leccionario.Api/Application/Asistencia/AsistenciaDtos.cs`, documentar los campos que ya no deciden nada:

```csharp
public sealed class CrearSesionRequestDto
{
    /// <summary>Bloque de horario al que corresponde la clase. Obligatorio desde 2026-08-08.</summary>
    public int? IdHorarioInicio { get; init; }

    /// <summary>
    /// Obsoleto: la fecha se deriva de <see cref="IdHorarioInicio"/>. Se conserva
    /// para no romper la deserialización de clientes viejos; el servicio la ignora.
    /// </summary>
    public DateOnly Fecha { get; init; }

    public required string Tema { get; init; }
    public string? Observacion { get; init; }

    /// <summary>
    /// Obsoleto: el bloque se deriva de <see cref="IdHorarioInicio"/>. El servicio lo ignora.
    /// </summary>
    public sbyte NumeroBloque { get; init; } = 1;
}
```

- [x] **Step 5: Adaptar los tests que dependían del modo transición**

En `SesionServiceTests.cs`, los tests que usan `SembrarAsignacion` (sin horario) y esperan éxito deben pasar a sembrar horario. Cambios concretos:

1. Agregar un helper que siembre horario sobre el `SembrarAsignacion` existente, justo debajo de `SembrarAsignacion`:

```csharp
    /// <summary>Agrega una franja Z y su celda a la asignación sembrada por <see cref="SembrarAsignacion"/>.</summary>
    private static void SembrarCeldaSobreAsignacion(sigafi_esContext db, int idAsignacion = 100)
    {
        db.horas_clases.Add(new FranjaBuilder().ConId(910).DeTipo("Z").DeRango("07:00", "08:00").Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(10)
            .DeAsignacion(idAsignacion).EnFecha(100).EnFranja(910).Build());
        db.SaveChanges();
    }
```

2. En `Crear_FechaValida_CreaSesionConNomina`: añadir `SembrarCeldaSobreAsignacion(db);` después de `SembrarAsignacion(db);` y cambiar el request a `new CrearSesionRequestDto { IdHorarioInicio = 10, Tema = "Educación vial" }`.

3. En `Crear_FechaFueraDeVentana_LanzaFueraDeVentana`: añadir `SembrarCeldaSobreAsignacion(db);` y cambiar el request a `new CrearSesionRequestDto { IdHorarioInicio = 10, Tema = "X" }`. La celda cae en `idFecha = 100` (2026-08-07), fuera del rango sembrado (septiembre-diciembre), así que el test sigue midiendo lo mismo.

4. En `Crear_TemaVacio_LanzaValidacion`: **no** necesita horario — la validación de tema ocurre antes del anclaje (`SesionService.cs:85-86`). Verificar que sigue verde sin tocarlo.

5. Para cualquier otro test que falle por `SIN_HORARIO`, aplicar el mismo patrón: sembrar celda y pasar `IdHorarioInicio`.

- [x] **Step 6: Correr toda la suite backend**

```bash
cd src && dotnet test
```

Esperado: PASS, con al menos un test más que los 247 de partida.

- [x] **Step 7: Commit**

```bash
git add src/Leccionario.Api/Application/Common/Exceptions/SinHorarioException.cs \
        src/Leccionario.Api/Application/Asistencia/SesionService.cs \
        src/Leccionario.Api/Application/Asistencia/AsistenciaDtos.cs \
        src/Leccionario.Tests/Asistencia/SesionServiceTests.cs
git commit -m "feat(asistencia): sin horario planificado no hay asistencia (SIN_HORARIO)"
```

---

### Task 2: `AgendaService` batch y `IdAsignacion` en el DTO

**Files:**
- Modify: `src/Leccionario.Api/Application/Asistencia/AgendaService.cs:10-12,20-25,38-99,117-149`
- Test: `src/Leccionario.Tests/Asistencia/AgendaServiceTests.cs`

**Interfaces:**
- Consumes: nada de la Task 1.
- Produces:
  - `BloqueAgendaDto` gana `int IdAsignacion` como **primer** parámetro posicional del record.
  - Nueva sobrecarga en `IAgendaService`:
    `Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(IReadOnlyCollection<int> idsAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default)`.
  - La Task 3 consume esa sobrecarga.

- [x] **Step 1: Escribir el test que falla**

Agregar a `src/Leccionario.Tests/Asistencia/AgendaServiceTests.cs`:

```csharp
    [TestMethod]
    public async Task ObtenerAgenda_Batch_DevuelveLoMismoQueLlamadasIndividuales()
    {
        using var db = CrearContexto(nameof(ObtenerAgenda_Batch_DevuelveLoMismoQueLlamadasIndividuales));
        await SembrarDosAsignacionesAsync(db);
        var svc = CrearServicio(db);
        var desde = new DateOnly(2026, 8, 3);
        var hasta = new DateOnly(2026, 8, 9);

        var a = await svc.ObtenerAgendaAsync(100, desde, hasta);
        var b = await svc.ObtenerAgendaAsync(200, desde, hasta);
        var batch = await svc.ObtenerAgendaAsync(new[] { 100, 200 }, desde, hasta);

        batch.Should().HaveCount(a.Count + b.Count);
        batch.Where(x => x.IdAsignacion == 100).Should().BeEquivalentTo(a);
        batch.Where(x => x.IdAsignacion == 200).Should().BeEquivalentTo(b);
    }

    [TestMethod]
    public async Task ObtenerAgenda_Batch_ConListaVacia_DevuelveVacio()
    {
        using var db = CrearContexto(nameof(ObtenerAgenda_Batch_ConListaVacia_DevuelveVacio));
        await SembrarDosAsignacionesAsync(db);

        var r = await CrearServicio(db).ObtenerAgendaAsync(
            Array.Empty<int>(), new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9));

        r.Should().BeEmpty();
    }
```

Si `AgendaServiceTests.cs` no tiene helpers `CrearContexto` / `CrearServicio` / un sembrado con dos asignaciones, agregarlos siguiendo el patrón de `SesionServiceTests.cs:27-31` y `SembrarHorarioAsync`. `SembrarDosAsignacionesAsync` debe crear: `cursos` con `idNivel = 35, idCarrera = 6`; dos asignaciones (100 y 200) de carrera 6; `fechas_horarios` para el lunes 2026-08-03 (`idFecha = 500`) y el martes 2026-08-04 (`idFecha = 501`); franjas Z contiguas 07:00-08:00 y 08:00-09:00; y celdas: asignación 100 el lunes en ambas franjas, asignación 200 el martes en la primera.

- [x] **Step 2: Correr el test y verificar que falla**

```bash
cd src && dotnet test --filter "FullyQualifiedName~AgendaServiceTests"
```

Esperado: FALLA en compilación — no existe la sobrecarga ni `IdAsignacion`.

- [x] **Step 3: Cambiar el DTO y la interfaz**

En `AgendaService.cs`, línea 10:

```csharp
public sealed record BloqueAgendaDto(int IdAsignacion, DateOnly Fecha, string Dia, int IdHorarioInicio,
    int NumeroBloque, string HoraInicio, string HoraFin, int FranjasPlanificadas,
    int MinutosPlanificados, EstadoBloque Estado, int? IdSesion, int DiasRetraso);
```

En `IAgendaService` (línea 22), agregar la sobrecarga:

```csharp
    Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(int idAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default);

    /// <summary>
    /// Igual que la sobrecarga de un solo idAsignacion, pero resuelve todas las
    /// asignaciones en una sola consulta. Cada bloque trae su <c>IdAsignacion</c>.
    /// </summary>
    Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(IReadOnlyCollection<int> idsAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default);
```

- [x] **Step 4: Implementar la batch y delegar**

Reemplazar el cuerpo de `ObtenerAgendaAsync` (líneas 38-99) por:

```csharp
    public Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(
        int idAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        ObtenerAgendaAsync(new[] { idAsignacion }, desde, hasta, ct);

    public async Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(
        IReadOnlyCollection<int> idsAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        if (idsAsignacion.Count == 0)
            return Array.Empty<BloqueAgendaDto>();

        var ids = idsAsignacion.Distinct().ToList();
        var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);

        // 1. Celdas activas de las asignaciones en el rango, con su franja y su fecha.
        var filas = await (
            from hd in _db.horario_detalle.AsNoTracking()
            join hc in _db.horas_clases.AsNoTracking() on hd.idhora equals hc.idhora
            join fh in _db.fechas_horarios.AsNoTracking() on hd.idFecha equals fh.idFecha
            where ids.Contains(hd.idAsignacion)
                  && hd.activo == 1
                  && fh.fecha != null && fh.fecha >= desde && fh.fecha <= hasta
            select new
            {
                hd.idAsignacion, hd.idHorario, hd.idhora, hd.idFecha,
                Fecha = fh.fecha!.Value, Dia = fh.dia,
                hc.hora_inicio, hc.hora_fin, hc.minutos
            }).ToListAsync(ct);

        // 2. Sesiones existentes, indexadas por (idAsignacion, idFecha, numeroBloque).
        var sesiones = await _db.cplec_sesiones.AsNoTracking()
            .Where(s => ids.Contains(s.idAsignacion) && s.activo == true)
            .Select(s => new { s.idSesion, s.idAsignacion, s.idFecha, s.numeroBloque, s.estado })
            .ToListAsync(ct);

        // 3. Un grupo por (asignación, fecha); dentro, agrupar en bloques contiguos.
        var resultado = new List<BloqueAgendaDto>();
        foreach (var grupo in filas
            .GroupBy(f => new { f.idAsignacion, f.idFecha, f.Fecha, f.Dia })
            .OrderBy(g => g.Key.Fecha).ThenBy(g => g.Key.idAsignacion))
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
                    x.idAsignacion == grupo.Key.idAsignacion
                    && x.idFecha == grupo.Key.idFecha
                    && x.numeroBloque == b.NumeroBloque);

                var estado = grupo.Key.Fecha > hoy ? EstadoBloque.Futura
                    : s is null                    ? EstadoBloque.Pendiente
                    : s.estado == "cerrada"        ? EstadoBloque.Cerrada
                                                   : EstadoBloque.Borrador;

                resultado.Add(new BloqueAgendaDto(
                    grupo.Key.idAsignacion,
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

- [x] **Step 5: Quitar el N+1 de `DiasSinRegistrarAsync`**

Reemplazar el bucle de las líneas 138-148 por:

```csharp
        var docentePorAsignacion = asignaciones
            .GroupBy(a => a.idAsignacion)
            .ToDictionary(g => g.Key, g => g.First().Docente);

        var agenda = await ObtenerAgendaAsync(docentePorAsignacion.Keys.ToList(), desde, hasta, ct);

        return agenda
            .Where(b => b.Estado == EstadoBloque.Pendiente)
            .Select(b => new DiaSinRegistrarDto(
                b.IdAsignacion, b.Fecha,
                docentePorAsignacion.TryGetValue(b.IdAsignacion, out var d) ? d : null,
                b.NumeroBloque, b.DiasRetraso))
            .OrderByDescending(d => d.DiasVencido).ThenBy(d => d.Fecha).ToList();
```

- [x] **Step 6: Correr toda la suite backend**

```bash
cd src && dotnet test
```

Esperado: PASS. Si algún test de `AgendaServiceTests` o de reportes construye un `BloqueAgendaDto` posicionalmente, hay que agregarle el `idAsignacion` como primer argumento.

- [x] **Step 7: Commit**

```bash
git add src/Leccionario.Api/Application/Asistencia/AgendaService.cs \
        src/Leccionario.Tests/Asistencia/AgendaServiceTests.cs
git commit -m "refactor(asistencia): agenda por lote y sin N+1 en dias-sin-registrar"
```

---

### Task 3: `MiHorarioService`

**Files:**
- Create: `src/Leccionario.Api/Application/Horarios/Services/MiHorarioService.cs`
- Create: `src/Leccionario.Tests/Horarios/MiHorarioServiceTests.cs`

**Interfaces:**
- Consumes: `IAgendaService.ObtenerAgendaAsync(IReadOnlyCollection<int>, DateOnly, DateOnly, CancellationToken)` de la Task 2; `IMisParalelosService.ResolverAsync(string idProfesor, string? idPeriodo, DateOnly? fechaReferencia, CancellationToken)` que ya existe y devuelve `IReadOnlyList<MiParaleloDto>` con `IdAsignacion`, `IdPeriodo`, `Asignatura`, `TipoLicencia`, `Jornada`, `Modalidad`, `Paralelo`.
- Produces:
  - `record BloqueMiHorarioDto(int IdAsignacion, string TipoLicencia, string Jornada, string Paralelo, string Asignatura, DateOnly Fecha, string Dia, int IdHorarioInicio, int NumeroBloque, string HoraInicio, string HoraFin, int FranjasPlanificadas, int MinutosPlanificados, string Estado, int? IdSesion, int DiasRetraso)`
  - `IMiHorarioService.ObtenerAsync(string idProfesor, DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)` → `Task<IReadOnlyList<BloqueMiHorarioDto>>`
  - La Task 4 consume la interfaz; la Task 5 consume la forma JSON del DTO.

- [x] **Step 1: Escribir los tests que fallan**

`src/Leccionario.Tests/Horarios/MiHorarioServiceTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// GET /api/mi-horario. Vigencia por ventana de la asignación (spec 2026-08-08 §D1)
/// y bloques del horario del docente.
/// </summary>
[TestClass]
public sealed class MiHorarioServiceTests
{
    private const string Duenio = "0000000001";
    private const string Ajeno = "0000000002";
    private const int NivelC6 = 35;
    private static readonly DateOnly Lunes = new(2026, 8, 3);
    private static readonly DateOnly Domingo = new(2026, 8, 9);

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    // Miércoles 2026-08-05 al mediodía.
    private static TimeProvider Reloj => new RelojFijo(new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

    private static sigafi_esContext CrearContexto(string nombre) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombre).Options);

    // El reloj se inyecta también en MiHorarioService: sin él, la semana en curso
    // se calcularía con la fecha real y Obtener_SinRango_UsaLaSemanaEnCurso fallaría.
    private static IMiHorarioService Crear(sigafi_esContext db) =>
        new MiHorarioService(
            new MisParalelosService(db, Reloj),
            new AgendaService(db, Reloj),
            Reloj);

    /// <summary>
    /// Docente `Duenio` con la asignación 100 (vigente, dos franjas contiguas el lunes)
    /// y `Ajeno` con la 200 (una franja el martes).
    /// </summary>
    private static async Task SembrarAsync(sigafi_esContext db,
        DateOnly? finAsig100 = null)
    {
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.asignaturas.Add(new asignaturas { idAsignatura = 1, asignatura = "Normativa de tránsito", anulada = false });
        db.secciones.Add(new secciones { idSeccion = 1, seccion = "MATUTINA" });
        db.modalidades.Add(new modalidades { idModalidad = 1, modalidad = "PRESENCIAL" });

        db.asignaciones_profesores.AddRange(
            new AsignacionBuilder().ConId(100).DelProfesor(Duenio).ConNivel(NivelC6).ConParalelo("A")
                .ConRango(new DateOnly(2026, 7, 1), finAsig100 ?? new DateOnly(2026, 12, 31)).Build(),
            new AsignacionBuilder().ConId(200).DelProfesor(Ajeno).ConNivel(NivelC6).ConParalelo("B")
                .ConRango(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).Build());

        db.fechas_horarios.AddRange(
            new fechas_horarios { idFecha = 500, fecha = Lunes, dia = "Lunes" },
            new fechas_horarios { idFecha = 501, fecha = Lunes.AddDays(1), dia = "Martes" },
            new fechas_horarios { idFecha = 504, fecha = Lunes.AddDays(4), dia = "Viernes" });

        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(902).DeTipo("Z").DeRango("08:00", "09:00").Build());

        db.horario_detalle.AddRange(
            new HorarioDetalleBuilder().ConId(1).DeAsignacion(100).EnFecha(500).EnFranja(901).Build(),
            new HorarioDetalleBuilder().ConId(2).DeAsignacion(100).EnFecha(500).EnFranja(902).Build(),
            new HorarioDetalleBuilder().ConId(3).DeAsignacion(100).EnFecha(504).EnFranja(901).Build(),
            new HorarioDetalleBuilder().ConId(4).DeAsignacion(200).EnFecha(501).EnFranja(901).Build());

        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Obtener_DevuelveSoloLosBloquesDelDocente()
    {
        using var db = CrearContexto(nameof(Obtener_DevuelveSoloLosBloquesDelDocente));
        await SembrarAsync(db);

        var r = await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo);

        r.Should().OnlyContain(b => b.IdAsignacion == 100);
        r.Should().HaveCount(2);   // lunes (bloque de 2 franjas) y viernes
    }

    [TestMethod]
    public async Task Obtener_AgrupaFranjasContiguasEnUnBloque()
    {
        using var db = CrearContexto(nameof(Obtener_AgrupaFranjasContiguasEnUnBloque));
        await SembrarAsync(db);

        var lunes = (await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo))
            .Single(b => b.Fecha == Lunes);

        lunes.HoraInicio.Should().Be("07:00");
        lunes.HoraFin.Should().Be("09:00");
        lunes.FranjasPlanificadas.Should().Be(2);
        lunes.MinutosPlanificados.Should().Be(120);
    }

    [TestMethod]
    public async Task Obtener_MarcaPasadoComoPendienteYFuturoComoFutura()
    {
        using var db = CrearContexto(nameof(Obtener_MarcaPasadoComoPendienteYFuturoComoFutura));
        await SembrarAsync(db);

        var r = await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo);

        r.Single(b => b.Fecha == Lunes).Estado.Should().Be("Pendiente");
        r.Single(b => b.Fecha == Lunes).DiasRetraso.Should().Be(2);       // lunes → miércoles
        r.Single(b => b.Fecha == Lunes.AddDays(4)).Estado.Should().Be("Futura");
    }

    [TestMethod]
    public async Task Obtener_ExcluyeAsignacionVencidaMasDeLaGracia()
    {
        using var db = CrearContexto(nameof(Obtener_ExcluyeAsignacionVencidaMasDeLaGracia));
        // Terminó el 2026-06-01: más de 15 días antes del reloj (2026-08-05).
        await SembrarAsync(db, finAsig100: new DateOnly(2026, 6, 1));

        var r = await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo);

        r.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Obtener_SinRango_UsaLaSemanaEnCurso()
    {
        using var db = CrearContexto(nameof(Obtener_SinRango_UsaLaSemanaEnCurso));
        await SembrarAsync(db);

        // Reloj = miércoles 2026-08-05 → semana lunes 03 a domingo 09.
        var r = await Crear(db).ObtenerAsync(Duenio, null, null);

        r.Should().HaveCount(2);
        r.Select(b => b.Fecha).Should().OnlyContain(f => f >= Lunes && f <= Domingo);
    }

    [TestMethod]
    public async Task Obtener_RangoMayorA16Semanas_Rechaza()
    {
        using var db = CrearContexto(nameof(Obtener_RangoMayorA16Semanas_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ObtenerAsync(Duenio, Lunes, Lunes.AddDays(16 * 7 + 1));

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("RANGO_EXCEDE_TOPE");
    }

    [TestMethod]
    public async Task Obtener_DesdeMayorQueHasta_Rechaza()
    {
        using var db = CrearContexto(nameof(Obtener_DesdeMayorQueHasta_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ObtenerAsync(Duenio, Domingo, Lunes);

        await acto.Should().ThrowAsync<ValidacionException>();
    }

    [TestMethod]
    public async Task Obtener_SinIdProfesor_Rechaza()
    {
        using var db = CrearContexto(nameof(Obtener_SinIdProfesor_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ObtenerAsync("", Lunes, Domingo);

        await acto.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task Obtener_TraeLaDescripcionDelParalelo()
    {
        using var db = CrearContexto(nameof(Obtener_TraeLaDescripcionDelParalelo));
        await SembrarAsync(db);

        var b = (await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo)).First();

        b.Paralelo.Should().Be("A");
        b.Jornada.Should().Be("MATUTINA");
        b.Asignatura.Should().Be("Normativa de tránsito");
    }
}
```

Si algún builder no tiene los métodos usados (`ConParalelo`, `ConRango`, `DelProfesor`, `ConNivel`), revisar `src/Leccionario.Tests/Builders/` y usar los que existan; no inventar API de builder.

- [x] **Step 2: Correr los tests y verificar que fallan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~MiHorarioServiceTests"
```

Esperado: FALLA en compilación — `MiHorarioService` no existe.

- [x] **Step 3: Implementar el servicio**

`src/Leccionario.Api/Application/Horarios/Services/MiHorarioService.cs`:

```csharp
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>Un bloque de clase del docente, con su paralelo resuelto.</summary>
public sealed record BloqueMiHorarioDto(
    int IdAsignacion, string TipoLicencia, string Jornada, string Paralelo, string Asignatura,
    DateOnly Fecha, string Dia, int IdHorarioInicio, int NumeroBloque,
    string HoraInicio, string HoraFin, int FranjasPlanificadas, int MinutosPlanificados,
    string Estado, int? IdSesion, int DiasRetraso);

/// <summary>
/// Horario propio del docente. El alcance de datos lo da
/// <see cref="IMisParalelosService"/>, acotado al <c>sub</c> del token: este
/// endpoint no recibe <c>idAsignacion</c>, así que no pasa por
/// <c>DistributivoGuard</c>. Ver spec 2026-08-08 sección 4.3.
/// </summary>
public interface IMiHorarioService
{
    /// <summary>
    /// Bloques de las asignaciones vigentes del docente entre <paramref name="desde"/> y
    /// <paramref name="hasta"/>. Si ambos son nulos, usa la semana en curso (lunes a domingo).
    /// </summary>
    Task<IReadOnlyList<BloqueMiHorarioDto>> ObtenerAsync(
        string idProfesor, DateOnly? desde, DateOnly? hasta, CancellationToken ct = default);
}

public sealed class MiHorarioService : IMiHorarioService
{
    /// <summary>Mismo tope que las operaciones por rango (ADR-008 decisión 11).</summary>
    private const int MaximoSemanas = 16;

    private readonly IMisParalelosService _misParalelos;
    private readonly IAgendaService _agenda;
    private readonly TimeProvider _reloj;

    public MiHorarioService(
        IMisParalelosService misParalelos,
        IAgendaService agenda,
        TimeProvider? reloj = null)
    {
        _misParalelos = misParalelos;
        _agenda = agenda;
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<BloqueMiHorarioDto>> ObtenerAsync(
        string idProfesor, DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idProfesor))
            throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");

        var (inicio, fin) = ResolverRango(desde, hasta);

        // Vigencia: ventana fecha_inicial..fecha_fin + 15 días de gracia. No se
        // usa periodos.activo, que está en 1 hasta en periodos de 2022.
        var paralelos = await _misParalelos.ResolverAsync(idProfesor, null, inicio, ct);
        if (paralelos.Count == 0)
            return Array.Empty<BloqueMiHorarioDto>();

        var porAsignacion = paralelos.ToDictionary(p => p.IdAsignacion);
        var bloques = await _agenda.ObtenerAgendaAsync(porAsignacion.Keys.ToList(), inicio, fin, ct);

        return bloques
            .Where(b => porAsignacion.ContainsKey(b.IdAsignacion))
            .Select(b =>
            {
                var p = porAsignacion[b.IdAsignacion];
                return new BloqueMiHorarioDto(
                    b.IdAsignacion, p.TipoLicencia, p.Jornada, p.Paralelo, p.Asignatura,
                    b.Fecha, b.Dia, b.IdHorarioInicio, b.NumeroBloque,
                    b.HoraInicio, b.HoraFin, b.FranjasPlanificadas, b.MinutosPlanificados,
                    b.Estado.ToString(), b.IdSesion, b.DiasRetraso);
            })
            .OrderBy(b => b.Fecha).ThenBy(b => b.HoraInicio).ThenBy(b => b.IdAsignacion)
            .ToList();
    }

    /// <summary>Semana en curso si no se dio rango; valida orden y tope.</summary>
    private (DateOnly Desde, DateOnly Hasta) ResolverRango(DateOnly? desde, DateOnly? hasta)
    {
        if (desde is null && hasta is null)
        {
            var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);
            var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
            return (lunes, lunes.AddDays(6));
        }

        if (desde is null || hasta is null)
            throw new ValidacionException("Indica ambas fechas del rango, o ninguna.");

        if (desde > hasta)
            throw new ValidacionException("La fecha inicial no puede ser posterior a la final.");

        if (hasta.Value.DayNumber - desde.Value.DayNumber > MaximoSemanas * 7)
            throw new ConflictoLoteException("RANGO_EXCEDE_TOPE",
                $"El rango no puede superar {MaximoSemanas} semanas.");

        return (desde.Value, hasta.Value);
    }
}
```

- [x] **Step 4: Correr los tests y verificar que pasan**

```bash
cd src && dotnet test --filter "FullyQualifiedName~MiHorarioServiceTests"
```

Esperado: PASS, 9 tests.

- [x] **Step 5: Correr toda la suite**

```bash
cd src && dotnet test
```

Esperado: PASS.

- [x] **Step 6: Commit**

```bash
git add src/Leccionario.Api/Application/Horarios/Services/MiHorarioService.cs \
        src/Leccionario.Tests/Horarios/MiHorarioServiceTests.cs
git commit -m "feat(horarios): MiHorarioService con vigencia por ventana de asignacion"
```

---

### Task 4: `GET /api/mi-horario`

**Files:**
- Create: `src/Leccionario.Api/Controllers/Horarios/MiHorarioController.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs:96`

**Interfaces:**
- Consumes: `IMiHorarioService.ObtenerAsync` de la Task 3.
- Produces: el endpoint HTTP `GET /api/mi-horario?desde=&hasta=` que consume la Task 5.

- [x] **Step 1: Registrar el servicio en DI**

En `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`, justo después de la línea 96 (`services.AddScoped<IAgendaService, AgendaService>();`):

```csharp
        services.AddScoped<IMiHorarioService, MiHorarioService>();
```

- [x] **Step 2: Escribir el controlador**

`src/Leccionario.Api/Controllers/Horarios/MiHorarioController.cs`:

```csharp
using System.Security.Claims;
using Leccionario.Api.Application.Horarios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Horarios;

/// <summary>
/// Horario propio del docente. No recibe idAsignacion: el alcance lo da el
/// claim <c>sub</c>. Ver docs/04 sección Docente.
/// </summary>
[ApiController]
[Route("api/mi-horario")]
[Authorize(Roles = "cplec_docente")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class MiHorarioController : ControllerBase
{
    private readonly IMiHorarioService _miHorario;

    public MiHorarioController(IMiHorarioService miHorario) => _miHorario = miHorario;

    /// <summary>
    /// Bloques de clase del docente en el rango. Sin rango, la semana en curso.
    /// Tope de 16 semanas → 422 RANGO_EXCEDE_TOPE.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BloqueMiHorarioDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) =>
        Ok(await _miHorario.ObtenerAsync(User.FindFirstValue("sub") ?? string.Empty, desde, hasta, ct));
}
```

- [x] **Step 3: Verificar que compila y que la suite sigue verde**

```bash
cd src && dotnet test
```

Esperado: PASS. Si existe un smoke test que enumera rutas (`ApiSmokeTests.cs`), puede requerir agregar la ruta nueva; revisar el fallo antes de tocar nada.

- [x] **Step 4: Verificar el endpoint a mano**

```bash
cd src && dotnet run --project Leccionario.Api
```

En otra terminal, con un token de docente válido:

```bash
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:5000/api/mi-horario" | head -40
```

Esperado: `200` con un array (posiblemente vacío si el docente no tiene horario cargado). Con un token de inspector: `403` (el endpoint es solo `cplec_docente`).

- [x] **Step 5: Commit**

```bash
git add src/Leccionario.Api/Controllers/Horarios/MiHorarioController.cs \
        src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs
git commit -m "feat(horarios): GET /api/mi-horario para el docente"
```

---

### Task 5: Modelo, servicio y store de `mi-horario` (frontend)

**Files:**
- Create: `client/src/app/features/docente/models/mi-horario.model.ts`
- Create: `client/src/app/features/docente/services/mi-horario.service.ts`
- Create: `client/src/app/features/docente/pages/mi-horario/mi-horario.store.ts`
- Test: `client/src/app/features/docente/pages/mi-horario/mi-horario.store.spec.ts`

**Interfaces:**
- Consumes: el JSON de `GET /api/mi-horario` de la Task 4 (camelCase por la serialización por defecto de la API).
- Produces:
  - `interface BloqueMiHorario` con los 16 campos del DTO.
  - `MiHorarioStore` con: `cargar()`, `cambiarSemana(delta: number)`, `irAEstaSemana()`, y selectores `bloques()`, `lunes()`, `filas()`, `cargando()`, `error()`.
  - `interface FilaHorario { horaInicio: string; horaFin: string; celdas: (BloqueMiHorario | null)[] }` — `celdas` tiene 7 posiciones, lunes a domingo. La Task 6 consume `filas()`.

- [x] **Step 1: Escribir el modelo y el servicio**

`client/src/app/features/docente/models/mi-horario.model.ts`:

```typescript
export type EstadoBloqueHorario = 'Pendiente' | 'Borrador' | 'Cerrada' | 'Futura';

export interface BloqueMiHorario {
  idAsignacion: number;
  tipoLicencia: string;
  jornada: string;
  paralelo: string;
  asignatura: string;
  fecha: string;
  dia: string;
  idHorarioInicio: number;
  numeroBloque: number;
  horaInicio: string;
  horaFin: string;
  franjasPlanificadas: number;
  minutosPlanificados: number;
  estado: EstadoBloqueHorario;
  idSesion: number | null;
  diasRetraso: number;
}
```

`client/src/app/features/docente/services/mi-horario.service.ts`:

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import type { Observable } from 'rxjs';
import { environment } from '@env/environment';
import type { BloqueMiHorario } from '../models/mi-horario.model';

@Injectable({ providedIn: 'root' })
export class MiHorarioService {
  private readonly http = inject(HttpClient);

  obtener(desde: string, hasta: string): Observable<BloqueMiHorario[]> {
    const params = new HttpParams().set('desde', desde).set('hasta', hasta);
    return this.http.get<BloqueMiHorario[]>(`${environment.apiUrl}/mi-horario`, { params });
  }
}
```

- [x] **Step 2: Escribir el test del store que falla**

`client/src/app/features/docente/pages/mi-horario/mi-horario.store.spec.ts`:

```typescript
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';

import { MiHorarioStore } from './mi-horario.store';
import type { BloqueMiHorario } from '@features/docente/models/mi-horario.model';

function bloque(over: Partial<BloqueMiHorario>): BloqueMiHorario {
  return {
    idAsignacion: 100,
    tipoLicencia: 'C',
    jornada: 'MATUTINA',
    paralelo: 'A',
    asignatura: 'Normativa de tránsito',
    fecha: '2026-08-03',
    dia: 'Lunes',
    idHorarioInicio: 1,
    numeroBloque: 1,
    horaInicio: '07:00',
    horaFin: '09:00',
    franjasPlanificadas: 2,
    minutosPlanificados: 120,
    estado: 'Pendiente',
    idSesion: null,
    diasRetraso: 2,
    ...over,
  };
}

describe('MiHorarioStore', () => {
  let store: MiHorarioStore;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [MiHorarioStore, provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(MiHorarioStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('pide la semana del lunes actual y guarda los bloques', () => {
    store.irASemanaDe('2026-08-05');
    const req = httpMock.expectOne(
      (r) => r.url.endsWith('/mi-horario')
        && r.params.get('desde') === '2026-08-03'
        && r.params.get('hasta') === '2026-08-09',
    );
    req.flush([bloque({})]);

    expect(store.lunes()).toBe('2026-08-03');
    expect(store.bloques()).toHaveLength(1);
    expect(store.cargando()).toBe(false);
  });

  it('cambiarSemana(1) avanza siete días y vuelve a pedir', () => {
    store.irASemanaDe('2026-08-05');
    httpMock.expectOne((r) => r.url.endsWith('/mi-horario')).flush([]);

    store.cambiarSemana(1);
    const req = httpMock.expectOne(
      (r) => r.params.get('desde') === '2026-08-10' && r.params.get('hasta') === '2026-08-16',
    );
    req.flush([]);

    expect(store.lunes()).toBe('2026-08-10');
  });

  it('agrupa los bloques en filas por franja y columnas por día', () => {
    store.irASemanaDe('2026-08-05');
    httpMock.expectOne((r) => r.url.endsWith('/mi-horario')).flush([
      bloque({ fecha: '2026-08-03', horaInicio: '07:00', horaFin: '09:00' }),
      bloque({ fecha: '2026-08-05', horaInicio: '07:00', horaFin: '09:00', idHorarioInicio: 3 }),
      bloque({ fecha: '2026-08-05', horaInicio: '10:00', horaFin: '11:00', idHorarioInicio: 4 }),
    ]);

    const filas = store.filas();
    expect(filas).toHaveLength(2);
    expect(filas[0].horaInicio).toBe('07:00');
    expect(filas[0].celdas[0]?.idHorarioInicio).toBe(1);   // lunes
    expect(filas[0].celdas[2]?.idHorarioInicio).toBe(3);   // miércoles
    expect(filas[0].celdas[1]).toBeNull();                 // martes
    expect(filas[1].horaInicio).toBe('10:00');
  });

  it('guarda un mensaje de error si la petición falla', () => {
    store.irASemanaDe('2026-08-05');
    httpMock.expectOne((r) => r.url.endsWith('/mi-horario'))
      .flush({ codigo: 'RANGO_EXCEDE_TOPE', mensaje: 'x' }, { status: 422, statusText: 'Unprocessable' });

    expect(store.error()).toBeTruthy();
    expect(store.cargando()).toBe(false);
  });
});
```

- [x] **Step 3: Correr el test y verificar que falla**

```bash
cd client && npm test -- mi-horario.store
```

Esperado: FALLA — no existe `mi-horario.store.ts`.

- [x] **Step 4: Implementar el store**

`client/src/app/features/docente/pages/mi-horario/mi-horario.store.ts`:

```typescript
import { Injectable, computed, inject, signal } from '@angular/core';
import { MiHorarioService } from '@features/docente/services/mi-horario.service';
import type { BloqueMiHorario } from '@features/docente/models/mi-horario.model';

/** Una franja horaria con sus siete celdas, de lunes a domingo. */
export interface FilaHorario {
  readonly horaInicio: string;
  readonly horaFin: string;
  readonly celdas: readonly (BloqueMiHorario | null)[];
}

/** Lunes de la semana que contiene `fechaISO`, en formato ISO. */
export function lunesDe(fechaISO: string): string {
  const d = new Date(`${fechaISO}T00:00:00`);
  const diaLunes0 = (d.getDay() + 6) % 7;
  d.setDate(d.getDate() - diaLunes0);
  return aISO(d);
}

function aISO(d: Date): string {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

function sumarDias(fechaISO: string, dias: number): string {
  const d = new Date(`${fechaISO}T00:00:00`);
  d.setDate(d.getDate() + dias);
  return aISO(d);
}

@Injectable()
export class MiHorarioStore {
  private readonly api = inject(MiHorarioService);

  private readonly _lunes = signal<string>(lunesDe(aISO(new Date())));
  private readonly _bloques = signal<BloqueMiHorario[]>([]);
  private readonly _cargando = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly lunes = this._lunes.asReadonly();
  readonly bloques = this._bloques.asReadonly();
  readonly cargando = this._cargando.asReadonly();
  readonly error = this._error.asReadonly();

  readonly domingo = computed(() => sumarDias(this._lunes(), 6));

  /** Los siete días de la semana mostrada, en ISO. */
  readonly dias = computed(() =>
    Array.from({ length: 7 }, (_, i) => sumarDias(this._lunes(), i)),
  );

  /** Bloques agrupados: una fila por franja distinta, una columna por día. */
  readonly filas = computed<FilaHorario[]>(() => {
    const dias = this.dias();
    const porFranja = new Map<string, FilaHorario>();

    for (const b of this._bloques()) {
      const clave = `${b.horaInicio}-${b.horaFin}`;
      let fila = porFranja.get(clave);
      if (!fila) {
        fila = { horaInicio: b.horaInicio, horaFin: b.horaFin, celdas: Array(7).fill(null) };
        porFranja.set(clave, fila);
      }
      const col = dias.indexOf(b.fecha);
      if (col >= 0) (fila.celdas as (BloqueMiHorario | null)[])[col] = b;
    }

    return [...porFranja.values()].sort((a, b) => a.horaInicio.localeCompare(b.horaInicio));
  });

  /** Posiciona la semana en la que cae `fechaISO` y carga. */
  irASemanaDe(fechaISO: string): void {
    this._lunes.set(lunesDe(fechaISO));
    this.cargar();
  }

  irAEstaSemana(): void {
    this.irASemanaDe(aISO(new Date()));
  }

  cambiarSemana(deltaSemanas: number): void {
    this._lunes.set(sumarDias(this._lunes(), deltaSemanas * 7));
    this.cargar();
  }

  cargar(): void {
    this._cargando.set(true);
    this._error.set(null);
    this.api.obtener(this._lunes(), this.domingo()).subscribe({
      next: (bloques) => {
        this._bloques.set(bloques);
        this._cargando.set(false);
      },
      error: () => {
        this._bloques.set([]);
        this._error.set('No se pudo cargar tu horario. Intenta de nuevo.');
        this._cargando.set(false);
      },
    });
  }
}
```

- [x] **Step 5: Correr los tests y verificar que pasan**

```bash
cd client && npm test -- mi-horario.store
```

Esperado: PASS, 4 tests.

- [x] **Step 6: Lint y build**

```bash
cd client && npm run lint && npm run build
```

Esperado: sin errores ni warnings.

- [x] **Step 7: Commit**

```bash
git add client/src/app/features/docente/models/mi-horario.model.ts \
        client/src/app/features/docente/services/mi-horario.service.ts \
        client/src/app/features/docente/pages/mi-horario/
git commit -m "feat(client): store y servicio de mi-horario del docente"
```

---

### Task 6: Página `mi-horario`, ruta y navegación

**Files:**
- Create: `client/src/app/features/docente/pages/mi-horario/mi-horario.page.ts`
- Modify: `client/src/app/app.routes.ts:31-38` (redirección por rol) y la lista de rutas hijas
- Modify: `client/src/app/layout/main-layout/main-layout.ts:21`

**Interfaces:**
- Consumes: `MiHorarioStore` (`filas()`, `dias()`, `lunes()`, `cargando()`, `error()`, `cambiarSemana()`, `irAEstaSemana()`) y `FilaHorario` de la Task 5.
- Produces: la ruta `/mi-horario` y la navegación a `/paralelos/:idAsignacion/pasar-lista?idHorarioInicio=N`, que la Task 7 espera.

- [x] **Step 1: Escribir la página**

`client/src/app/features/docente/pages/mi-horario/mi-horario.page.ts`:

```typescript
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import { MiHorarioStore } from './mi-horario.store';
import type { BloqueMiHorario } from '@features/docente/models/mi-horario.model';

const DIAS = ['Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb', 'Dom'] as const;

@Component({
  selector: 'app-mi-horario-page',
  providers: [MiHorarioStore],
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatTooltipModule],
  template: `
    <header class="page-header">
      <h1>Mi horario</h1>
      <div class="semana">
        <button mat-icon-button (click)="store.cambiarSemana(-1)" aria-label="Semana anterior">
          <mat-icon>chevron_left</mat-icon>
        </button>
        <span class="rango">{{ store.lunes() }} → {{ store.domingo() }}</span>
        <button mat-icon-button (click)="store.cambiarSemana(1)" aria-label="Semana siguiente">
          <mat-icon>chevron_right</mat-icon>
        </button>
        <button mat-stroked-button (click)="store.irAEstaSemana()">Esta semana</button>
      </div>
    </header>

    @if (store.cargando()) {
      <p class="estado">Cargando tu horario…</p>
    } @else if (store.error()) {
      <p class="estado error">{{ store.error() }}</p>
    } @else if (store.filas().length === 0) {
      <p class="estado">No tienes clases planificadas en esta semana.</p>
    } @else {
      <div class="scroll">
        <table class="grid">
          <thead>
            <tr>
              <th scope="col" class="col-hora">Hora</th>
              @for (d of dias; track d.iso) {
                <th scope="col">{{ d.etiqueta }}<br /><small>{{ d.iso }}</small></th>
              }
            </tr>
          </thead>
          <tbody>
            @for (fila of store.filas(); track fila.horaInicio) {
              <tr>
                <th scope="row" class="col-hora">{{ fila.horaInicio }}–{{ fila.horaFin }}</th>
                @for (celda of fila.celdas; track $index) {
                  <td>
                    @if (celda) {
                      <button
                        type="button"
                        class="bloque"
                        [class]="'estado-' + celda.estado.toLowerCase()"
                        [disabled]="celda.estado === 'Futura'"
                        [attr.aria-disabled]="celda.estado === 'Futura'"
                        [matTooltip]="tooltip(celda)"
                        (click)="abrir(celda)"
                      >
                        <span class="paralelo">{{ celda.tipoLicencia }} · {{ celda.paralelo }}</span>
                        <span class="jornada">{{ celda.jornada }}</span>
                        @if (celda.estado === 'Pendiente' && celda.diasRetraso > 0) {
                          <span class="retraso">{{ celda.diasRetraso }} d</span>
                        }
                      </button>
                    }
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: [`
    .page-header { display: flex; flex-wrap: wrap; align-items: center; gap: 16px; margin-bottom: 16px; }
    .semana { display: flex; align-items: center; gap: 8px; }
    .rango { font-variant-numeric: tabular-nums; }
    .estado { padding: 24px; text-align: center; color: rgb(0 0 0 / 60%); }
    .estado.error { color: #c5211f; }
    .scroll { overflow-x: auto; }
    .grid { width: 100%; border-collapse: collapse; }
    .grid th, .grid td { border: 1px solid rgb(0 0 0 / 12%); padding: 4px; vertical-align: top; }
    .grid thead th { font-weight: 500; font-size: 0.85rem; }
    .col-hora { white-space: nowrap; font-variant-numeric: tabular-nums; font-size: 0.8rem; }
    .bloque {
      display: flex; flex-direction: column; gap: 2px; width: 100%;
      border: 0; border-radius: 6px; padding: 6px 8px; cursor: pointer;
      font: inherit; text-align: left;
    }
    .bloque:disabled { cursor: default; }
    .paralelo { font-weight: 600; }
    .jornada, .retraso { font-size: 0.75rem; }
    .estado-pendiente { background: #fff3e0; color: #ef6c00; }
    .estado-borrador { background: #e3f2fd; color: #1565c0; }
    .estado-cerrada { background: #e8f5e9; color: #2e7d32; }
    .estado-futura { background: #f5f5f5; color: rgb(0 0 0 / 45%); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MiHorarioPage {
  protected readonly store = inject(MiHorarioStore);
  private readonly router = inject(Router);

  constructor() {
    this.store.irAEstaSemana();
  }

  protected get dias(): { iso: string; etiqueta: string }[] {
    return this.store.dias().map((iso, i) => ({ iso, etiqueta: DIAS[i] }));
  }

  protected tooltip(b: BloqueMiHorario): string {
    if (b.estado === 'Futura') return 'Todavía no puedes pasar lista de esta clase.';
    if (b.estado === 'Cerrada') return 'Sesión cerrada. Abre para consultar.';
    return `${b.asignatura} — pasar lista`;
  }

  protected abrir(b: BloqueMiHorario): void {
    if (b.estado === 'Futura') return;
    void this.router.navigate(['/paralelos', b.idAsignacion, 'pasar-lista'], {
      queryParams: { idHorarioInicio: b.idHorarioInicio },
    });
  }
}
```

- [x] **Step 2: Registrar la ruta**

En `client/src/app/app.routes.ts`, agregar como primera ruta hija después del `redirectTo`:

```typescript
      {
        path: 'mi-horario',
        canActivate: [roleGuard],
        data: { roles: [Roles.docente] },
        loadComponent: () =>
          import('@features/docente/pages/mi-horario/mi-horario.page').then(
            (m) => m.MiHorarioPage,
          ),
      },
```

Y cambiar la redirección por rol (líneas 31-38) para que el docente aterrice en su horario:

```typescript
        redirectTo: () => {
          const auth = inject(AuthService);
          if (auth.tieneRol(Roles.docente)) {
            return 'mi-horario';
          }
          return 'horarios';
        },
```

- [x] **Step 3: Agregar la entrada al sidebar**

En `client/src/app/layout/main-layout/main-layout.ts`, **antes** de la línea 21:

```typescript
  { label: 'Mi horario', path: '/mi-horario', icon: 'calendar_month', roles: [Roles.docente] },
```

- [x] **Step 4: Verificar tests, lint y build**

```bash
cd client && npm test && npm run lint && npm run build
```

Esperado: PASS en todo. Si algún test de rutas o de `main-layout` afirma sobre la lista de items o el destino de la redirección, actualizarlo para reflejar los cambios.

- [x] **Step 5: Verificar en el navegador**

Arrancar el backend (`cd src && dotnet run --project Leccionario.Api`) y el frontend (`cd client && npm start`). Entrar como docente con horario cargado y confirmar: aterriza en `/mi-horario`, el grid muestra los bloques de la semana, los futuros están deshabilitados, y un click en un bloque pasado navega a `pasar-lista` con `?idHorarioInicio=`.

- [x] **Step 6: Commit**

```bash
git add client/src/app/features/docente/pages/mi-horario/mi-horario.page.ts \
        client/src/app/app.routes.ts \
        client/src/app/layout/main-layout/main-layout.ts
git commit -m "feat(client): pagina mi-horario del docente con grid semanal"
```

---

### Task 7: `pasar-lista` exige el bloque

**Files:**
- Modify: `client/src/app/features/docente/pages/pasar-lista/pasar-lista.page.ts:236-256,296-302`
- Modify: `client/src/app/features/docente/pages/pasar-lista/pasar-lista.store.ts:83-118`
- Test: `client/src/app/features/docente/pages/pasar-lista/pasar-lista.store.spec.ts`

**Interfaces:**
- Consumes: la navegación con `?idHorarioInicio=` de la Task 6; el código `SIN_HORARIO` de la Task 1.
- Produces: `PasarListaStore.cargar(idAsignacion: number, idHorarioInicio: number, temaInicial: string): void` — firma nueva, sin `fecha`.

- [x] **Step 1: Escribir el test que falla**

Agregar a `client/src/app/features/docente/pages/pasar-lista/pasar-lista.store.spec.ts`:

```typescript
  it('crea la sesión mandando idHorarioInicio y sin fecha', () => {
    store.cargar(100, 77, 'Clase');
    httpMock.expectOne((r) => r.url.includes('/paralelos/100/alumnos')).flush([]);

    const req = httpMock.expectOne((r) => r.url.includes('/paralelos/100/sesiones'));
    expect(req.request.body.idHorarioInicio).toBe(77);
    expect(req.request.body.fecha).toBeUndefined();
    req.flush({ idSesion: 1, idAsignacion: 100, estado: 'borrador', asistencias: [] });
  });

  it('muestra un mensaje propio cuando el backend responde SIN_HORARIO', () => {
    store.cargar(100, 77, 'Clase');
    httpMock.expectOne((r) => r.url.includes('/paralelos/100/alumnos')).flush([]);
    httpMock.expectOne((r) => r.url.includes('/paralelos/100/sesiones'))
      .flush({ codigo: 'SIN_HORARIO', mensaje: 'x' }, { status: 422, statusText: 'Unprocessable' });

    expect(store.errorGuardar()).toContain('no tiene horario planificado');
  });
```

Ajustar el `beforeEach` y los mocks existentes del spec si la firma de `cargar` cambia en todos los tests del archivo.

- [x] **Step 2: Correr el test y verificar que falla**

```bash
cd client && npm test -- pasar-lista.store
```

Esperado: FALLA — la firma actual es `cargar(idAsignacion, fecha, temaInicial, idHorarioInicio?)` y el body incluye `fecha`.

- [x] **Step 3: Cambiar la firma del store**

En `client/src/app/features/docente/pages/pasar-lista/pasar-lista.store.ts`, reemplazar la firma y el cuerpo del método `cargar` (desde la línea 83) por:

```typescript
  /**
   * Carga inicial: pide la nómina y abre (o recupera) la sesión del bloque de
   * horario indicado. `idHorarioInicio` es obligatorio: sin bloque no hay
   * asistencia (spec 2026-08-08 §D2).
   */
  cargar(idAsignacion: number, idHorarioInicio: number, temaInicial: string): void {
    this._idAsignacion.set(idAsignacion);
    this._errorGuardar.set(null);
```

y, dentro del `next` de la nómina, cambiar la llamada:

```typescript
          this.asistencia
            .crearSesion(idAsignacion, { tema: temaInicial, idHorarioInicio })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: (sesion) => this.aplicarSesion(sesion),
              error: (err) => {
                const code = err?.error?.codigo || err?.error?.code;
                if (code === 'SIN_HORARIO') {
                  this._errorGuardar.set(
                    'Este paralelo no tiene horario planificado. Pide al inspector que lo cargue.',
                  );
                } else if (code === 'HORARIO_REQUERIDO') {
                  this._errorGuardar.set(
                    'Se requiere seleccionar un bloque de horario planificado para este paralelo.',
                  );
                } else {
                  this._errorGuardar.set('No se pudo abrir la sesión de esta clase.');
                }
              },
            });
```

Si el tipo del request (`CrearSesionRequest` en `client/src/app/features/docente/models/asistencia.model.ts`) declara `fecha` como obligatorio, cambiarlo a opcional y marcarlo como obsoleto en un comentario.

- [x] **Step 4: Cambiar la página**

En `client/src/app/features/docente/pages/pasar-lista/pasar-lista.page.ts`:

1. Reemplazar las declaraciones de inputs y el `constructor` (líneas 236-256) por:

```typescript
export class PasarListaPage {
  protected readonly idAsignacion = input.required<number>();
  protected readonly idHorarioInicio = input<number | null>(null);
  protected readonly store = inject(PasarListaStore);
  private readonly snack = inject(MatSnackBar);
  private readonly router = inject(Router);

  protected readonly estados = TODOS_ESTADOS;

  constructor() {
    effect(() => {
      const id = this.idAsignacion();
      const bloque = this.idHorarioInicio();
      if (!id) return;

      // Sin bloque no hay asistencia: se vuelve a la agenda a elegirlo.
      if (bloque === null || bloque === undefined || Number.isNaN(Number(bloque))) {
        this.snack.open('Elige el bloque de clase para pasar lista.', 'OK', { duration: 4000 });
        void this.router.navigate(['/paralelos', id, 'agenda']);
        return;
      }

      this.store.cargar(id, Number(bloque), 'Clase del día');
    });
  }
```

2. Eliminar el input `fechaParam`, la signal `fechaHoy` y el método privado `hoyISO()` (líneas 296-302).

3. En el template, reemplazar `<p class="fecha">{{ fechaHoy() }}</p>` por la fecha de la sesión:

```html
      @if (store.sesion(); as s) {
        <p class="fecha">{{ s.fecha }}</p>
      }
```

4. Reemplazar el texto `Abriendo la sesión del día…` por `Abriendo la sesión de esta clase…`.

5. Agregar `import { Router } from '@angular/router';` a los imports del archivo.

- [x] **Step 5: Correr los tests y verificar que pasan**

```bash
cd client && npm test -- pasar-lista
```

Esperado: PASS.

- [x] **Step 6: Verificar que `mis-paralelos` no ofrece pasar lista directo**

```bash
grep -n "pasar-lista" client/src/app/features/docente/pages/mis-paralelos/mis-paralelos.page.ts
```

Esperado: **sin resultados** — la página ya solo enlaza a `agenda` (`mis-paralelos.page.ts:98`). Si aparece algún enlace a `pasar-lista`, eliminarlo dejando solo "Ver agenda".

- [x] **Step 7: Suite completa, lint y build**

```bash
cd client && npm test && npm run lint && npm run build
```

Esperado: PASS en todo.

- [x] **Step 8: Commit**

```bash
git add client/src/app/features/docente/pages/pasar-lista/ \
        client/src/app/features/docente/models/asistencia.model.ts
git commit -m "feat(client): pasar lista solo desde un bloque de horario"
```

---

### Task 8: Documentación

**Files:**
- Modify: `docs/04-contrato-api.md:33-49` (tabla de errores), `:89-121` (sección Docente), `:167-192` (POST sesiones)
- Modify: `docs/adr/ADR-008-horarios-en-tabla-compartida.md` (decisión 9)
- Modify: `docs/10-navegacion-distributivo.md`
- Modify: `docs/00-roadmap.md` (tabla de hitos)
- Modify: `CLAUDE.md` (§Modelo de datos y §Estado actual)

**Interfaces:**
- Consumes: todo lo construido en las Tasks 1-7.
- Produces: nada de código.

- [x] **Step 1: Tabla de errores en `docs/04-contrato-api.md`**

Agregar dos filas en la tabla de códigos (después de la fila `422 FUERA_DE_VENTANA`):

```markdown
| 422 | `SIN_HORARIO` | El paralelo no tiene horario planificado: no se puede registrar asistencia |
| 422 | `HORARIO_REQUERIDO` | El paralelo tiene horario: falta `idHorarioInicio` en el request |
| 422 | `RANGO_EXCEDE_TOPE` | El rango de fechas pedido supera las 16 semanas |
```

- [x] **Step 2: Documentar `GET /api/mi-horario`**

En la sección `## Docente`, después de `### GET /api/periodos/por-nivel`, agregar:

````markdown
### `GET /api/mi-horario?desde=2026-08-03&hasta=2026-08-09`
Rol: `cplec_docente`. El horario propio del docente. **No recibe `idAsignacion`**: el
alcance sale del claim `sub`.

- `desde` y `hasta` son opcionales, pero van juntos: o ambos, o ninguno. Sin ellos,
  la semana en curso (lunes a domingo).
- Tope de **16 semanas** entre ambos → `422 RANGO_EXCEDE_TOPE`.
- `desde > hasta` → `400 VALIDACION`.
- Solo asignaciones **vigentes**: la ventana `fecha_inicial .. fecha_fin` cubre `desde`,
  con 15 días de gracia después de `fecha_fin`; ventana `NULL` cuenta como vigente.
  **No se usa `periodos.activo`**, que está en 1 hasta en períodos de 2022.

```json
[
  {
    "idAsignacion": 41822,
    "tipoLicencia": "C",
    "jornada": "MATUTINA",
    "paralelo": "A",
    "asignatura": "Normativa de tránsito",
    "fecha": "2026-08-04",
    "dia": "Martes",
    "idHorarioInicio": 90311,
    "numeroBloque": 1,
    "horaInicio": "07:00",
    "horaFin": "09:00",
    "franjasPlanificadas": 2,
    "minutosPlanificados": 120,
    "estado": "Pendiente",
    "idSesion": null,
    "diasRetraso": 4
  }
]
```

`estado`: `Pendiente` (pasada, sin sesión) · `Borrador` (sesión abierta) · `Cerrada` ·
`Futura` (aún no ocurre; no se puede registrar).
````

- [x] **Step 3: Reescribir `POST /api/paralelos/{idAsignacion}/sesiones`**

Reemplazar la sección completa (líneas 167-192) por:

````markdown
### `POST /api/paralelos/{idAsignacion}/sesiones`
Rol: `cplec_docente`. Abre la clase de un **bloque de horario**.

```json
// request
{
  "idHorarioInicio": 90311,
  "tema": "Señalización vertical y horizontal",
  "observacion": "Faltó el proyector; se usó pizarra"
}
```

- `idHorarioInicio` es **obligatorio**: identifica el bloque planificado. La fecha y el
  `numeroBloque` de la sesión se derivan de él — el cliente no los elige.
- `tema` es **obligatorio** (máx. 250). Es el propósito del leccionario.
- Si la asignación no tiene ninguna celda de horario activa → `422 SIN_HORARIO`. El
  inspector debe cargar el horario primero. (Retira el modo transición de ADR-008 §9.)
- Si la tiene pero falta `idHorarioInicio` → `422 HORARIO_REQUERIDO`.
- Si el `idHorarioInicio` no pertenece a un bloque de esa asignación → `403 DISTRIBUTIVO_AJENO`.
- La fecha del bloque debe caer dentro de `asignaciones_profesores.fecha_inicial .. fecha_fin`
  → si no, `422 FUERA_DE_VENTANA`.
- Un bloque futuro se rechaza.
- **Idempotente**: si ya existe la tupla (`idAsignacion`, `idFecha`, `numeroBloque`),
  devuelve la sesión existente en vez de fallar.
- `201` con la sesión creada y la nómina precargada en `presente`.
- `origen` en la respuesta: `"horario"` en toda sesión nueva. `"libre"` solo aparece en
  sesiones históricas creadas antes del 2026-08-08.

`fecha` y `numeroBloque` siguen aceptándose en el body por compatibilidad, pero **se
ignoran**.
````

- [x] **Step 4: Addendum en ADR-008**

En `docs/adr/ADR-008-horarios-en-tabla-compartida.md`, al final de la decisión 9, agregar:

```markdown
> **Addendum 2026-08-08 — el modo transición se retira.** Decisión del dueño del
> producto: sin horario planificado no hay asistencia. `SesionService.CrearAsync`
> responde `422 SIN_HORARIO` en vez de crear una sesión libre. `idHorarioInicio` sigue
> siendo NULL-able en `cplec_sesiones` solo por las filas históricas, que se conservan
> legibles y editables. Consecuencia aceptada: el inspector es prerequisito duro del
> docente, y el despliegue exige tener cargados los horarios de los paralelos vigentes.
> Ver `docs/superpowers/specs/2026-08-08-asistencia-anclada-horario-design.md`.
```

- [x] **Step 5: Regla de vigencia y anclaje en `docs/10`**

Agregar una sección al final de `docs/10-navegacion-distributivo.md`:

```markdown
## Vigencia y anclaje de la asistencia (2026-08-08)

**Qué es "vigente".** Una asignación está vigente si su ventana
`fecha_inicial .. fecha_fin` cubre la fecha de referencia, con 15 días de gracia después
de `fecha_fin`; una ventana `NULL` cuenta como vigente (son 737 asignaciones activas de
carrera 6). **`periodos.activo` no sirve**: está en 1 en casi todos los períodos,
incluidos los de 2022. La regla vive en `MisParalelosService.ResolverAsync` y la
reutilizan `GET /api/mis-paralelos` y `GET /api/mi-horario`.

**Qué ancla la asistencia.** Una sesión cuelga de un bloque de `horario_detalle`, no de
una fecha. `idFecha` y `numeroBloque` se derivan del `idHorarioInicio`; el cliente no los
elige. Sin celdas de horario activas para la asignación, `POST .../sesiones` responde
`422 SIN_HORARIO`. El registro retroactivo sigue permitido sin tope, marcado con
`esTardia` y `diasRetraso` congelados en el primer guardado.
```

- [x] **Step 6: Hito M4c en el roadmap**

En `docs/00-roadmap.md`, agregar una fila a la tabla de hitos después de **M4b** (y su nodo en el diagrama Mermaid, entre `M4` y `M5`):

```markdown
| **M4c**| Horario del docente + asistencia anclada al bloque       |   ✅   | (en `dv_jb`) `feature/asistencia-anclada-horario` | `GET /api/mi-horario`; `SIN_HORARIO` retira el modo transición de ADR-008 §9; `AgendaService` por lote sin N+1; página `mi-horario` con grid semanal; `pasar-lista` exige `idHorarioInicio`. | M4b |
```

En el diagrama:

```
  M4b[M4b: Horarios inspector] --> M4c[M4c: Mi horario docente<br/>+ asistencia anclada]
  M4c --> M5
```

- [x] **Step 7: Actualizar `CLAUDE.md`**

En §**Modelo de datos — lo no obvio**, reemplazar la viñeta "Las sesiones son **por día, no por hora**. Sin FK a `horas_clases`." por:

```markdown
- Las sesiones tienen **grano día pero están ancladas a un bloque de horario**
  (`cplec_sesiones.idHorarioInicio` → `horario_detalle`). Desde 2026-08-08, sin horario
  planificado no se puede registrar asistencia: `422 SIN_HORARIO`. Las sesiones con
  `idHorarioInicio` NULL son históricas.
- "Periodo vigente" se decide por la ventana `fecha_inicial..fecha_fin` de la asignación
  (+15 días de gracia; `NULL` = vigente), nunca por `periodos.activo`.
```

En §**Estado actual**, agregar tras la viñeta de M4b:

```markdown
- **M4c completado**: `GET /api/mi-horario`, retiro del modo transición
  (`SIN_HORARIO`), agenda por lote sin N+1, página `mi-horario` del docente y
  `pasar-lista` anclada al bloque.
```

Actualizar los conteos de tests de §Comandos y §Estado actual con los números reales al terminar.

- [x] **Step 8: Verificación final completa**

```bash
cd src && dotnet test
cd ../client && npm test && npm run lint && npm run build
```

Esperado: todo verde. Anotar los conteos reales de tests y corregirlos en `CLAUDE.md` si difieren de lo escrito en el Step 7.

- [x] **Step 9: Commit**

```bash
git add docs/ CLAUDE.md
git commit -m "docs(asistencia): mi-horario, SIN_HORARIO y regla de vigencia (M4c)"
```

---

## Verificación pre-despliegue (fuera del plan de código)

El riesgo de la sección 8 del spec necesita una comprobación manual antes de llevar esto
a producción. Consulta a correr sobre `sigafi_es` (solo lectura, sin `DELETE`/`UPDATE`):

```sql
-- Asignaciones vigentes de carrera 6 sin ninguna celda de horario activa.
-- Cada fila es un paralelo que quedará sin poder pasar lista.
SELECT ap.idAsignacion, ap.idPeriodo, ap.idNivel, ap.idSeccion, ap.idModalidad,
       TRIM(ap.paralelo) AS paralelo, ap.fecha_inicial, ap.fecha_fin
FROM asignaciones_profesores ap
JOIN cursos c ON c.idNivel = ap.idNivel
LEFT JOIN horario_detalle hd
       ON hd.idAsignacion = ap.idAsignacion AND hd.activo = 1
WHERE c.idCarrera = 6
  AND (ap.activo IS NULL OR ap.activo = 1)
  AND (ap.esActivaAsignacion IS NULL OR ap.esActivaAsignacion = 1)
  AND (ap.fecha_inicial IS NULL OR ap.fecha_inicial <= CURDATE())
  AND (ap.fecha_fin IS NULL OR ap.fecha_fin >= DATE_SUB(CURDATE(), INTERVAL 15 DAY))
  AND hd.idHorario IS NULL
ORDER BY ap.fecha_inicial;
```

Si devuelve filas, el inspector debe cargarles el horario antes del despliegue.
