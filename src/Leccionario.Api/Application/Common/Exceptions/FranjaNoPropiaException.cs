namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 403 FRANJA_NO_PROPIA — la franja horaria no es de cplec (`tipo != 'Z'`).
/// </summary>
/// <remarks>
/// `X` es del instituto (gestion_academica), `I` es legacy y `C` es de otro
/// sistema. cplec las lee para detectar conflictos pero nunca las escribe.
/// Ver <c>ADR-008</c> decisión 3.
/// </remarks>
public sealed class FranjaNoPropiaException : ProhibidoException
{
    public FranjaNoPropiaException()
        : base("FRANJA_NO_PROPIA",
               "La franja horaria pertenece a otro sistema y no puede modificarse.") { }
}
