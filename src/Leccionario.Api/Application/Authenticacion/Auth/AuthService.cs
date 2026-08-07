using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// Puerto ajustado de <c>BienestarInstitucional.Api/Application/Services/AuthService.cs</c>:
/// solo <c>profesor</c> inicia sesión, sin asignación automática de rol. Ver
/// docs/superpowers/specs/2026-08-06-auth-login-design.md.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly sigafi_esContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly ILogger<AuthService> _logger;
    private readonly string _sistemaCodigo;

    public AuthService(
        sigafi_esContext db,
        IJwtTokenService jwt,
        IRefreshTokenService refreshTokens,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _db = db;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _logger = logger;
        _sistemaCodigo = config["SistemaCodigo"] ?? "cplec";
    }

    public async Task<LoginResponseDto> LoginAsync(string username, string password, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var usuario = await ObtenerOCrearUsuarioAsync(username, password, ct);

        var roles = await CargarRolesCplecAsync(usuario.idUsuario, ct);
        if (roles.Count == 0)
            throw new SinAccesoSistemaException();

        var tipoUsuario = ResolverTipoUsuario(usuario.tablaSigafi);
        var email = await ResolverEmailAsync(usuario, ct);

        var (refreshToken, _) = await _refreshTokens.IssueAsync(usuario.idUsuario, deviceInfo, ipAddress, ct);

        var accessToken = _jwt.GenerateAccessToken(new JwtTokenClaims
        {
            IdSigafi = usuario.idSigafi,
            IdUsuario = usuario.idUsuario,
            Nombre = usuario.nombre ?? usuario.idSigafi,
            Email = email,
            TipoUsuario = tipoUsuario,
            Roles = roles,
            CodigoSistema = _sistemaCodigo
        });

        _logger.LogInformation("Login exitoso para {IdSigafiMask}.", EnmascararIdSigafi(usuario.idSigafi));

        return new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _jwt.ExpiryHours * 3600,
            Usuario = new UsuarioDto
            {
                IdSigafi = usuario.idSigafi,
                Nombre = usuario.nombre ?? usuario.idSigafi,
                Email = email,
                TipoUsuario = tipoUsuario,
                Roles = roles
            }
        };
    }

    public Task<RefreshTokenResponseDto> RefreshTokenAsync(string refreshToken, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
        => throw new NotImplementedException("Se implementa en la Tarea 7.");

    public Task LogoutAsync(string refreshToken, CancellationToken ct = default)
        => throw new NotImplementedException("Se implementa en la Tarea 7.");

    public Task<MiPerfilDto> ObtenerMiPerfilAsync(int idUsuario, CancellationToken ct = default)
        => throw new NotImplementedException("Se implementa en la Tarea 7.");

    // ------------------------------------------------------------------
    // Helpers privados
    // ------------------------------------------------------------------

    private async Task<usuarios> ObtenerOCrearUsuarioAsync(string username, string password, CancellationToken ct)
    {
        var usuario = await _db.usuarios.FirstOrDefaultAsync(u => u.idSigafi == username, ct);

        if (usuario is not null)
        {
            if (!await VerificarCredencialAsync(usuario, password, ct))
                throw new UnauthorizedAccessException("Credenciales inválidas.");

            // Crítico: activo se valida DESPUÉS de la credencial (docs/03 §2).
            if (usuario.activo != 1)
                throw new CuentaInactivaException();

            await MigrarPasswordSiCorrespondeAsync(usuario, password, ct);

            return usuario;
        }

        return await AutoRegistrarProfesorAsync(username, password, ct);
    }

    private async Task<bool> VerificarCredencialAsync(usuarios usuario, string password, CancellationToken ct)
    {
        if (PasswordService.IsHashed(usuario.contrasenia))
        {
            if (PasswordService.Verify(password, usuario.contrasenia))
                return true;
        }
        else if (usuario.contrasenia.Trim() == password)
        {
            return true;
        }

        if (EsProfesor(usuario.tablaSigafi))
            return await MatcheaProfesorLegacyAsync(usuario.idSigafi, password, ct);

        return false;
    }

    private async Task MigrarPasswordSiCorrespondeAsync(usuarios usuario, string password, CancellationToken ct)
    {
        // Solo migra el caso "hash centinela + credencial legacy correcta" —
        // NO el caso "contrasenia en texto plano ya matcheaba" (docs/03 §2).
        if (PasswordService.IsHashed(usuario.contrasenia)
            && EsProfesor(usuario.tablaSigafi)
            && await MatcheaProfesorLegacyAsync(usuario.idSigafi, password, ct))
        {
            usuario.contrasenia = PasswordService.Hash(password);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cuenta {IdSigafiMask} migró su contraseña a bcrypt.", EnmascararIdSigafi(usuario.idSigafi));
        }
    }

    private async Task<bool> MatcheaProfesorLegacyAsync(string idSigafi, string password, CancellationToken ct)
    {
        var profesor = await _db.profesores.AsNoTracking()
            .FirstOrDefaultAsync(p => p.idProfesor == idSigafi && p.esReal == 1, ct);
        return profesor is not null && !string.IsNullOrEmpty(profesor.clave) && profesor.clave.Trim() == password;
    }

    private async Task<usuarios> AutoRegistrarProfesorAsync(string username, string password, CancellationToken ct)
    {
        var profesor = await _db.profesores.AsNoTracking()
            .FirstOrDefaultAsync(p => p.idProfesor == username && p.esReal == 1, ct);

        if (profesor is null || string.IsNullOrEmpty(profesor.clave) || profesor.clave.Trim() != password)
            throw new UnauthorizedAccessException("Credenciales inválidas.");

        var nuevo = new usuarios
        {
            idSigafi = username,
            tablaSigafi = "profesor",
            nombre = FormatearNombreProfesor(profesor),
            contrasenia = PasswordService.Hash(password),
            activo = 1,
            administrador = 0
        };

        _db.usuarios.Add(nuevo);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Profesor {IdSigafiMask} auto-registrado en usuarios (sin rol asignado).", EnmascararIdSigafi(username));

        return nuevo;
    }

    private async Task<List<string>> CargarRolesCplecAsync(int idUsuario, CancellationToken ct)
    {
        return await _db.rbac_rol
            .Where(r => r.rbac_usuario_rol.Any(ur => ur.idUsuario == idUsuario && ur.esActivo == 1))
            .Where(r => r.esActivo == 1 || r.esActivo == null)
            .Where(r => r.rbac_rol_modulo_operacion.Any(rmo =>
                rmo.esActivo == 1
                && rmo.idModulosOperacionesNavigation.idModulosNavigation.id_sistemaNavigation.codigo == _sistemaCodigo))
            .Select(r => r.codigo_rol)
            .Distinct()
            .ToListAsync(ct);
    }

    private async Task<string?> ResolverEmailAsync(usuarios usuario, CancellationToken ct)
    {
        if (!EsProfesor(usuario.tablaSigafi))
            return usuario.emailInstitucional;

        var profesor = await _db.profesores.AsNoTracking()
            .FirstOrDefaultAsync(p => p.idProfesor == usuario.idSigafi, ct);

        return profesor?.emailInstitucional ?? profesor?.email ?? usuario.emailInstitucional;
    }

    private static bool EsProfesor(string tablaSigafi) => tablaSigafi.Trim().Equals("profesor", StringComparison.OrdinalIgnoreCase);

    private static string ResolverTipoUsuario(string tablaSigafi) => tablaSigafi.Trim().ToLowerInvariant() switch
    {
        "alumno" => "alumno",
        "profesor" => "profesor",
        _ => "otros"
    };

    private static string FormatearNombreProfesor(profesores p)
    {
        var apellidos = p.apellidos?.Trim() ?? string.Empty;
        var nombres = p.nombres?.Trim() ?? string.Empty;
        if (apellidos.Length == 0) return nombres;
        if (nombres.Length == 0) return apellidos;
        return $"{apellidos}, {nombres}";
    }

    private static string EnmascararIdSigafi(string idSigafi) =>
        idSigafi.Length <= 4 ? "****" : $"{idSigafi[..2]}****{idSigafi[^2..]}";
}
