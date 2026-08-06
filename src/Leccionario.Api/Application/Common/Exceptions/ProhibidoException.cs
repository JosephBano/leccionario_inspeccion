namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>403 — base de las excepciones de prohibición.</summary>
/// <remarks>Las subclases concretas fijan un <see cref="AppException.Codigo"/> específico.</remarks>
public abstract class ProhibidoException : AppException
{
    protected ProhibidoException(string codigo, string mensaje)
        : base(codigo, 403, mensaje) { }
}
