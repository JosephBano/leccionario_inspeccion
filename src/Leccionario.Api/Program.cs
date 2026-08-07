using Leccionario.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ─── Servicios ────────────────────────────────────────────────────────────────
builder.Services
    .AddInfrastructureLayer(builder.Configuration)
    .AddApplicationLayer();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRateLimiting(builder.Configuration);
builder.Services.AddSwaggerDocumentation();
builder.Services.AddCorsPolicy(builder.Configuration);

// Controllers (convención MVC estándar).
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // JSON estable para el contrato de error: camelCase, sin escapar caracteres.
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy   = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// ModelState inválido (p. ej. LoginDto sin username) debe devolver el mismo
// contrato {codigo, mensaje, detalles, traceId, timestamp} que ApiExceptionMiddleware,
// no el ValidationProblemDetails por defecto de ASP.NET.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
{
    o.InvalidModelStateResponseFactory = context =>
    {
        var detalles = context.ModelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        var body = new Dictionary<string, object?>
        {
            ["codigo"] = "VALIDACION",
            ["mensaje"] = "La solicitud no cumple las reglas de validación.",
            ["detalles"] = detalles,
            ["traceId"] = context.HttpContext.TraceIdentifier,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(body)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});

var app = builder.Build();

// ─── Pipeline ────────────────────────────────────────────────────────────────
app.UseApiExceptionHandling();

// Swagger: Development y Testing. NO en Production.
if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing")
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseSecurityHeaders();
app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAudit();

app.MapControllers();

app.Run();

// Hacer visible Program para WebApplicationFactory<Program> en los tests.
public partial class Program { }
