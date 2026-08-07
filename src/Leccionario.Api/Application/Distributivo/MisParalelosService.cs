using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Distributivo;

/// <summary>
/// Lista los paralelos del distributivo de un docente. Ver
/// <c>docs/04-contrato-api.md</c> sección Docente / <c>/api/mis-paralelos</c> y
/// <c>docs/10</c> sección 1.
/// </summary>
public interface IMisParalelosService
{
    /// <summary>
    /// Devuelve las asignaciones activas del docente en su distributivo. Si
    /// <paramref name="idPeriodo"/> es nulo, devuelve todas las activas; si
    /// no, filtra a ese período. Solo se incluyen asignaciones cuya ventana
    /// <c>fecha_inicial..fecha_fin</c> cubre <paramref name="fechaReferencia"/>
    /// (hoy, si es nula), con un margen de gracia de
    /// <see cref="MisParalelosService.DiasGracia"/> días después de
    /// <c>fecha_fin</c>.
    /// </summary>
    Task<IReadOnlyList<MiParaleloDto>> ResolverAsync(
        string idProfesor,
        string? idPeriodo,
        DateOnly? fechaReferencia = null,
        CancellationToken ct = default);
}

public sealed class MisParalelosService : IMisParalelosService
{
    private const int CarreraConduccion = 6;
    private const int DiasGracia = 15;
    private readonly sigafi_esContext _db;
    private readonly TimeProvider _reloj;

    public MisParalelosService(sigafi_esContext db, TimeProvider? reloj = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<MiParaleloDto>> ResolverAsync(
        string idProfesor,
        string? idPeriodo,
        DateOnly? fechaReferencia = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idProfesor))
            throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");

        var referencia = fechaReferencia ?? DateOnly.FromDateTime(_reloj.GetUtcNow().UtcDateTime);

        // Paso 1: traer las asignaciones activas del docente en la carrera 6.
        // El filtro de ventana fecha_inicial..fecha_fin (+ DiasGracia) se aplica
        // después de materializar: Pomelo/MySQL no traduce aritmética de fechas
        // (`DateOnly?.AddDays`) sobre columnas dentro de la consulta (mismo
        // patrón que PeriodosPorNivelService.CalcularVigencia).
        var asignaciones = await (
            from ap in _db.asignaciones_profesores.AsNoTracking()
            join c  in _db.cursos.AsNoTracking() on ap.idNivel equals c.idNivel
            join a  in _db.asignaturas.AsNoTracking() on ap.idAsignatura equals a.idAsignatura
            join s  in _db.secciones.AsNoTracking() on ap.idSeccion equals s.idSeccion
            join mo in _db.modalidades.AsNoTracking() on ap.idModalidad equals mo.idModalidad
            where ap.idProfesor == idProfesor
                  && c.idCarrera == CarreraConduccion
                  && (ap.activo == null || ap.activo == 1)
                  && (ap.esActivaAsignacion == null || ap.esActivaAsignacion == 1)
                  && (idPeriodo == null || ap.idPeriodo == idPeriodo)
                  && (a.anulada == null || a.anulada == false)
                  && a.asignatura != null
            orderby ap.fecha_inicial, a.asignatura, ap.paralelo
            select new AsignacionPlana(
                ap.idAsignacion,
                ap.idPeriodo,
                ap.idNivel,
                ap.idSeccion,
                ap.idModalidad,
                ap.paralelo,
                a.asignatura ?? string.Empty,
                c.Nivel ?? string.Empty,
                s.seccion ?? string.Empty,
                mo.modalidad ?? string.Empty,
                ap.fecha_inicial,
                ap.fecha_fin)
        ).ToListAsync(ct);

        asignaciones = asignaciones
            .Where(a => (a.fecha_inicial is null || referencia >= a.fecha_inicial.Value)
                && (a.fecha_fin is null || referencia <= a.fecha_fin.Value.AddDays(DiasGracia)))
            .ToList();

        if (asignaciones.Count == 0)
            return Array.Empty<MiParaleloDto>();

        // Paso 2: conteo de matrículas activas por tupla de 5 columnas (per docs/10 sección 2).
        var idsAsignacion = asignaciones.Select(a => a.idAsignacion).ToList();
        var asignacionesPorId = await _db.asignaciones_profesores
            .AsNoTracking()
            .Where(ap => idsAsignacion.Contains(ap.idAsignacion))
            .ToListAsync(ct);

        var claves = asignacionesPorId
            .Select(ap => new {
                ap.idAsignacion,
                ap.idPeriodo,
                ap.idNivel,
                ap.idSeccion,
                ap.idModalidad,
                ParaleloTrim = ap.paralelo.Trim()
            })
            .ToList();

        // EF Core no puede traducir `claves.Any(k => ...)` (colección local de
        // tuplas anónimas) contra columnas de `matriculas` — "Primitive
        // collections support has not been enabled". Se filtra en SQL por cada
        // columna por separado (sí traducible, IN estándar) y la coincidencia
        // exacta de la tupla de 5 columnas se hace en memoria.
        var idsPeriodo = claves.Select(k => k.idPeriodo).Distinct().ToList();
        var idsNivel = claves.Select(k => k.idNivel).Distinct().ToList();
        var idsSeccion = claves.Select(k => k.idSeccion).Distinct().ToList();
        var idsModalidad = claves.Select(k => k.idModalidad).Distinct().ToList();

        var matriculasCandidatas = await _db.matriculas
            .AsNoTracking()
            .Where(m => idsPeriodo.Contains(m.idPeriodo)
                && idsNivel.Contains(m.idNivel)
                && idsSeccion.Contains(m.idSeccion)
                && idsModalidad.Contains(m.idModalidad)
                && (m.retirado == null || m.retirado == false))
            .ToListAsync(ct);

        var clavesSet = claves
            .Select(k => (k.idPeriodo, k.idNivel, k.idSeccion, k.idModalidad, k.ParaleloTrim))
            .ToHashSet();

        var matriculas = matriculasCandidatas
            .Where(m => clavesSet.Contains((m.idPeriodo, m.idNivel, m.idSeccion, m.idModalidad, (m.paralelo ?? string.Empty).Trim())))
            .ToList();

        var conteoPorTupla = matriculas
            .GroupBy(m => new {
                m.idPeriodo,
                m.idNivel,
                m.idSeccion,
                m.idModalidad,
                ParaleloTrim = (m.paralelo ?? string.Empty).Trim()
            })
            .ToDictionary(
                g => $"{g.Key.idPeriodo}|{g.Key.idNivel}|{g.Key.idSeccion}|{g.Key.idModalidad}|{g.Key.ParaleloTrim}",
                g => g.Count());

        // Paso 3: sesiones registradas por asignación (conteo simple).
        // Última sesión se completará cuando se implemente el join con fechas_horarios.
        var sesiones = await _db.cplec_sesiones
            .AsNoTracking()
            .Where(s => (s.activo == true) && idsAsignacion.Contains(s.idAsignacion))
            .GroupBy(s => s.idAsignacion)
            .Select(g => new {
                IdAsignacion = g.Key,
                Registradas = g.Count()
            })
            .ToListAsync(ct);

        var sesionesPorAsignacion = sesiones.ToDictionary(s => s.IdAsignacion);

        // Componer resultado.
        return asignaciones.Select(a =>
        {
            var ap = asignacionesPorId.First(x => x.idAsignacion == a.idAsignacion);
            var key = $"{ap.idPeriodo}|{ap.idNivel}|{ap.idSeccion}|{ap.idModalidad}|{ap.paralelo.Trim()}";
            conteoPorTupla.TryGetValue(key, out var totalAlumnos);
            sesionesPorAsignacion.TryGetValue(a.idAsignacion, out var s);
            return new MiParaleloDto
            {
                IdAsignacion = a.idAsignacion,
                IdPeriodo = a.idPeriodo,
                Asignatura = a.Asignatura,
                TipoLicencia = a.TipoLicencia,
                Jornada = a.Jornada,
                Modalidad = a.Modalidad,
                Paralelo = a.Paralelo.Trim(),
                FechaInicial = a.fecha_inicial,
                FechaFin = a.fecha_fin,
                TotalAlumnos = totalAlumnos,
                SesionesRegistradas = s?.Registradas ?? 0,
                UltimaSesion = null
            };
        }).ToList();
    }

    private sealed record AsignacionPlana(
        int idAsignacion,
        string idPeriodo,
        int idNivel,
        int idSeccion,
        int idModalidad,
        string Paralelo,
        string Asignatura,
        string TipoLicencia,
        string Jornada,
        string Modalidad,
        DateOnly? fecha_inicial,
        DateOnly? fecha_fin);
}