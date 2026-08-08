using FluentAssertions;
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Leccionario.Tests.Asistencia;

[TestClass]
public sealed class SesionServiceTests
{
    private const int Asig100 = 100;
    private const string Duenio = "0000000001";

    /// <summary>Reloj fijo, para poder afirmar sobre `diasRetraso`.</summary>
    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

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

    private static SesionService CrearServicio(sigafi_esContext db, TimeProvider? reloj = null)
    {
        var guard = new Mock<IDistributivoGuard>();
        guard.Setup(g => g.EnsureDocenteTieneAsignacionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var nomina = new Mock<INominaAlumnosService>();
        nomina.Setup(n => n.ResolverAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<AlumnoNominaDto>());
        return new SesionService(db, guard.Object, nomina.Object, reloj);
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
                new CrearSesionRequestDto { Fecha = new DateOnly(2026, 8, 6), Tema = "X" }, default);

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

    [TestMethod]
    public async Task Crear_DesdeIdHorarioInicio_DerivaFechaBloqueYMinutos()
    {
        using var db = CrearContexto(nameof(Crear_DesdeIdHorarioInicio_DerivaFechaBloqueYMinutos));
        await SembrarHorarioAsync(db);

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
        await SembrarHorarioAsync(db);

        var sesion = await CrearServicio(db, new RelojFijo(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero)))
            .CrearAsync(Asig100, Duenio, false,
                new CrearSesionRequestDto { IdHorarioInicio = 1, Tema = "Señalética" });

        sesion.EsTardia.Should().BeTrue();
        sesion.DiasRetraso.Should().Be(3);
    }

    [TestMethod]
    public async Task Editar_NoRecalculaElRetraso()
    {
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
}