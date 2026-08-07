using System.Security.Claims;
using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Controllers.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Leccionario.Tests;

[TestClass]
public sealed class AuthControllerTests
{
    private static AuthController NewController(Mock<IAuthService> mock, DefaultHttpContext? httpContext = null)
    {
        var controller = new AuthController(mock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext ?? new DefaultHttpContext() }
        };
        return controller;
    }

    private static LoginResponseDto SampleLoginResponse() => new()
    {
        AccessToken = "access-token",
        RefreshToken = "refresh-token",
        ExpiresIn = 28800,
        Usuario = new UsuarioDto { IdSigafi = "1804567890", Nombre = "PEREZ, JUAN", TipoUsuario = "profesor", Roles = new[] { "cplec_docente" } }
    };

    [TestMethod]
    public async Task Login_ExtraeDeviceInfoYForwardedFor_LosPasaAlService()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.LoginAsync("1804567890", "clave", "Chrome/Win", "203.0.113.5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleLoginResponse());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Device-Info"] = "Chrome/Win";
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.5, 10.0.0.1";
        var controller = NewController(mock, httpContext);

        var result = await controller.Login(new LoginDto { Username = "1804567890", Password = "clave" }, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<LoginResponseDto>().Which.AccessToken.Should().Be("access-token");
        mock.VerifyAll();
    }

    [TestMethod]
    public async Task Login_SinXForwardedFor_UsaRemoteIpAddress()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), "127.0.0.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleLoginResponse());

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        var controller = NewController(mock, httpContext);

        await controller.Login(new LoginDto { Username = "x", Password = "y" }, CancellationToken.None);

        mock.VerifyAll();
    }

    [TestMethod]
    public async Task Login_SiAuthServiceLanza_LaExcepcionSePropagaSinCapturar()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Credenciales inválidas."));
        var controller = NewController(mock);

        var act = async () => await controller.Login(new LoginDto { Username = "x", Password = "y" }, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task Refresh_DelegaEnAuthServiceYDevuelveOk()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.RefreshTokenAsync("token-viejo", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenResponseDto { AccessToken = "nuevo", RefreshToken = "nuevo-refresh", ExpiresIn = 28800 });
        var controller = NewController(mock);

        var result = await controller.Refresh(new RefreshTokenRequestDto { RefreshToken = "token-viejo" }, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<RefreshTokenResponseDto>().Which.AccessToken.Should().Be("nuevo");
    }

    [TestMethod]
    public async Task Logout_DelegaEnAuthServiceYDevuelveNoContent()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.LogoutAsync("token", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var controller = NewController(mock);

        var result = await controller.Logout(new LogoutRequestDto { RefreshToken = "token" }, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        mock.VerifyAll();
    }

    [TestMethod]
    public async Task Me_LeeClaimUidYDelegaEnAuthService()
    {
        var perfil = new MiPerfilDto
        {
            Usuario = new UsuarioDto { IdSigafi = "1804567890", Nombre = "PEREZ, JUAN", TipoUsuario = "profesor", Roles = new[] { "cplec_docente" } },
            Paralelos = Array.Empty<ParaleloResumenDto>(),
            Permisos = new PermisosDto { PuedeEditarAsistencia = true, PuedeCerrarSesion = true, PuedeReabrirSesion = false, PuedeEliminarAsistencia = false, PuedeDescargarReportes = true }
        };
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.ObtenerMiPerfilAsync(412, It.IsAny<CancellationToken>())).ReturnsAsync(perfil);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", "412") }, "TestAuth"))
        };
        var controller = NewController(mock, httpContext);

        var result = await controller.Me(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(perfil);
    }

    [TestMethod]
    public async Task Me_ClaimUidAusente_LanzaUnauthorized()
    {
        var mock = new Mock<IAuthService>();
        var controller = NewController(mock); // sin claims

        var act = async () => await controller.Me(CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
