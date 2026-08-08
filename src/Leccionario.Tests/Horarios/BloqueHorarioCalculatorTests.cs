using FluentAssertions;
using Leccionario.Api.Application.Horarios.Services;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Agrupación de franjas contiguas en bloques. Las franjas Z tienen duración
/// libre (las crea el inspector), así que no se puede asumir una grilla regular.
/// </summary>
[TestClass]
public sealed class BloqueHorarioCalculatorTests
{
    private static FranjaOrdenable F(int idHorario, string ini, string fin) =>
        new(idHorario, idHorario + 1000, TimeOnly.Parse(ini), TimeOnly.Parse(fin),
            (int)(TimeOnly.Parse(fin) - TimeOnly.Parse(ini)).TotalMinutes);

    [TestMethod]
    public void CuatroFranjasContiguas_UnSoloBloque()
    {
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(1, "07:00", "08:00"), F(2, "08:00", "09:00"),
            F(3, "09:00", "10:00"), F(4, "10:00", "11:00"),
        });

        bloques.Should().HaveCount(1);
        bloques[0].NumeroBloque.Should().Be(1);
        bloques[0].IdHorarioInicio.Should().Be(1);
        bloques[0].FranjasPlanificadas.Should().Be(4);
        bloques[0].MinutosPlanificados.Should().Be(240);
        bloques[0].Fin.Should().Be(TimeOnly.Parse("11:00"));
    }

    [TestMethod]
    public void DiaPartidoMananaYTarde_DosBloquesNumeradosEnOrden()
    {
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(1, "07:00", "08:00"), F(2, "08:00", "09:00"),
            F(5, "15:00", "16:00"), F(6, "16:00", "17:00"),
        });

        bloques.Should().HaveCount(2);
        bloques[0].NumeroBloque.Should().Be(1);
        bloques[0].IdHorarioInicio.Should().Be(1);
        bloques[1].NumeroBloque.Should().Be(2);
        bloques[1].IdHorarioInicio.Should().Be(5);
        bloques[1].MinutosPlanificados.Should().Be(120);
    }

    [TestMethod]
    public void UnaSolaFranja_UnBloqueDeUna()
    {
        var bloques = BloqueHorarioCalculator.Agrupar(new[] { F(7, "19:00", "21:00") });

        bloques.Should().HaveCount(1);
        bloques[0].FranjasPlanificadas.Should().Be(1);
        bloques[0].MinutosPlanificados.Should().Be(120);
    }

    [TestMethod]
    public void HuecoDeUnMinuto_CortaElBloque()
    {
        // Contigüidad es igualdad exacta, no "casi". 08:00 != 08:01.
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(1, "07:00", "08:00"), F(2, "08:01", "09:00"),
        });

        bloques.Should().HaveCount(2);
    }

    [TestMethod]
    public void FranjasDesordenadas_SeOrdenanAntesDeAgrupar()
    {
        // La consulta no garantiza orden: `hora_inicio` es varchar y el ORDER BY
        // podría no aplicarse. El cálculo ordena por su cuenta.
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(3, "09:00", "10:00"), F(1, "07:00", "08:00"), F(2, "08:00", "09:00"),
        });

        bloques.Should().HaveCount(1);
        bloques[0].IdHorarioInicio.Should().Be(1);
        bloques[0].FranjasPlanificadas.Should().Be(3);
    }

    [TestMethod]
    public void FranjasDeDistintaDuracion_SeSumanLosMinutosReales()
    {
        // 90 + 45 + 60. Contar franjas daría 3 y mentiría sobre la duración:
        // por eso la sesión guarda `minutosPlanificados`, no "horas".
        var bloques = BloqueHorarioCalculator.Agrupar(new[]
        {
            F(1, "07:00", "08:30"), F(2, "08:30", "09:15"), F(3, "09:15", "10:15"),
        });

        bloques.Should().HaveCount(1);
        bloques[0].FranjasPlanificadas.Should().Be(3);
        bloques[0].MinutosPlanificados.Should().Be(195);
    }

    [TestMethod]
    public void SinFranjas_ListaVacia()
    {
        BloqueHorarioCalculator.Agrupar(Array.Empty<FranjaOrdenable>()).Should().BeEmpty();
    }
}
