using System.Security.Claims;
using FluentAssertions;
using Leccionario.Api.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace Leccionario.Tests;

[TestClass]
public sealed class AuditMiddlewareTests
{
    private static HttpContext NewContext(string path, string method = "GET", int status = 200)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        ctx.Response.StatusCode = status;
        return ctx;
    }

    [TestMethod]
    public async Task Invoke_EndpointNormal_LogueaConMetodoPathYStatus()
    {
        var logger = new Mock<ILogger<AuditMiddleware>>();
        var middleware = new AuditMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(NewContext("/api/sesiones/1"), logger.Object);

        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("AUDIT")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    [DataRow("/api/auth/login")]
    [DataRow("/api/auth/refresh")]
    [DataRow("/api/auth/logout")]
    public async Task Invoke_EndpointsSensibles_NoLoguea(string path)
    {
        var logger = new Mock<ILogger<AuditMiddleware>>();
        var middleware = new AuditMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(NewContext(path), logger.Object);

        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("AUDIT")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [TestMethod]
    public async Task Invoke_UsuarioAutenticado_IncluyeUidYJtiEnLog()
    {
        var logger = new Mock<ILogger<AuditMiddleware>>();
        var middleware = new AuditMiddleware(_ => Task.CompletedTask);
        var ctx = NewContext("/api/sesiones/1");
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("uid", "412"),
            new Claim("jti", "token-xyz")
        }, "TestAuth"));

        await middleware.InvokeAsync(ctx, logger.Object);

        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("user=412")
                                                 && v.ToString()!.Contains("jti=token-xyz")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
