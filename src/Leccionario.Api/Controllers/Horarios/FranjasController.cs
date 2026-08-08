using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Horarios;

/// <summary>
/// Catálogo de franjas horarias de cplec. El docente solo lee; crear, editar y
/// desactivar es del inspector.
/// </summary>
[ApiController]
[Route("api/franjas")]
[Authorize(Roles = "cplec_docente,cplec_inspector")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class FranjasController : ControllerBase
{
    private readonly IFranjaService _franjas;

    public FranjasController(IFranjaService franjas) => _franjas = franjas;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FranjaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await _franjas.ListarAsync(ct));

    [HttpPost]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(FranjaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear([FromBody] CrearFranjaDto request, CancellationToken ct)
    {
        var f = await _franjas.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Listar), new { idhora = f.Idhora }, f);
    }

    [HttpPut("{idhora:int}")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(typeof(FranjaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Actualizar(int idhora, [FromBody] CrearFranjaDto request, CancellationToken ct) =>
        Ok(await _franjas.ActualizarAsync(idhora, request, ct));

    /// <summary>Borrado lógico (`activo = 0`). Rechaza si tiene horario activo.</summary>
    [HttpDelete("{idhora:int}")]
    [Authorize(Roles = "cplec_inspector")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Desactivar(int idhora, CancellationToken ct)
    {
        await _franjas.DesactivarAsync(idhora, ct);
        return NoContent();
    }
}
