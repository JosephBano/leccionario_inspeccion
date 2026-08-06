using System.Diagnostics;

namespace Leccionario.Api.Middlewares;

/// <summary>
/// Stub de auditoría: escribe a <see cref="ILogger{TCategoryName}"/> con los
/// datos de la request. <strong>NO persiste en BD</strong> — la versión
/// persistida en <c>gest_audit_registros</c> llega con el PR
/// <c>feature/ef-powertools-scaffold</c> (#2), cuando EF Power Tools
/// scaffoldee la entidad. Ver ADR-005 decisión (f).
/// </summary>
/// <remarks>
/// <para>No se auditan los endpoints que reciben credenciales o refresh
/// tokens en el body:</para>
/// <list type="bullet">
///   <item><c>POST /api/auth/login</c></item>
///   <item><c>POST /api/auth/refresh</c></item>
///   <item><c>POST /api/auth/logout</c></item>
/// </list>
/// <para>El resto se loguea con nivel Information y plantilla estructurada.
/// Cuando se persista, el nivel se mantiene y se agrega el INSERT a BD.</para>
/// </remarks>
public sealed class AuditMiddleware
{
    private static readonly HashSet<string> RutasExcluidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/logout"
    };

    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ILogger<AuditMiddleware> logger)
    {
        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        if (EsRutaExcluida(context.Request.Path)) return;

        var userId = context.User?.FindFirst("uid")?.Value ?? "anonymous";
        var jti    = context.User?.FindFirst("jti")?.Value ?? "-";

        // Structured logging: el destino (Seq, Application Insights, etc.) puede
        // consultar por estos campos sin parsear el mensaje.
        logger.LogInformation(
            "AUDIT {Method} {Path} {StatusCode} {ElapsedMs}ms user={UserId} jti={Jti}",
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Response.StatusCode,
            sw.ElapsedMilliseconds,
            userId,
            jti);
    }

    private static bool EsRutaExcluida(PathString path)
    {
        if (!path.HasValue) return false;
        return RutasExcluidas.Contains(path.Value!);
    }
}
