using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>Grid semanal y CRUD de celda del horario. Ver spec sección 5.5.</summary>
public interface IHorarioService
{
    /// <summary>
    /// El docente solo ve el grid de su propio paralelo: <paramref name="idProfesorDocente"/>
    /// pasa por <c>DistributivoGuard</c> igual que el resto del alcance de datos.
    /// El inspector no tiene restricción.
    /// </summary>
    /// <param name="lunes">Primer día de la semana a mostrar.</param>
    /// <param name="idProfesorDocente">Cédula del claim <c>sub</c>; ignorado si <paramref name="esInspector"/> es <c>true</c>.</param>
    Task<GridDto> ObtenerGridAsync(
        ParaleloClaveDto p, DateOnly lunes,
        string? idProfesorDocente, bool esInspector,
        CancellationToken ct = default);

    Task<CeldaCreadaDto> CrearAsync(CrearCeldaDto req, CancellationToken ct = default);
    Task<CeldaCreadaDto> ActualizarAsync(int idHorario, CrearCeldaDto req, CancellationToken ct = default);

    /// <summary>Borrado lógico.</summary>
    Task DesactivarAsync(int idHorario, CancellationToken ct = default);
}

/// <inheritdoc cref="IHorarioService"/>
public sealed class HorarioService : IHorarioService
{
    private static readonly string[] DiasSemana =
        ["Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo"];

    private readonly sigafi_esContext _db;
    private readonly IHorarioCarreraGuard _carrera;
    private readonly IFranjaZGuard _franjaZ;
    private readonly IConflictoHorarioService _conflictos;
    private readonly IFranjaService _franjas;
    private readonly IEscrituraSerializable _escritura;
    private readonly IDistributivoGuard _distributivo;

    public HorarioService(
        sigafi_esContext db,
        IHorarioCarreraGuard carrera,
        IFranjaZGuard franjaZ,
        IConflictoHorarioService conflictos,
        IFranjaService franjas,
        IEscrituraSerializable escritura,
        IDistributivoGuard distributivo)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _carrera = carrera ?? throw new ArgumentNullException(nameof(carrera));
        _franjaZ = franjaZ ?? throw new ArgumentNullException(nameof(franjaZ));
        _conflictos = conflictos ?? throw new ArgumentNullException(nameof(conflictos));
        _franjas = franjas ?? throw new ArgumentNullException(nameof(franjas));
        _escritura = escritura ?? throw new ArgumentNullException(nameof(escritura));
        _distributivo = distributivo ?? throw new ArgumentNullException(nameof(distributivo));
    }

    public async Task<GridDto> ObtenerGridAsync(
        ParaleloClaveDto p, DateOnly lunes,
        string? idProfesorDocente, bool esInspector,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);

        if (!esInspector && string.IsNullOrWhiteSpace(idProfesorDocente))
            throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");

        await _distributivo.EnsureDocenteTieneParaleloAsync(
            idProfesorDocente ?? string.Empty,
            p.IdPeriodo, p.IdNivel, p.IdSeccion, p.IdModalidad, p.Paralelo,
            esInspector, ct);

        var franjas = await _franjas.ListarAsync(ct);
        var fechas = Enumerable.Range(0, 7).Select(lunes.AddDays).ToList();

        var calendario = await _db.fechas_horarios
            .AsNoTracking()
            .Where(f => f.fecha != null && fechas.Contains(f.fecha.Value))
            .ToDictionaryAsync(f => f.fecha!.Value, f => f.idFecha, ct);

        var dias = fechas.Select((fecha, i) =>
        {
            var tiene = calendario.TryGetValue(fecha, out var idFecha);
            return new DiaGridDto(
                DiasSemana[i], fecha,
                tiene ? idFecha : null,
                tiene,
                // fechas_horarios se alimenta por fuera de cplec y termina el
                // 2026-12-31: sin fila no hay horario ni sesión posible.
                tiene ? null : "La fecha no existe en el calendario institucional.");
        }).ToList();

        var idsFecha = calendario.Values.ToList();
        var paraleloNorm = p.Paralelo.Trim();

        var celdas = await (
            from hd in _db.horario_detalle.AsNoTracking()
            join ap in _db.asignaciones_profesores.AsNoTracking() on hd.idAsignacion equals ap.idAsignacion
            join pr in _db.profesores.AsNoTracking() on ap.idProfesor equals pr.idProfesor into prJoin
            from pr in prJoin.DefaultIfEmpty()
            join fh in _db.fechas_horarios.AsNoTracking() on hd.idFecha equals fh.idFecha
            where hd.activo == 1
                  && idsFecha.Contains(hd.idFecha)
                  && ap.idPeriodo == p.IdPeriodo
                  && ap.idNivel == p.IdNivel
                  && ap.idSeccion == p.IdSeccion
                  && ap.idModalidad == p.IdModalidad
                  && ap.paralelo != null && ap.paralelo.Trim() == paraleloNorm
            select new CeldaGridDto(
                hd.idHorario, hd.idAsignacion, hd.idhora, fh.dia ?? string.Empty,
                pr == null ? null : (pr.apellidos + " " + pr.nombres).Trim(),
                hd.tipoBloque))
            .ToListAsync(ct);

        return new GridDto(franjas, dias, celdas);
    }

    public Task<CeldaCreadaDto> CrearAsync(CrearCeldaDto req, CancellationToken ct = default) =>
        EscribirAsync(req, idHorarioExistente: null, ct);

    public Task<CeldaCreadaDto> ActualizarAsync(int idHorario, CrearCeldaDto req, CancellationToken ct = default) =>
        EscribirAsync(req, idHorario, ct);

    public async Task DesactivarAsync(int idHorario, CancellationToken ct = default)
    {
        var fila = await _db.horario_detalle.FirstOrDefaultAsync(h => h.idHorario == idHorario, ct)
            ?? throw new NoEncontradoException("La celda de horario no existe.");

        await _carrera.EnsureAsignacionEsDeCarrera6Async(fila.idAsignacion, ct);

        fila.activo = 0;
        await _db.SaveChangesAsync(ct);
    }

    /// <remarks>
    /// Las validaciones baratas y de frontera van FUERA de la transacción; solo
    /// la revalidación de conflictos y el escribir van dentro, para que el
    /// alcance del lock sea corto (ADR-008 decisión 6).
    /// </remarks>
    private async Task<CeldaCreadaDto> EscribirAsync(
        CrearCeldaDto req, int? idHorarioExistente, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await _carrera.EnsureAsignacionEsDeCarrera6Async(req.IdAsignacion, ct);
        await _franjaZ.EnsureEsFranjaZAsync(req.Idhora, ct);

        if (!await _db.fechas_horarios.AsNoTracking().AnyAsync(f => f.idFecha == req.IdFecha, ct))
            throw new ValidacionException("La fecha no existe en el calendario institucional.");

        return await _escritura.EjecutarAsync(async token =>
        {
            var conflictos = await _conflictos.ValidarAsync(
                new SolicitudConflictoDto(req.IdAsignacion, req.IdFecha, req.Idhora, idHorarioExistente),
                token);

            if (conflictos.HayBloqueantes)
                throw new ConflictoException("CONFLICTO_HORARIO", conflictos.Bloqueantes[0].Mensaje);

            if (conflictos.Advertencias.Count > 0 && !req.ConfirmarAdvertencias)
                throw new ConflictoException("ADVERTENCIA_NO_CONFIRMADA",
                    conflictos.Advertencias[0].Mensaje);

            var fila = idHorarioExistente is null
                ? await CrearORevivirAsync(req, token)
                : await _db.horario_detalle.FirstAsync(h => h.idHorario == idHorarioExistente, token);

            fila.idhora = req.Idhora;
            fila.idFecha = req.IdFecha;
            fila.tipoBloque = req.TipoBloque;
            fila.idEspacio = null;   // cplec no gestiona aulas (ADR-008 decisión 4)
            fila.activo = 1;

            await _db.SaveChangesAsync(token);

            return new CeldaCreadaDto(fila.idHorario, conflictos.Advertencias);
        }, ct);
    }

    /// <summary>
    /// Si la misma celda existe con `activo = 0`, se revive. Sin esto, borrar y
    /// reponer acumula filas muertas en una tabla que es de otro sistema.
    /// </summary>
    private async Task<horario_detalle> CrearORevivirAsync(CrearCeldaDto req, CancellationToken ct)
    {
        var muerta = await _db.horario_detalle.FirstOrDefaultAsync(h =>
            h.idAsignacion == req.IdAsignacion
            && h.idFecha == req.IdFecha
            && h.idhora == req.Idhora
            && h.activo != 1, ct);

        if (muerta is not null)
            return muerta;

        var nueva = new horario_detalle
        {
            idAsignacion = req.IdAsignacion,
            idFecha = req.IdFecha,
            idhora = req.Idhora,
            idEspacio = null,
            tipoBloque = req.TipoBloque,
            activo = 1
        };
        _db.horario_detalle.Add(nueva);
        return nueva;
    }
}
