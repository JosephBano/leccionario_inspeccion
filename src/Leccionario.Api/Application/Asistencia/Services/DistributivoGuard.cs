using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Asistencia.Services;

/// <summary>
/// Implementación de <see cref="IDistributivoGuard"/> basada en
/// <c>sigafi_esContext</c>. Ver <c>ADR-007</c> y <c>docs/03 sección 5.2</c>.
/// </summary>
/// <remarks>
/// <para>Filtro canónico:</para>
/// <code>
/// WHERE idAsignacion = @id
///   AND idProfesor   = @idProfesor       -- del claim sub
///   AND (activo IS NULL OR activo = 1)   -- tolerar NULL legacy
///   AND (esActivaAsignacion IS NULL OR esActivaAsignacion = 1)
/// </code>
/// <para>Sin tracking (no se muta la entidad) y sin caché (revocación
/// inmediata). <c>idProfesor</c> llega ya resuelto por el controller.</para>
/// </remarks>
public sealed class DistributivoGuard : IDistributivoGuard
{
    private readonly sigafi_esContext _db;

    public DistributivoGuard(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<bool> DocenteTieneAsignacionAsync(
        string idProfesor,
        int idAsignacion,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idProfesor))
            return false;

        return await _db.asignaciones_profesores
            .AsNoTracking()
            .AnyAsync(a =>
                a.idAsignacion == idAsignacion
                && a.idProfesor == idProfesor
                && (a.activo == null || a.activo == 1)
                && (a.esActivaAsignacion == null || a.esActivaAsignacion == 1),
                ct);
    }

    public async Task EnsureDocenteTieneAsignacionAsync(
        string idProfesor,
        int idAsignacion,
        bool esInspector,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idProfesor))
            throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");

        if (esInspector)
            return; // Bypass explícito: el inspector ya pasó [Authorize(Roles = "cplec_inspector")].

        var ok = await DocenteTieneAsignacionAsync(idProfesor, idAsignacion, ct);
        if (!ok)
            throw new DistributivoAjenoException();
    }
}