using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Distributivo;

/// <summary>
/// Resuelve el período más reciente por cada nivel (tipo de licencia) de la
/// carrera 6. Ver <c>docs/04-contrato-api.md</c> § Docente y <c>docs/10</c> § 5.
/// </summary>
/// <remarks>
/// Implementación en dos pasos (per docs/10):
/// <list type="number">
/// <item>Una consulta agrupa por <c>(idNivel, idPeriodo)</c> con
/// <c>MIN</c>/<c>MAX</c> de fechas y conteos.</item>
/// <item>El "más reciente por nivel" se elige en memoria — son ~6 niveles.</item>
/// </list>
/// El <c>HAVING</c> correlacionado del SQL original es ilegible en LINQ; el
/// orden de desempate <c>fecha_fin → fecha_inicial → idPeriodo</c> se preserva.
/// </remarks>
public interface IPeriodosPorNivelService
{
    /// <summary>
    /// Devuelve una fila por nivel (tipo de licencia) de la carrera 6, con el
    /// período más reciente de ese nivel. Si <paramref name="idProfesor"/> no
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
                Detalle = _db.periodos.Where(p => p.idPeriodo == ap.idPeriodo).Select(p => p.detalle).FirstOrDefault()
            })
            .Select(g => new
            {
                g.Key.idNivel,
                g.Key.idPeriodo,
                g.Key.Nivel,
                g.Key.Detalle,
                FechaInicial = g.Min(ap => ap.fecha_inicial),
                FechaFin = g.Max(ap => ap.fecha_fin),
                Asignaciones = g.Count(),
                Docentes = g.Select(ap => ap.idProfesor).Distinct().Count()
            })
            .ToListAsync(ct);

        if (grupos.Count == 0)
            return Array.Empty<PeriodoPorNivelDto>();

        // Paso 2: por nivel, elegir el "más reciente" con desempate determinista.
        var referencia = fechaReferencia ?? DateOnly.FromDateTime(_reloj.GetUtcNow().UtcDateTime);

        var masRecientes = grupos
            .GroupBy(g => g.idNivel)
            .Select(nivel => nivel
                .OrderByDescending(g => g.FechaFin ?? DateOnly.MinValue)
                .ThenByDescending(g => g.FechaInicial ?? DateOnly.MinValue)
                .ThenByDescending(g => g.idPeriodo)
                .First())
            .ToList();

        return masRecientes
            .OrderByDescending(g => g.FechaFin ?? DateOnly.MinValue)
            .ThenBy(g => g.idNivel)
            .Select(g => new PeriodoPorNivelDto
            {
                IdNivel = g.idNivel,
                TipoLicencia = g.Nivel ?? string.Empty,
                IdPeriodo = g.idPeriodo,
                Detalle = g.Detalle,
                FechaInicial = g.FechaInicial,
                FechaFin = g.FechaFin,
                Vigencia = CalcularVigencia(g.FechaInicial, g.FechaFin, referencia),
                Asignaciones = g.Asignaciones,
                Docentes = g.Docentes
            })
            .ToList();
    }

    private static string CalcularVigencia(DateOnly? fechaInicial, DateOnly? fechaFin, DateOnly referencia)
    {
        if (fechaInicial.HasValue && referencia < fechaInicial.Value) return "FUTURO";
        if (fechaFin.HasValue && referencia > fechaFin.Value) return "CERRADO";
        return "VIGENTE";
    }
}