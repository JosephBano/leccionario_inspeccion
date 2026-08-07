using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Middlewares;

namespace Leccionario.Tests;

[TestClass]
public sealed class ExceptionClassifierTests
{
    [TestMethod]
    public void GetHttpStatus_AppException_DevuelveStatusPropio()
    {
        ExceptionClassifier.GetHttpStatus(new DistributivoAjenoException())
            .Should().Be(403);
        ExceptionClassifier.GetHttpStatus(new ValidacionException("x"))
            .Should().Be(400);
        ExceptionClassifier.GetHttpStatus(new NoEncontradoException("x"))
            .Should().Be(404);
        ExceptionClassifier.GetHttpStatus(new ConflictoException("x"))
            .Should().Be(409);
    }

    [TestMethod]
    public void GetHttpStatus_CuentaInactivaException_Es401()
    {
        ExceptionClassifier.GetHttpStatus(new CuentaInactivaException())
            .Should().Be(401);
    }

    [TestMethod]
    public void GetHttpStatus_UnauthorizedAccessException_Es401()
    {
        ExceptionClassifier.GetHttpStatus(new UnauthorizedAccessException("x"))
            .Should().Be(401);
    }

    [TestMethod]
    public void GetHttpStatus_Generica_Es500()
    {
        ExceptionClassifier.GetHttpStatus(new InvalidOperationException("x"))
            .Should().Be(500);
    }

    [TestMethod]
    public void GetCodigo_AppException_DevuelveCodigoPropio()
    {
        ExceptionClassifier.GetCodigo(new DistributivoAjenoException())
            .Should().Be("DISTRIBUTIVO_AJENO");
        ExceptionClassifier.GetCodigo(new ValidacionException("x"))
            .Should().Be("VALIDACION");
        ExceptionClassifier.GetCodigo(new NoEncontradoException("x"))
            .Should().Be("NO_ENCONTRADO");
        ExceptionClassifier.GetCodigo(new SesionCerradaException())
            .Should().Be("SESION_CERRADA");
        ExceptionClassifier.GetCodigo(new FueraDePlazoException(72))
            .Should().Be("FUERA_DE_PLAZO");
        ExceptionClassifier.GetCodigo(new ConflictoException("x"))
            .Should().Be("CONFLICTO");
    }

    [TestMethod]
    public void GetCodigo_CuentaInactiva_DevuelveCodigoEstable()
    {
        ExceptionClassifier.GetCodigo(new CuentaInactivaException())
            .Should().Be("CUENTA_INACTIVA");
    }

    [TestMethod]
    public void GetCodigo_SinAccesoSistema_DevuelveCodigoEstable()
    {
        ExceptionClassifier.GetCodigo(new SinAccesoSistemaException())
            .Should().Be("SIN_ACCESO_SISTEMA");
        ExceptionClassifier.GetHttpStatus(new SinAccesoSistemaException())
            .Should().Be(403);
    }

    [TestMethod]
    public void GetCodigo_Generica_DevuelveErrorInterno()
    {
        ExceptionClassifier.GetCodigo(new InvalidOperationException("x"))
            .Should().Be("ERROR_INTERNO");
    }
}
