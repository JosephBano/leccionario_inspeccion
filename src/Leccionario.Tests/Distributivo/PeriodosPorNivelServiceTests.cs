using FluentAssertions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Leccionario.Tests.Distributivo;

[TestClass]
public sealed class PeriodosPorNivelServiceTests
{
    private static sigafi_esContext CrearContexto(string nombre) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombre)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DateOnly Ref => new(2026, 8, 7);

    [TestMethod]
    public async Task Resolver_DocenteSinAsignaciones_ListaVacia()
    {
        using var db = CrearContexto(nameof(Resolver_DocenteSinAsignaciones_ListaVacia));

        var items = await new PeriodosPorNivelService(db).ResolverAsync("0000000001", Ref);

        items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Resolver_UnaAsignacion_Carrera6_DevuelveUnaFila()
    {
        using var db = CrearContexto(nameof(Resolver_UnaAsignacion_Carrera6_DevuelveUnaFila));
        db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.periodos.Add(new periodos { idPeriodo = "TEST0001", fecha_inicial = new DateOnly(2026, 1, 1), fecha_final = new DateOnly(2026, 12, 31) });
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
            fecha_inicial = new DateOnly(2026, 1, 1),
            fecha_fin = new DateOnly(2026, 12, 31)
        });
        await db.SaveChangesAsync();

        var items = await new PeriodosPorNivelService(db).ResolverAsync(null, Ref);

        items.Should().HaveCount(1);
        items[0].IdNivel.Should().Be(35);
        items[0].TipoLicencia.Should().Be("TIPO \"C\"");
        items[0].IdPeriodo.Should().Be("TEST0001");
        items[0].Vigencia.Should().Be("VIGENTE");
        items[0].Asignaciones.Should().Be(1);
        items[0].Docentes.Should().Be(1);
    }

    [TestMethod]
    public async Task Resolver_DosNiveles_MismoPeriodo_FueraCarrera_DevuelveSoloCarrera6()
    {
        using var db = CrearContexto(nameof(Resolver_DosNiveles_MismoPeriodo_FueraCarrera_DevuelveSoloCarrera6));
        db.cursos.AddRange(
            new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" },
            new cursos { idNivel = 99, idCarrera = 9, Nivel = "OTRA" });
        db.periodos.Add(new periodos { idPeriodo = "TEST0001" });
        db.asignaciones_profesores.AddRange(
            new asignaciones_profesores { idAsignacion = 1, idProfesor = "0000000001", idAsignatura = 1, idPeriodo = "TEST0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A", activo = 1, esActivaAsignacion = 1 },
            new asignaciones_profesores { idAsignacion = 2, idProfesor = "0000000001", idAsignatura = 1, idPeriodo = "TEST0001", idNivel = 99, idSeccion = 1, idModalidad = 1, paralelo = "A", activo = 1, esActivaAsignacion = 1 }
        );
        await db.SaveChangesAsync();

        var items = await new PeriodosPorNivelService(db).ResolverAsync(null, Ref);

        items.Should().HaveCount(1);
        items[0].IdNivel.Should().Be(35);
    }

    [TestMethod]
    public async Task Resolver_PeriodoCerrado_VigenciaCERRADO()
    {
        using var db = CrearContexto(nameof(Resolver_PeriodoCerrado_VigenciaCERRADO));
        db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.periodos.Add(new periodos { idPeriodo = "OLD0001" });
        db.asignaciones_profesores.Add(new asignaciones_profesores
        {
            idAsignacion = 1,
            idProfesor = "0000000001",
            idAsignatura = 1,
            idPeriodo = "OLD0001",
            idNivel = 35,
            idSeccion = 1,
            idModalidad = 1,
            paralelo = "A",
            activo = 1,
            esActivaAsignacion = 1,
            fecha_inicial = new DateOnly(2024, 1, 1),
            fecha_fin = new DateOnly(2024, 12, 31)
        });
        await db.SaveChangesAsync();

        var items = await new PeriodosPorNivelService(db).ResolverAsync(null, Ref);

        items.Should().HaveCount(1);
        items[0].Vigencia.Should().Be("CERRADO");
    }

    [TestMethod]
    public async Task Resolver_PeriodoFuturo_VigenciaFUTURO()
    {
        using var db = CrearContexto(nameof(Resolver_PeriodoFuturo_VigenciaFUTURO));
        db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.periodos.Add(new periodos { idPeriodo = "FUT0001" });
        db.asignaciones_profesores.Add(new asignaciones_profesores
        {
            idAsignacion = 1,
            idProfesor = "0000000001",
            idAsignatura = 1,
            idPeriodo = "FUT0001",
            idNivel = 35,
            idSeccion = 1,
            idModalidad = 1,
            paralelo = "A",
            activo = 1,
            esActivaAsignacion = 1,
            fecha_inicial = new DateOnly(2027, 1, 1),
            fecha_fin = new DateOnly(2027, 12, 31)
        });
        await db.SaveChangesAsync();

        var items = await new PeriodosPorNivelService(db).ResolverAsync(null, Ref);

        items[0].Vigencia.Should().Be("FUTURO");
    }

    [TestMethod]
    public async Task Resolver_MismoNivelDosPeriodos_TomaElMasReciente()
    {
        using var db = CrearContexto(nameof(Resolver_MismoNivelDosPeriodos_TomaElMasReciente));
        db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.periodos.AddRange(
            new periodos { idPeriodo = "OLD0001" },
            new periodos { idPeriodo = "NEW0001" });
        db.asignaciones_profesores.AddRange(
            new asignaciones_profesores { idAsignacion = 1, idProfesor = "0000000001", idAsignatura = 1, idPeriodo = "OLD0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A", activo = 1, esActivaAsignacion = 1, fecha_inicial = new DateOnly(2024, 1, 1), fecha_fin = new DateOnly(2024, 12, 31) },
            new asignaciones_profesores { idAsignacion = 2, idProfesor = "0000000001", idAsignatura = 1, idPeriodo = "NEW0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A", activo = 1, esActivaAsignacion = 1, fecha_inicial = new DateOnly(2026, 1, 1), fecha_fin = new DateOnly(2026, 12, 31) }
        );
        await db.SaveChangesAsync();

        var items = await new PeriodosPorNivelService(db).ResolverAsync(null, Ref);

        items.Should().HaveCount(1);
        items[0].IdPeriodo.Should().Be("NEW0001");
    }

    [TestMethod]
    public async Task Resolver_DocenteEspecifico_SoloSusNiveles()
    {
        using var db = CrearContexto(nameof(Resolver_DocenteEspecifico_SoloSusNiveles));
        db.cursos.AddRange(
            new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" },
            new cursos { idNivel = 36, idCarrera = 6, Nivel = "TIPO \"D\"" });
        db.periodos.AddRange(
            new periodos { idPeriodo = "TEST0001" },
            new periodos { idPeriodo = "TEST0002" });
        db.asignaciones_profesores.AddRange(
            new asignaciones_profesores { idAsignacion = 1, idProfesor = "0000000001", idAsignatura = 1, idPeriodo = "TEST0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A", activo = 1, esActivaAsignacion = 1, fecha_inicial = new DateOnly(2026, 1, 1), fecha_fin = new DateOnly(2026, 12, 31) },
            new asignaciones_profesores { idAsignacion = 2, idProfesor = "0000000002", idAsignatura = 1, idPeriodo = "TEST0002", idNivel = 36, idSeccion = 1, idModalidad = 1, paralelo = "A", activo = 1, esActivaAsignacion = 1, fecha_inicial = new DateOnly(2026, 1, 1), fecha_fin = new DateOnly(2026, 12, 31) }
        );
        await db.SaveChangesAsync();

        var items = await new PeriodosPorNivelService(db).ResolverAsync("0000000001", Ref);

        items.Should().HaveCount(1);
        items[0].IdNivel.Should().Be(35);
    }
}