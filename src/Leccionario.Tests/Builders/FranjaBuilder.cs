using Leccionario.Api.Domain.Entities;

namespace Leccionario.Tests.Builders;

/// <summary>
/// Builder fluido para <see cref="horas_clases"/>. Patrón de docs/07 sección "Builders".
/// </summary>
/// <remarks>
/// Por defecto crea una franja de cplec: <c>tipo = "Z"</c>, <c>idCarrera</c> e
/// <c>idSeccion</c> en NULL (ADR-008 decisión 3). Las horas son
/// <c>varchar(5)</c> en formato <c>HH:MM</c> zero-padded, como en la base real.
/// </remarks>
public sealed class FranjaBuilder
{
    private int _idhora = 900;
    private string? _tipo = "Z";
    private string? _horaInicio = "07:00";
    private string? _horaFin = "08:00";
    private int? _minutos = 60;
    private int? _numeroHora = 1;
    private int? _idCarrera;
    private int? _idSeccion;
    private sbyte? _activo = 1;

    public FranjaBuilder ConId(int idhora) { _idhora = idhora; return this; }
    public FranjaBuilder DeTipo(string? tipo) { _tipo = tipo; return this; }
    public FranjaBuilder Activa(sbyte? activo) { _activo = activo; return this; }
    public FranjaBuilder ConCarrera(int? idCarrera) { _idCarrera = idCarrera; return this; }
    public FranjaBuilder ConSeccion(int? idSeccion) { _idSeccion = idSeccion; return this; }

    /// <summary>Fija el rango y recalcula `minutos` a partir de él.</summary>
    public FranjaBuilder DeRango(string? horaInicio, string? horaFin)
    {
        _horaInicio = horaInicio;
        _horaFin = horaFin;
        if (TimeOnly.TryParse(horaInicio, out var ini) && TimeOnly.TryParse(horaFin, out var fin))
            _minutos = (int)(fin - ini).TotalMinutes;
        return this;
    }

    public FranjaBuilder ConNumeroHora(int? numeroHora) { _numeroHora = numeroHora; return this; }

    public horas_clases Build() => new()
    {
        idhora = _idhora,
        tipo = _tipo,
        hora_inicio = _horaInicio,
        hora_fin = _horaFin,
        minutos = _minutos,
        numero_hora = _numeroHora,
        idCarrera = _idCarrera,
        idSeccion = _idSeccion,
        activo = _activo
    };
}
