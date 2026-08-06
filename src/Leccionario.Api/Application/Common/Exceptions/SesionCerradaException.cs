namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>403 SESION_CERRADA — la sesión ya está cerrada y solo un inspector puede reabrirla.</summary>
public sealed class SesionCerradaException : ProhibidoException
{
    public SesionCerradaException()
        : base("SESION_CERRADA", "La sesión está cerrada. Solo un inspector puede reabrirla.") { }
}
