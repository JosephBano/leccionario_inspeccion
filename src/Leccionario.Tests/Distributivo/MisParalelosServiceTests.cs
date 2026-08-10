using FluentAssertions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Leccionario.Tests.Distributivo;

[TestClass]
public sealed class MisParalelosServiceTests
{
    private static sigafi_esContext CrearContexto(string nombre) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombre)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DateOnly Hoy => new(2026, 8, 7);

    private static void SembrarAsignacion(sigafi_esContext db, DateOnly? fechaInicial, DateOnly? fechaFin)
    {
        db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.asignaturas.Add(new asignaturas { idAsignatura = 1, asignatura = "Legislación" });
        db.secciones.Add(new secciones { idSeccion = 1, seccion = "Matutina" });
        db.modalidades.Add(new modalidades { idModalidad = 1, modalidad = "Presencial" });
        db.asignaciones_profesores.Add(new asignaciones_profesores
        {
            idAsignacion = 1,
            idProfesor = "0000000001",
            idAsignatura = 1,
            idPeriodo = "TEST0001",
            idNivel = 35,
            idSeccion = 1,
            idModalidad = 1,
            paralelo = "A",
            activo = 1,
            esActivaAsignacion = 1,
            fecha_inicial = fechaInicial,
            fecha_fin = fechaFin
        });
    }

    [TestMethod]
    public async Task Resolver_ParaleloDentroDeVentana_SeIncluye()
    {
        using var db = CrearContexto(nameof(Resolver_ParaleloDentroDeVentana_SeIncluye));
        SembrarAsignacion(db, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31));
        await db.SaveChangesAsync();

        var items = await new MisParalelosService(db).ResolverAsync("0000000001", null, Hoy);

        items.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Resolver_ParaleloTerminadoHace15Dias_SeIncluye()
    {
        using var db = CrearContexto(nameof(Resolver_ParaleloTerminadoHace15Dias_SeIncluye));
        SembrarAsignacion(db, new DateOnly(2026, 1, 1), Hoy.AddDays(-15));
        await db.SaveChangesAsync();

        var items = await new MisParalelosService(db).ResolverAsync("0000000001", null, Hoy);

        items.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Resolver_ParaleloTerminadoHace16Dias_NoSeIncluye()
    {
        using var db = CrearContexto(nameof(Resolver_ParaleloTerminadoHace16Dias_NoSeIncluye));
        SembrarAsignacion(db, new DateOnly(2026, 1, 1), Hoy.AddDays(-16));
        await db.SaveChangesAsync();

        var items = await new MisParalelosService(db).ResolverAsync("0000000001", null, Hoy);

        items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Resolver_ParaleloFuturo_NoSeIncluye()
    {
        using var db = CrearContexto(nameof(Resolver_ParaleloFuturo_NoSeIncluye));
        SembrarAsignacion(db, Hoy.AddDays(1), Hoy.AddDays(30));
        await db.SaveChangesAsync();

        var items = await new MisParalelosService(db).ResolverAsync("0000000001", null, Hoy);

        items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Resolver_ModuloTerminadoHace16Dias_PeroPeriodoVigente_SeIncluye()
    {
        using var db = CrearContexto(nameof(Resolver_ModuloTerminadoHace16Dias_PeroPeriodoVigente_SeIncluye));
        SembrarAsignacion(db, new DateOnly(2026, 6, 25), Hoy.AddDays(-17));
        db.periodos.Add(new periodos
        {
            idPeriodo = "TEST0001",
            fecha_inicial = new DateOnly(2026, 3, 16),
            fecha_final = new DateOnly(2026, 9, 30)
        });
        await db.SaveChangesAsync();

        var items = await new MisParalelosService(db).ResolverAsync("0000000001", null, Hoy);

        items.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Resolver_IdPeriodoExplicito_BypassaFiltroVencimiento()
    {
        using var db = CrearContexto(nameof(Resolver_IdPeriodoExplicito_BypassaFiltroVencimiento));
        SembrarAsignacion(db, new DateOnly(2026, 1, 1), Hoy.AddDays(-30));
        await db.SaveChangesAsync();

        var items = await new MisParalelosService(db).ResolverAsync("0000000001", "TEST0001", Hoy);

        items.Should().HaveCount(1);
    }
}
