using FluentAssertions;
using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Leccionario.Tests.Asistencia;

/// <summary>
/// Tests del guard de autorización central de cplec (M3). Cobertura objetivo:
/// 100 % de la rama del guard (per docs/07).
///
/// Patrón TDD: estos tests están en RED mientras <c>DistributivoGuard</c> no
/// esté implementado. La interfaz provisional está en
/// <c>IDistributivoGuard</c>; la implementación llega después.
/// </summary>
[TestClass]
public sealed class DistributivoGuardTests
{
    private const string IdProfesorDuenio = "0000000001";
    private const string IdProfesorAjeno = "0000000002";
    private const int IdAsignacion = 100;

    private static sigafi_esContext CrearContexto(string nombreDb)
    {
        var opciones = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new sigafi_esContext(opciones);
    }

    private static IDistributivoGuard CrearGuard(sigafi_esContext db) =>
        new DistributivoGuard(db);

    // ---------------------------------------------------------------------
    // Verificar (sin lanzar)
    // ---------------------------------------------------------------------

    [TestMethod]
    public async Task Verificar_DocenteDuenio_ActivoEs1EsActiva1_True()
    {
        using var db = CrearContexto(nameof(Verificar_DocenteDuenio_ActivoEs1EsActiva1_True));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorDuenio).Activo(1).EsActivaAsignacion(1).Build());
        await db.SaveChangesAsync();

        var ok = await CrearGuard(db).DocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion);

        ok.Should().BeTrue();
    }

    [TestMethod]
    public async Task Verificar_DocenteDuenio_ActivoNullEsActiva1_True()
    {
        using var db = CrearContexto(nameof(Verificar_DocenteDuenio_ActivoNullEsActiva1_True));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorDuenio).Activo(null).EsActivaAsignacion(1).Build());
        await db.SaveChangesAsync();

        var ok = await CrearGuard(db).DocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion);

        ok.Should().BeTrue();
    }

    [TestMethod]
    public async Task Verificar_DocenteDuenio_Activo1EsActivaNull_True()
    {
        using var db = CrearContexto(nameof(Verificar_DocenteDuenio_Activo1EsActivaNull_True));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorDuenio).Activo(1).EsActivaAsignacion(null).Build());
        await db.SaveChangesAsync();

        var ok = await CrearGuard(db).DocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion);

        ok.Should().BeTrue();
    }

    [TestMethod]
    public async Task Verificar_DocenteDuenio_Activo0_False()
    {
        using var db = CrearContexto(nameof(Verificar_DocenteDuenio_Activo0_False));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorDuenio).Activo(0).EsActivaAsignacion(1).Build());
        await db.SaveChangesAsync();

        var ok = await CrearGuard(db).DocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion);

        ok.Should().BeFalse();
    }

    [TestMethod]
    public async Task Verificar_DocenteDuenio_EsActivaAsignacion0_False()
    {
        using var db = CrearContexto(nameof(Verificar_DocenteDuenio_EsActivaAsignacion0_False));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorDuenio).Activo(1).EsActivaAsignacion(0).Build());
        await db.SaveChangesAsync();

        var ok = await CrearGuard(db).DocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion);

        ok.Should().BeFalse();
    }

    [TestMethod]
    public async Task Verificar_OtraPersonaDuenia_False()
    {
        using var db = CrearContexto(nameof(Verificar_OtraPersonaDuenia_False));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorAjeno).Activo(1).EsActivaAsignacion(1).Build());
        await db.SaveChangesAsync();

        var ok = await CrearGuard(db).DocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion);

        ok.Should().BeFalse();
    }

    [TestMethod]
    public async Task Verificar_AsignacionInexistente_False()
    {
        using var db = CrearContexto(nameof(Verificar_AsignacionInexistente_False));

        var ok = await CrearGuard(db).DocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion);

        ok.Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // Ensure (lanza DistributivoAjenoException si falla)
    // ---------------------------------------------------------------------

    [TestMethod]
    public async Task Ensure_DocenteNoDuenio_LanzaDistributivoAjeno()
    {
        using var db = CrearContexto(nameof(Ensure_DocenteNoDuenio_LanzaDistributivoAjeno));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorAjeno).Build());
        await db.SaveChangesAsync();

        var act = async () => await CrearGuard(db).EnsureDocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion, esInspector: false);

        await act.Should().ThrowAsync<DistributivoAjenoException>();
    }

    [TestMethod]
    public async Task Ensure_DocenteNoDuenio_Propaga403NoEnmascarado()
    {
        // El guard lanza DistributivoAjenoException; ApiExceptionMiddleware lo
        // mapea a 403 con codigo "DISTRIBUTIVO_AJENO". Acá verificamos el
        // shape de la excepción (Codigo del ProhibidoException base).
        using var db = CrearContexto(nameof(Ensure_DocenteNoDuenio_Propaga403NoEnmascarado));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorAjeno).Build());
        await db.SaveChangesAsync();

        var act = async () => await CrearGuard(db).EnsureDocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion, esInspector: false);

        var ex = await act.Should().ThrowAsync<DistributivoAjenoException>();
        ex.Which.Codigo.Should().Be("DISTRIBUTIVO_AJENO");
        ex.Which.Should().BeAssignableTo<ProhibidoException>();
    }

    [TestMethod]
    public async Task Ensure_DocenteDuenio_NoLanza()
    {
        using var db = CrearContexto(nameof(Ensure_DocenteDuenio_NoLanza));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorDuenio).Build());
        await db.SaveChangesAsync();

        var act = async () => await CrearGuard(db).EnsureDocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion, esInspector: false);

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Ensure_Inspector_True_NuncaConsultaBD()
    {
        // Si esInspector=true, el guard debe retornar sin tocar la BD.
        // Se valida por comportamiento: ausencia de fila y bypass activo
        // devuelven sin lanzar. (Mockear el DbSet completo rompe la
        // inicialización de EF Core Power Tools en runtime.)
        using var db = CrearContexto(nameof(Ensure_Inspector_True_NuncaConsultaBD));

        var act = async () => await CrearGuard(db).EnsureDocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion, esInspector: true);

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Ensure_Inspector_True_InclusoSiAsignacionNoExiste_NoLanza()
    {
        using var db = CrearContexto(nameof(Ensure_Inspector_True_InclusoSiAsignacionNoExiste_NoLanza));

        var act = async () => await CrearGuard(db).EnsureDocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion, esInspector: true);

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Ensure_InspectorFalse_DocenteNoDuenio_Lanza()
    {
        // Confirmación: si el controller olvida pasar el flag o el JWT no
        // trae el rol, esInspector=false y el docente no entra a datos ajenos.
        using var db = CrearContexto(nameof(Ensure_InspectorFalse_DocenteNoDuenio_Lanza));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorAjeno).Build());
        await db.SaveChangesAsync();

        var act = async () => await CrearGuard(db).EnsureDocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion, esInspector: false);

        await act.Should().ThrowAsync<DistributivoAjenoException>();
    }

    [TestMethod]
    public async Task Ensure_IdProfesorVacio_LanzaUnauthorizedAccess()
    {
        // El idProfesor sale del claim sub; si viene vacío no podemos
        // autorizar. Debe lanzar UNAUTH (no filtrar info).
        using var db = CrearContexto(nameof(Ensure_IdProfesorVacio_LanzaUnauthorizedAccess));

        var act = async () => await CrearGuard(db).EnsureDocenteTieneAsignacionAsync("", IdAsignacion, esInspector: false);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task Ensure_PropagaCancellationToken()
    {
        using var db = CrearContexto(nameof(Ensure_PropagaCancellationToken));
        db.asignaciones_profesores.Add(new AsignacionBuilder().DelProfesor(IdProfesorDuenio).Build());
        await db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await CrearGuard(db).EnsureDocenteTieneAsignacionAsync(IdProfesorDuenio, IdAsignacion, esInspector: false, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}