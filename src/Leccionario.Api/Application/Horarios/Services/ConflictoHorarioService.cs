using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Detecta conflictos de horario por <b>solapamiento de rangos</b> en la misma
/// fecha. Ver <c>ADR-008</c> decisión 5.
/// </summary>
public interface IConflictoHorarioService
{
    Task<ResultadoConflictoDto> ValidarAsync(SolicitudConflictoDto s, CancellationToken ct = default);
}

/// <inheritdoc cref="IConflictoHorarioService"/>
/// <remarks>
/// <para>Predicado: <c>a.inicio &lt; b.fin ∧ b.inicio &lt; a.fin</c>. Bordes que se
/// tocan no solapan.</para>
/// <para><b>No se compara por <c>idhora</c></b>, que es lo que hace
/// <c>gestion_academica</c>, por dos razones independientes: las franjas Z y X
/// son filas distintas para el mismo horario de reloj, y las franjas Z tienen
/// duración libre, así que dos <c>idhora</c> distintos pueden solaparse de verdad.</para>
/// <para>La lectura cruza todas las carreras y todos los tipos de franja a
/// propósito. Lo que cambia es la severidad: bloqueamos contra franjas Z (nuestro
/// territorio, el inspector puede resolverlo) y avisamos contra franjas ajenas
/// (choque real, pero no podemos editar el horario de otra carrera).</para>
/// <para>El parseo de horas ocurre en memoria: <c>hora_inicio</c> es
/// <c>varchar(5)</c> nullable y una franja sin horas se descarta.</para>
/// </remarks>
public sealed class ConflictoHorarioService : IConflictoHorarioService
{
    private readonly sigafi_esContext _db;

    public ConflictoHorarioService(sigafi_esContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>Proyección plana de una celda ocupada, con su contexto.</summary>
    private sealed record Ocupacion(
        int IdHorario, int IdAsignacion, string? Tipo, string? HoraInicio, string? HoraFin,
        string IdProfesor, string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad,
        string? Paralelo, string? Carrera, string? Nivel);

    public async Task<ResultadoConflictoDto> ValidarAsync(
        SolicitudConflictoDto s, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(s);

        var candidata = await LeerFranjaAsync(s.Idhora, ct)
            ?? throw new ValidacionException("La franja horaria no existe o no tiene horas definidas.");

        var propia = await _db.asignaciones_profesores
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.idAsignacion == s.IdAsignacion, ct)
            ?? throw new NoEncontradoException("La asignación solicitada no existe.");

        var ocupadas = await LeerOcupacionesDelDiaAsync(s.IdFecha, s.IdHorarioExcluir, ct);

        var bloqueantes = new List<ConflictoDto>();
        var advertencias = new List<ConflictoDto>();

        foreach (var o in ocupadas)
        {
            if (!TryRango(o.HoraInicio, o.HoraFin, out var ini, out var fin))
                continue; // franja sin horas: no se puede comparar

            if (!Solapan(candidata.Inicio, candidata.Fin, ini, fin))
                continue;

            var esFranjaPropia = o.Tipo == FranjaZGuard.TipoCplec;
            var rango = $"{ini:HH\\:mm}–{fin:HH\\:mm}";

            if (o.IdAsignacion == s.IdAsignacion)
            {
                bloqueantes.Add(Crear("ASIGNACION_DUPLICADA", SeveridadConflicto.Bloqueante,
                    "Esta asignación ya tiene una clase que se solapa con ese horario.", o, rango));
                continue;
            }

            if (o.IdProfesor == propia.idProfesor)
            {
                if (esFranjaPropia)
                {
                    bloqueantes.Add(Crear("DOCENTE_OCUPADO", SeveridadConflicto.Bloqueante,
                        "El docente ya tiene clase en ese horario.", o, rango));
                }
                else
                {
                    advertencias.Add(Crear("DOCENTE_OCUPADO", SeveridadConflicto.Advertencia,
                        $"El docente tiene clase en {o.Carrera ?? "otra carrera"} en ese horario. " +
                        "Ese horario pertenece a otro sistema y no se puede modificar desde acá.",
                        o, rango));
                }
                continue;
            }

            if (esFranjaPropia && EsMismoParalelo(propia, o))
            {
                bloqueantes.Add(Crear("PARALELO_OCUPADO", SeveridadConflicto.Bloqueante,
                    "El paralelo ya tiene otra clase en ese horario.", o, rango));
            }
        }

        return new ResultadoConflictoDto(bloqueantes, advertencias);
    }

    /// <summary>Solape estricto: los bordes que se tocan no cuentan.</summary>
    private static bool Solapan(TimeOnly aIni, TimeOnly aFin, TimeOnly bIni, TimeOnly bFin) =>
        aIni < bFin && bIni < aFin;

    /// <summary>El paralelo es la 5-tupla completa. Con menos, el join miente.</summary>
    private static bool EsMismoParalelo(Domain.Entities.asignaciones_profesores a, Ocupacion o) =>
        a.idPeriodo == o.IdPeriodo
        && a.idNivel == o.IdNivel
        && a.idSeccion == o.IdSeccion
        && a.idModalidad == o.IdModalidad
        && string.Equals(a.paralelo?.Trim(), o.Paralelo?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static ConflictoDto Crear(
        string tipo, SeveridadConflicto sev, string mensaje, Ocupacion o, string rango) =>
        new(tipo, sev, mensaje, o.IdHorario, o.IdProfesor, o.Carrera, o.Nivel,
            o.Paralelo?.Trim(), rango);

    private sealed record Rango(TimeOnly Inicio, TimeOnly Fin);

    private async Task<Rango?> LeerFranjaAsync(int idhora, CancellationToken ct)
    {
        var f = await _db.horas_clases
            .AsNoTracking()
            .Where(h => h.idhora == idhora)
            .Select(h => new { h.hora_inicio, h.hora_fin })
            .FirstOrDefaultAsync(ct);

        if (f is null || !TryRango(f.hora_inicio, f.hora_fin, out var ini, out var fin))
            return null;

        return new Rango(ini, fin);
    }

    /// <summary>
    /// `hora_inicio`/`hora_fin` son varchar(5) nullable. Se parsean en memoria;
    /// una franja sin horas o con basura se descarta en vez de asumir 00:00.
    /// </summary>
    private static bool TryRango(string? inicio, string? fin, out TimeOnly ini, out TimeOnly f)
    {
        ini = default;
        f = default;
        return TimeOnly.TryParse(inicio, out ini)
            && TimeOnly.TryParse(fin, out f)
            && ini < f;
    }

    /// <summary>
    /// Trae las celdas activas de la fecha con su contexto. Filtra por `idFecha`
    /// y `activo = 1` en la base (usa `ix_horario_detalle_fecha_hora_activo`) y
    /// compara los rangos en memoria: son decenas de filas por fecha.
    /// </summary>
    private Task<List<Ocupacion>> LeerOcupacionesDelDiaAsync(
        int idFecha, int? excluir, CancellationToken ct) =>
        (from hd in _db.horario_detalle.AsNoTracking()
         join hc in _db.horas_clases.AsNoTracking() on hd.idhora equals hc.idhora
         join ap in _db.asignaciones_profesores.AsNoTracking() on hd.idAsignacion equals ap.idAsignacion
         join cu in _db.cursos.AsNoTracking() on ap.idNivel equals cu.idNivel into cursoJoin
         from cu in cursoJoin.DefaultIfEmpty()
         join ca in _db.carreras.AsNoTracking() on cu.idCarrera equals ca.idCarrera into carreraJoin
         from ca in carreraJoin.DefaultIfEmpty()
         where hd.idFecha == idFecha
               && hd.activo == 1
               && (excluir == null || hd.idHorario != excluir)
         select new Ocupacion(
             hd.idHorario, hd.idAsignacion, hc.tipo, hc.hora_inicio, hc.hora_fin,
             ap.idProfesor, ap.idPeriodo, ap.idNivel, ap.idSeccion, ap.idModalidad,
             ap.paralelo, ca.Carrera, cu.Nivel))
        .ToListAsync(ct);
}
