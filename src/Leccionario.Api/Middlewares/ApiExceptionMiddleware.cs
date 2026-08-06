using System.Diagnostics;
using System.Text.Json;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Authenticacion.Auth;

namespace Leccionario.Api.Middlewares;

/// <summary>
/// Middleware que captura cualquier excepción no controlada, la traduce a un
/// JSON estable y la escribe como respuesta. Inspirado en
/// <c>BienestarInstitucional.Api/Middlewares/ApiExceptionMiddleware.cs</c>,
/// ajustado al clasificador propio de cplec.
/// </summary>
/// <remarks>
/// <para>Contrato de error (ver docs/04 §"Contrato de error"):</para>
/// <code>
/// {
///   "codigo":    "DISTRIBUTIVO_AJENO",
///   "mensaje":   "La asignación solicitada no pertenece a su distributivo.",
///   "detalles":  { ... },                 // opcional
///   "traceId":   "0HMVA…",
///   "timestamp": "2026-08-06T12:34:56Z"
/// }
/// </code>
/// <para>En Development, además se incluye <c>stackTrace</c> y
/// <c>innerException</c> para acelerar el debug. En producción se omiten.</para>
/// </remarks>
public sealed class ApiExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // Si la respuesta ya empezó, no podemos sobrescribir el status.
                // Relanzamos para que el host lo loguee y aborte la conexión.
                _logger.LogError(ex, "Excepción después de que la respuesta ya empezó. TraceId={TraceId}",
                    context.TraceIdentifier);
                throw;
            }

            var status = ExceptionClassifier.GetHttpStatus(ex);
            var codigo = ExceptionClassifier.GetCodigo(ex);

            // Log estructurado: nivel según severidad.
            if (status >= 500)
                _logger.LogError(ex, "Error interno {Codigo} en {Path}. TraceId={TraceId}",
                    codigo, context.Request.Path, context.TraceIdentifier);
            else
                _logger.LogInformation("Error de cliente {Codigo} {Status} en {Path}. TraceId={TraceId} — {Mensaje}",
                    codigo, status, context.Request.Path, context.TraceIdentifier, ex.Message);

            var body = new Dictionary<string, object?>
            {
                ["codigo"]    = codigo,
                ["mensaje"]   = ex.Message,
                ["traceId"]   = context.TraceIdentifier,
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };

            // Detalles estructurados, si la excepción los trae.
            if (ex is ValidacionException ve && ve.Detalles is not null)
                body["detalles"] = ve.Detalles;

            // En Development, exponer stack para acelerar el debug.
            if (_env.IsDevelopment())
            {
                body["stackTrace"] = ex.ToString();
                if (ex.InnerException is not null)
                    body["innerException"] = ex.InnerException.Message;
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(context.Response.Body, body, JsonOptions);
        }
    }
}
