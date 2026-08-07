using System.Security.Cryptography;
using System.Text;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// Implementación de <see cref="IRefreshTokenService"/>. Adapta la lógica de
/// rotación + detección de reuso de
/// <c>BienestarInstitucional.Api/.../RefreshTokenService.cs</c> al contrato
/// propio de cplec (ver docs/superpowers/specs/2026-08-06-auth-login-design.md §2).
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    private const int ExpiryDays = 7;
    private const int ClockSkewMinutes = 5;

    private readonly sigafi_esContext _db;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(sigafi_esContext db, ILogger<RefreshTokenService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(string Token, DateTime ExpiresAt)> IssueAsync(int idUsuario, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var token = GenerarTokenSeguro();
        var expiresAt = DateTime.UtcNow.AddDays(ExpiryDays);

        _db.rbac_refresh_tokens.Add(new rbac_refresh_tokens
        {
            idUsuario = idUsuario,
            tokenHash = ComputarHash(token),
            familyId = Guid.NewGuid().ToString("N"),
            sequence = 1,
            deviceInfo = deviceInfo,
            ipAddress = ipAddress,
            createdAt = DateTime.UtcNow,
            expiresAt = expiresAt
        });
        await _db.SaveChangesAsync(ct);

        return (token, expiresAt);
    }

    public async Task<RefreshTokenValidationResult> ValidateAndRotateAsync(string token, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var hash = ComputarHash(token);
        var existente = await _db.rbac_refresh_tokens.FirstOrDefaultAsync(t => t.tokenHash == hash, ct);

        if (existente is null)
        {
            _logger.LogWarning("Refresh token desconocido presentado.");
            return new RefreshTokenValidationResult { Status = RefreshTokenStatus.Invalid };
        }

        if (existente.revokedAt is not null)
        {
            if (existente.revokedReason == RefreshTokenRevokedReason.Rotation)
            {
                _logger.LogWarning("Reuso de refresh token rotado detectado para familia {FamilyId}.", existente.familyId);
                if (existente.familyId is not null)
                    await RevocarFamiliaAsync(existente.familyId, RefreshTokenRevokedReason.ReuseDetected, ct);

                return new RefreshTokenValidationResult { Status = RefreshTokenStatus.ReuseDetected };
            }

            return new RefreshTokenValidationResult { Status = RefreshTokenStatus.Invalid };
        }

        if (existente.expiresAt.AddMinutes(-ClockSkewMinutes) < DateTime.UtcNow)
        {
            return new RefreshTokenValidationResult { Status = RefreshTokenStatus.Invalid };
        }

        var nuevoToken = GenerarTokenSeguro();
        var nuevaExpiracion = DateTime.UtcNow.AddDays(ExpiryDays);

        _db.rbac_refresh_tokens.Add(new rbac_refresh_tokens
        {
            idUsuario = existente.idUsuario,
            tokenHash = ComputarHash(nuevoToken),
            familyId = existente.familyId,
            sequence = (existente.sequence ?? 0) + 1,
            deviceInfo = deviceInfo,
            ipAddress = ipAddress,
            createdAt = DateTime.UtcNow,
            expiresAt = nuevaExpiracion
        });

        existente.revokedAt = DateTime.UtcNow;
        existente.revokedReason = RefreshTokenRevokedReason.Rotation;

        await _db.SaveChangesAsync(ct); // una sola llamada: revocación + alta nueva, atómico

        return new RefreshTokenValidationResult
        {
            Status = RefreshTokenStatus.Ok,
            IdUsuario = existente.idUsuario,
            NewRefreshToken = nuevoToken,
            NewRefreshTokenExpiresAt = nuevaExpiracion
        };
    }

    public async Task RevokeAsync(string token, string reason, CancellationToken ct = default)
    {
        var hash = ComputarHash(token);
        var existente = await _db.rbac_refresh_tokens.FirstOrDefaultAsync(t => t.tokenHash == hash, ct);
        if (existente is null || existente.revokedAt is not null)
            return;

        existente.revokedAt = DateTime.UtcNow;
        existente.revokedReason = reason;
        await _db.SaveChangesAsync(ct);
    }

    private async Task RevocarFamiliaAsync(string familyId, string reason, CancellationToken ct)
    {
        var tokens = await _db.rbac_refresh_tokens
            .Where(t => t.familyId == familyId && t.revokedAt == null)
            .ToListAsync(ct);

        foreach (var t in tokens)
        {
            t.revokedAt = DateTime.UtcNow;
            t.revokedReason = reason;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Revocados {Count} tokens de la familia {FamilyId} por {Reason}.", tokens.Count, familyId, reason);
    }

    private static string GenerarTokenSeguro()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string ComputarHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
