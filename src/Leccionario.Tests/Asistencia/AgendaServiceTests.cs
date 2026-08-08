using FluentAssertions;
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Asistencia;

/// <summary>
/// Agenda del docente: qué días toca clase y cuáles debe todavía.
/// Clase base sembrada: lunes 2026-08-03, franjas 07:00–08:00 y 08:00–09:00.
/// </summary>
[TestClass]
public sealed class AgendaServiceTests
{
    private const int Asig100 = 100;
    private const string Duenio = "0000000001";
    private static readonly DateOnly Lunes = new(2026, 8, 3);

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static DateTimeOffset El(int dia) => new(2026, 8, dia, 9, 0, 0, TimeSpan.Zero);

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static IAgendaService CrearAgenda(sigafi_esContext db, TimeProvider reloj) =>
        new AgendaService(db, reloj);

    private static IAgendaService CrearServicio(sigafi_esContext db) =>
        new AgendaService(db, new RelojFijo(El(9)));

    /// <summary>
    /// Dos asignaciones de carrera 6: la 100 el lunes 2026-08-03 en ambas franjas
    /// (07:00-08:00 y 08:00-09:00), la 200 el martes 2026-08-04 solo en la primera.
    /// </summary>
    private static async Task SembrarDosAsignacionesAsync(sigafi_esContext db)
    {
        db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.profesores.Add(new profesores { idProfesor = Duenio, apellidos = "PEREZ", nombres = "JUAN", tipoSangre = "O+" });
        db.asignaciones_profesores.Add(new AsignacionBuilder()
            .ConId(100).DelProfesor(Duenio).ConNivel(35)
            .ConRango(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).Build());
        db.asignaciones_profesores.Add(new AsignacionBuilder()
            .ConId(200).DelProfesor(Duenio).ConNivel(35)
            .ConRango(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).Build());
        db.fechas_horarios.AddRange(
            new fechas_horarios { idFecha = 500, fecha = new DateOnly(2026, 8, 3), dia = "Lunes" },
            new fechas_horarios { idFecha = 501, fecha = new DateOnly(2026, 8, 4), dia = "Martes" });
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(902).DeTipo("Z").DeRango("08:00", "09:00").Build());
        db.horario_detalle.AddRange(
            new HorarioDetalleBuilder().ConId(1).DeAsignacion(100).EnFecha(500).EnFranja(901).Build(),
            new HorarioDetalleBuilder().ConId(2).DeAsignacion(100).EnFecha(500).EnFranja(902).Build(),
            new HorarioDetalleBuilder().ConId(3).DeAsignacion(200).EnFecha(501).EnFranja(901).Build());
        await db.SaveChangesAsync();
    }

    /// <summary>Igual que en SesionServiceTests: horario del lunes con 2 franjas contiguas.</summary>
    private static async Task SembrarHorarioAsync(sigafi_esContext db)
    {
        db.cursos.Add(new cursos { idNivel = 35, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.profesores.Add(new profesores { idProfesor = Duenio, apellidos = "PEREZ", nombres = "JUAN", tipoSangre = "O+" });
        db.asignaciones_profesores.Add(new AsignacionBuilder()
            .ConId(Asig100).DelProfesor(Duenio).ConNivel(35)
            .ConRango(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).Build());
        db.fechas_horarios.AddRange(
            new fechas_horarios { idFecha = 500, fecha = Lunes, dia = "Lunes" },
            new fechas_horarios { idFecha = 507, fecha = Lunes.AddDays(7), dia = "Lunes" });
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(902).DeTipo("Z").DeRango("08:00", "09:00").Build(),
            new FranjaBuilder().ConId(903).DeTipo("Z").DeRango("15:00", "16:00").Build());
        db.horario_detalle.AddRange(
            new HorarioDetalleBuilder().ConId(1).DeAsignacion(Asig100).EnFecha(500).EnFranja(901).Build(),
            new HorarioDetalleBuilder().ConId(2).DeAsignacion(Asig100).EnFecha(500).EnFranja(902).Build());
        await db.SaveChangesAsync();
    }

    private static cplec_sesiones Sesion(int idSesion, int idFecha, sbyte bloque, string estado) => new()
    {
        idSesion = idSesion, idAsignacion = Asig100, idFecha = idFecha, numeroBloque = bloque,
        tema = "Señalética", estado = estado, activo = true,
        usuarioCreacion = Duenio, fechaCreacion = DateTime.UtcNow
    };

    [TestMethod]
    public async Task Agenda_DiaConHorarioSinSesionYaPasado_EsPendiente()
    {
        using var db = CrearContexto(nameof(Agenda_DiaConHorarioSinSesionYaPasado_EsPendiente));
        await SembrarHorarioAsync(db);

        var a = await CrearAgenda(db, new RelojFijo(El(5))).ObtenerAgendaAsync(Asig100, Lunes, Lunes);

        a.Should().ContainSingle().Which.Estado.Should().Be(EstadoBloque.Pendiente);
    }

    [TestMethod]
    public async Task Agenda_DiaConSesionBorrador_EsBorrador()
    {
        using var db = CrearContexto(nameof(Agenda_DiaConSesionBorrador_EsBorrador));
        await SembrarHorarioAsync(db);
        db.cplec_sesiones.Add(Sesion(1, 500, 1, "borrador"));
        await db.SaveChangesAsync();

        var a = await CrearAgenda(db, new RelojFijo(El(5))).ObtenerAgendaAsync(Asig100, Lunes, Lunes);

        a.Should().ContainSingle().Which.Estado.Should().Be(EstadoBloque.Borrador);
        a[0].IdSesion.Should().Be(1);
    }

    [TestMethod]
    public async Task Agenda_DiaConSesionCerrada_EsCerrada()
    {
        using var db = CrearContexto(nameof(Agenda_DiaConSesionCerrada_EsCerrada));
        await SembrarHorarioAsync(db);
        db.cplec_sesiones.Add(Sesion(1, 500, 1, "cerrada"));
        await db.SaveChangesAsync();

        var a = await CrearAgenda(db, new RelojFijo(El(5))).ObtenerAgendaAsync(Asig100, Lunes, Lunes);

        a.Should().ContainSingle().Which.Estado.Should().Be(EstadoBloque.Cerrada);
    }

    [TestMethod]
    public async Task Agenda_DiaFuturo_EsFuturaYNoRegistrable()
    {
        // El lunes siguiente todavía no ocurrió: no se pasa lista de una clase futura.
        using var db = CrearContexto(nameof(Agenda_DiaFuturo_EsFuturaYNoRegistrable));
        await SembrarHorarioAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(10)
            .DeAsignacion(Asig100).EnFecha(507).EnFranja(901).Build());
        await db.SaveChangesAsync();

        var a = await CrearAgenda(db, new RelojFijo(El(5)))
            .ObtenerAgendaAsync(Asig100, Lunes, Lunes.AddDays(7));

        a.Single(b => b.Fecha == Lunes.AddDays(7)).Estado.Should().Be(EstadoBloque.Futura);
        a.Single(b => b.Fecha == Lunes.AddDays(7)).DiasRetraso.Should().Be(0);
    }

    [TestMethod]
    public async Task Agenda_PendienteReportaLosDiasDeAtraso()
    {
        using var db = CrearContexto(nameof(Agenda_PendienteReportaLosDiasDeAtraso));
        await SembrarHorarioAsync(db);

        var a = await CrearAgenda(db, new RelojFijo(El(6))).ObtenerAgendaAsync(Asig100, Lunes, Lunes);

        a.Should().ContainSingle().Which.DiasRetraso.Should().Be(3);
    }

    [TestMethod]
    public async Task SesionesTardias_DevuelveSoloLasMarcadas_OrdenadasPorRetraso()
    {
        using var db = CrearContexto(nameof(SesionesTardias_DevuelveSoloLasMarcadas_OrdenadasPorRetraso));
        await SembrarHorarioAsync(db);
        var puntual = Sesion(1, 500, 1, "cerrada");
        var tardia2 = Sesion(2, 507, 1, "cerrada");
        tardia2.esTardia = true; tardia2.diasRetraso = 2;
        var tardia9 = Sesion(3, 507, 2, "cerrada");
        tardia9.esTardia = true; tardia9.diasRetraso = 9;
        db.cplec_sesiones.AddRange(puntual, tardia2, tardia9);
        await db.SaveChangesAsync();

        var r = await CrearAgenda(db, new RelojFijo(El(20)))
            .SesionesTardiasAsync(Lunes, Lunes.AddDays(30));

        r.Select(x => x.IdSesion).Should().Equal(3, 2);
        r[0].DiasRetraso.Should().Be(9);
        r[0].NombreDocente.Should().Contain("PEREZ");
    }

    [TestMethod]
    public async Task DiasSinRegistrar_ExcluyeLosFuturosYLosYaRegistrados()
    {
        using var db = CrearContexto(nameof(DiasSinRegistrar_ExcluyeLosFuturosYLosYaRegistrados));
        await SembrarHorarioAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(10)
            .DeAsignacion(Asig100).EnFecha(507).EnFranja(901).Build());
        db.cplec_sesiones.Add(Sesion(1, 500, 1, "cerrada"));   // el lunes 3 ya está
        await db.SaveChangesAsync();

        var r = await CrearAgenda(db, new RelojFijo(El(5)))
            .DiasSinRegistrarAsync(Lunes, Lunes.AddDays(7));

        r.Should().BeEmpty();   // el 3 registrado, el 10 aún es futuro
    }

    [TestMethod]
    public async Task DiasSinRegistrar_IgnoraCeldasConActivoDistintoDeUno()
    {
        // `activo` es tinyint(4) con basura en la base: solo `= 1` cuenta (spec H10).
        using var db = CrearContexto(nameof(DiasSinRegistrar_IgnoraCeldasConActivoDistintoDeUno));
        await SembrarHorarioAsync(db);
        foreach (var h in db.horario_detalle) h.activo = 11;
        await db.SaveChangesAsync();

        var r = await CrearAgenda(db, new RelojFijo(El(5))).DiasSinRegistrarAsync(Lunes, Lunes);

        r.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Agenda_DiaPartido_DevuelveDosBloquesConSusNumeros()
    {
        // Lunes 07:00–09:00 (dos franjas) y 15:00–16:00. Son dos clases distintas,
        // así que el docente pasa dos listas.
        using var db = CrearContexto(nameof(Agenda_DiaPartido_DevuelveDosBloquesConSusNumeros));
        await SembrarHorarioAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(3)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(903).Build());   // 15:00–16:00
        await db.SaveChangesAsync();

        var agenda = await CrearAgenda(db, new RelojFijo(new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero)))
            .ObtenerAgendaAsync(Asig100, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 3));

        agenda.Should().HaveCount(2);
        agenda[0].NumeroBloque.Should().Be(1);
        agenda[0].IdHorarioInicio.Should().Be(1);
        agenda[0].MinutosPlanificados.Should().Be(120);
        agenda[1].NumeroBloque.Should().Be(2);
        agenda[1].IdHorarioInicio.Should().Be(3);
        agenda.Should().OnlyContain(b => b.Estado == EstadoBloque.Pendiente);
        agenda[0].DiasRetraso.Should().Be(2);
    }

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
}
