using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;

namespace Leccionario.Tests;

[TestClass]
public sealed class AppExceptionTests
{
    [TestMethod]
    public void AppException_ValidaCodigoYStatus()
    {
        var ex = new ValidacionException("campo X requerido");

        ex.Codigo.Should().Be("VALIDACION");
        ex.HttpStatus.Should().Be(400);
        ex.Message.Should().Be("campo X requerido");
    }

    [TestMethod]
    public void ValidacionException_ConDetalles_LosGuarda()
    {
        var detalles = new { Campo = "email", Valor = "abc" };
        var ex = new ValidacionException("inválido", detalles);

        ex.Detalles.Should().BeSameAs(detalles);
    }

    [TestMethod]
    public void NoEncontradoException_SinId_MensajeGenerico()
    {
        var ex = new NoEncontradoException("no encontrado");
        ex.Codigo.Should().Be("NO_ENCONTRADO");
        ex.HttpStatus.Should().Be(404);
        ex.Entidad.Should().BeNull();
        ex.Id.Should().BeNull();
    }

    [TestMethod]
    public void NoEncontradoException_ConEntidadEId_MensajeTipado()
    {
        var ex = new NoEncontradoException("Sesion", 123);
        ex.Entidad.Should().Be("Sesion");
        ex.Id.Should().Be(123);
        ex.Message.Should().Contain("Sesion").And.Contain("123");
    }

    [TestMethod]
    public void DistributivoAjenoException_DevuelveCodigo403()
    {
        var ex = new DistributivoAjenoException();
        ex.Codigo.Should().Be("DISTRIBUTIVO_AJENO");
        ex.HttpStatus.Should().Be(403);
    }

    [TestMethod]
    public void SesionCerradaException_DevuelveCodigo403()
    {
        var ex = new SesionCerradaException();
        ex.Codigo.Should().Be("SESION_CERRADA");
        ex.HttpStatus.Should().Be(403);
    }

    [TestMethod]
    public void FueraDePlazoException_GuardaVentana()
    {
        var ex = new FueraDePlazoException(72);
        ex.Codigo.Should().Be("FUERA_DE_PLAZO");
        ex.HttpStatus.Should().Be(403);
        ex.VentanaHoras.Should().Be(72);
    }

    [TestMethod]
    public void ConflictoException_SinCodigo_DevuelveGenerico()
    {
        var ex = new ConflictoException("ya existe");
        ex.Codigo.Should().Be("CONFLICTO");
        ex.HttpStatus.Should().Be(409);
    }

    [TestMethod]
    public void ConflictoException_ConCodigo_DevuelveCustom()
    {
        var ex = new ConflictoException("SESION_DUPLICADA", "ya hay sesión para esta fecha");
        ex.Codigo.Should().Be("SESION_DUPLICADA");
    }

    [TestMethod]
    public void SinAccesoSistemaException_DevuelveCodigo403()
    {
        var ex = new SinAccesoSistemaException();
        ex.Codigo.Should().Be("SIN_ACCESO_SISTEMA");
        ex.HttpStatus.Should().Be(403);
    }
}
