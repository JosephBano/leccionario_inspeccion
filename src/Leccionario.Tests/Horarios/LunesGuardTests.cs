using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Horarios.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Defensa de backend para el desfase de zona horaria del cliente (ADR-009):
/// el frontend debe enviar un lunes real; si llega cualquier otro día,
/// <see cref="ILunesGuard.EnsureEsLunes"/> lanza 400 VALIDACION en vez de
/// devolver un grid con etiquetas desalineadas.
/// </summary>
[TestClass]
public sealed class LunesGuardTests
{
    private readonly ILunesGuard _sut = new LunesGuard();

    [TestMethod]
    [DataRow(2026, 7, 6, DisplayName = "lunes del bug original")]
    [DataRow(2025, 1, 6, DisplayName = "lunes de otro año")]
    [DataRow(2026, 12, 28, DisplayName = "lunes que cruza fin de año")]
    [DataRow(2027, 1, 4, DisplayName = "lunes del año siguiente")]
    public void EnsureEsLunes_NoLanza_SiLaFechaEsLunes(int y, int m, int d)
    {
        var fecha = new DateOnly(y, m, d);

        var acto = () => _sut.EnsureEsLunes(fecha);

        acto.Should().NotThrow();
    }

    [TestMethod]
    [DataRow(2026, 7, 7, DisplayName = "martes: caso del bug (2026-07-07)")]
    [DataRow(2026, 7, 8, DisplayName = "miércoles")]
    [DataRow(2026, 7, 9, DisplayName = "jueves")]
    [DataRow(2026, 7, 10, DisplayName = "viernes")]
    [DataRow(2026, 7, 11, DisplayName = "sábado")]
    [DataRow(2026, 7, 12, DisplayName = "domingo: el caso trampa del helper")]
    [DataRow(2026, 7, 5, DisplayName = "domingo anterior (mismo día-1)")]
    public void EnsureEsLunes_LanzaValidacion_SiNoEsLunes(int y, int m, int d)
    {
        var fecha = new DateOnly(y, m, d);

        var acto = () => _sut.EnsureEsLunes(fecha);

        acto.Should().Throw<ValidacionException>()
            .Where(e => e.Codigo == "VALIDACION")
            .WithMessage($"*{fecha:yyyy-MM-dd}*");
    }
}
