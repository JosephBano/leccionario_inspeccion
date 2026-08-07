using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// Interfaz del servicio de emisión y validación de access tokens JWT.
/// Implementada por <c>JwtTokenService</c> (mismo archivo, mismo patrón que
/// Bienestar).
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Genera un access token HS256 firmado con el secreto configurado.</summary>
    string GenerateAccessToken(JwtTokenClaims claims);

    /// <summary>Valida el token. Devuelve <c>null</c> si la firma, issuer, audience o expiración no coinciden.</summary>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>Horas de vigencia del access token — usado para calcular ExpiresIn en segundos.</summary>
    int ExpiryHours { get; }
}

/// <summary>Claims de entrada para emitir un access token.</summary>
public sealed class JwtTokenClaims
{
    public required string IdSigafi { get; init; }              // → claim "sub"
    public required int IdUsuario { get; init; }                // → claim "uid"
    public required string Nombre { get; init; }                // → claim "nombre"
    public string? Email { get; init; }                         // → claim "email"
    public required string TipoUsuario { get; init; }           // → claim "tipo_usuario" (alumno/profesor/otros)
    public required IReadOnlyList<string> Roles { get; init; }  // → un claim "role" por cada uno + "codigo_rol" con el primero
    public required string CodigoSistema { get; init; }         // → claim "codigo_sistema" (siempre "cplec")
}

/// <summary>
/// Emisor y validador de access tokens JWT HS256. Port de
/// <c>BienestarInstitucional.Api/Application/Authenticacion/Auth/JwtTokenService.cs</c>
/// con los defaults ajustados a cplec: <c>Issuer="leccionario_conduccion"</c>,
/// <c>Audience="cplec"</c>, expiración 8 h.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private const string DefaultIssuer = "leccionario_conduccion";
    private const string DefaultAudience = "cplec";
    private const int DefaultExpiryHours = 8;

    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryHours;

    public JwtTokenService(string secret, string? issuer = null, string? audience = null, int? expiryHours = null)
    {
        // Mínimo 32 bytes recomendado por OWASP para HS256.
        if (string.IsNullOrEmpty(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new ArgumentException("El secreto JWT debe tener al menos 32 bytes UTF-8.", nameof(secret));

        _secret = secret;
        _issuer = issuer ?? DefaultIssuer;
        _audience = audience ?? DefaultAudience;
        _expiryHours = expiryHours ?? DefaultExpiryHours;
    }

    public int ExpiryHours => _expiryHours;

    public string GenerateAccessToken(JwtTokenClaims claims)
    {
        if (claims.Roles.Count == 0)
            throw new ArgumentException("El usuario debe tener al menos un rol para emitir un token.", nameof(claims));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: BuildClaims(claims),
            notBefore: now,
            expires: now.AddHours(_expiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret))
        };

        try
        {
            return handler.ValidateToken(token, validation, out _);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    private static IEnumerable<Claim> BuildClaims(JwtTokenClaims c)
    {
        // Orden estable. sub/uid/jti primero (identidad), después atributos de negocio.
        yield return new Claim(JwtRegisteredClaimNames.Sub, c.IdSigafi);
        yield return new Claim("uid", c.IdUsuario.ToString());
        yield return new Claim("nombre", c.Nombre);
        if (!string.IsNullOrEmpty(c.Email))
            yield return new Claim(JwtRegisteredClaimNames.Email, c.Email);
        yield return new Claim("tipo_usuario", c.TipoUsuario);
        yield return new Claim("codigo_rol", c.Roles[0]);
        yield return new Claim("codigo_sistema", c.CodigoSistema);
        yield return new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"));

        // Un claim "role" por cada rol RBAC activo. Debe usar el string literal
        // "role" (no ClaimTypes.Role, que serializa como la URI larga
        // http://schemas.../role): la validación JWT tiene MapInboundClaims=false
        // y RoleClaimType="role", así que el nombre debe coincidir exactamente o
        // [Authorize(Roles = "cplec_docente")] nunca encuentra el claim y da 403.
        foreach (var rol in c.Roles)
            yield return new Claim("role", rol);
    }
}
