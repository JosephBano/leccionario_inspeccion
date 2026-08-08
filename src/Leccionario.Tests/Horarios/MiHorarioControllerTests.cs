using System.Security.Claims;
using FluentAssertions;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Controllers.Horarios;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Leccionario.Tests.Horarios;

[TestClass]
public sealed class MiHorarioControllerTests
{
    private readonly Mock<IMiHorarioService> _serviceMock = new();

    private MiHorarioController CrearController(string? subClaim = "docente123")
    {
        var controller = new MiHorarioController(_serviceMock.Object);
        var claims = new List<Claim>();
        if (subClaim != null)
        {
            claims.Add(new Claim("sub", subClaim));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };

        return controller;
    }

    [TestMethod]
    public async Task Get_Devuelve200ConBloquesDelDocente()
    {
        var desde = new DateOnly(2026, 8, 3);
        var hasta = new DateOnly(2026, 8, 9);
        var bloquesEsperados = new List<BloqueMiHorarioDto>
        {
            new(100, "C", "MATUTINA", "A", "Normativa de tránsito", desde, "Lunes", 1, 1, "07:00", "09:00", 2, 120, "Pendiente", null, 2)
        };

        _serviceMock.Setup(s => s.ObtenerAsync("docente123", desde, hasta, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bloquesEsperados);

        var controller = CrearController("docente123");
        var response = await controller.Get(desde, hasta, CancellationToken.None);

        var okResult = response.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        var dto = okResult.Value.Should().BeAssignableTo<IReadOnlyList<BloqueMiHorarioDto>>().Subject;
        dto.Should().HaveCount(1);
        dto[0].IdAsignacion.Should().Be(100);
    }
}
