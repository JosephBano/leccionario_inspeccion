using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Leccionario.Tests;

[TestClass]
public sealed class RefreshTokenServiceTests
{
    private static sigafi_esContext NewDb()
    {
        var options = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(databaseName: $"refreshtokentest-{Guid.NewGuid()}")
            .Options;
        return new sigafi_esContext(options);
    }

    private static RefreshTokenService NewService(sigafi_esContext db) =>
        new(db, NullLogger<RefreshTokenService>.Instance);

    [TestMethod]
    public async Task IssueAsync_CreaTokenYLoPersisteHasheado()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var (token, expiresAt) = await svc.IssueAsync(idUsuario: 1, deviceInfo: "Chrome/Win", ipAddress: "10.0.0.1");

        token.Should().NotBeNullOrEmpty();
        expiresAt.Should().BeAfter(DateTime.UtcNow);

        var fila = await db.rbac_refresh_tokens.SingleAsync();
        fila.idUsuario.Should().Be(1);
        fila.tokenHash.Should().NotBe(token); // nunca se guarda el token en claro
        fila.deviceInfo.Should().Be("Chrome/Win");
        fila.ipAddress.Should().Be("10.0.0.1");
        fila.revokedAt.Should().BeNull();
        fila.familyId.Should().NotBeNullOrEmpty();
        fila.sequence.Should().Be(1u);
    }

    [TestMethod]
    public async Task ValidateAndRotateAsync_TokenDesconocido_DevuelveInvalid()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var result = await svc.ValidateAndRotateAsync("token-que-no-existe", null, null);

        result.Status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [TestMethod]
    public async Task ValidateAndRotateAsync_TokenValido_RotaYDevuelveNuevoToken()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var (token, _) = await svc.IssueAsync(idUsuario: 7, null, null);

        var result = await svc.ValidateAndRotateAsync(token, "Chrome/Win", "10.0.0.2");

        result.Status.Should().Be(RefreshTokenStatus.Ok);
        result.IdUsuario.Should().Be(7);
        result.NewRefreshToken.Should().NotBeNullOrEmpty().And.NotBe(token);

        var filas = await db.rbac_refresh_tokens.OrderBy(t => t.idRefreshToken).ToListAsync();
        filas.Should().HaveCount(2);
        filas[0].revokedAt.Should().NotBeNull();
        filas[0].revokedReason.Should().Be(RefreshTokenRevokedReason.Rotation);
        filas[1].revokedAt.Should().BeNull();
        filas[1].familyId.Should().Be(filas[0].familyId);
        filas[1].sequence.Should().Be(2u);
    }

    [TestMethod]
    public async Task ValidateAndRotateAsync_TokenExpirado_DevuelveInvalid()
    {
        using var db2 = NewDb();
        var svc2 = NewService(db2);
        var (token, _) = await svc2.IssueAsync(idUsuario: 3, null, null);
        var fila = await db2.rbac_refresh_tokens.SingleAsync();
        fila.expiresAt = DateTime.UtcNow.AddMinutes(-10);
        await db2.SaveChangesAsync();

        var result = await svc2.ValidateAndRotateAsync(token, null, null);

        result.Status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [TestMethod]
    public async Task ValidateAndRotateAsync_TokenYaRotado_DevuelveReuseDetectedYRevocaFamilia()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var (tokenOriginal, _) = await svc.IssueAsync(idUsuario: 9, null, null);
        var primeraRotacion = await svc.ValidateAndRotateAsync(tokenOriginal, null, null);
        primeraRotacion.Status.Should().Be(RefreshTokenStatus.Ok);

        // Alguien presenta el token ORIGINAL de nuevo — ya fue rotado. Reuso.
        var reuso = await svc.ValidateAndRotateAsync(tokenOriginal, null, null);

        reuso.Status.Should().Be(RefreshTokenStatus.ReuseDetected);

        var todas = await db.rbac_refresh_tokens.ToListAsync();
        todas.Should().AllSatisfy(t => t.revokedAt.Should().NotBeNull());
        todas.Should().Contain(t => t.revokedReason == RefreshTokenRevokedReason.ReuseDetected);
    }

    [TestMethod]
    public async Task RevokeAsync_TokenValido_LoMarcaRevocado()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var (token, _) = await svc.IssueAsync(idUsuario: 5, null, null);

        await svc.RevokeAsync(token, RefreshTokenRevokedReason.Logout);

        var fila = await db.rbac_refresh_tokens.SingleAsync();
        fila.revokedAt.Should().NotBeNull();
        fila.revokedReason.Should().Be(RefreshTokenRevokedReason.Logout);
    }

    [TestMethod]
    public async Task RevokeAsync_TokenDesconocido_NoLanza()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var act = async () => await svc.RevokeAsync("no-existe", RefreshTokenRevokedReason.Logout);

        await act.Should().NotThrowAsync();
    }
}
