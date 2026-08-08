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
