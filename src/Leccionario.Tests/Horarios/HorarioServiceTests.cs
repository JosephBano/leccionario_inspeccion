using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>Grid semanal y CRUD de celda. Ver spec sección 5.5.</summary>
[TestClass]
public sealed class HorarioServiceTests
{
    private const int NivelC6 = 35;
    private const int Asig100 = 100;
    private const int Z_0700 = 901;
    private const int Z_0800 = 902;
    private static readonly DateOnly Lunes = new(2026, 8, 3);

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static IHorarioService Crear(sigafi_esContext db) =>
        new HorarioService(db,
            new HorarioCarreraGuard(db), new FranjaZGuard(db),
            new ConflictoHorarioService(db), new FranjaService(db, new FranjaZGuard(db)),
            new EscrituraDirecta());

    private static ParaleloClaveDto Clave => new("TEST0001", NivelC6, 1, 1, "A");

    private static async Task SembrarAsync(sigafi_esContext db, bool calendarioCompleto = true)
    {
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(Z_0700).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(Z_0800).DeTipo("Z").DeRango("08:00", "09:00").Build());
        db.asignaciones_profesores.Add(
            new AsignacionBuilder().ConId(Asig100).ConNivel(NivelC6).ConParalelo("A").Build());
        db.profesores.Add(new profesores
        {
            idProfesor = "0000000001", apellidos = "PEREZ", nombres = "JUAN", tipoSangre = "O+"
        });

        // Lunes a domingo de la semana del 2026-08-03.
        var dias = new[] { "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo" };
        var total = calendarioCompleto ? 7 : 6;   // sin el domingo
        for (var i = 0; i < total; i++)
        {
            db.fechas_horarios.Add(new fechas_horarios
            {
                idFecha = 500 + i, fecha = Lunes.AddDays(i), dia = dias[i]
            });
        }

        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Grid_SinCeldas_DevuelveFranjasYSieteDias()
    {
        using var db = CrearContexto(nameof(Grid_SinCeldas_DevuelveFranjasYSieteDias));
        await SembrarAsync(db);

        var g = await Crear(db).ObtenerGridAsync(Clave, Lunes);

        g.Franjas.Should().HaveCount(2);
        g.Dias.Should().HaveCount(7);
        g.Dias.Should().OnlyContain(d => d.Habilitado);
        g.Celdas.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Grid_ConDiaAusenteDelCalendario_LoMarcaDeshabilitadoConMotivo()
    {
        using var db = CrearContexto(nameof(Grid_ConDiaAusenteDelCalendario_LoMarcaDeshabilitadoConMotivo));
        await SembrarAsync(db, calendarioCompleto: false);

        var g = await Crear(db).ObtenerGridAsync(Clave, Lunes);

        var domingo = g.Dias.Single(d => d.Dia == "Domingo");
        domingo.Habilitado.Should().BeFalse();
        domingo.IdFecha.Should().BeNull();
        domingo.Motivo.Should().Contain("calendario");
    }

    [TestMethod]
    public async Task Grid_DevuelveLasCeldasDelParalelo()
    {
        using var db = CrearContexto(nameof(Grid_DevuelveLasCeldasDelParalelo));
        await SembrarAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(Z_0700).Build());
        await db.SaveChangesAsync();

        var g = await Crear(db).ObtenerGridAsync(Clave, Lunes);

        var c = g.Celdas.Should().ContainSingle().Which;
        c.Idhora.Should().Be(Z_0700);
        c.Dia.Should().Be("Lunes");
        c.NombreDocente.Should().Contain("PEREZ");
    }

    [TestMethod]
    public async Task Crear_CeldaLibre_InsertaConIdEspacioNull()
    {
        using var db = CrearContexto(nameof(Crear_CeldaLibre_InsertaConIdEspacioNull));
        await SembrarAsync(db);

        var r = await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, Z_0700, "teorico"));

        r.IdHorario.Should().BeGreaterThan(0);
        var fila = await db.horario_detalle.SingleAsync();
        fila.idEspacio.Should().BeNull();
        fila.activo.Should().Be(1);
        fila.tipoBloque.Should().Be("teorico");
    }

    [TestMethod]
    public async Task Crear_SobreFilaSoftDeleted_ReviveEnVezDeInsertar()
    {
        using var db = CrearContexto(nameof(Crear_SobreFilaSoftDeleted_ReviveEnVezDeInsertar));
        await SembrarAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(9)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(Z_0700).Activo(0).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, Z_0700, "practico"));

        r.IdHorario.Should().Be(9);
        db.horario_detalle.Should().HaveCount(1);
        var fila = await db.horario_detalle.SingleAsync();
        fila.activo.Should().Be(1);
        fila.tipoBloque.Should().Be("practico");
    }

    [TestMethod]
    public async Task Crear_AsignacionDeOtraCarrera_Rechaza()
    {
        using var db = CrearContexto(nameof(Crear_AsignacionDeOtraCarrera_Rechaza));
        await SembrarAsync(db);
        db.cursos.Add(new cursos { idNivel = 77, idCarrera = 19, Nivel = "PRIMERO" });
        db.asignaciones_profesores.Add(new AsignacionBuilder().ConId(300).ConNivel(77).Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearCeldaDto(300, 500, Z_0700, "teorico"));

        await acto.Should().ThrowAsync<FueraDeAlcanceException>();
    }

    [TestMethod]
    public async Task Crear_ConFranjaX_Rechaza()
    {
        using var db = CrearContexto(nameof(Crear_ConFranjaX_Rechaza));
        await SembrarAsync(db);
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").DeRango("07:00", "08:00").Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, 12, "teorico"));

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }

    [TestMethod]
    public async Task Crear_ConConflictoBloqueante_LanzaConflictoHorario()
    {
        using var db = CrearContexto(nameof(Crear_ConConflictoBloqueante_LanzaConflictoHorario));
        await SembrarAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(Z_0700).Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, Z_0700, "teorico"));

        (await acto.Should().ThrowAsync<ConflictoException>())
            .Which.Codigo.Should().Be("CONFLICTO_HORARIO");
    }

    [TestMethod]
    public async Task Crear_ConAdvertenciaSinConfirmar_Rechaza()
    {
        using var db = CrearContexto(nameof(Crear_ConAdvertenciaSinConfirmar_Rechaza));
        await SembrarAsync(db);
        db.carreras.Add(new carreras { idCarrera = 19, Carrera = "GASTRONOMIA" });
        db.cursos.Add(new cursos { idNivel = 77, idCarrera = 19, Nivel = "PRIMERO" });
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").DeRango("07:00", "08:00").Build());
        db.asignaciones_profesores.Add(
            new AsignacionBuilder().ConId(300).DelProfesor("0000000001").ConNivel(77).Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(300).EnFecha(500).EnFranja(12).Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearCeldaDto(Asig100, 500, Z_0700, "teorico"));

        (await acto.Should().ThrowAsync<ConflictoException>())
            .Which.Codigo.Should().Be("ADVERTENCIA_NO_CONFIRMADA");
    }

    [TestMethod]
    public async Task Crear_ConAdvertenciaConfirmada_GuardaYDevuelveLaAdvertencia()
    {
        using var db = CrearContexto(nameof(Crear_ConAdvertenciaConfirmada_GuardaYDevuelveLaAdvertencia));
        await SembrarAsync(db);
        db.carreras.Add(new carreras { idCarrera = 19, Carrera = "GASTRONOMIA" });
        db.cursos.Add(new cursos { idNivel = 77, idCarrera = 19, Nivel = "PRIMERO" });
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").DeRango("07:00", "08:00").Build());
        db.asignaciones_profesores.Add(
            new AsignacionBuilder().ConId(300).DelProfesor("0000000001").ConNivel(77).Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(300).EnFecha(500).EnFranja(12).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).CrearAsync(
            new CrearCeldaDto(Asig100, 500, Z_0700, "teorico", ConfirmarAdvertencias: true));

        r.IdHorario.Should().BeGreaterThan(0);
        r.Advertencias.Should().ContainSingle().Which.Carrera.Should().Be("GASTRONOMIA");
    }

    [TestMethod]
    public async Task Desactivar_MarcaActivoCeroSinBorrarLaFila()
    {
        using var db = CrearContexto(nameof(Desactivar_MarcaActivoCeroSinBorrarLaFila));
        await SembrarAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(500).EnFranja(Z_0700).Build());
        await db.SaveChangesAsync();

        await Crear(db).DesactivarAsync(1);

        var fila = await db.horario_detalle.SingleAsync();
        fila.activo.Should().Be(0);
    }
}
