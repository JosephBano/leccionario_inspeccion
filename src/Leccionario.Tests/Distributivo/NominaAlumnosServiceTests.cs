using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Leccionario.Tests.Distributivo;

[TestClass]
public sealed class NominaAlumnosServiceTests
{
    private static sigafi_esContext CrearContexto(string nombre) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombre)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [TestMethod]
    public async Task Resolver_DocenteDuenio_DevuelveNominas()
    {
        using var db = CrearContexto(nameof(Resolver_DocenteDuenio_DevuelveNominas));
        db.asignaciones_profesores.Add(new asignaciones_profesores
        {
            idAsignacion = 100,
            idProfesor = "0000000001",
            idAsignatura = 1,
            idPeriodo = "TEST0001",
            idNivel = 35,
            idSeccion = 1,
            idModalidad = 1,
            paralelo = "A",
            activo = 1,
            esActivaAsignacion = 1
        });
        db.alumnos.AddRange(
            new alumnos { idAlumno = "ALU0001", apellidoPaterno = "PEREZ", apellidoMaterno = "LOPEZ", primerNombre = "JUAN", segundoNombre = "CARLOS" },
            new alumnos { idAlumno = "ALU0002", apellidoPaterno = "GOMEZ", apellidoMaterno = "RUIZ", primerNombre = "ANA" }
        );
        db.matriculas.AddRange(
            new matriculas { idMatricula = 1, idAlumno = "ALU0001", idPeriodo = "TEST0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A" },
            new matriculas { idMatricula = 2, idAlumno = "ALU0002", idPeriodo = "TEST0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A" }
        );
        await db.SaveChangesAsync();

        var guard = new Mock<Leccionario.Api.Application.Asistencia.Services.IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), 100, false, It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var items = await new NominaAlumnosService(db, guard.Object).ResolverAsync(100, "0000000001", false);

        items.Should().HaveCount(2);
        items.Should().Contain(i => i.IdMatricula == 1 && i.Apellidos == "PEREZ LOPEZ" && i.Nombres == "JUAN CARLOS");
        items.Should().Contain(i => i.IdMatricula == 2 && i.Apellidos == "GOMEZ RUIZ");
    }

    [TestMethod]
    public async Task Resolver_DocenteNoDuenio_LanzaDistributivoAjeno()
    {
        using var db = CrearContexto(nameof(Resolver_DocenteNoDuenio_LanzaDistributivoAjeno));
        var guard = new Mock<Leccionario.Api.Application.Asistencia.Services.IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), 100, false, It.IsAny<CancellationToken>()))
             .ThrowsAsync(new DistributivoAjenoException());

        var act = async () => await new NominaAlumnosService(db, guard.Object).ResolverAsync(100, "0000000001", false);

        await act.Should().ThrowAsync<DistributivoAjenoException>();
    }

    [TestMethod]
    public async Task Resolver_Inspector_NoPasaPorGuard()
    {
        using var db = CrearContexto(nameof(Resolver_Inspector_NoPasaPorGuard));
        var guard = new Mock<Leccionario.Api.Application.Asistencia.Services.IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), 100, true, It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var act = async () => await new NominaAlumnosService(db, guard.Object).ResolverAsync(100, null, true);

        // Como no hay asignación, lanza NoEncontrado (después del guard, no por el guard).
        await act.Should().ThrowAsync<NoEncontradoException>();
        guard.Verify(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), 100, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Resolver_AsignacionInexistente_NoEncontrado()
    {
        using var db = CrearContexto(nameof(Resolver_AsignacionInexistente_NoEncontrado));
        var guard = new Mock<Leccionario.Api.Application.Asistencia.Services.IDistributivoGuard>();

        var act = async () => await new NominaAlumnosService(db, guard.Object).ResolverAsync(999, null, true);

        await act.Should().ThrowAsync<NoEncontradoException>();
    }

    [TestMethod]
    public async Task Resolver_ExcluyeRetirados()
    {
        using var db = CrearContexto(nameof(Resolver_ExcluyeRetirados));
        db.asignaciones_profesores.Add(new asignaciones_profesores
        {
            idAsignacion = 100,
            idProfesor = "0000000001",
            idAsignatura = 1,
            idPeriodo = "TEST0001",
            idNivel = 35,
            idSeccion = 1,
            idModalidad = 1,
            paralelo = "A",
            activo = 1,
            esActivaAsignacion = 1
        });
        db.alumnos.AddRange(
            new alumnos { idAlumno = "ALU0001", apellidoPaterno = "PEREZ", primerNombre = "JUAN" },
            new alumnos { idAlumno = "ALU0002", apellidoPaterno = "GOMEZ", primerNombre = "ANA" }
        );
        db.matriculas.AddRange(
            new matriculas { idMatricula = 1, idAlumno = "ALU0001", idPeriodo = "TEST0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A", retirado = false },
            new matriculas { idMatricula = 2, idAlumno = "ALU0002", idPeriodo = "TEST0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A", retirado = true }
        );
        await db.SaveChangesAsync();

        var guard = new Mock<Leccionario.Api.Application.Asistencia.Services.IDistributivoGuard>();
        var items = await new NominaAlumnosService(db, guard.Object).ResolverAsync(100, "0000000001", false);

        items.Should().HaveCount(1);
        items[0].IdMatricula.Should().Be(1);
    }
}