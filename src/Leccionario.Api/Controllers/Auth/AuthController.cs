using Leccionario.Api.Application.Authenticacion.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Leccionario.Api.Controllers.Auth;

/// <summary>
/// Login, refresh, logout y perfil del usuario autenticado. Sin
/// <c>try/catch</c> a propósito: las excepciones (<see cref="UnauthorizedAccessException"/>,
/// <c>CuentaInactivaException</c>, <c>SinAccesoSistemaException</c>) las
/// traduce <c>ApiExceptionMiddleware</c>. Ver
/// docs/superpowers/specs/2026-08-06-auth-login-design.md §0e.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginDto dto, CancellationToken ct)
    {
        var respuesta = await _authService.LoginAsync(dto.Username, dto.Password, ObtenerDeviceInfo(), ObtenerIpAddress(), ct);
        return Ok(respuesta);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<RefreshTokenResponseDto>> Refresh([FromBody] RefreshTokenRequestDto dto, CancellationToken ct)
    {
        var respuesta = await _authService.RefreshTokenAsync(dto.RefreshToken, ObtenerDeviceInfo(), ObtenerIpAddress(), ct);
        return Ok(respuesta);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto dto, CancellationToken ct)
    {
        await _authService.LogoutAsync(dto.RefreshToken, ct);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<MiPerfilDto>> Me(CancellationToken ct)
    {
        var uidClaim = User.FindFirst("uid")?.Value;
        if (!int.TryParse(uidClaim, out var idUsuario))
            throw new UnauthorizedAccessException("Token inválido.");

        var perfil = await _authService.ObtenerMiPerfilAsync(idUsuario, ct);
        return Ok(perfil);
    }

    private string? ObtenerDeviceInfo() => Request.Headers["X-Device-Info"].FirstOrDefault();

    private string ObtenerIpAddress()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
            return forwarded.Split(',')[0].Trim();

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
