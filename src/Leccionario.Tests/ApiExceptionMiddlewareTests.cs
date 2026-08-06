using System.Text.Json;
using FluentAssertions;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Leccionario.Tests;

[TestClass]
public sealed class ApiExceptionMiddlewareTests
{
    private static async Task<(int status, string body)> InvokeAsync(Exception thrown)
    {
        var env = new TestHostEnvironment { EnvironmentName = Environments.Development };
        var middleware = new ApiExceptionMiddleware(
            _ => throw thrown,
            NullLogger<ApiExceptionMiddleware>.Instance,
            env);

        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.TraceIdentifier = "trace-abc";

        try { await middleware.InvokeAsync(ctx); } catch { /* ignore */ }

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ctx.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (ctx.Response.StatusCode, body);
    }

    [TestMethod]
    public async Task AppException_DevuelveJsonConCodigoMensajeYTraceId()
    {
        var (status, body) = await InvokeAsync(new DistributivoAjenoException());

        status.Should().Be(403);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("codigo").GetString().Should().Be("DISTRIBUTIVO_AJENO");
        json.GetProperty("traceId").GetString().Should().Be("trace-abc");
        json.GetProperty("mensaje").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("timestamp").GetString().Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task ValidacionException_IncluyeDetalles()
    {
        var detalles = new { Campo = "x", Valor = 1 };
        var (status, body) = await InvokeAsync(new ValidacionException("inválido", detalles));

        status.Should().Be(400);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("detalles").GetProperty("campo").GetString().Should().Be("x");
    }

    [TestMethod]
    public async Task UnauthorizedAccessException_Devuelve401CREDENCIALES_INVALIDAS()
    {
        var (status, body) = await InvokeAsync(new UnauthorizedAccessException("bad"));

        status.Should().Be(401);
        JsonDocument.Parse(body).RootElement.GetProperty("codigo").GetString()
            .Should().Be("CREDENCIALES_INVALIDAS");
    }

    [TestMethod]
    public async Task CuentaInactivaException_Devuelve401CUENTA_INACTIVA()
    {
        var (status, body) = await InvokeAsync(new CuentaInactivaException());

        status.Should().Be(401);
        JsonDocument.Parse(body).RootElement.GetProperty("codigo").GetString()
            .Should().Be("CUENTA_INACTIVA");
    }

    [TestMethod]
    public async Task ExcepcionGenerica_Devuelve500ErrorInterno()
    {
        var (status, body) = await InvokeAsync(new InvalidOperationException("boom"));

        status.Should().Be(500);
        JsonDocument.Parse(body).RootElement.GetProperty("codigo").GetString()
            .Should().Be("ERROR_INTERNO");
    }
}
