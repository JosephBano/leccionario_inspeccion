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

    [TestMethod]
    public async Task Get_SinRango_DelegaNulosParaQueElServicioUseLaSemanaEnCurso()
    {
        _serviceMock.Setup(s => s.ObtenerAsync("docente123", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BloqueMiHorarioDto>());

        var controller = CrearController("docente123");
        var response = await controller.Get(null, null, CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        _serviceMock.Verify(
            s => s.ObtenerAsync("docente123", null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Get_SinClaimSub_NoInventaUnDocenteYPropagaEl401()
    {
        // El alcance sale del token y de ningún otro lado: sin `sub` el
        // controlador pasa cadena vacía y el servicio corta con 401
        // (ExceptionClassifier mapea UnauthorizedAccessException → 401).
        _serviceMock.Setup(s => s.ObtenerAsync(string.Empty, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("No se puede determinar el docente autenticado."));

        var controller = CrearController(subClaim: null);

        var act = async () => await controller.Get(null, null, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _serviceMock.Verify(
            s => s.ObtenerAsync(
                It.Is<string>(id => id != string.Empty),
                It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
