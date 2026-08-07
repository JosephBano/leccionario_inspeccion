using System.Security.Claims;
using Leccionario.Api.Application.Asistencia;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Asistencia;

[ApiController]
[Route("api")]
[Authorize(Roles = "cplec_docente,cplec_inspector")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class SesionesController : ControllerBase
{
    private readonly ISesionService _sesiones;
    private readonly IAsistenciaService _asistencia;

    public SesionesController(ISesionService sesiones, IAsistenciaService asistencia)
    {
        _sesiones = sesiones;
        _asistencia = asistencia;
    }

    private string? IdProfesorSub => User.FindFirstValue("sub");
    private bool EsInspector => User.IsInRole("cplec_inspector");
    private string? Rol => EsInspector ? "cplec_inspector" : "cplec_docente";
    private string? IpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Crea una sesión de clase (idempotente por tupla).</summary>
    [HttpPost("paralelos/{idAsignacion:int}/sesiones")]
    [Authorize(Roles = "cplec_docente")]
    [ProducesResponseType(typeof(SesionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(SesionDto), StatusCodes.Status200OK)] // existente (idempotente)
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Crear(int idAsignacion, [FromBody] CrearSesionRequestDto request, CancellationToken ct)
    {
        var sesion = await _sesiones.CrearAsync(idAsignacion, IdProfesorSub ?? throw new UnauthorizedAccessException(), false, request, ct);
        // Idempotente: si ya existía, devolvemos 200 con el idSesion existente.
        return Ok(sesion);
    }

    /// <summary>Obtiene una sesión con sus marcas.</summary>
    [HttpGet("sesiones/{idSesion:int}")]
    [ProducesResponseType(typeof(SesionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int idSesion, CancellationToken ct)
    {
        var sesion = await _sesiones.ObtenerAsync(idSesion, ct);
        return Ok(sesion);
    }

    /// <summary>Edita tema y/u observación. No cambia estado.</summary>
    [HttpPut("sesiones/{idSesion:int}")]
    [ProducesResponseType(typeof(SesionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Editar(int idSesion, [FromBody] EditarSesionRequestDto request, CancellationToken ct)
    {
        var sesion = await _sesiones.EditarAsync(idSesion, IdProfesorSub ?? string.Empty, EsInspector, request, ct);
        return Ok(sesion);
    }

    /// <summary>Cierra la sesión. Docente o inspector.</summary>
    [HttpPost("sesiones/{idSesion:int}/cerrar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cerrar(int idSesion, CancellationToken ct)
    {
        await _sesiones.CerrarAsync(idSesion, IdProfesorSub ?? string.Empty, EsInspector, ct);
        return NoContent();
    }

    /// <summary>Reabre una sesión cerrada. Solo inspector.</summary>
    [HttpPost("sesiones/{idSesion:int}/reabrir")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reabrir(int idSesion, [FromBody] ReabrirSesionRequestDto request, CancellationToken ct)
    {
        await _sesiones.ReabrirAsync(idSesion, IdProfesorSub ?? string.Empty, EsInspector, request, ct);
        return NoContent();
    }

    /// <summary>Registra marcas de asistencia (idempotente).</summary>
    [HttpPost("sesiones/{idSesion:int}/asistencias")]
    [ProducesResponseType(typeof(SesionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegistrarAsistencia(int idSesion, [FromBody] RegistrarAsistenciaRequestDto request, CancellationToken ct)
    {
        var sesion = await _asistencia.RegistrarAsync(idSesion, IdProfesorSub ?? string.Empty, EsInspector, Rol, IpAddress, request, ct);
        return Ok(sesion);
    }
}