namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 422 HORARIO_REQUERIDO — la asignación tiene horario planificado, así que la
/// sesión debe colgar de un bloque y no de una fecha suelta.
/// </summary>
public sealed class HorarioRequeridoException : AppException
{
    public HorarioRequeridoException()
        : base("HORARIO_REQUERIDO", 422,
               "Esta asignación tiene horario: indica el bloque al que corresponde la clase.") { }
}
