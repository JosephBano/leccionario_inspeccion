using FluentAssertions;
using Leccionario.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Tests;

[TestClass]
public sealed class HealthControllerTests
{
    [TestMethod]
    public void Get_Devuelve200ConSistemaCplec()
    {
        var controller = new HealthController();

        var result = controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var payload = ok.Value!;
        payload.GetType().GetProperty("status")!.GetValue(payload).Should().Be("ok");
        payload.GetType().GetProperty("sistema")!.GetValue(payload).Should().Be("cplec");
    }
}
