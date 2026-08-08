namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>422 SESION_FUTURA — no se pasa lista de una clase que no ocurrió.</summary>
public sealed class SesionFuturaException : AppException
{
    public SesionFuturaException()
        : base("SESION_FUTURA", 422, "No se puede registrar una clase que aún no ocurrió.") { }
}
