namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>Resultado de validar y rotar un refresh token.</summary>
public enum RefreshTokenStatus
{
    /// <summary>Rotación exitosa. <c>NewRefreshToken</c> contiene el nuevo token en claro.</summary>
    Ok,
    /// <summary>El token es desconocido, ya estaba revocado, o expiró.</summary>
    Invalid,
    /// <summary>Se detectó reuso de un token previamente revocado. Toda la familia queda revocada.</summary>
    ReuseDetected
}

public sealed class RefreshTokenValidationResult
{
    public required RefreshTokenStatus Status { get; init; }
    public int? IdUsuario { get; init; }
    public string? NewRefreshToken { get; init; }
    public DateTime? NewRefreshTokenExpiresAt { get; init; }
}

/// <summary>
/// Contrato del servicio de refresh tokens. La implementación real requiere
/// <c>sigafi_esContext</c> y la entidad <c>RbacRefreshTokens</c> — ambos llegan
/// con el PR <c>feature/ef-powertools-scaffold</c> (#2).
/// </summary>
/// <remarks>
/// En el scaffold esta interfaz se declara pero no se inyecta en el container:
/// el <c>AuthService</c> (PR <c>feature/auth-login</c>) la recibirá cuando
/// exista la implementación. Mientras tanto, ningún endpoint la consume.
/// </remarks>
public interface IRefreshTokenService
{
    /// <summary>Genera un nuevo refresh token, lo persiste hasheado y lo devuelve en claro.</summary>
    Task<(string Token, DateTime ExpiresAt)> IssueAsync(int idUsuario, string? deviceInfo, string? ipAddress, CancellationToken ct = default);

    /// <summary>Valida un refresh token, lo rota y devuelve el nuevo. Detecta reuso proactivamente.</summary>
    Task<RefreshTokenValidationResult> ValidateAndRotateAsync(string token, string? deviceInfo, string? ipAddress, CancellationToken ct = default);

    /// <summary>Revoca el refresh token de un usuario (logout).</summary>
    Task RevokeAsync(string token, string reason, CancellationToken ct = default);
}
