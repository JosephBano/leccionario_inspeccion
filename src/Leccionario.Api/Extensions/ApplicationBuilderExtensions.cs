using Leccionario.Api.Middlewares;

namespace Leccionario.Api.Extensions;

/// <summary>
/// Métodos de extensión que configuran el pipeline HTTP. Inspirado en
/// <c>BienestarInstitucional.Api/Extensions/ApplicationBuilderExtensions.cs</c>.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>Captura todas las excepciones no controladas y las traduce a JSON estable.</summary>
    public static IApplicationBuilder UseApiExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ApiExceptionMiddleware>();

    /// <summary>Agrega los headers de seguridad a cada respuesta.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();

    /// <summary>Registra cada request en el log (stub; persiste cuando llega EF Power Tools).</summary>
    public static IApplicationBuilder UseAudit(this IApplicationBuilder app)
        => app.UseMiddleware<AuditMiddleware>();
}
