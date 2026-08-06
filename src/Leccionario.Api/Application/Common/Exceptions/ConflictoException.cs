namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>409 — conflicto con el estado actual del recurso (p. ej. sesión duplicada).</summary>
public sealed class ConflictoException : AppException
{
    public ConflictoException(string codigo, string mensaje)
        : base(codigo, 409, mensaje) { }

    public ConflictoException(string mensaje)
        : base("CONFLICTO", 409, mensaje) { }
}
