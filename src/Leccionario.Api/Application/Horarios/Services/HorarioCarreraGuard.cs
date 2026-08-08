using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Frontera de propiedad de cplec sobre la tabla compartida `horario_detalle`:
/// solo se escriben filas cuya asignación pertenece a la carrera 6.
/// Ver <c>ADR-008</c> decisión 2 y <c>docs/10</c>.
/// </summary>
public interface IHorarioCarreraGuard
{
    /// <summary>¿La asignación pertenece a la Escuela de Conducción?</summary>
    Task<bool> AsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default);

    /// <summary>
    /// Igual que el anterior pero lanza <see cref="FueraDeAlcanceException"/>.
    /// Se invoca antes de TODO INSERT y UPDATE sobre `horario_detalle`.
    /// </summary>
    Task EnsureAsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default);
}

/// <inheritdoc cref="IHorarioCarreraGuard"/>
/// <remarks>
/// <para>Camino canónico, verificado en <c>docs/10</c>:</para>
/// <code>
/// asignaciones_profesores.idAsignacion → cursos.idNivel → cursos.idCarrera = 6
/// </code>
/// <para><b>No hay bypass de inspector.</b> Este guard no recibe <c>esInspector</c>:
/// es una frontera de datos, no de rol. Un inspector tampoco puede escribir el
/// horario de Gastronomía.</para>
/// <para>Si el <c>idNivel</c> de la asignación no tiene fila en <c>cursos</c>, se
/// niega: no se puede probar la pertenencia, y ante la duda no se escribe en una
/// tabla de otro sistema.</para>
/// </remarks>
public sealed class HorarioCarreraGuard : IHorarioCarreraGuard
{
    /// <summary>Escuela de Conducción. Ver <c>docs/10</c>.</summary>
    public const int IdCarreraConduccion = 6;

    private readonly sigafi_esContext _db;

    public HorarioCarreraGuard(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<bool> AsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default) =>
        _db.asignaciones_profesores
            .AsNoTracking()
            .Join(_db.cursos.AsNoTracking(),
                  ap => ap.idNivel,
                  c => c.idNivel,
                  (ap, c) => new { ap.idAsignacion, c.idCarrera })
            .AnyAsync(x => x.idAsignacion == idAsignacion
                        && x.idCarrera == IdCarreraConduccion, ct);

    public async Task EnsureAsignacionEsDeCarrera6Async(int idAsignacion, CancellationToken ct = default)
    {
        if (!await AsignacionEsDeCarrera6Async(idAsignacion, ct))
            throw new FueraDeAlcanceException();
    }
}
