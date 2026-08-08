using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>CRUD del catálogo de franjas Z. Ver ADR-008 decisión 3.</summary>
[TestClass]
public sealed class FranjaServiceTests
{
    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    private static IFranjaService Crear(sigafi_esContext db) =>
        new FranjaService(db, new FranjaZGuard(db));

    [TestMethod]
    public async Task Listar_DevuelveSoloFranjasZActivas_OrdenadasPorHora()
    {
        using var db = CrearContexto(nameof(Listar_DevuelveSoloFranjasZActivas_OrdenadasPorHora));
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(902).DeTipo("Z").DeRango("09:00", "10:00").Build(),
            new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build(),
            new FranjaBuilder().ConId(903).DeTipo("Z").DeRango("11:00", "12:00").Activa(0).Build(),
            new FranjaBuilder().ConId(12).DeTipo("X").DeRango("08:00", "09:00").Build(),
            new FranjaBuilder().ConId(3).DeTipo("C").ConCarrera(6).DeRango("10:00", "11:30").Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ListarAsync();

        r.Select(f => f.Idhora).Should().Equal(901, 902);
    }

    [TestMethod]
    public async Task Crear_DerivaLosMinutosDelRango()
    {
        using var db = CrearContexto(nameof(Crear_DerivaLosMinutosDelRango));

        var f = await Crear(db).CrearAsync(new CrearFranjaDto("07:00", "08:30", 1));

        f.Minutos.Should().Be(90);
        var fila = await db.horas_clases.SingleAsync();
        fila.tipo.Should().Be("Z");
        fila.idCarrera.Should().BeNull();
        fila.idSeccion.Should().BeNull();
        fila.activo.Should().Be(1);
    }

    [TestMethod]
    public async Task Crear_ConInicioMayorOIgualAlFin_Rechaza()
    {
        using var db = CrearContexto(nameof(Crear_ConInicioMayorOIgualAlFin_Rechaza));

        var acto = async () => await Crear(db).CrearAsync(new CrearFranjaDto("10:00", "10:00", 1));

        await acto.Should().ThrowAsync<ValidacionException>();
    }

    [TestMethod]
    public async Task Crear_SolapadaConOtraFranjaZActiva_Rechaza()
    {
        // Dos franjas Z solapadas harían que el grid ofrezca horarios imposibles
        // y que el cálculo de bloques contiguos pierda sentido.
        using var db = CrearContexto(nameof(Crear_SolapadaConOtraFranjaZActiva_Rechaza));
        db.horas_clases.Add(new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "09:00").Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).CrearAsync(new CrearFranjaDto("08:00", "08:45", 2));

        (await acto.Should().ThrowAsync<ConflictoException>())
            .Which.Codigo.Should().Be("CONFLICTO_HORARIO");
    }

    [TestMethod]
    public async Task Crear_PegadaAOtra_SeAcepta()
    {
        // 07:00–08:00 y 08:00–09:00 son consecutivas: es el caso normal.
        using var db = CrearContexto(nameof(Crear_PegadaAOtra_SeAcepta));
        db.horas_clases.Add(new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build());
        await db.SaveChangesAsync();

        var f = await Crear(db).CrearAsync(new CrearFranjaDto("08:00", "09:00", 2));

        f.Minutos.Should().Be(60);
    }

    [TestMethod]
    public async Task Desactivar_FranjaConHorarioActivo_Rechaza()
    {
        using var db = CrearContexto(nameof(Desactivar_FranjaConHorarioActivo_Rechaza));
        db.horas_clases.Add(new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1).EnFranja(901).Build());
        await db.SaveChangesAsync();

        var acto = async () => await Crear(db).DesactivarAsync(901);

        (await acto.Should().ThrowAsync<ConflictoException>())
            .Which.Codigo.Should().Be("FRANJA_EN_USO");
    }

    [TestMethod]
    public async Task Desactivar_FranjaLibre_MarcaActivoCeroSinBorrar()
    {
        using var db = CrearContexto(nameof(Desactivar_FranjaLibre_MarcaActivoCeroSinBorrar));
        db.horas_clases.Add(new FranjaBuilder().ConId(901).DeTipo("Z").DeRango("07:00", "08:00").Build());
        await db.SaveChangesAsync();

        await Crear(db).DesactivarAsync(901);

        var fila = await db.horas_clases.SingleAsync();
        fila.activo.Should().Be(0);
    }

    [TestMethod]
    public async Task Actualizar_FranjaX_Rechaza()
    {
        using var db = CrearContexto(nameof(Actualizar_FranjaX_Rechaza));
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").DeRango("08:00", "09:00").Build());
        await db.SaveChangesAsync();

        var acto = async () =>
            await Crear(db).ActualizarAsync(12, new CrearFranjaDto("08:00", "10:00", 1));

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }
}
