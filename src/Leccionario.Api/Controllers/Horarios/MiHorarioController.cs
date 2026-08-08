using System.Security.Claims;
using Leccionario.Api.Application.Horarios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Horarios;

/// <summary>
/// Horario propio del docente. No recibe idAsignacion: el alcance lo da el
/// claim <c>sub</c>. Ver docs/04 sección Docente.
/// </summary>
[ApiController]
[Route("api/mi-horario")]
[Authorize(Roles = "cplec_docente")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class MiHorarioController : ControllerBase
{
    private readonly IMiHorarioService _miHorario;

    public MiHorarioController(IMiHorarioService miHorario) => _miHorario = miHorario;

    /// <summary>
    /// Bloques de clase del docente en el rango. Sin rango, la semana en curso.
    /// Tope de 16 semanas → 422 RANGO_EXCEDE_TOPE.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BloqueMiHorarioDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) =>
        Ok(await _miHorario.ObtenerAsync(User.FindFirstValue("sub") ?? string.Empty, desde, hasta, ct));
}
