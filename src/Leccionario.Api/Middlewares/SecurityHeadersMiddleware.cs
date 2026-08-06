namespace Leccionario.Api.Middlewares;

/// <summary>
/// Middleware que agrega headers de seguridad a todas las respuestas.
/// Port verbatim de <c>BienestarInstitucional.Api/Middlewares/SecurityHeadersMiddleware.cs</c>.
/// </summary>
/// <remarks>
/// Previene:
/// <list type="bullet">
///   <item><c>X-Content-Type-Options: nosniff</c> — bloquea MIME-sniffing.</item>
///   <item><c>X-Frame-Options: DENY</c> — anti-clickjacking.</item>
///   <item><c>Referrer-Policy: no-referrer</c> — la URL no se filtra a terceros.</item>
/// </list>
/// </remarks>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        // Headers fijos (no dependen del status). Se setean de inmediato en
        // lugar de OnStarting para que sean visibles en cualquier punto del
        // pipeline — también antes del flush del response.
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"]        = "DENY";
        h["Referrer-Policy"]        = "no-referrer";

        return _next(context);
    }
}
