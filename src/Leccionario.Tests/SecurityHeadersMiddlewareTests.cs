using FluentAssertions;
using Leccionario.Api.Middlewares;
using Microsoft.AspNetCore.Http;

namespace Leccionario.Tests;

[TestClass]
public sealed class SecurityHeadersMiddlewareTests
{
    [TestMethod]
    public async Task Invoke_AgregaHeadersDeSeguridadEnRespuesta()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ctx);

        ctx.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        ctx.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        ctx.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
    }
}
