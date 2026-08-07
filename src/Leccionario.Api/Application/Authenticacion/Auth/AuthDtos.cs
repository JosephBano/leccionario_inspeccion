using System.ComponentModel.DataAnnotations;

namespace Leccionario.Api.Application.Authenticacion.Auth;

public sealed class LoginDto
{
    [Required(ErrorMessage = "El usuario es requerido.")]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "El usuario debe tener entre 1 y 20 caracteres.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es requerida.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "La contraseña debe tener entre 1 y 100 caracteres.")]
    public string Password { get; init; } = string.Empty;
}

public sealed class RefreshTokenRequestDto
{
    [Required(ErrorMessage = "El refresh token es requerido.")]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class LogoutRequestDto
{
    [Required(ErrorMessage = "El refresh token es requerido.")]
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>Perfil devuelto en login, refresh y <c>/api/auth/me</c>.</summary>
public sealed class UsuarioDto
{
    public required string IdSigafi { get; init; }
    public required string Nombre { get; init; }
    public string? Email { get; init; }
    public required string TipoUsuario { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}

public sealed class LoginResponseDto
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required int ExpiresIn { get; init; }
    public required UsuarioDto Usuario { get; init; }
}

public sealed class RefreshTokenResponseDto
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required int ExpiresIn { get; init; }
}

/// <summary>
/// Resumen de un paralelo del docente. Shape fijo desde este PR aunque
/// <c>/api/auth/me</c> siempre devuelva la lista vacía hasta que exista el
/// DistributivoGuard (ver docs/superpowers/specs/2026-08-06-auth-login-design.md §3).
/// </summary>
public sealed class ParaleloResumenDto
{
    public required int IdAsignacion { get; init; }
    public required string IdPeriodo { get; init; }
    public required string Asignatura { get; init; }
    public required string TipoLicencia { get; init; }
    public required string Jornada { get; init; }
    public required string Modalidad { get; init; }
    public required string Paralelo { get; init; }
    public DateOnly? FechaInicial { get; init; }
    public DateOnly? FechaFin { get; init; }
    public required int TotalAlumnos { get; init; }
}

/// <summary>Permisos derivados puramente de los roles del JWT — ver docs/03 §4.</summary>
public sealed class PermisosDto
{
    public required bool PuedeEditarAsistencia { get; init; }
    public required bool PuedeCerrarSesion { get; init; }
    public required bool PuedeReabrirSesion { get; init; }
    public required bool PuedeEliminarAsistencia { get; init; }
    public required bool PuedeDescargarReportes { get; init; }
}

public sealed class MiPerfilDto
{
    public required UsuarioDto Usuario { get; init; }
    public required IReadOnlyList<ParaleloResumenDto> Paralelos { get; init; }
    public required PermisosDto Permisos { get; init; }
}
