using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Asistencia;

/// <summary>
/// Registra marcas de asistencia en una sesión. Idempotente sobre
/// <c>(idSesion, idMatricula)</c>: reenviar la misma lista produce el mismo
/// estado. Ver <c>docs/04-contrato-api.md</c> sección Asistencia y
/// <c>docs/07</c> sección "Asistencia".
/// </summary>
public interface IAsistenciaService
{
    /// <summary>
    /// Registra (o corrige) la lista de marcas de una sesión. Devuelve la
    /// sesión con sus marcas actualizadas. Lanza
    /// <see cref="SesionCerradaException"/> si la sesión está cerrada.
    /// </summary>
    Task<SesionDto> RegistrarAsync(
        int idSesion,
        string idProfesorDocente,
        bool esInspector,
        string? rol,
        string? ipAddress,
        RegistrarAsistenciaRequestDto request,
        CancellationToken ct = default);
}

public sealed class AsistenciaService : IAsistenciaService
{
    private readonly sigafi_esContext _db;
    private readonly IDistributivoGuard _guard;
    private readonly TimeProvider _reloj;

    public AsistenciaService(
        sigafi_esContext db,
        IDistributivoGuard guard,
        TimeProvider? reloj = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<SesionDto> RegistrarAsync(
        int idSesion,
        string idProfesorDocente,
        bool esInspector,
        string? rol,
        string? ipAddress,
        RegistrarAsistenciaRequestDto request,
        CancellationToken ct = default)
    {
        if (!esInspector && string.IsNullOrWhiteSpace(idProfesorDocente))
            throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");
        if (request.Marcas.Count == 0)
            throw new ValidacionException("Debe enviar al menos una marca.");

        // Validar estado de cada marca.
        var idsMatriculaEnBody = new HashSet<int>();
        foreach (var m in request.Marcas)
        {
            if (!EstadoAsistencia.Todos.Contains(m.Estado))
                throw new ValidacionException($"Estado inválido: {m.Estado}.");
            if (m.Estado == EstadoAsistencia.Atraso && m.MinutosAtraso is null)
                throw new ValidacionException("minutosAtraso es obligatorio cuando estado = atraso.");
            if (m.Estado != EstadoAsistencia.Atraso && m.MinutosAtraso.HasValue)
                throw new ValidacionException("minutosAtraso solo se acepta con estado = atraso.");
            idsMatriculaEnBody.Add(m.IdMatricula);
        }

        var sesion = await _db.cplec_sesiones
            .FirstOrDefaultAsync(s => s.idSesion == idSesion && (s.activo == true), ct)
            ?? throw new NoEncontradoException("La sesión solicitada no existe.");

        if (sesion.estado == EstadoSesion.Cerrada)
            throw new SesionCerradaException();

        await _guard.EnsureDocenteTieneAsignacionAsync(
            idProfesorDocente ?? string.Empty,
            sesion.idAsignacion,
            esInspector,
            ct);

        // Validar pertenencia: cada idMatricula pertenece al paralelo.
        var ap = await _db.asignaciones_profesores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.idAsignacion == sesion.idAsignacion, ct)
            ?? throw new NoEncontradoException("La asignación no existe.");

        var matriculasValidas = await _db.matriculas
            .AsNoTracking()
            .Where(m =>
                m.idPeriodo == ap.idPeriodo
                && m.idNivel == ap.idNivel
                && m.idSeccion == ap.idSeccion
                && m.idModalidad == ap.idModalidad
                && (m.paralelo ?? string.Empty).Trim() == ap.paralelo.Trim())
            .Select(m => m.idMatricula)
            .ToListAsync(ct);

        var idsValidos = new HashSet<int>(matriculasValidas);
        var ofensoras = idsMatriculaEnBody.Where(id => !idsValidos.Contains(id)).ToList();
        if (ofensoras.Count > 0)
            throw new ValidacionException("MATRICULA_AJENA", ofensoras);

        // Procesar marcas en una transacción. Idempotente.
        var marcasExistentes = await _db.cplec_asistencias
            .Where(a => a.idSesion == idSesion && idsMatriculaEnBody.Contains(a.idMatricula))
            .ToListAsync(ct);
        var marcasPorMatricula = marcasExistentes.ToDictionary(m => m.idMatricula);

        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var nuevosHistoriales = new List<cplec_asistencias_historial>();

        foreach (var m in request.Marcas)
        {
            if (marcasPorMatricula.TryGetValue(m.IdMatricula, out var existente))
            {
                if (existente.estado == m.Estado
                    && existente.minutosAtraso == m.MinutosAtraso
                    && existente.observacion == m.Observacion)
                {
                    continue; // Sin cambios.
                }

                nuevosHistoriales.Add(new cplec_asistencias_historial
                {
                    idAsistencia = existente.idAsistencia,
                    idSesion = existente.idSesion,
                    idMatricula = existente.idMatricula,
                    estadoAnterior = existente.estado,
                    estadoNuevo = m.Estado,
                    motivo = request.Motivo,
                    usuario = idProfesorDocente ?? string.Empty,
                    rol = rol,
                    ipAddress = ipAddress,
                    fecha = ahora
                });

                existente.estado = m.Estado;
                existente.minutosAtraso = m.MinutosAtraso;
                existente.observacion = m.Observacion;
                existente.usuarioActualiza = idProfesorDocente;
                existente.fechaActualizacion = ahora;
            }
            else
            {
                var nueva = new cplec_asistencias
                {
                    idSesion = idSesion,
                    idMatricula = m.IdMatricula,
                    estado = m.Estado,
                    minutosAtraso = m.MinutosAtraso,
                    observacion = m.Observacion,
                    usuarioCreacion = idProfesorDocente ?? string.Empty,
                    fechaCreacion = ahora
                };
                _db.cplec_asistencias.Add(nueva);
                await _db.SaveChangesAsync(ct); // necesario para idAsistencia.

                nuevosHistoriales.Add(new cplec_asistencias_historial
                {
                    idAsistencia = nueva.idAsistencia,
                    idSesion = idSesion,
                    idMatricula = m.IdMatricula,
                    estadoAnterior = null,
                    estadoNuevo = m.Estado,
                    motivo = request.Motivo,
                    usuario = idProfesorDocente ?? string.Empty,
                    rol = rol,
                    ipAddress = ipAddress,
                    fecha = ahora
                });
            }
        }

        if (nuevosHistoriales.Count > 0)
            _db.cplec_asistencias_historial.AddRange(nuevosHistoriales);

        sesion.usuarioActualiza = idProfesorDocente;
        sesion.fechaActualizacion = ahora;
        await _db.SaveChangesAsync(ct);

        return await MapearSesionAsync(idSesion, ct);
    }

    private async Task<SesionDto> MapearSesionAsync(int idSesion, CancellationToken ct)
    {
        var s = await _db.cplec_sesiones
            .AsNoTracking()
            .FirstAsync(s => s.idSesion == idSesion, ct);

        var fecha = await _db.fechas_horarios
            .Where(f => f.idFecha == s.idFecha)
            .Select(f => f.fecha)
            .FirstAsync(ct);

        var marcas = await _db.cplec_asistencias
            .AsNoTracking()
            .Where(a => a.idSesion == idSesion)
            .Select(a => new MarcaSesionDto
            {
                IdAsistencia = a.idAsistencia,
                IdMatricula = a.idMatricula,
                Estado = a.estado,
                MinutosAtraso = a.minutosAtraso,
                Observacion = a.observacion
            })
            .ToListAsync(ct);

        return new SesionDto
        {
            IdSesion = s.idSesion,
            IdAsignacion = s.idAsignacion,
            Fecha = fecha ?? default,
            NumeroBloque = s.numeroBloque,
            Tema = s.tema,
            Observacion = s.observacion,
            Estado = s.estado,
            FechaCierre = s.fechaCierre,
            Asistencias = marcas
        };
    }
}