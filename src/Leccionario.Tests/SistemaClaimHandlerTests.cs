using System.Security.Claims;
using FluentAssertions;
using Leccionario.Api.Application.Auth.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Leccionario.Tests;

[TestClass]
public sealed class SistemaClaimHandlerTests
{
    private static SistemaClaimAuthorizationHandler NewHandler() => new();

    private static AuthorizationHandlerContext NewContextWithClaim(string? codigoSistema)
    {
        var claims = new List<Claim>();
        if (codigoSistema is not null)
            claims.Add(new Claim("codigo_sistema", codigoSistema));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var requirement = new SistemaClaimRequirement();
        return new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);
    }

    [TestMethod]
    public void Handle_ClaimCplec_Succeede()
    {
        var handler = NewHandler();
        var ctx = NewContextWithClaim("cplec");

        handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }

    [TestMethod]
    public void Handle_ClaimDistintoAFalla()
    {
        var handler = NewHandler();
        var ctx = NewContextWithClaim("bien_proy062026");   // token de Bienestar

        handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [TestMethod]
    public void Handle_SinClaim_Falla()
    {
        var handler = NewHandler();
        var ctx = NewContextWithClaim(null);

        handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [TestMethod]
    public void Handle_ClaimVacio_Falla()
    {
        var handler = NewHandler();
        var ctx = NewContextWithClaim("");

        handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [TestMethod]
    public void Handle_ConCodigoSistemaDistinto_Rechaza()
    {
        var handler = NewHandler();
        var requirement = new SistemaClaimRequirement("otro_sistema");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("codigo_sistema", "otro_sistema") }, "TestAuth"));
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);

        handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }
}
