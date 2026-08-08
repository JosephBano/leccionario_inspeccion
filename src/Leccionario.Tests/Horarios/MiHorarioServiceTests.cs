using FluentAssertions;
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// GET /api/mi-horario. Vigencia por ventana de la asignación (spec 2026-08-08 §D1)
/// y bloques del horario del docente.
/// </summary>
[TestClass]
public sealed class MiHorarioServiceTests
{
    private const string Duenio = "0000000001";
    private const string Ajeno = "0000000002";
    private const int NivelC6 = 35;
    private static readonly DateOnly Lunes = new(2026, 8, 3);
    private static readonly DateOnly Domingo = new(2026, 8, 9);

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    // Miércoles 2026-08-05 al mediodía.
    private static TimeProvider Reloj => new RelojFijo(new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

    private static sigafi_esContext CrearContexto(string nombre) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombre).Options);

    // El reloj se inyecta también en MiHorarioService: sin él, la semana en curso
    // se calcularía con la fecha real y Obtener_SinRango_UsaLaSemanaEnCurso fallaría.
    private static IMiHorarioService Crear(sigafi_esContext db) =>
        new MiHorarioService(
            new MisParalelosService(db, Reloj),
            new AgendaService(db, Reloj),
            Reloj);

    /// <summary>
    /// Docente `Duenio` con la asignación 100 (vigente, dos franjas contiguas el lunes)
    /// y `Ajeno` con la 200 (una franja el martes).
    /// </summary>
    private static async Task SembrarAsync(sigafi_esContext db,
        DateOnly? finAsig100 = null)
    {
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.asignaturas.Add(new asignaturas { idAsignatura = 1, asignatura = "Normativa de tránsito", anulada = false });
        db.secciones.Add(new secciones { idSeccion = 1, seccion = "MATUTINA" });
        db.modalidades.Add(new modalidades { idModalidad = 1, modalidad = "PRESENCIAL" });

        db.asignaciones_profesores.AddRange(
            new AsignacionBuilder().ConId(100).ConAsignatura(1).DelProfesor(Duenio).ConNivel(NivelC6).ConParalelo("A")
                .ConRango(new DateOnly(2026, 7, 1), finAsig100 ?? new DateOnly(2026, 12, 31)).Build(),
            new AsignacionBuilder().ConId(200).ConAsignatura(1).DelProfesor(Ajeno).ConNivel(NivelC6).ConParalelo("B")
                .ConRango(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)).Build());

        db.fechas_horarios.AddRange(
            new fechas_horarios { idFecha = 500, fecha = Lunes, dia = "Lunes" },
            new fechas_horarios { idFecha = 501, fecha = Lunes.AddDays(1), dia = "Martes" },
            new fechas_horarios { idFecha = 504, fecha = Lunes.AddDays(4), dia = "Viernes" });

        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(902).DeTipo("Z").DeRango("08:00", "09:00").Build());

        db.horario_detalle.AddRange(
            new HorarioDetalleBuilder().ConId(1).DeAsignacion(100).EnFecha(500).EnFranja(901).Build(),
            new HorarioDetalleBuilder().ConId(2).DeAsignacion(100).EnFecha(500).EnFranja(902).Build(),
            new HorarioDetalleBuilder().ConId(3).DeAsignacion(100).EnFecha(504).EnFranja(901).Build(),
            new HorarioDetalleBuilder().ConId(4).DeAsignacion(200).EnFecha(501).EnFranja(901).Build());

        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Obtener_DevuelveSoloLosBloquesDelDocente()
    {
        using var db = CrearContexto(nameof(Obtener_DevuelveSoloLosBloquesDelDocente));
        await SembrarAsync(db);

        var r = await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo);

        r.Should().OnlyContain(b => b.IdAsignacion == 100);
        r.Should().HaveCount(2);   // lunes (bloque de 2 franjas) y viernes
    }

    [TestMethod]
    public async Task Obtener_AgrupaFranjasContiguasEnUnBloque()
    {
        using var db = CrearContexto(nameof(Obtener_AgrupaFranjasContiguasEnUnBloque));
        await SembrarAsync(db);

        var lunes = (await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo))
            .Single(b => b.Fecha == Lunes);

        lunes.HoraInicio.Should().Be("07:00");
        lunes.HoraFin.Should().Be("09:00");
        lunes.FranjasPlanificadas.Should().Be(2);
        lunes.MinutosPlanificados.Should().Be(120);
    }

    [TestMethod]
    public async Task Obtener_MarcaPasadoComoPendienteYFuturoComoFutura()
    {
        using var db = CrearContexto(nameof(Obtener_MarcaPasadoComoPendienteYFuturoComoFutura));
        await SembrarAsync(db);

        var r = await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo);

        r.Single(b => b.Fecha == Lunes).Estado.Should().Be("Pendiente");
        r.Single(b => b.Fecha == Lunes).DiasRetraso.Should().Be(2);       // lunes → miércoles
        r.Single(b => b.Fecha == Lunes.AddDays(4)).Estado.Should().Be("Futura");
    }

    [TestMethod]
    public async Task Obtener_ExcluyeAsignacionVencidaMasDeLaGracia()
    {
        using var db = CrearContexto(nameof(Obtener_ExcluyeAsignacionVencidaMasDeLaGracia));
        // Terminó el 2026-06-01: más de 15 días antes del reloj (2026-08-05).
        await SembrarAsync(db, finAsig100: new DateOnly(2026, 6, 1));

        var r = await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo);

        r.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Obtener_SinRango_UsaLaSemanaEnCurso()
    {
        using var db = CrearContexto(nameof(Obtener_SinRango_UsaLaSemanaEnCurso));
        await SembrarAsync(db);

        // Reloj = miércoles 2026-08-05 → semana lunes 03 a domingo 09.
        var r = await Crear(db).ObtenerAsync(Duenio, null, null);

        r.Should().HaveCount(2);
        r.Select(b => b.Fecha).Should().OnlyContain(f => f >= Lunes && f <= Domingo);
    }

    [TestMethod]
    public async Task Obtener_RangoMayorA16Semanas_Rechaza()
    {
        using var db = CrearContexto(nameof(Obtener_RangoMayorA16Semanas_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ObtenerAsync(Duenio, Lunes, Lunes.AddDays(16 * 7 + 1));

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("RANGO_EXCEDE_TOPE");
    }

    [TestMethod]
    public async Task Obtener_DesdeMayorQueHasta_Rechaza()
    {
        using var db = CrearContexto(nameof(Obtener_DesdeMayorQueHasta_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ObtenerAsync(Duenio, Domingo, Lunes);

        await acto.Should().ThrowAsync<ValidacionException>();
    }

    [TestMethod]
    public async Task Obtener_SinIdProfesor_Rechaza()
    {
        using var db = CrearContexto(nameof(Obtener_SinIdProfesor_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ObtenerAsync("", Lunes, Domingo);

        await acto.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task Obtener_TraeLaDescripcionDelParalelo()
    {
        using var db = CrearContexto(nameof(Obtener_TraeLaDescripcionDelParalelo));
        await SembrarAsync(db);

        var b = (await Crear(db).ObtenerAsync(Duenio, Lunes, Domingo)).First();

        b.Paralelo.Should().Be("A");
        b.Jornada.Should().Be("MATUTINA");
        b.Asignatura.Should().Be("Normativa de tránsito");
    }
}
