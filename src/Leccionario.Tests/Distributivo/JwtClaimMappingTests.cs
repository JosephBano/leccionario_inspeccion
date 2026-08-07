using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Controllers.Distributivo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MiParaleloDto = Leccionario.Api.Application.Distributivo.MiParaleloDto;

namespace Leccionario.Tests.Distributivo;

/// <summary>
/// Regresión: <c>User.FindFirstValue("sub")</c> debe devolver el idProfesor
/// del token. Si la config JWT tiene <c>MapInboundClaims=true</c> (default),
/// ASP.NET Core mapea <c>sub → ClaimTypes.NameIdentifier</c> y
/// <c>FindFirstValue("sub")</c> devuelve null, lo que rompe
/// <see cref="DistributivoController.MisParalelos"/> y todos los endpoints
/// que dependen del idProfesor del token.
///
/// Ver: <c>Extensions/DependencyInjectionExtensions.AddJwtAuthentication</c>
/// debe tener <c>MapInboundClaims = false</c>.
/// </summary>
[TestClass]
public sealed class JwtClaimMappingTests
{
    private static ControllerContext ContextoComoDocente(string idProfesor)
    {
        var claims = new[]
        {
            new Claim("sub", idProfesor),
            new Claim("uid", "580"),
            new Claim("nombre", "DOCENTE PRUEBA"),
            new Claim("tipo_usuario", "profesor"),
            new Claim("codigo_rol", "cplec_docente"),
            new Claim("codigo_sistema", "cplec"),
            new Claim("role", "cplec_docente"),
        };
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };
    }

    [TestMethod]
    public async Task MisParalelos_ConClaimSubPlano_DevuelveIdProfesorDelToken()
    {
        var misParalelos = new Mock<IMisParalelosService>();
        misParalelos
            .Setup(s => s.ResolverAsync("1804991527", null, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MiParaleloDto>)Array.Empty<MiParaleloDto>());

        var periodos = new Mock<IPeriodosPorNivelService>();
        var nomina = new Mock<INominaAlumnosService>();

        var controller = new DistributivoController(periodos.Object, misParalelos.Object, nomina.Object)
        {
            ControllerContext = ContextoComoDocente("1804991527"),
        };

        var result = await controller.MisParalelos(idPeriodo: null, ct: CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        misParalelos.Verify(s => s.ResolverAsync("1804991527", null, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
