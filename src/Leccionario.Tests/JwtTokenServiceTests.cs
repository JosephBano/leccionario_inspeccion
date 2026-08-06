using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;

namespace Leccionario.Tests;

[TestClass]
public sealed class JwtTokenServiceTests
{
    private const string Secret = "this-is-a-test-secret-32-bytes-min!";
    private static readonly string[] Roles = new[] { "cplec_docente" };

    private static JwtTokenClaims BuildClaims() => new()
    {
        IdSigafi = "1804567890",
        IdUsuario = 412,
        Nombre = "MARIA ELENA TORRES",
        Email = "mtorres@istpet.edu.ec",
        TipoUsuario = "profesor",
        Roles = Roles,
        CodigoSistema = "cplec"
    };

    [TestMethod]
    public void GenerateAccessToken_ConClaimsValidos_ProduceJwtFirmado()
    {
        var svc = new JwtTokenService(Secret);

        var token = svc.GenerateAccessToken(BuildClaims());

        token.Should().NotBeNullOrEmpty();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Issuer.Should().Be("leccionario_conduccion");
        jwt.Audiences.Should().Contain("cplec");
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == "1804567890");
        jwt.Claims.Should().Contain(c => c.Type == "uid" && c.Value == "412");
        jwt.Claims.Should().Contain(c => c.Type == "nombre" && c.Value == "MARIA ELENA TORRES");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "mtorres@istpet.edu.ec");
        jwt.Claims.Should().Contain(c => c.Type == "tipo_usuario" && c.Value == "profesor");
        jwt.Claims.Should().Contain(c => c.Type == "codigo_rol" && c.Value == "cplec_docente");
        jwt.Claims.Should().Contain(c => c.Type == "codigo_sistema" && c.Value == "cplec");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
        // El claim "role" puede venir mapeado a la URL canónica por JwtSecurityTokenHandler
        // (es el comportamiento por defecto al leer el token). Aceptamos ambas formas.
        jwt.Claims.Should().Contain(c =>
            (c.Type == "role" || c.Type == ClaimTypes.Role)
            && c.Value == "cplec_docente");
    }

    [TestMethod]
    public void ValidateToken_TokenValido_DevuelvePrincipal()
    {
        var svc = new JwtTokenService(Secret);
        var token = svc.GenerateAccessToken(BuildClaims());

        var principal = svc.ValidateToken(token);

        principal.Should().NotBeNull();
        principal!.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [TestMethod]
    public void ValidateToken_TokenConSecretoDistinto_DevuelveNull()
    {
        var emisor = new JwtTokenService(Secret);
        var validador = new JwtTokenService("otro-secreto-32-bytes-minimum-xx");
        var token = emisor.GenerateAccessToken(BuildClaims());

        var principal = validador.ValidateToken(token);

        principal.Should().BeNull();
    }

    [TestMethod]
    [DataRow("corto")]                 // 5 bytes
    [DataRow("")]                      // vacío
    public void Constructor_SecretoMenorA32Bytes_Lanza(string secreto)
    {
        Action act = () => new JwtTokenService(secreto);
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void GenerateAccessToken_SinRoles_Lanza()
    {
        var svc = new JwtTokenService(Secret);
        var claims = new JwtTokenClaims
        {
            IdSigafi = "1", IdUsuario = 1, Nombre = "x", TipoUsuario = "profesor",
            Roles = Array.Empty<string>(), CodigoSistema = "cplec"
        };

        Action act = () => svc.GenerateAccessToken(claims);
        act.Should().Throw<ArgumentException>();
    }
}
