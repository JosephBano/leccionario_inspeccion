using FluentAssertions;
using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Operaciones por rango. Los topes existen porque escribimos en una tabla
/// compartida de producción y los módulos llegan a 408 días (spec H7).
/// </summary>
[TestClass]
public sealed class HorarioRangoServiceTests
{
    private const int NivelC6 = 35;
    private const int Asig100 = 100;
    private const int Z_0700 = 901;
    private static readonly DateOnly Lunes = new(2026, 8, 3);

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static IHorarioRangoService Crear(sigafi_esContext db)
    {
        var franjaZ = new FranjaZGuard(db);
        var horarios = new HorarioService(db, new HorarioCarreraGuard(db), franjaZ,
            new ConflictoHorarioService(db), new FranjaService(db, franjaZ), new EscrituraDirecta(),
            new DistributivoGuard(db));
        return new HorarioRangoService(db, horarios, new HorarioCarreraGuard(db), franjaZ);
    }

    /// <summary>Siembra el calendario de `semanas` semanas desde el lunes base.</summary>
    private static async Task SembrarAsync(
        sigafi_esContext db, int semanas = 4,
        DateOnly? ventanaIni = null, DateOnly? ventanaFin = null, bool sinVentana = false,
        int diasFaltantes = 0)
    {
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.horas_clases.Add(new FranjaBuilder().ConId(Z_0700).DeTipo("Z").DeRango("07:00", "08:00").Build());

        var asig = new AsignacionBuilder().ConId(Asig100).ConNivel(NivelC6).ConParalelo("A")
            .ConRango(sinVentana ? null : ventanaIni ?? Lunes.AddDays(-30),
                      sinVentana ? null : ventanaFin ?? Lunes.AddDays(365))
            .Build();
        db.asignaciones_profesores.Add(asig);

        var nombres = new[] { "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo" };
        var id = 500;
        var total = semanas * 7;
        for (var i = 0; i < total - diasFaltantes; i++)
        {
            db.fechas_horarios.Add(new fechas_horarios
            {
                idFecha = id++, fecha = Lunes.AddDays(i), dia = nombres[i % 7]
            });
        }

        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Camino feliz
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Replicar_CuatroSemanas_CreaUnaCeldaPorLunes()
    {
        using var db = CrearContexto(nameof(Replicar_CuatroSemanas_CreaUnaCeldaPorLunes));
        await SembrarAsync(db, semanas: 4);

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(27), "teorico"));

        r.TotalExitosos.Should().Be(4);
        r.TotalFallidos.Should().Be(0);
        r.Advertencia.Should().BeNull();
        (await db.horario_detalle.CountAsync(h => h.activo == 1)).Should().Be(4);
    }

    [TestMethod]
    public async Task Replicar_SoloTocaElDiaDeLaSemanaPedido()
    {
        using var db = CrearContexto(nameof(Replicar_SoloTocaElDiaDeLaSemanaPedido));
        await SembrarAsync(db, semanas: 2);

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Miercoles", Z_0700, Lunes, Lunes.AddDays(13), "teorico"));

        r.TotalExitosos.Should().Be(2);
        r.Detalles.Should().OnlyContain(d => d.Fecha.DayOfWeek == DayOfWeek.Wednesday);
    }

    [TestMethod]
    public async Task Eliminar_DesactivaLasOcurrenciasDelRango()
    {
        using var db = CrearContexto(nameof(Eliminar_DesactivaLasOcurrenciasDelRango));
        await SembrarAsync(db, semanas: 3);
        var svc = Crear(db);
        await svc.ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        var r = await svc.EliminarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20)));

        r.TotalExitosos.Should().Be(3);
        (await db.horario_detalle.CountAsync(h => h.activo == 1)).Should().Be(0);
        (await db.horario_detalle.CountAsync()).Should().Be(3); // borrado lógico
    }

    // -------------------------------------------------------------------------
    // Topes: la parte que protege la tabla compartida
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Replicar_RangoDeMasDe16Semanas_RechazaAntesDeEscribir()
    {
        using var db = CrearContexto(nameof(Replicar_RangoDeMasDe16Semanas_RechazaAntesDeEscribir));
        await SembrarAsync(db, semanas: 20);

        var acto = async () => await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(7 * 17), "teorico"));

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("RANGO_EXCEDE_TOPE");
        (await db.horario_detalle.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task Replicar_Exactamente16Semanas_SeAcepta()
    {
        // El tope es inclusivo: 16 semanas justas pasan.
        using var db = CrearContexto(nameof(Replicar_Exactamente16Semanas_SeAcepta));
        await SembrarAsync(db, semanas: 17);

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(7 * 16 - 1), "teorico"));

        r.TotalExitosos.Should().Be(16);
    }

    [TestMethod]
    public async Task Replicar_LoteQueSuperaLas500Filas_RechazaAntesDeEscribir()
    {
        // Con un solo día de la semana no se llega a 500 en 16 semanas, así que
        // se fuerza el tope bajándolo por parámetro para poder ejercitarlo.
        using var db = CrearContexto(nameof(Replicar_LoteQueSuperaLas500Filas_RechazaAntesDeEscribir));
        await SembrarAsync(db, semanas: 5);

        var acto = async () => await Crear(db).ReplicarAsync(
            new OperacionRangoDto(Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(34), "teorico"),
            maxFilas: 3);

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("LOTE_EXCEDE_TOPE");
        (await db.horario_detalle.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public void Constantes_TienenLosValoresDelAdr()
    {
        // Los topes son constantes, no configuración: un tope ajustable desde la
        // UI no es un tope (ADR-008 decisión 11).
        HorarioRangoService.MaxSemanas.Should().Be(16);
        HorarioRangoService.MaxFilasPorLote.Should().Be(500);
    }

    // -------------------------------------------------------------------------
    // Rango y ventana
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Replicar_RangoInvertido_Rechaza()
    {
        using var db = CrearContexto(nameof(Replicar_RangoInvertido_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes.AddDays(7), Lunes, "teorico"));

        (await acto.Should().ThrowAsync<AppException>())
            .Which.Codigo.Should().Be("RANGO_INVALIDO");
    }

    [TestMethod]
    public async Task Replicar_FueraDeLaVentanaDeLaAsignacion_Rechaza()
    {
        using var db = CrearContexto(nameof(Replicar_FueraDeLaVentanaDeLaAsignacion_Rechaza));
        await SembrarAsync(db, semanas: 8,
            ventanaIni: Lunes, ventanaFin: Lunes.AddDays(13));

        var acto = async () => await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(27), "teorico"));

        (await acto.Should().ThrowAsync<FueraDeVentanaException>())
            .Which.Codigo.Should().Be("FUERA_DE_VENTANA");
    }

    [TestMethod]
    public async Task Replicar_AsignacionSinVentana_AceptaConAdvertencia()
    {
        // 737 asignaciones activas de carrera 6 no tienen fecha_inicial/fecha_fin
        // (spec H8). No hay contra qué acotar: se acepta el rango explícito y se
        // avisa, en vez de bloquear por un dato que la carrera nunca cargó.
        using var db = CrearContexto(nameof(Replicar_AsignacionSinVentana_AceptaConAdvertencia));
        await SembrarAsync(db, semanas: 3, sinVentana: true);

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        r.TotalExitosos.Should().Be(3);
        r.Advertencia.Should().Be("ASIGNACION_SIN_VENTANA");
    }

    // -------------------------------------------------------------------------
    // Resiliencia del lote
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Replicar_ConFechaAusenteDelCalendario_LaReportaYSigueConElResto()
    {
        // El lote NO falla entero: se reporta el día que no se pudo y el resto
        // se procesa. fechas_horarios se alimenta por fuera de cplec.
        using var db = CrearContexto(nameof(Replicar_ConFechaAusenteDelCalendario_LaReportaYSigueConElResto));
        await SembrarAsync(db, semanas: 3, diasFaltantes: 7); // falta la 3ª semana

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        r.TotalProcesados.Should().Be(3);
        r.TotalExitosos.Should().Be(2);
        r.TotalFallidos.Should().Be(1);
        r.Detalles.Single(d => !d.Exitoso).MotivoFallo.Should().Contain("calendario");
    }

    [TestMethod]
    public async Task Replicar_ConUnDiaEnConflicto_LoReportaYCreaLosDemas()
    {
        using var db = CrearContexto(nameof(Replicar_ConUnDiaEnConflicto_LoReportaYCreaLosDemas));
        await SembrarAsync(db, semanas: 3);
        // Ocupamos el segundo lunes con la misma asignación y franja.
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(507).EnFranja(Z_0700).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        r.TotalExitosos.Should().Be(2);
        r.TotalFallidos.Should().Be(1);
    }

    [TestMethod]
    public async Task Replicar_SinNingunaFechaQueCoincida_DevuelveLoteVacio()
    {
        using var db = CrearContexto(nameof(Replicar_SinNingunaFechaQueCoincida_DevuelveLoteVacio));
        await SembrarAsync(db, semanas: 1);

        // Rango de martes a miércoles: no contiene ningún lunes.
        var r = await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes.AddDays(1), Lunes.AddDays(2), "teorico"));

        r.TotalProcesados.Should().Be(0);
        r.Detalles.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Replicar_ConDiaInvalido_Rechaza()
    {
        using var db = CrearContexto(nameof(Replicar_ConDiaInvalido_Rechaza));
        await SembrarAsync(db);

        var acto = async () => await Crear(db).ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunez", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        await acto.Should().ThrowAsync<ValidacionException>();
    }

    // -------------------------------------------------------------------------
    // Operaciones de actualización por rango
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Actualizar_CambiaElTipoDeBloqueEnTodoElRango()
    {
        using var db = CrearContexto(nameof(Actualizar_CambiaElTipoDeBloqueEnTodoElRango));
        await SembrarAsync(db, semanas: 3);
        var svc = Crear(db);
        await svc.ReplicarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "teorico"));

        var r = await svc.ActualizarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(20), "practico"));

        r.TotalExitosos.Should().Be(3);
        (await db.horario_detalle.Where(h => h.activo == 1).ToListAsync())
            .Should().OnlyContain(h => h.tipoBloque == "practico");
    }

    [TestMethod]
    public async Task Actualizar_DiaSinHorario_LoReportaSinFallarElLote()
    {
        using var db = CrearContexto(nameof(Actualizar_DiaSinHorario_LoReportaSinFallarElLote));
        await SembrarAsync(db, semanas: 2);

        var r = await Crear(db).ActualizarAsync(new OperacionRangoDto(
            Asig100, "Lunes", Z_0700, Lunes, Lunes.AddDays(13), "practico"));

        r.TotalFallidos.Should().Be(2);
        r.Detalles.Should().OnlyContain(d => d.MotivoFallo!.Contains("No había horario"));
    }
}
