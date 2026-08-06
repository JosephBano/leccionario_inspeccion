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
