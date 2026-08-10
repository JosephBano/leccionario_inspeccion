using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Distributivo;

/// <summary>
/// Resuelve, por cada nivel (tipo de licencia) de la carrera 6, los períodos
/// que un docente o inspector debería poder elegir hoy. Ver
/// <c>docs/04-contrato-api.md</c> sección Docente y <c>docs/10</c> sección 5.
/// </summary>
/// <remarks>
/// Implementación en dos pasos (per docs/10):
/// <list type="number">
/// <item>Una consulta agrupa por <c>(idNivel, idPeriodo)</c> con
/// <c>MIN</c>/<c>MAX</c> de fechas y conteos.</item>
/// <item>La ventana de visibilidad (<see cref="EsVisible"/>) se aplica en
/// memoria — son ~6 niveles, unos pocos períodos cada uno.</item>
/// </list>
/// <para>
/// <b>Por qué "por nivel" y no "el período del sistema":</b> los niveles de
/// la carrera 6 corren en calendarios independientes (TIPO "C" puede estar a
/// mitad de período mientras TIPO "E" acaba de cerrar). No existe un período
/// activo único — agrupar por <c>idNivel</c> es la única forma de identificar
/// correctamente cuál período le corresponde a cada tipo de licencia.
/// </para>
/// <para>
/// <b>Qué cuenta como "vigencia" aquí:</b> a diferencia de la primera versión
/// (un solo período, el más reciente, por nivel), ahora se devuelve
/// <i>cada</i> período de un nivel que sea relevante para elegir hoy:
/// <c>VIGENTE</c> (hoy cae dentro de <c>fecha_inicial..fecha_fin</c>),
/// <c>FUTURO</c> (ya cargado en el distributivo pero aún no arranca — debe
/// verse para que el docente/inspector lo anticipe), o <c>CERRADO</c> pero
/// solo si terminó hace <see cref="MesesVisibilidadCierre"/> meses o menos
/// (un período cerrado hace más de eso ya no es útil para el selector y solo
/// agrega ruido). Períodos sin <c>fecha_inicial</c>/<c>fecha_fin</c> (rango
/// NULL, ver CLAUDE.md) se tratan como <c>VIGENTE</c>: sin fechas no hay
/// forma de excluirlos con seguridad, así que se muestran siempre.
/// </para>
/// </remarks>
public interface IPeriodosPorNivelService
{
    /// <summary>
    /// Devuelve, por cada nivel (tipo de licencia) de la carrera 6, los
    /// períodos vigentes, futuros o cerrados recientemente (ver
    /// <see cref="PeriodosPorNivelService.MesesVisibilidadCierre"/>). Puede
    /// haber más de una fila por nivel. Si <paramref name="idProfesor"/> no
    /// es nulo, limita a los niveles en los que el docente tiene distributivo.
    /// </summary>
    Task<IReadOnlyList<PeriodoPorNivelDto>> ResolverAsync(
        string? idProfesor,
        DateOnly? fechaReferencia = null,
        CancellationToken ct = default);
}

public sealed class PeriodosPorNivelService : IPeriodosPorNivelService
{
    private const int CarreraConduccion = 6;

    /// <summary>
    /// Un período CERRADO sigue siendo visible en el selector hasta este
    /// número de meses después de su <c>fecha_fin</c>. Más allá, se oculta.
    /// </summary>
    public const int MesesVisibilidadCierre = 6;

    private readonly sigafi_esContext _db;
    private readonly TimeProvider _reloj;

    public PeriodosPorNivelService(sigafi_esContext db, TimeProvider? reloj = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<PeriodoPorNivelDto>> ResolverAsync(
        string? idProfesor,
        DateOnly? fechaReferencia = null,
        CancellationToken ct = default)
    {
        // Paso 1: agrupar por (idNivel, idPeriodo) con fechas y conteos.
        var grupos = await _db.asignaciones_profesores
            .AsNoTracking()
            .Where(ap =>
                (ap.activo == null || ap.activo == 1)
                && (ap.esActivaAsignacion == null || ap.esActivaAsignacion == 1))
            .Where(ap => _db.cursos.Any(c =>
                c.idNivel == ap.idNivel && c.idCarrera == CarreraConduccion))
            .Where(ap => _db.periodos.Any(p => p.idPeriodo == ap.idPeriodo))
            .Where(ap => idProfesor == null || ap.idProfesor == idProfesor)
            .GroupBy(ap => new
            {
                ap.idNivel,
                ap.idPeriodo,
                Nivel = _db.cursos.Where(c => c.idNivel == ap.idNivel).Select(c => c.Nivel).FirstOrDefault(),
                Detalle = _db.periodos.Where(p => p.idPeriodo == ap.idPeriodo).Select(p => p.detalle).FirstOrDefault(),
                PeriodoFechaInicial = _db.periodos.Where(p => p.idPeriodo == ap.idPeriodo).Select(p => p.fecha_inicial).FirstOrDefault(),
                PeriodoFechaFinal = _db.periodos.Where(p => p.idPeriodo == ap.idPeriodo).Select(p => p.fecha_final).FirstOrDefault()
            })
            .Select(g => new
            {
                g.Key.idNivel,
                g.Key.idPeriodo,
                g.Key.Nivel,
                g.Key.Detalle,
                FechaInicial = g.Key.PeriodoFechaInicial ?? g.Min(ap => ap.fecha_inicial),
                FechaFin = g.Key.PeriodoFechaFinal ?? g.Max(ap => ap.fecha_fin),
                Asignaciones = g.Count(),
                Docentes = g.Select(ap => ap.idProfesor).Distinct().Count()
            })
            .ToListAsync(ct);

        if (grupos.Count == 0)
            return Array.Empty<PeriodoPorNivelDto>();

        // Paso 2: por nivel, quedarse con los períodos visibles hoy (vigentes,
        // futuros, o cerrados dentro de la ventana de gracia). A diferencia de
        // la primera versión, esto puede dejar varios períodos por nivel — p.ej.
        // uno VIGENTE y otro ya FUTURO cargado en el distributivo.
        var referencia = fechaReferencia ?? DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);
        var limiteCierre = referencia.AddMonths(-MesesVisibilidadCierre);

        var visibles = grupos
            .Select(g => new { Grupo = g, Vigencia = CalcularVigencia(g.FechaInicial, g.FechaFin, referencia) })
            .Where(x => EsVisible(x.Vigencia, x.Grupo.FechaFin, limiteCierre))
            .ToList();

        return visibles
            .OrderBy(x => x.Grupo.idNivel)
            .ThenByDescending(x => x.Grupo.FechaFin ?? DateOnly.MinValue)
            .ThenByDescending(x => x.Grupo.FechaInicial ?? DateOnly.MinValue)
            .ThenByDescending(x => x.Grupo.idPeriodo)
            .Select(x => new PeriodoPorNivelDto
            {
                IdNivel = x.Grupo.idNivel,
                TipoLicencia = x.Grupo.Nivel ?? string.Empty,
                IdPeriodo = x.Grupo.idPeriodo,
                Detalle = x.Grupo.Detalle,
                FechaInicial = x.Grupo.FechaInicial,
                FechaFin = x.Grupo.FechaFin,
                Vigencia = x.Vigencia,
                Asignaciones = x.Grupo.Asignaciones,
                Docentes = x.Grupo.Docentes
            })
            .ToList();
    }

    private static string CalcularVigencia(DateOnly? fechaInicial, DateOnly? fechaFin, DateOnly referencia)
    {
        if (fechaInicial.HasValue && referencia < fechaInicial.Value) return "FUTURO";
        if (fechaFin.HasValue && referencia > fechaFin.Value) return "CERRADO";
        return "VIGENTE";
    }

    private static bool EsVisible(string vigencia, DateOnly? fechaFin, DateOnly limiteCierre) =>
        vigencia != "CERRADO" || (fechaFin.HasValue && fechaFin.Value >= limiteCierre);
}