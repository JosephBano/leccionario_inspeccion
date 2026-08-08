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
