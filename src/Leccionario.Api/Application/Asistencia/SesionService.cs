using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Asistencia;

/// <summary>
/// Crea, edita, cierra y reabre sesiones de clase. Ver
/// <c>docs/04-contrato-api.md</c> sección Sesiones y <c>docs/10</c> sección 4.
/// </summary>
public interface ISesionService
{
    /// <summary>
    /// Crea una sesión nueva. Idempotente: si ya existe la tupla
    /// <c>(idAsignacion, idFecha, numeroBloque)</c>, devuelve la existente con
    /// <c>idSesion</c> poblado. Lanza <see cref="FueraDeVentanaException"/> si
    /// la fecha cae fuera del rango de la asignación.
    /// </summary>
    Task<SesionDto> CrearAsync(
        int idAsignacion,
        string idProfesorDocente,
        bool esInspector,
        CrearSesionRequestDto request,
        CancellationToken ct = default);

    /// <summary>Devuelve la sesión con su lista completa de marcas.</summary>
    Task<SesionDto> ObtenerAsync(int idSesion, CancellationToken ct = default);

    /// <summary>Edita tema/observacion. No cambia estado.</summary>
    Task<SesionDto> EditarAsync(
        int idSesion,
        string idProfesorDocente,
        bool esInspector,
        EditarSesionRequestDto request,
        CancellationToken ct = default);

    /// <summary>Cierra la sesión. Docente o inspector.</summary>
    Task CerrarAsync(
        int idSesion,
        string idProfesorDocente,
        bool esInspector,
        CancellationToken ct = default);

    /// <summary>Reabre una sesión cerrada. Solo inspector, con motivo obligatorio.</summary>
    Task ReabrirAsync(
        int idSesion,
        string idProfesorDocente,
        bool esInspector,
        ReabrirSesionRequestDto request,
        CancellationToken ct = default);
}

public sealed class SesionService : ISesionService
{
    private readonly sigafi_esContext _db;
    private readonly IDistributivoGuard _guard;
    private readonly INominaAlumnosService _nomina;
    private readonly TimeProvider _reloj;

    public SesionService(
        sigafi_esContext db,
        IDistributivoGuard guard,
        INominaAlumnosService nomina,
        TimeProvider? reloj = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _nomina = nomina ?? throw new ArgumentNullException(nameof(nomina));
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<SesionDto> CrearAsync(
        int idAsignacion,
        string idProfesorDocente,
        bool esInspector,
        CrearSesionRequestDto request,
        CancellationToken ct = default)
    {
        if (!esInspector && string.IsNullOrWhiteSpace(idProfesorDocente))
            throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");
        if (string.IsNullOrWhiteSpace(request.Tema))
            throw new ValidacionException("El tema es obligatorio.");

        await _guard.EnsureDocenteTieneAsignacionAsync(
            idProfesorDocente ?? string.Empty,
            idAsignacion,
            esInspector,
            ct);

        // Resolver idFecha desde fechas_horarios (calendario institucional).
        var idFecha = await _db.fechas_horarios
            .Where(f => f.fecha == request.Fecha)
            .Select(f => (int?)f.idFecha)
            .FirstOrDefaultAsync(ct);

        if (idFecha is null)
            throw new ValidacionException($"La fecha {request.Fecha:yyyy-MM-dd} no existe en el calendario institucional.");

        // Validar ventana de la asignación.
        var ap = await _db.asignaciones_profesores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.idAsignacion == idAsignacion, ct)
            ?? throw new NoEncontradoException("La asignación solicitada no existe.");

        if (ap.fecha_inicial.HasValue && request.Fecha < ap.fecha_inicial.Value
            || ap.fecha_fin.HasValue && request.Fecha > ap.fecha_fin.Value)
        {
            throw new FueraDeVentanaException();
        }

        // Idempotencia: si ya existe la tupla, devolver la existente.
        var existente = await _db.cplec_sesiones
            .FirstOrDefaultAsync(s =>
                s.idAsignacion == idAsignacion
                && s.idFecha == idFecha.Value
                && s.numeroBloque == request.NumeroBloque, ct);

        if (existente is not null)
            return await MapearSesionAsync(existente.idSesion, ct);

        // Crear nueva sesión precargada con la nómina en "presente".
        var sesion = new cplec_sesiones
        {
            idAsignacion = idAsignacion,
            idFecha = idFecha.Value,
            numeroBloque = request.NumeroBloque,
            tema = request.Tema.Trim(),
            observacion = request.Observacion?.Trim(),
            estado = EstadoSesion.Borrador,
            activo = true,
            usuarioCreacion = idProfesorDocente ?? string.Empty,
            fechaCreacion = _reloj.GetUtcNow().UtcDateTime
        };
        _db.cplec_sesiones.Add(sesion);
        await _db.SaveChangesAsync(ct);

        // Precargar marcas en "presente" para los alumnos matriculados.
        var nomina = await _nomina.ResolverAsync(idAsignacion, idProfesorDocente, esInspector, ct);
        if (nomina.Count > 0)
        {
            foreach (var alumno in nomina)
            {
                _db.cplec_asistencias.Add(new cplec_asistencias
                {
                    idSesion = sesion.idSesion,
                    idMatricula = alumno.IdMatricula,
                    estado = EstadoAsistencia.Presente,
                    usuarioCreacion = idProfesorDocente ?? string.Empty,
                    fechaCreacion = _reloj.GetUtcNow().UtcDateTime
                });
            }
            await _db.SaveChangesAsync(ct);
        }

        return await MapearSesionAsync(sesion.idSesion, ct);
    }

    public async Task<SesionDto> ObtenerAsync(int idSesion, CancellationToken ct = default)
    {
        var sesion = await _db.cplec_sesiones
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.idSesion == idSesion && (s.activo == true), ct)
            ?? throw new NoEncontradoException("La sesión solicitada no existe.");

        return await MapearSesionAsync(sesion.idSesion, ct);
    }

    public async Task<SesionDto> EditarAsync(
        int idSesion,
        string idProfesorDocente,
        bool esInspector,
        EditarSesionRequestDto request,
        CancellationToken ct = default)
    {
        var sesion = await _db.cplec_sesiones
            .FirstOrDefaultAsync(s => s.idSesion == idSesion && (s.activo == true), ct)
            ?? throw new NoEncontradoException("La sesión solicitada no existe.");

        await _guard.EnsureDocenteTieneAsignacionAsync(
            idProfesorDocente ?? string.Empty,
            sesion.idAsignacion,
            esInspector,
            ct);

        if (request.Tema is not null) sesion.tema = request.Tema.Trim();
        if (request.Observacion is not null) sesion.observacion = request.Observacion.Trim();
        sesion.usuarioActualiza = idProfesorDocente;
        sesion.fechaActualizacion = _reloj.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(ct);
        return await MapearSesionAsync(sesion.idSesion, ct);
    }

    public async Task CerrarAsync(
        int idSesion,
        string idProfesorDocente,
        bool esInspector,
        CancellationToken ct = default)
    {
        var sesion = await _db.cplec_sesiones
            .FirstOrDefaultAsync(s => s.idSesion == idSesion && (s.activo == true), ct)
            ?? throw new NoEncontradoException("La sesión solicitada no existe.");

        await _guard.EnsureDocenteTieneAsignacionAsync(
            idProfesorDocente ?? string.Empty,
            sesion.idAsignacion,
            esInspector,
            ct);

        if (sesion.estado == EstadoSesion.Cerrada) return; // idempotente.

        sesion.estado = EstadoSesion.Cerrada;
        sesion.fechaCierre = _reloj.GetUtcNow().UtcDateTime;
        sesion.usuarioActualiza = idProfesorDocente;
        sesion.fechaActualizacion = sesion.fechaCierre;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReabrirAsync(
        int idSesion,
        string idProfesorDocente,
        bool esInspector,
        ReabrirSesionRequestDto request,
        CancellationToken ct = default)
    {
        if (!esInspector)
            throw new SesionCerradaException(); // Reutiliza la jerarquía 403.
        if (string.IsNullOrWhiteSpace(request.Motivo))
            throw new ValidacionException("El motivo es obligatorio para reabrir una sesión.");

        var sesion = await _db.cplec_sesiones
            .FirstOrDefaultAsync(s => s.idSesion == idSesion && (s.activo == true), ct)
            ?? throw new NoEncontradoException("La sesión solicitada no existe.");

        if (sesion.estado != EstadoSesion.Cerrada) return; // ya está abierta.

        sesion.estado = EstadoSesion.Borrador;
        sesion.fechaCierre = null;
        sesion.usuarioActualiza = idProfesorDocente;
        sesion.fechaActualizacion = _reloj.GetUtcNow().UtcDateTime;

        // Registrar el evento de reapertura en el historial genérico.
        _db.gest_audit_registros.Add(new gest_audit_registros
        {
            codigoSistema = "cplec",
            accion = "SESION_REABIERTA",
            descripcion = $"idSesion={sesion.idSesion};motivo={request.Motivo.Trim()}",
            idUsuario = idProfesorDocente ?? string.Empty,
            idModulo = "cplec_sesiones",
            tablaAfectada = "cplec_sesiones",
            idEntidad = sesion.idSesion,
            fechaHora = _reloj.GetUtcNow().UtcDateTime
        });

        await _db.SaveChangesAsync(ct);
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