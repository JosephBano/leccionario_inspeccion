using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Distributivo;

/// <summary>
/// Devuelve la nómina de alumnos de un paralelo. El docente pasa por
/// <c>DistributivoGuard</c>; el inspector lo consulta sin restricción.
/// Ver <c>docs/04-contrato-api.md</c> § Docente y <c>docs/10</c> § 2.
/// </summary>
public interface INominaAlumnosService
{
    /// <summary>
    /// Devuelve la lista de alumnos matriculados en la tupla de 5 columnas de
    /// la asignación, excluyendo retirados. Lanza
    /// <see cref="NoEncontradoException"/> si la asignación no existe y
    /// <see cref="DistributivoAjenoException"/> si el docente no es dueño.
    /// </summary>
    Task<IReadOnlyList<AlumnoNominaDto>> ResolverAsync(
        int idAsignacion,
        string? idProfesorDocente,
        bool esInspector,
        CancellationToken ct = default);
}

public sealed class NominaAlumnosService : INominaAlumnosService
{
    private readonly sigafi_esContext _db;
    private readonly IDistributivoGuard _guard;

    public NominaAlumnosService(sigafi_esContext db, IDistributivoGuard guard)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public async Task<IReadOnlyList<AlumnoNominaDto>> ResolverAsync(
        int idAsignacion,
        string? idProfesorDocente,
        bool esInspector,
        CancellationToken ct = default)
    {
        // El docente requiere idProfesor del token; el inspector puede no traerlo.
        if (!esInspector && string.IsNullOrWhiteSpace(idProfesorDocente))
            throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");

        // El guard valida el alcance para el docente; el inspector pasa sin consultar.
        await _guard.EnsureDocenteTieneAsignacionAsync(
            idProfesorDocente ?? string.Empty,
            idAsignacion,
            esInspector,
            ct);

        // Resolver la tupla de 5 columnas de la asignación.
        var ap = await _db.asignaciones_profesores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.idAsignacion == idAsignacion, ct)
            ?? throw new NoEncontradoException("La asignación solicitada no existe.");

        // Listar matrículas activas por la tupla completa (5 columnas).
        var matriculas = await _db.matriculas
            .AsNoTracking()
            .Where(m =>
                m.idPeriodo == ap.idPeriodo
                && m.idNivel == ap.idNivel
                && m.idSeccion == ap.idSeccion
                && m.idModalidad == ap.idModalidad
                && (m.paralelo ?? string.Empty).Trim() == ap.paralelo.Trim()
                && (m.retirado == null || m.retirado == false))
            .Join(
                _db.alumnos.AsNoTracking(),
                m => m.idAlumno,
                a => a.idAlumno,
                (m, a) => new { m, a })
            .OrderBy(x => x.a.apellidoPaterno)
            .ThenBy(x => x.a.apellidoMaterno)
            .ThenBy(x => x.a.primerNombre)
            .ThenBy(x => x.a.segundoNombre)
            .Select(x => new AlumnoNominaDto
            {
                IdMatricula = x.m.idMatricula,
                IdAlumno = x.m.idAlumno,
                Apellidos = ((x.a.apellidoPaterno ?? string.Empty) + " " + (x.a.apellidoMaterno ?? string.Empty)).Trim(),
                Nombres = ((x.a.primerNombre ?? string.Empty) + " " + (x.a.segundoNombre ?? string.Empty)).Trim(),
                Retirado = false,
                EsOyente = x.m.esOyente == 1
            })
            .ToListAsync(ct);

        return matriculas;
    }
}