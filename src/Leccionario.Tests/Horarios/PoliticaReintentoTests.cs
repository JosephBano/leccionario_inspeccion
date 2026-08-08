using FluentAssertions;
using Leccionario.Api.Application.Horarios.Services;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// La política de reintento ante deadlock. Con `horario_detalle` sin índice
/// único (spec H1), esto ES el mecanismo de unicidad, no una mejora.
/// </summary>
[TestClass]
public sealed class PoliticaReintentoTests
{
    private sealed class DeadlockSimulado : Exception { }
    private sealed class ErrorPermanente : Exception { }

    private static bool EsDeadlock(Exception ex) => ex is DeadlockSimulado;

    [TestMethod]
    public async Task Exito_AlPrimerIntento_NoReintenta()
    {
        var intentos = 0;

        var r = await PoliticaReintento.EjecutarAsync<int>(
            (_, _) => { intentos++; return Task.FromResult(42); },
            EsDeadlock);

        r.Should().Be(42);
        intentos.Should().Be(1);
    }

    [TestMethod]
    public async Task Deadlock_LuegoExito_DevuelveElResultadoDelSegundoIntento()
    {
        var intentos = 0;

        var r = await PoliticaReintento.EjecutarAsync<string>(
            (_, _) =>
            {
                intentos++;
                if (intentos == 1) throw new DeadlockSimulado();
                return Task.FromResult("ok");
            },
            EsDeadlock);

        r.Should().Be("ok");
        intentos.Should().Be(2);
    }

    [TestMethod]
    public async Task Deadlock_EnTodosLosIntentos_PropagaTrasAgotarlos()
    {
        var intentos = 0;

        var acto = async () => await PoliticaReintento.EjecutarAsync<int>(
            (_, _) => { intentos++; throw new DeadlockSimulado(); },
            EsDeadlock);

        await acto.Should().ThrowAsync<DeadlockSimulado>();
        intentos.Should().Be(PoliticaReintento.MaxIntentos);
    }

    [TestMethod]
    public async Task ErrorNoTransitorio_NoSeReintenta()
    {
        // Un conflicto de negocio o una violación de FK no se reintenta:
        // reintentarlo solo repetiría el mismo fallo y ocultaría la causa.
        var intentos = 0;

        var acto = async () => await PoliticaReintento.EjecutarAsync<int>(
            (_, _) => { intentos++; throw new ErrorPermanente(); },
            EsDeadlock);

        await acto.Should().ThrowAsync<ErrorPermanente>();
        intentos.Should().Be(1);
    }
}
