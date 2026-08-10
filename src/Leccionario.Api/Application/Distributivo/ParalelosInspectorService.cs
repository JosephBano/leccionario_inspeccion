using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Distributivo;

/// <param name="Licencia">`cursos.Nivel` — en la carrera 6 es el tipo de licencia.</param>
/// <param name="Jornada">`secciones.seccion` — matutina, vespertina, nocturna.</param>
public sealed record ParaleloDto(
    string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad, string Paralelo,
    string? Licencia, string? Jornada, string? Modalidad, int Asignaciones);

public sealed record AsignacionParaleloDto(
    int IdAsignacion, string Asignatura, string? IdProfesor, string? NombreDocente,
    DateOnly? FechaInicial, DateOnly? FechaFin);

public sealed record PeriodoDto(
    string IdPeriodo, string? Detalle, DateOnly? FechaInicial, DateOnly? FechaFin);

/// <summary>
/// Listado transversal de paralelos de la carrera 6, para el inspector.
/// <c>/api/mis-paralelos</c> está acotado al distributivo del docente.
/// </summary>
public interface IParalelosInspectorService
{
    Task<IReadOnlyList<PeriodoDto>> ListarPeriodosAsync(
        bool soloVigentes = false, CancellationToken ct = default);

    Task<IReadOnlyList<ParaleloDto>> ListarAsync(
        string? idPeriodo, bool soloVigentes, CancellationToken ct = default);

    Task<IReadOnlyList<AsignacionParaleloDto>> ObtenerAsignacionesAsync(
        string idPeriodo, int idNivel, int idSeccion, int idModalidad, string paralelo,
        CancellationToken ct = default);
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
            join p in _db.periodos.AsNoTracking() on ap.idPeriodo equals p.idPeriodo into pJoin
            from p in pJoin.DefaultIfEmpty()
            where cu.idCarrera == IdCarreraConduccion
                  && (idPeriodo == null || ap.idPeriodo == idPeriodo)
                  && (!soloVigentes
                      || idPeriodo != null
                      || (ap.fecha_inicial != null && ap.fecha_fin != null
                          && ap.fecha_inicial <= hoy && hoy <= ap.fecha_fin)
                      || (p != null && p.fecha_inicial != null && p.fecha_final != null
                          && p.fecha_inicial <= hoy && hoy <= p.fecha_final))
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

    public async Task<IReadOnlyList<AsignacionParaleloDto>> ObtenerAsignacionesAsync(
        string idPeriodo, int idNivel, int idSeccion, int idModalidad, string paralelo,
        CancellationToken ct = default)
    {
        var paraleloTrimmed = paralelo?.Trim() ?? string.Empty;

        var items = await (
            from ap in _db.asignaciones_profesores.AsNoTracking()
            join cu in _db.cursos.AsNoTracking() on ap.idNivel equals cu.idNivel
            join a in _db.asignaturas.AsNoTracking() on ap.idAsignatura equals a.idAsignatura
            join pr in _db.profesores.AsNoTracking() on ap.idProfesor equals pr.idProfesor into prJoin
            from pr in prJoin.DefaultIfEmpty()
            where cu.idCarrera == IdCarreraConduccion
                  && ap.idPeriodo == idPeriodo
                  && ap.idNivel == idNivel
                  && ap.idSeccion == idSeccion
                  && ap.idModalidad == idModalidad
                  && (ap.activo == null || ap.activo == 1)
                  && (ap.esActivaAsignacion == null || ap.esActivaAsignacion == 1)
                  && (a.anulada == null || a.anulada == false)
                  && a.asignatura != null
            select new
            {
                ap.idAsignacion,
                ap.paralelo,
                Asignatura = a.asignatura!,
                ap.idProfesor,
                NombreDocente = pr == null ? null : (pr.apellidos + " " + pr.nombres).Trim(),
                ap.fecha_inicial,
                ap.fecha_fin
            }).ToListAsync(ct);

        return items
            .Where(x => (x.paralelo?.Trim() ?? string.Empty) == paraleloTrimmed)
            .OrderBy(x => x.Asignatura)
            .Select(x => new AsignacionParaleloDto(
                x.idAsignacion,
                x.Asignatura,
                x.idProfesor,
                x.NombreDocente,
                x.fecha_inicial,
                x.fecha_fin))
            .ToList();
    }

    public async Task<IReadOnlyList<PeriodoDto>> ListarPeriodosAsync(
        bool soloVigentes = false, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);
        var limiteSeisMeses = hoy.AddMonths(-6);

        var query = from ap in _db.asignaciones_profesores.AsNoTracking()
                    join cu in _db.cursos.AsNoTracking() on ap.idNivel equals cu.idNivel
                    join p in _db.periodos.AsNoTracking() on ap.idPeriodo equals p.idPeriodo into pJoin
                    from p in pJoin.DefaultIfEmpty()
                    where cu.idCarrera == IdCarreraConduccion
                          && (!soloVigentes
                              || (p != null && p.fecha_inicial != null && p.fecha_inicial > hoy)
                              || (ap.fecha_inicial != null && ap.fecha_inicial > hoy)
                              || (ap.fecha_inicial <= hoy && ap.fecha_fin != null && hoy <= ap.fecha_fin)
                              || (p != null && p.fecha_inicial <= hoy && p.fecha_final != null && hoy <= p.fecha_final)
                              || (ap.fecha_fin != null && ap.fecha_fin >= limiteSeisMeses)
                              || (p != null && p.fecha_final != null && p.fecha_final >= limiteSeisMeses))
                    select new
                    {
                        idPeriodo = ap.idPeriodo,
                        detalle = p == null ? null : p.detalle,
                        fecha_inicial = p == null ? null : p.fecha_inicial,
                        fecha_final = p == null ? null : p.fecha_final
                    };

        var periodos = await query.Distinct().ToListAsync(ct);

        return periodos
            .OrderByDescending(p => p.fecha_inicial ?? DateOnly.MinValue)
            .ThenByDescending(p => p.fecha_final ?? DateOnly.MinValue)
            .ThenByDescending(p => p.idPeriodo)
            .Select(p => new PeriodoDto(p.idPeriodo, p.detalle, p.fecha_inicial, p.fecha_final))
            .ToList();
    }
}
