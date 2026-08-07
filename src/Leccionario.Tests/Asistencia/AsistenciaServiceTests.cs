using FluentAssertions;
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Leccionario.Tests.Asistencia;

[TestClass]
public sealed class AsistenciaServiceTests
{
    private static sigafi_esContext CrearContexto(string nombre) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombre)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static (sigafi_esContext db, int idSesion) Sembrar(string nombreDb)
    {
        var db = CrearContexto(nombreDb);
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
        db.matriculas.AddRange(
            new matriculas { idMatricula = 1, idAlumno = "ALU1", idPeriodo = "TEST0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A" },
            new matriculas { idMatricula = 2, idAlumno = "ALU2", idPeriodo = "TEST0001", idNivel = 35, idSeccion = 1, idModalidad = 1, paralelo = "A" }
        );
        db.fechas_horarios.Add(new fechas_horarios { idFecha = 100, fecha = new DateOnly(2026, 8, 7) });
        db.cplec_sesiones.Add(new cplec_sesiones
        {
            idSesion = 1,
            idAsignacion = 100,
            idFecha = 100,
            numeroBloque = 1,
            tema = "Tema",
            estado = "borrador",
            activo = true,
            usuarioCreacion = "0000000001",
            fechaCreacion = DateTime.UtcNow
        });
        db.SaveChanges();
        return (db, 1);
    }

    [TestMethod]
    public async Task Registrar_EstadoInvalido_LanzaValidacion()
    {
        var (db, idSesion) = Sembrar(nameof(Registrar_EstadoInvalido_LanzaValidacion));
        var guard = new Mock<IDistributivoGuard>();

        var act = async () => await new AsistenciaService(db, guard.Object)
            .RegistrarAsync(idSesion, "0000000001", false, "cplec_docente", "127.0.0.1",
                new RegistrarAsistenciaRequestDto
                {
                    Marcas = new[] { new MarcaAsistenciaDto { IdMatricula = 1, Estado = "invalid" } }
                }, default);

        await act.Should().ThrowAsync<ValidacionException>();
    }

    [TestMethod]
    public async Task Registrar_AtrasoSinMinutos_LanzaValidacion()
    {
        var (db, idSesion) = Sembrar(nameof(Registrar_AtrasoSinMinutos_LanzaValidacion));
        var guard = new Mock<IDistributivoGuard>();

        var act = async () => await new AsistenciaService(db, guard.Object)
            .RegistrarAsync(idSesion, "0000000001", false, "cplec_docente", "127.0.0.1",
                new RegistrarAsistenciaRequestDto
                {
                    Marcas = new[] { new MarcaAsistenciaDto { IdMatricula = 1, Estado = "atraso" } }
                }, default);

        await act.Should().ThrowAsync<ValidacionException>();
    }

    [TestMethod]
    public async Task Registrar_MatriculaAjena_LanzaValidacion()
    {
        var (db, idSesion) = Sembrar(nameof(Registrar_MatriculaAjena_LanzaValidacion));
        var guard = new Mock<IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var act = async () => await new AsistenciaService(db, guard.Object)
            .RegistrarAsync(idSesion, "0000000001", false, "cplec_docente", "127.0.0.1",
                new RegistrarAsistenciaRequestDto
                {
                    Marcas = new[] { new MarcaAsistenciaDto { IdMatricula = 999, Estado = "presente" } }
                }, default);

        await act.Should().ThrowAsync<ValidacionException>()
            .Where(e => e.Codigo == "MATRICULA_AJENA" || (e.Message != null && e.Message.Contains("MATRICULA_AJENA")));
    }

    [TestMethod]
    public async Task Registrar_DocenteNoDuenio_LanzaDistributivoAjeno()
    {
        var (db, idSesion) = Sembrar(nameof(Registrar_DocenteNoDuenio_LanzaDistributivoAjeno));
        var guard = new Mock<IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new DistributivoAjenoException());

        var act = async () => await new AsistenciaService(db, guard.Object)
            .RegistrarAsync(idSesion, "0000000001", false, "cplec_docente", "127.0.0.1",
                new RegistrarAsistenciaRequestDto
                {
                    Marcas = new[] { new MarcaAsistenciaDto { IdMatricula = 1, Estado = "presente" } }
                }, default);

        await act.Should().ThrowAsync<DistributivoAjenoException>();
    }

    [TestMethod]
    public async Task Registrar_SesionCerrada_LanzaSesionCerrada()
    {
        var (db, idSesion) = Sembrar(nameof(Registrar_SesionCerrada_LanzaSesionCerrada));
        db.cplec_sesiones.First().estado = "cerrada";
        db.SaveChanges();
        var guard = new Mock<IDistributivoGuard>();

        var act = async () => await new AsistenciaService(db, guard.Object)
            .RegistrarAsync(idSesion, "0000000001", false, "cplec_docente", "127.0.0.1",
                new RegistrarAsistenciaRequestDto
                {
                    Marcas = new[] { new MarcaAsistenciaDto { IdMatricula = 1, Estado = "presente" } }
                }, default);

        await act.Should().ThrowAsync<SesionCerradaException>();
    }

    [TestMethod]
    public async Task Registrar_MarcaNueva_CreaFila()
    {
        var (db, idSesion) = Sembrar(nameof(Registrar_MarcaNueva_CreaFila));
        var guard = new Mock<IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var sesion = await new AsistenciaService(db, guard.Object)
            .RegistrarAsync(idSesion, "0000000001", false, "cplec_docente", "127.0.0.1",
                new RegistrarAsistenciaRequestDto
                {
                    Marcas = new[]
                    {
                        new MarcaAsistenciaDto { IdMatricula = 1, Estado = "presente" },
                        new MarcaAsistenciaDto { IdMatricula = 2, Estado = "ausente", Observacion = "No vino" }
                    }
                }, default);

        sesion.Asistencias.Should().HaveCount(2);
        sesion.Asistencias.Should().Contain(m => m.IdMatricula == 1 && m.Estado == "presente");
        sesion.Asistencias.Should().Contain(m => m.IdMatricula == 2 && m.Estado == "ausente");
    }

    [TestMethod]
    public async Task Registrar_MarcaExistente_Idempotente_NoGeneraHistorial()
    {
        var (db, idSesion) = Sembrar(nameof(Registrar_MarcaExistente_Idempotente_NoGeneraHistorial));
        // Pre-cargar marca existente.
        db.cplec_asistencias.Add(new cplec_asistencias
        {
            idSesion = idSesion,
            idMatricula = 1,
            estado = "presente",
            usuarioCreacion = "0000000001",
            fechaCreacion = DateTime.UtcNow
        });
        db.SaveChanges();
        var guard = new Mock<IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        await new AsistenciaService(db, guard.Object)
            .RegistrarAsync(idSesion, "0000000001", false, "cplec_docente", "127.0.0.1",
                new RegistrarAsistenciaRequestDto
                {
                    Marcas = new[] { new MarcaAsistenciaDto { IdMatricula = 1, Estado = "presente" } }
                }, default);

        db.cplec_asistencias_historial.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Registrar_CambioEstado_GeneraHistorial()
    {
        var (db, idSesion) = Sembrar(nameof(Registrar_CambioEstado_GeneraHistorial));
        db.cplec_asistencias.Add(new cplec_asistencias
        {
            idSesion = idSesion,
            idMatricula = 1,
            estado = "presente",
            usuarioCreacion = "0000000001",
            fechaCreacion = DateTime.UtcNow
        });
        db.SaveChanges();
        var guard = new Mock<IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        await new AsistenciaService(db, guard.Object)
            .RegistrarAsync(idSesion, "0000000001", false, "cplec_docente", "127.0.0.1",
                new RegistrarAsistenciaRequestDto
                {
                    Marcas = new[] { new MarcaAsistenciaDto { IdMatricula = 1, Estado = "ausente", Observacion = "Error" } },
                    Motivo = "Corrección"
                }, default);

        db.cplec_asistencias_historial.Should().HaveCount(1);
        db.cplec_asistencias_historial.First().estadoAnterior.Should().Be("presente");
        db.cplec_asistencias_historial.First().estadoNuevo.Should().Be("ausente");
        db.cplec_asistencias_historial.First().motivo.Should().Be("Corrección");
    }
}