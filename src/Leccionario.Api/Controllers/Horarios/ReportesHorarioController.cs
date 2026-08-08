using Leccionario.Api.Application.Asistencia;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Horarios;

[ApiController]
[Route("api/reportes")]
[Authorize(Roles = "cplec_inspector")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class ReportesHorarioController : ControllerBase
{
    private readonly IAgendaService _agenda;

    public ReportesHorarioController(IAgendaService agenda)
    {
        _agenda = agenda;
    }

    /// <summary>Reporte de sesiones que se cerraron con atraso (esTardia = true).</summary>
    [HttpGet("sesiones-tardias")]
    [ProducesResponseType(typeof(IReadOnlyList<SesionTardiaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SesionesTardias(
        [FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
    {
        return Ok(await _agenda.SesionesTardiasAsync(desde, hasta, ct));
    }

    /// <summary>Reporte de días pasados con horario que aún no tienen sesión registrada.</summary>
    [HttpGet("dias-sin-registrar")]
    [ProducesResponseType(typeof(IReadOnlyList<DiaSinRegistrarDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DiasSinRegistrar(
        [FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct)
    {
        return Ok(await _agenda.DiasSinRegistrarAsync(desde, hasta, ct));
    }
}
