namespace Leccionario.Api.Application.Horarios;

/// <summary>Celda que se quiere ocupar, para validar antes de escribir.</summary>
/// <param name="IdHorarioExcluir">Al editar, la propia fila no cuenta como conflicto.</param>
public sealed record SolicitudConflictoDto(
    int IdAsignacion,
    int IdFecha,
    int Idhora,
    int? IdHorarioExcluir = null);

/// <summary>
/// Bloqueante = está bajo nuestra autoridad y se puede resolver.
/// Advertencia = choque real contra el horario de otro sistema, que cplec no
/// puede ni debe editar. Ver <c>ADR-008</c> decisión 5.
/// </summary>
public enum SeveridadConflicto
{
    Bloqueante,
    Advertencia
}

/// <param name="FranjaAjena">Rango legible de la franja en conflicto, p. ej. "08:00–09:00".</param>
public sealed record ConflictoDto(
    string Tipo,
    SeveridadConflicto Severidad,
    string Mensaje,
    int IdHorarioConflicto,
    string? IdProfesor,
    string? Carrera,
    string? Nivel,
    string? Paralelo,
    string? FranjaAjena);

public sealed record ResultadoConflictoDto(
    IReadOnlyList<ConflictoDto> Bloqueantes,
    IReadOnlyList<ConflictoDto> Advertencias)
{
    public bool HayBloqueantes => Bloqueantes.Count > 0;

    public static ResultadoConflictoDto Vacio { get; } =
        new(Array.Empty<ConflictoDto>(), Array.Empty<ConflictoDto>());
}

/// <param name="HoraInicio">Formato <c>HH:mm</c>, como la columna varchar(5).</param>
public sealed record FranjaDto(
    int Idhora, string HoraInicio, string HoraFin, int Minutos, int? NumeroHora);

public sealed record CrearFranjaDto(string HoraInicio, string HoraFin, int? NumeroHora);

