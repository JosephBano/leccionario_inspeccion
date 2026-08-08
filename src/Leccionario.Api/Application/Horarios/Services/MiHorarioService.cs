using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>Un bloque de clase del docente, con su paralelo resuelto.</summary>
public sealed record BloqueMiHorarioDto(
    int IdAsignacion, string TipoLicencia, string Jornada, string Paralelo, string Asignatura,
    DateOnly Fecha, string Dia, int IdHorarioInicio, int NumeroBloque,
    string HoraInicio, string HoraFin, int FranjasPlanificadas, int MinutosPlanificados,
    string Estado, int? IdSesion, int DiasRetraso);

/// <summary>
/// Horario propio del docente. El alcance de datos lo da
/// <see cref="IMisParalelosService"/>, acotado al <c>sub</c> del token: este
/// endpoint no recibe <c>idAsignacion</c>, así que no pasa por
/// <c>DistributivoGuard</c>. Ver spec 2026-08-08 sección 4.3.
/// </summary>
public interface IMiHorarioService
{
    /// <summary>
    /// Bloques de las asignaciones vigentes del docente entre <paramref name="desde"/> y
    /// <paramref name="hasta"/>. Si ambos son nulos, usa la semana en curso (lunes a domingo).
    /// </summary>
    Task<IReadOnlyList<BloqueMiHorarioDto>> ObtenerAsync(
        string idProfesor, DateOnly? desde, DateOnly? hasta, CancellationToken ct = default);
}

public sealed class MiHorarioService : IMiHorarioService
{
    /// <summary>Mismo tope que las operaciones por rango (ADR-008 decisión 11).</summary>
    private const int MaximoSemanas = 16;

    private readonly IMisParalelosService _misParalelos;
    private readonly IAgendaService _agenda;
    private readonly TimeProvider _reloj;

    public MiHorarioService(
        IMisParalelosService misParalelos,
        IAgendaService agenda,
        TimeProvider? reloj = null)
    {
        _misParalelos = misParalelos;
        _agenda = agenda;
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<BloqueMiHorarioDto>> ObtenerAsync(
        string idProfesor, DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idProfesor))
            throw new UnauthorizedAccessException("No se puede determinar el docente autenticado.");

        var (inicio, fin) = ResolverRango(desde, hasta);

        // Vigencia: ventana fecha_inicial..fecha_fin + 15 días de gracia. No se
        // usa periodos.activo, que está en 1 hasta en periodos de 2022.
        var paralelos = await _misParalelos.ResolverAsync(idProfesor, idPeriodo: null, fechaReferencia: null, ct);
        if (paralelos.Count == 0)
            return Array.Empty<BloqueMiHorarioDto>();

        var porAsignacion = paralelos.ToDictionary(p => p.IdAsignacion);
        var bloques = await _agenda.ObtenerAgendaAsync(porAsignacion.Keys.ToList(), inicio, fin, ct);

        return bloques
            .Where(b => porAsignacion.ContainsKey(b.IdAsignacion))
            .Select(b =>
            {
                var p = porAsignacion[b.IdAsignacion];
                return new BloqueMiHorarioDto(
                    b.IdAsignacion, p.TipoLicencia, p.Jornada, p.Paralelo, p.Asignatura,
                    b.Fecha, b.Dia, b.IdHorarioInicio, b.NumeroBloque,
                    b.HoraInicio, b.HoraFin, b.FranjasPlanificadas, b.MinutosPlanificados,
                    b.Estado.ToString(), b.IdSesion, b.DiasRetraso);
            })
            .OrderBy(b => b.Fecha).ThenBy(b => b.HoraInicio).ThenBy(b => b.IdAsignacion)
            .ToList();
    }

    /// <summary>Semana en curso si no se dio rango; valida orden y tope.</summary>
    private (DateOnly Desde, DateOnly Hasta) ResolverRango(DateOnly? desde, DateOnly? hasta)
    {
        if (desde is null && hasta is null)
        {
            var hoy = DateOnly.FromDateTime(_reloj.GetUtcNow().LocalDateTime);
            var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
            return (lunes, lunes.AddDays(6));
        }

        if (desde is null || hasta is null)
            throw new ValidacionException("Indica ambas fechas del rango, o ninguna.");

        if (desde > hasta)
            throw new ValidacionException("La fecha inicial no puede ser posterior a la final.");

        if ((hasta.Value.DayNumber - desde.Value.DayNumber + 1) > MaximoSemanas * 7)
            throw new ConflictoLoteException("RANGO_EXCEDE_TOPE",
                $"El rango no puede superar {MaximoSemanas} semanas.");

        return (desde.Value, hasta.Value);
    }
}
