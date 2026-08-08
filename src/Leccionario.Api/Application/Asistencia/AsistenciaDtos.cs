namespace Leccionario.Api.Application.Asistencia;

/// <summary>Estados válidos de una marca de asistencia.</summary>
public static class EstadoAsistencia
{
    public const string Presente = "presente";
    public const string Ausente = "ausente";
    public const string Atraso = "atraso";
    public const string Justificado = "justificado";

    public static readonly IReadOnlySet<string> Todos =
        new HashSet<string>(StringComparer.Ordinal) { Presente, Ausente, Atraso, Justificado };
}

/// <summary>Estados de una sesión.</summary>
public static class EstadoSesion
{
    public const string Borrador = "borrador";
    public const string Cerrada = "cerrada";
}

/// <summary>Marca individual del cuerpo de <c>POST /api/sesiones/{idSesion}/asistencias</c>.</summary>
public sealed class MarcaAsistenciaDto
{
    public required int IdMatricula { get; init; }
    public required string Estado { get; init; }
    public ushort? MinutosAtraso { get; init; }
    public string? Observacion { get; init; }
}

/// <summary>Cuerpo de <c>POST /api/sesiones/{idSesion}/asistencias</c>.</summary>
public sealed class RegistrarAsistenciaRequestDto
{
    public required IReadOnlyList<MarcaAsistenciaDto> Marcas { get; init; }
    public string? Motivo { get; init; }
}

/// <summary>Fila devuelta por <c>GET /api/sesiones/{idSesion}</c>.</summary>
public sealed class SesionDto
{
    public required int IdSesion { get; init; }
    public required int IdAsignacion { get; init; }
    public required DateOnly Fecha { get; init; }
    public required sbyte NumeroBloque { get; init; }
    public required string Tema { get; init; }
    public string? Observacion { get; init; }
    public required string Estado { get; init; }
    public DateTime? FechaCierre { get; init; }

    /// <summary>"horario" si la sesión cuelga de una celda planificada; "libre" si no.</summary>
    public string Origen { get; init; } = "libre";

    /// <summary>Se registró después del día de clase. Congelado al crear.</summary>
    public bool EsTardia { get; init; }

    /// <summary>Días entre la clase y el primer guardado. Congelado al crear.</summary>
    public int DiasRetraso { get; init; }

    public int? FranjasPlanificadas { get; init; }
    public int? MinutosPlanificados { get; init; }

    public required IReadOnlyList<MarcaSesionDto> Asistencias { get; init; }
}

/// <summary>Marca persistida de un alumno en una sesión.</summary>
public sealed class MarcaSesionDto
{
    public required int IdAsistencia { get; init; }
    public required int IdMatricula { get; init; }
    public required string Estado { get; init; }
    public ushort? MinutosAtraso { get; init; }
    public string? Observacion { get; init; }
}

/// <summary>Cuerpo de <c>POST /api/paralelos/{idAsignacion}/sesiones</c>.</summary>
public sealed class CrearSesionRequestDto
{
    public int? IdHorarioInicio { get; init; }
    public DateOnly Fecha { get; init; }
    public required string Tema { get; init; }
    public string? Observacion { get; init; }
    public sbyte NumeroBloque { get; init; } = 1;
}

/// <summary>Cuerpo de <c>PUT /api/sesiones/{idSesion}</c>.</summary>
public sealed class EditarSesionRequestDto
{
    public string? Tema { get; init; }
    public string? Observacion { get; init; }
}

/// <summary>Cuerpo de <c>POST /api/sesiones/{idSesion}/reabrir</c>.</summary>
public sealed class ReabrirSesionRequestDto
{
    public required string Motivo { get; init; }
}