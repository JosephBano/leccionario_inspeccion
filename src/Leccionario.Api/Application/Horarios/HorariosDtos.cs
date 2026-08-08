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

/// <summary>Un paralelo es la 5-tupla completa. Con menos, el join miente.</summary>
public sealed record ParaleloClaveDto(
    string IdPeriodo, int IdNivel, int IdSeccion, int IdModalidad, string Paralelo);

/// <param name="IdFecha">NULL si `fechas_horarios` no tiene esa fecha.</param>
public sealed record DiaGridDto(
    string Dia, DateOnly Fecha, int? IdFecha, bool Habilitado, string? Motivo);

public sealed record CeldaGridDto(
    int IdHorario, int IdAsignacion, int Idhora, string Dia,
    string? NombreDocente, string? TipoBloque);

public sealed record GridDto(
    IReadOnlyList<FranjaDto> Franjas,
    IReadOnlyList<DiaGridDto> Dias,
    IReadOnlyList<CeldaGridDto> Celdas);

/// <param name="ConfirmarAdvertencias">
/// El inspector vio el choque contra el horario de otra carrera y decidió guardar igual.
/// </param>
public sealed record CrearCeldaDto(
    int IdAsignacion, int IdFecha, int Idhora, string? TipoBloque,
    bool ConfirmarAdvertencias = false);

public sealed record CeldaCreadaDto(int IdHorario, IReadOnlyList<ConflictoDto> Advertencias);

/// <param name="Dia">Día de la semana en español sin tilde: Lunes … Domingo.</param>
public sealed record OperacionRangoDto(
    int IdAsignacion, string Dia, int Idhora, DateOnly Desde, DateOnly Hasta,
    string? TipoBloque = null, bool ConfirmarAdvertencias = false);

public sealed record DetalleOperacionDto(
    DateOnly Fecha, bool Exitoso, string? MotivoFallo, int? IdHorario);

/// <param name="Advertencia">P. ej. ASIGNACION_SIN_VENTANA. No bloquea.</param>
public sealed record ResultadoRangoDto(
    int TotalProcesados, int TotalExitosos, int TotalFallidos,
    IReadOnlyList<DetalleOperacionDto> Detalles, string? Advertencia);



