namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>400 VALIDACION — la entrada del cliente no cumple las reglas de negocio.</summary>
public sealed class ValidacionException : AppException
{
    /// <summary>Información estructurada opcional (lista de campos, valores esperados, etc.).</summary>
    public object? Detalles { get; }

    public ValidacionException(string mensaje, object? detalles = null)
        : base("VALIDACION", 400, mensaje)
    {
        Detalles = detalles;
    }

    public ValidacionException(string mensaje, Exception innerException, object? detalles = null)
        : base("VALIDACION", 400, mensaje, innerException)
    {
        Detalles = detalles;
    }
}
