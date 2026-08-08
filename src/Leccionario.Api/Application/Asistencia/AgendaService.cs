using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Asistencia;

public enum EstadoBloque { Pendiente, Borrador, Cerrada, Futura }

public sealed record BloqueAgendaDto(DateOnly Fecha, string Dia, int IdHorarioInicio,
    int NumeroBloque, string HoraInicio, string HoraFin, int FranjasPlanificadas,
    int MinutosPlanificados, EstadoBloque Estado, int? IdSesion, int DiasRetraso);

public sealed record SesionTardiaDto(int IdSesion, int IdAsignacion, DateOnly Fecha,
    string? NombreDocente, string Tema, int DiasRetraso);

public sealed record DiaSinRegistrarDto(int IdAsignacion, DateOnly Fecha,
    string? NombreDocente, int NumeroBloque, int DiasVencido);

public interface IAgendaService
{
    Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(int idAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default);
    Task<IReadOnlyList<SesionTardiaDto>> SesionesTardiasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default);
    Task<IReadOnlyList<DiaSinRegistrarDto>> DiasSinRegistrarAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default);
}

public sealed class AgendaService : IAgendaService
{
    private readonly sigafi_esContext _db;
    private readonly TimeProvider _reloj;

    public AgendaService(sigafi_esContext db, TimeProvider? reloj = null)
    {
        _db = db;
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<BloqueAgendaDto>> ObtenerAgendaAsync(
        int idAsignacion, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);

        // 1. Celdas activas de la asignación en el rango, con su franja y su fecha.
        var filas = await (
            from hd in _db.horario_detalle.AsNoTracking()
            join hc in _db.horas_clases.AsNoTracking() on hd.idhora equals hc.idhora
            join fh in _db.fechas_horarios.AsNoTracking() on hd.idFecha equals fh.idFecha
            where hd.idAsignacion == idAsignacion
                  && hd.activo == 1
                  && fh.fecha != null && fh.fecha >= desde && fh.fecha <= hasta
            select new
            {
                hd.idHorario, hd.idhora, hd.idFecha,
                Fecha = fh.fecha!.Value, Dia = fh.dia,
                hc.hora_inicio, hc.hora_fin, hc.minutos
            }).ToListAsync(ct);

        // 2. Sesiones existentes, indexadas por (idFecha, numeroBloque).
        var sesiones = await _db.cplec_sesiones.AsNoTracking()
            .Where(s => s.idAsignacion == idAsignacion && s.activo == true)
            .Select(s => new { s.idSesion, s.idFecha, s.numeroBloque, s.estado })
            .ToListAsync(ct);

        // 3. Un grupo por fecha; dentro, agrupar en bloques contiguos.
        var resultado = new List<BloqueAgendaDto>();
        foreach (var grupo in filas.GroupBy(f => new { f.idFecha, f.Fecha, f.Dia }).OrderBy(g => g.Key.Fecha))
        {
            var franjas = grupo
                .Select(f => TimeOnly.TryParse(f.hora_inicio, out var i)
                          && TimeOnly.TryParse(f.hora_fin, out var fin) && i < fin
                    ? new FranjaOrdenable(f.idHorario, f.idhora, i, fin,
                        f.minutos ?? (int)(fin - i).TotalMinutes)
                    : null)
                .Where(f => f is not null).Select(f => f!);

            foreach (var b in BloqueHorarioCalculator.Agrupar(franjas))
            {
                var s = sesiones.FirstOrDefault(x =>
                    x.idFecha == grupo.Key.idFecha && x.numeroBloque == b.NumeroBloque);

                var estado = grupo.Key.Fecha > hoy ? EstadoBloque.Futura
                    : s is null                    ? EstadoBloque.Pendiente
                    : s.estado == "cerrada"        ? EstadoBloque.Cerrada
                                                   : EstadoBloque.Borrador;

                resultado.Add(new BloqueAgendaDto(
                    grupo.Key.Fecha, grupo.Key.Dia ?? string.Empty,
                    b.IdHorarioInicio, b.NumeroBloque,
                    b.Inicio.ToString("HH\\:mm"), b.Fin.ToString("HH\\:mm"),
                    b.FranjasPlanificadas, b.MinutosPlanificados,
                    estado, s?.idSesion,
                    estado == EstadoBloque.Pendiente
                        ? Math.Max(0, hoy.DayNumber - grupo.Key.Fecha.DayNumber)
                        : 0));
            }
        }

        return resultado;
    }

    public async Task<IReadOnlyList<SesionTardiaDto>> SesionesTardiasAsync(
        DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        await (from s in _db.cplec_sesiones.AsNoTracking()
               join f in _db.fechas_horarios.AsNoTracking() on s.idFecha equals f.idFecha
               join ap in _db.asignaciones_profesores.AsNoTracking() on s.idAsignacion equals ap.idAsignacion
               join pr in _db.profesores.AsNoTracking() on ap.idProfesor equals pr.idProfesor into prJoin
               from pr in prJoin.DefaultIfEmpty()
               where s.activo == true && s.esTardia
                     && f.fecha != null && f.fecha >= desde && f.fecha <= hasta
               orderby s.diasRetraso descending, f.fecha
               select new SesionTardiaDto(
                   s.idSesion, s.idAsignacion, f.fecha!.Value,
                   pr == null ? null : (pr.apellidos + " " + pr.nombres).Trim(),
                   s.tema, s.diasRetraso))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DiaSinRegistrarDto>> DiasSinRegistrarAsync(
        DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        // Solo asignaciones de carrera 6 que tengan al menos una celda en el rango.
        var asignaciones = await (
            from hd in _db.horario_detalle.AsNoTracking()
            join fh in _db.fechas_horarios.AsNoTracking() on hd.idFecha equals fh.idFecha
            join ap in _db.asignaciones_profesores.AsNoTracking() on hd.idAsignacion equals ap.idAsignacion
            join cu in _db.cursos.AsNoTracking() on ap.idNivel equals cu.idNivel
            join pr in _db.profesores.AsNoTracking() on ap.idProfesor equals pr.idProfesor into prJoin
            from pr in prJoin.DefaultIfEmpty()
            where hd.activo == 1 && cu.idCarrera == 6
                  && fh.fecha != null && fh.fecha >= desde && fh.fecha <= hasta
            select new
            {
                hd.idAsignacion,
                Docente = pr == null ? null : (pr.apellidos + " " + pr.nombres).Trim()
            })
            .Distinct()
            .ToListAsync(ct);

        var salida = new List<DiaSinRegistrarDto>();
        foreach (var a in asignaciones)
        {
            var agenda = await ObtenerAgendaAsync(a.idAsignacion, desde, hasta, ct);
            salida.AddRange(agenda
                .Where(b => b.Estado == EstadoBloque.Pendiente)
                .Select(b => new DiaSinRegistrarDto(
                    a.idAsignacion, b.Fecha, a.Docente, b.NumeroBloque, b.DiasRetraso)));
        }

        return salida.OrderByDescending(d => d.DiasVencido).ThenBy(d => d.Fecha).ToList();
    }
}
