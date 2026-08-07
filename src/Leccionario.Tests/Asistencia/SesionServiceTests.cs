using FluentAssertions;
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Leccionario.Tests.Asistencia;

[TestClass]
public sealed class SesionServiceTests
{
    private static sigafi_esContext CrearContexto(string nombre) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombre)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static (sigafi_esContext db, Mock<IDistributivoGuard> guard, Mock<INominaAlumnosService> nomina)
        Preparar(string nombreDb)
    {
        var db = CrearContexto(nombreDb);
        var guard = new Mock<IDistributivoGuard>();
        var nomina = new Mock<INominaAlumnosService>();
        // Por defecto, nomina devuelve lista vacía si el test no la sobrescribe.
        nomina.Setup(n => n.ResolverAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<AlumnoNominaDto>());
        return (db, guard, nomina);
    }

    private static void SembrarAsignacion(sigafi_esContext db, int idAsignacion = 100, DateOnly? desde = null, DateOnly? hasta = null)
    {
        db.asignaciones_profesores.Add(new asignaciones_profesores
        {
            idAsignacion = idAsignacion,
            idProfesor = "0000000001",
            idAsignatura = 1,
            idPeriodo = "TEST0001",
            idNivel = 35,
            idSeccion = 1,
            idModalidad = 1,
            paralelo = "A",
            activo = 1,
            esActivaAsignacion = 1,
            fecha_inicial = desde ?? new DateOnly(2026, 1, 1),
            fecha_fin = hasta ?? new DateOnly(2026, 12, 31)
        });
        db.fechas_horarios.Add(new fechas_horarios { idFecha = 100, fecha = new DateOnly(2026, 8, 7) });
        db.SaveChanges();
    }

    [TestMethod]
    public async Task Crear_FechaValida_CreaSesionConNomina()
    {
        var (db, guard, nomina) = Preparar(nameof(Crear_FechaValida_CreaSesionConNomina));
        SembrarAsignacion(db);
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync("0000000001", 100, false, It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        nomina.Setup(n => n.ResolverAsync(100, "0000000001", false, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<AlumnoNominaDto>
              {
                  new() { IdMatricula = 1, IdAlumno = "ALU1", Apellidos = "P", Nombres = "J", Retirado = false, EsOyente = false },
                  new() { IdMatricula = 2, IdAlumno = "ALU2", Apellidos = "G", Nombres = "A", Retirado = false, EsOyente = false }
              });

        var sesion = await new SesionService(db, guard.Object, nomina.Object)
            .CrearAsync(100, "0000000001", false,
                new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 7), Tema = "Educación vial" }, default);

        sesion.Tema.Should().Be("Educación vial");
        sesion.Asistencias.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Crear_FechaFueraDeVentana_LanzaFueraDeVentana()
    {
        var (db, guard, nomina) = Preparar(nameof(Crear_FechaFueraDeVentana_LanzaFueraDeVentana));
        SembrarAsignacion(db, desde: new DateOnly(2026, 9, 1), hasta: new DateOnly(2026, 12, 31));

        var act = async () => await new SesionService(db, guard.Object, nomina.Object)
            .CrearAsync(100, "0000000001", false,
                new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 7), Tema = "X" }, default);

        await act.Should().ThrowAsync<FueraDeVentanaException>();
    }

    [TestMethod]
    public async Task Crear_TemaVacio_LanzaValidacion()
    {
        var (db, guard, nomina) = Preparar(nameof(Crear_TemaVacio_LanzaValidacion));
        SembrarAsignacion(db);

        var act = async () => await new SesionService(db, guard.Object, nomina.Object)
            .CrearAsync(100, "0000000001", false,
                new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 7), Tema = "" }, default);

        await act.Should().ThrowAsync<ValidacionException>();
    }

    [TestMethod]
    public async Task Crear_FechaNoExisteEnCalendario_LanzaValidacion()
    {
        var (db, guard, nomina) = Preparar(nameof(Crear_FechaNoExisteEnCalendario_LanzaValidacion));
        SembrarAsignacion(db); // solo carga 2026-08-07

        var act = async () => await new SesionService(db, guard.Object, nomina.Object)
            .CrearAsync(100, "0000000001", false,
                new CrearSesionRequestDto { Fecha = new DateOnly(2030, 1, 1), Tema = "X" }, default);

        await act.Should().ThrowAsync<ValidacionException>();
    }

    [TestMethod]
    public async Task Crear_TuplaExistente_IdempotenteDevuelveMismaSesion()
    {
        var (db, guard, nomina) = Preparar(nameof(Crear_TuplaExistente_IdempotenteDevuelveMismaSesion));
        SembrarAsignacion(db);
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var svc = new SesionService(db, guard.Object, nomina.Object);
        var s1 = await svc.CrearAsync(100, "0000000001", false, new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 7), Tema = "A" }, default);
        var s2 = await svc.CrearAsync(100, "0000000001", false, new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 7), Tema = "A" }, default);

        s1.IdSesion.Should().Be(s2.IdSesion);
    }

    [TestMethod]
    public async Task Cerrar_DocenteDuenio_CambiaEstadoYCierra()
    {
        var (db, guard, nomina) = Preparar(nameof(Cerrar_DocenteDuenio_CambiaEstadoYCierra));
        SembrarAsignacion(db);
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync("0000000001", 100, false, It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var svc = new SesionService(db, guard.Object, nomina.Object);
        var s = await svc.CrearAsync(100, "0000000001", false, new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 7), Tema = "A" }, default);

        await svc.CerrarAsync(s.IdSesion, "0000000001", false, default);

        var sesionCerrada = await svc.ObtenerAsync(s.IdSesion, default);
        sesionCerrada.Estado.Should().Be("cerrada");
    }

    [TestMethod]
    public async Task Reabrir_DocenteNoPermitido_Lanza403()
    {
        var (db, guard, nomina) = Preparar(nameof(Reabrir_DocenteNoPermitido_Lanza403));
        SembrarAsignacion(db);
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var svc = new SesionService(db, guard.Object, nomina.Object);
        var s = await svc.CrearAsync(100, "0000000001", false, new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 7), Tema = "A" }, default);
        await svc.CerrarAsync(s.IdSesion, "0000000001", false, default);

        var act = async () => await svc.ReabrirAsync(s.IdSesion, "0000000001", false,
            new ReabrirSesionRequestDto { Motivo = "x" }, default);

        await act.Should().ThrowAsync<ProhibidoException>();
    }

    [TestMethod]
    public async Task Reabrir_Inspector_ConMotivo_CambiaEstado()
    {
        var (db, guard, nomina) = Preparar(nameof(Reabrir_Inspector_ConMotivo_CambiaEstado));
        SembrarAsignacion(db);
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var svc = new SesionService(db, guard.Object, nomina.Object);
        var s = await svc.CrearAsync(100, "0000000001", false, new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 7), Tema = "A" }, default);
        await svc.CerrarAsync(s.IdSesion, "0000000001", false, default);

        await svc.ReabrirAsync(s.IdSesion, "0000000099", true, new ReabrirSesionRequestDto { Motivo = "Corrección" }, default);

        var sesion = await svc.ObtenerAsync(s.IdSesion, default);
        sesion.Estado.Should().Be("borrador");
    }
}