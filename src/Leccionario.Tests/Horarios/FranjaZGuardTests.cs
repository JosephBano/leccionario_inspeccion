using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// cplec solo escribe franjas `tipo='Z'`. `X` es del instituto, `I` legacy y `C`
/// de otro sistema: se leen para conflictos, nunca se modifican (ADR-008 dec. 3).
/// </summary>
[TestClass]
public sealed class FranjaZGuardTests
{
    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    [TestMethod]
    public async Task Ensure_FranjaZActiva_NoLanza()
    {
        using var db = CrearContexto(nameof(Ensure_FranjaZActiva_NoLanza));
        db.horas_clases.Add(new FranjaBuilder().ConId(900).DeTipo("Z").Build());
        await db.SaveChangesAsync();

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(900);

        await acto.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Ensure_FranjaX_DelInstituto_Lanza()
    {
        using var db = CrearContexto(nameof(Ensure_FranjaX_DelInstituto_Lanza));
        db.horas_clases.Add(new FranjaBuilder().ConId(12).DeTipo("X").Build());
        await db.SaveChangesAsync();

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(12);

        (await acto.Should().ThrowAsync<FranjaNoPropiaException>())
            .Which.Codigo.Should().Be("FRANJA_NO_PROPIA");
    }

    [TestMethod]
    public async Task Ensure_FranjaC_DeCarrera6_Lanza()
    {
        // La carrera 6 YA tiene franjas 'C' con idSeccion poblado (spec H2).
        // Son de otro sistema: cplec no las toca aunque sean "de su carrera".
        using var db = CrearContexto(nameof(Ensure_FranjaC_DeCarrera6_Lanza));
        db.horas_clases.Add(new FranjaBuilder().ConId(3).DeTipo("C").ConCarrera(6).ConSeccion(1).Build());
        await db.SaveChangesAsync();

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(3);

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }

    [TestMethod]
    public async Task Ensure_FranjaConTipoNull_Lanza()
    {
        // `tipo` es char(1) DEFAULT NULL: hay filas sin tipo. No son nuestras.
        using var db = CrearContexto(nameof(Ensure_FranjaConTipoNull_Lanza));
        db.horas_clases.Add(new FranjaBuilder().ConId(50).DeTipo(null).Build());
        await db.SaveChangesAsync();

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(50);

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }

    [TestMethod]
    public async Task Ensure_FranjaInexistente_Lanza()
    {
        using var db = CrearContexto(nameof(Ensure_FranjaInexistente_Lanza));

        var acto = async () => await new FranjaZGuard(db).EnsureEsFranjaZAsync(999);

        await acto.Should().ThrowAsync<FranjaNoPropiaException>();
    }
}
