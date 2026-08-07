namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// Contrato del servicio de autenticación. Implementado por <c>AuthService</c>
/// (puerto ajustado de <c>BienestarInstitucional.Api/.../AuthService.cs</c> —
/// ver docs/superpowers/specs/2026-08-06-auth-login-design.md).
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Autentica y, si no existe una fila en <c>usuarios</c> para un profesor
    /// real, la crea (auto-registro). Lanza <see cref="Common.Exceptions.SinAccesoSistemaException"/>
    /// si el usuario no tiene ningún rol <c>cplec_*</c>.
    /// </summary>
    Task<LoginResponseDto> LoginAsync(string username, string password, string? deviceInfo, string? ipAddress, CancellationToken ct = default);

    /// <summary>Rota el refresh token y emite un access token nuevo.</summary>
    Task<RefreshTokenResponseDto> RefreshTokenAsync(string refreshToken, string? deviceInfo, string? ipAddress, CancellationToken ct = default);

    /// <summary>Revoca el refresh token. No lanza si el token ya no existe.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Perfil + permisos del usuario autenticado. <c>Paralelos</c> siempre
    /// vacío hasta que exista el DistributivoGuard.
    /// </summary>
    Task<MiPerfilDto> ObtenerMiPerfilAsync(int idUsuario, CancellationToken ct = default);
}
