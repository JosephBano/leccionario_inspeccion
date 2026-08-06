using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers;

/// <summary>Health check básico. Anónimo, sin auth, sin rate limit.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>200 OK si la API está levantada.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        sistema = "cplec",
        timestamp = DateTime.UtcNow.ToString("O")
    });
}
