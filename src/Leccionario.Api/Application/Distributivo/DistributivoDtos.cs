namespace Leccionario.Api.Application.Distributivo;

/// <summary>
/// Fila devuelta por <c>GET /api/periodos/por-nivel</c>. Una por cada nivel
/// (tipo de licencia) de la carrera 6, con el período más reciente de ese
/// nivel. Ver <c>docs/04-contrato-api.md</c> § Docente y <c>docs/10</c> § 5.
/// </summary>
public sealed class PeriodoPorNivelDto
{
    /// <summary>Identificador del nivel (tipo de licencia). PK de <c>cursos</c>.</summary>
    public required int IdNivel { get; init; }

    /// <summary>Etiqueta legible: <c>TIPO "C"</c>, <c>TIPO "D"</c>, etc.</summary>
    public required string TipoLicencia { get; init; }

    /// <summary>Período más reciente del nivel.</summary>
    public required string IdPeriodo { get; init; }

    /// <summary>Detalle humano del período (ej. <c>OCTUBRE 2025 - ABRIL 2026</c>).</summary>
    public string? Detalle { get; init; }

    /// <summary>Fecha de inicio del rango abarcado por las asignaciones del nivel en ese período.</summary>
    public DateOnly? FechaInicial { get; init; }

    /// <summary>Fecha de fin del rango abarcado por las asignaciones del nivel en ese período.</summary>
    public DateOnly? FechaFin { get; init; }

    /// <summary>Vigencia calculada contra la fecha del servidor: <c>VIGENTE</c>, <c>FUTURO</c> o <c>CERRADO</c>.</summary>
    public required string Vigencia { get; init; }

    /// <summary>Cantidad de asignaciones de ese nivel en ese período (activas).</summary>
    public required int Asignaciones { get; init; }

    /// <summary>Cantidad de docentes distintos con asignaciones en ese nivel y período.</summary>
    public required int Docentes { get; init; }
}

/// <summary>Fila devuelta por <c>GET /api/mis-paralelos</c> (docente).</summary>
public sealed class MiParaleloDto
{
    public required int IdAsignacion { get; init; }
    public required string IdPeriodo { get; init; }
    public required string Asignatura { get; init; }
    public required string TipoLicencia { get; init; }
    public required string Jornada { get; init; }
    public required string Modalidad { get; init; }
    public required string Paralelo { get; init; }
    public DateOnly? FechaInicial { get; init; }
    public DateOnly? FechaFin { get; init; }
    public required int TotalAlumnos { get; init; }
    public required int SesionesRegistradas { get; init; }
    public DateOnly? UltimaSesion { get; init; }
}

/// <summary>Fila devuelta por <c>GET /api/paralelos/{idAsignacion}/alumnos</c>.</summary>
public sealed class AlumnoNominaDto
{
    public required int IdMatricula { get; init; }
    public required string IdAlumno { get; init; }
    public required string Apellidos { get; init; }
    public required string Nombres { get; init; }
    public required bool Retirado { get; init; }
    public required bool EsOyente { get; init; }
}