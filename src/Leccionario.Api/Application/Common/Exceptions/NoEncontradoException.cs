namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>404 NO_ENCONTRADO — el recurso solicitado no existe.</summary>
public sealed class NoEncontradoException : AppException
{
    public NoEncontradoException(string mensaje)
        : base("NO_ENCONTRADO", 404, mensaje) { }

    /// <summary>Constructor tipado para respuestas consistentes: <c>{"entity": "Sesion", "id": 123}</c>.</summary>
    public NoEncontradoException(string entidad, object id)
        : base("NO_ENCONTRADO", 404, $"{entidad} con id {id} no encontrado.")
    {
        Entidad = entidad;
        Id = id;
    }

    public string? Entidad { get; }
    public object? Id { get; }
}
