using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Horarios;

/// <summary>Horario del paralelo. El docente solo lee su grid.</summary>
[ApiController]
[Route("api/horarios")]
[Authorize(Roles = "cplec_docente,cplec_inspector")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class HorariosController : ControllerBase
{
    private readonly IHorarioService _horarios;
    private readonly IConflictoHorarioService _conflictos;
    private readonly IHorarioRangoService _rangos;

    public HorariosController(
        IHorarioService horarios,
        IConflictoHorarioService conflictos,
        IHorarioRangoService rangos)
    {
        _horarios = horarios;
        _conflictos = conflictos;
        _rangos = rangos;
    }

    /// <summary>Grid de una semana. El paralelo es la 5-tupla completa.</summary>
    [HttpGet("grid")]
    [ProducesResponseType(typeof(GridDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Grid(
        [FromQuery] string idPeriodo, [FromQuery] int idNivel, [FromQuery] int idSeccion,
        [FromQuery] int idModalidad, [FromQuery] string paralelo, [FromQuery] DateOnly lunes,
        CancellationToken ct) =>
        Ok(await _horarios.ObtenerGridAsync(
            new ParaleloClaveDto(idPeriodo, idNivel, idSeccion, idModalidad, paralelo), lunes, ct));

    [HttpPost("validar-conflicto")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(ResultadoConflictoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidarConflicto(
        [FromBody] SolicitudConflictoDto request, CancellationToken ct) =>
        Ok(await _conflictos.ValidarAsync(request, ct));

    [HttpPost]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(CeldaCreadaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear([FromBody] CrearCeldaDto request, CancellationToken ct)
    {
        var r = await _horarios.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Grid), new { idHorario = r.IdHorario }, r);
    }

    [HttpPut("{idHorario:int}")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(CeldaCreadaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Actualizar(
        int idHorario, [FromBody] CrearCeldaDto request, CancellationToken ct) =>
        Ok(await _horarios.ActualizarAsync(idHorario, request, ct));

    [HttpDelete("{idHorario:int}")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Desactivar(int idHorario, CancellationToken ct)
    {
        await _horarios.DesactivarAsync(idHorario, ct);
        return NoContent();
    }

    /// <summary>
    /// Replica una celda sobre un rango. Rango explícito obligatorio, máximo
    /// 16 semanas y 500 filas por operación (ADR-008 decisión 11).
    /// </summary>
    [HttpPost("replicar-rango")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(ResultadoRangoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReplicarRango(
        [FromBody] OperacionRangoDto request, CancellationToken ct) =>
        Ok(await _rangos.ReplicarAsync(request, ct: ct));

    [HttpPut("rango")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(ResultadoRangoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ActualizarRango(
        [FromBody] OperacionRangoDto request, CancellationToken ct) =>
        Ok(await _rangos.ActualizarAsync(request, ct: ct));

    [HttpDelete("rango")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(ResultadoRangoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> EliminarRango(
        [FromBody] OperacionRangoDto request, CancellationToken ct) =>
        Ok(await _rangos.EliminarAsync(request, ct: ct));
}
