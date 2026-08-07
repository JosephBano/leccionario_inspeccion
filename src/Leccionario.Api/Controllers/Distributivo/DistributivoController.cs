using System.Security.Claims;
using Leccionario.Api.Application.Distributivo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Leccionario.Api.Controllers.Distributivo;

[ApiController]
[Route("api")]
[Authorize(Roles = "cplec_docente,cplec_inspector")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class DistributivoController : ControllerBase
{
    private readonly IPeriodosPorNivelService _periodos;
    private readonly IMisParalelosService _misParalelos;
    private readonly INominaAlumnosService _nomina;

    public DistributivoController(
        IPeriodosPorNivelService periodos,
        IMisParalelosService misParalelos,
        INominaAlumnosService nomina)
    {
        _periodos = periodos;
        _misParalelos = misParalelos;
        _nomina = nomina;
    }

    private string? IdProfesorSub => User.FindFirstValue("sub");

    private bool EsInspector => User.IsInRole("cplec_inspector");

    /// <summary>
    /// Una fila por nivel (tipo de licencia) de la carrera 6, con el período
    /// más reciente de cada nivel. Para un docente, se limita a los niveles
    /// en los que tiene distributivo. Inspector: ve todos.
    /// </summary>
    [HttpGet("periodos/por-nivel")]
    [ProducesResponseType(typeof(PeriodosPorNivelResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PeriodosPorNivel(CancellationToken ct)
    {
        var idProfesor = EsInspector ? null : IdProfesorSub;
        var items = await _periodos.ResolverAsync(idProfesor, fechaReferencia: null, ct);
        return Ok(new PeriodosPorNivelResponseDto(items));
    }

    /// <summary>
    /// Paralelos del distributivo del docente del token. Si se omite
    /// <c>idPeriodo</c>, devuelve todos los activos.
    /// </summary>
    [HttpGet("mis-paralelos")]
    [Authorize(Roles = "cplec_docente")]
    [ProducesResponseType(typeof(MisParalelosResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MisParalelos([FromQuery] string? idPeriodo, CancellationToken ct)
    {
        var idProfesor = IdProfesorSub
            ?? throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");
        var items = await _misParalelos.ResolverAsync(idProfesor, idPeriodo, ct: ct);
        return Ok(new MisParalelosResponseDto(items));
    }

    /// <summary>
    /// Nómina de alumnos de un paralelo. Docente: solo suyos (validado por
    /// <c>DistributivoGuard</c>). Inspector: cualquiera.
    /// </summary>
    [HttpGet("paralelos/{idAsignacion:int}/alumnos")]
    [ProducesResponseType(typeof(IReadOnlyList<AlumnoNominaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Alumnos(int idAsignacion, CancellationToken ct)
    {
        var idProfesor = EsInspector ? null : IdProfesorSub;
        var items = await _nomina.ResolverAsync(idAsignacion, idProfesor, EsInspector, ct);
        return Ok(items);
    }
}

public sealed record PeriodosPorNivelResponseDto(IReadOnlyList<PeriodoPorNivelDto> Items);

public sealed record MisParalelosResponseDto(IReadOnlyList<MiParaleloDto> Items);