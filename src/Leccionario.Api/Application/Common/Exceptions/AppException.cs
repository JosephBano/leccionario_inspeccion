namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// Base de todas las excepciones de aplicación de cplec.
/// Lleva el código de error y el HTTP status que el <c>ExceptionClassifier</c>
/// propaga al JSON de respuesta (ver docs/04 §"Contrato de error").
/// </summary>
/// <remarks>
/// <para>Las subclases concretas fijan <see cref="Codigo"/> y <see cref="HttpStatus"/>
/// en el constructor. El middleware <c>ApiExceptionMiddleware</c> no necesita
/// un <c>switch</c> por tipo: lee estas dos propiedades directamente.</para>
/// <para>Si en el futuro se agrega una nueva subclase, NO se toca el
/// <c>ExceptionClassifier</c>: el código se obtiene por polimorfismo.</para>
/// </remarks>
public abstract class AppException : Exception
{
    /// <summary>Código de error legible por máquina (p. ej. <c>"DISTRIBUTIVO_AJENO"</c>).</summary>
    public string Codigo { get; }

    /// <summary>HTTP status sugerido (p. ej. 403, 404, 409).</summary>
    public int HttpStatus { get; }

    protected AppException(string codigo, int httpStatus, string mensaje)
        : base(mensaje)
    {
        Codigo = codigo;
        HttpStatus = httpStatus;
    }

    protected AppException(string codigo, int httpStatus, string mensaje, Exception innerException)
        : base(mensaje, innerException)
    {
        Codigo = codigo;
        HttpStatus = httpStatus;
    }
}
