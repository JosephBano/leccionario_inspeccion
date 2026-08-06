using System.Diagnostics;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Authenticacion.Auth;

namespace Leccionario.Api.Middlewares;

/// <summary>
/// Traduce excepciones a HTTP status + código de error legible por máquina.
/// Centraliza la lógica de mapeo para que el middleware no tenga un
/// <c>switch</c> gigante y para que añadir una nueva excepción no requiera
/// tocar este archivo (ver ADR-005 decisión (d)).
/// </summary>
public static class ExceptionClassifier
{
    /// <summary>Mapea una excepción a un HTTP status code.</summary>
    public static int GetHttpStatus(Exception ex) => ex switch
    {
        AppException app          => app.HttpStatus,
        CuentaInactivaException  => 401,
        UnauthorizedAccessException => 401,
        ArgumentException        => 400,
        _ => 500
    };

    /// <summary>Mapea una excepción a un código de error estable para el cliente.</summary>
    public static string GetCodigo(Exception ex) => ex switch
    {
        AppException app          => app.Codigo,
        CuentaInactivaException  => "CUENTA_INACTIVA",
        UnauthorizedAccessException => "CREDENCIALES_INVALIDAS",
        ArgumentException        => "VALIDACION",
        _ => "ERROR_INTERNO"
    };
}
