using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Distributivo;

/// <param name="Licencia">`cursos.Nivel` — en la carrera 6 es el tipo de licencia.</param>
/// <param name="Jornada">`secciones.seccion` — matutina, vespertina, nocturna.</param>
public sealed record ParaleloDto(
    string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad, string Paralelo,
    string? Licencia, string? Jornada, string? Modalidad, int Asignaciones);

/// <summary>
/// Listado transversal de paralelos de la carrera 6, para el inspector.
/// <c>/api/mis-paralelos</c> está acotado al distributivo del docente.
/// </summary>
public interface IParalelosInspectorService
{
    Task<IReadOnlyList<ParaleloDto>> ListarAsync(
        string? idPeriodo, bool soloVigentes, CancellationToken ct = default);
}

/// <inheritdoc cref="IParalelosInspectorService"/>
/// <remarks>
/// <para>Agrupa por la 5-tupla completa. Con 3 columnas el join devuelve el
/// triple de alumnos, de otras jornadas (ver <c>docs/10</c>).</para>
/// <para><c>soloVigentes</c> usa <c>fecha_inicial .. fecha_fin</c>. Las 737
/// asignaciones activas sin rango quedan fuera de ese filtro: aparecen solo con
/// <c>soloVigentes = false</c>. <c>periodos.activo</c> no sirve para saber el
/// período vigente: está en 1 en casi todos.</para>
/// </remarks>
public sealed class ParalelosInspectorService : IParalelosInspectorService
{
    private const int IdCarreraConduccion = 6;

    private readonly sigafi_esContext _db;
    private readonly TimeProvider _reloj;

    public ParalelosInspectorService(sigafi_esContext db, TimeProvider? reloj = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ParaleloDto>> ListarAsync(
        string? idPeriodo, bool soloVigentes, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);

        var filas = await (
            from ap in _db.asignaciones_profesores.AsNoTracking()
            join cu in _db.cursos.AsNoTracking() on ap.idNivel equals cu.idNivel
            where cu.idCarrera == IdCarreraConduccion
                  && (idPeriodo == null || ap.idPeriodo == idPeriodo)
                  && (!soloVigentes
                      || (ap.fecha_inicial != null && ap.fecha_fin != null
                          && ap.fecha_inicial <= hoy && hoy <= ap.fecha_fin))
            select new
            {
                ap.idPeriodo, ap.idNivel, ap.idSeccion, ap.idModalidad, ap.paralelo, cu.Nivel
            }).ToListAsync(ct);

        var secciones = await _db.secciones.AsNoTracking()
            .ToDictionaryAsync(s => s.idSeccion, s => s.seccion, ct);
        var modalidades = await _db.modalidades.AsNoTracking()
            .ToDictionaryAsync(m => m.idModalidad, m => m.modalidad, ct);

        return filas
            .GroupBy(f => new
            {
                f.idPeriodo, f.idNivel, f.idSeccion, f.idModalidad,
                Paralelo = f.paralelo?.Trim() ?? string.Empty
            })
            .Select(g => new ParaleloDto(
                g.Key.idPeriodo, g.Key.idNivel, g.Key.idSeccion, g.Key.idModalidad, g.Key.Paralelo,
                g.First().Nivel,
                secciones.GetValueOrDefault(g.Key.idSeccion),
                modalidades.GetValueOrDefault(g.Key.idModalidad),
                g.Count()))
            .OrderBy(p => p.IdPeriodo)
            .ThenBy(p => p.Licencia)
            .ThenBy(p => p.Jornada)
            .ThenBy(p => p.Paralelo)
            .ToList();
    }
}
