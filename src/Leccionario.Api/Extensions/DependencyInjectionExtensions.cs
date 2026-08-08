using System.Text;
using System.Threading.RateLimiting;
using Leccionario.Api.Application.Asistencia;
using Leccionario.Api.Application.Asistencia.Services;
using Leccionario.Api.Application.Auth.Authorization;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace Leccionario.Api.Extensions;

/// <summary>
/// Métodos de extensión que registran servicios de la aplicación. Inspirado en
/// <c>BienestarInstitucional.Api/Extensions/DependencyInjectionExtensions.cs</c>,
/// con los servicios que NO aplican a cplec (EmailQueue, FormOptions, MvcHelpers
/// de Bienestar) omitidos.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Registra los servicios del capa de Application que NO requieren
    /// <c>sigafi_esContext</c>. Los que sí lo requieren (AuthService,
    /// RefreshTokenService, DistributivoGuard, AuditService) se registran en
    /// <c>AddInfrastructureLayer</c> cuando las entidades estén disponibles.
    /// </summary>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        // Servicios estáticos / sin estado. Singleton.
        services.AddSingleton<IJwtTokenService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var secret = config["JWTSettings:Secret"]
                ?? throw new InvalidOperationException("Falta JWTSettings:Secret en la configuración.");
            var issuer    = config["JWTSettings:Issuer"]    ?? "leccionario_conduccion";
            var audience  = config["JWTSettings:Audience"]  ?? "cplec";
            var expiryStr = config["JWTSettings:ExpiryHours"];
            var expiry    = int.TryParse(expiryStr, out var h) ? h : 8;
            return new JwtTokenService(secret, issuer, audience, expiry);
        });

        // Authorization: handler del filtro de codigo_sistema (singleton,
        // stateless — ver ADR-006).
        services.AddSingleton<IAuthorizationHandler, SistemaClaimAuthorizationHandler>();

        // Fallback policy: cualquier [Authorize] requiere que el JWT pertenezca
        // al sistema "cplec". [AllowAnonymous] corta la policy.
        services.AddAuthorization(o =>
        {
            o.AddPolicy("CplecSystem", p =>
                p.RequireAuthenticatedUser().AddRequirements(new SistemaClaimRequirement()));
            o.FallbackPolicy = o.GetPolicy("CplecSystem");
        });

        return services;
    }

    /// <summary>
    /// Registra la infraestructura compartida: el <c>sigafi_esContext</c> con
    /// el proveedor MySQL de Pomelo, apuntando a <c>sigafi_es</c> en MySQL 5.7.
    /// Los servicios que dependen del contexto (AuthService,
    /// RefreshTokenService, DistributivoGuard, AuditService) llegan en los
    /// PRs siguientes — ver <c>docs/05 sección "Regeneración de entidades"</c>.
    /// </summary>
    public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SigafiDb")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:SigafiDb en la configuración.");

        services.AddDbContext<sigafi_esContext>(o =>
            o.UseMySql(connectionString, ServerVersion.Create(new Version(5, 7, 21), Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql), my =>
                my.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromSeconds(2), errorNumbersToAdd: null)));

        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDistributivoGuard, DistributivoGuard>();
        services.AddScoped<IPeriodosPorNivelService, PeriodosPorNivelService>();
        services.AddScoped<IMisParalelosService, MisParalelosService>();
        services.AddScoped<IParalelosInspectorService, ParalelosInspectorService>();
        services.AddScoped<INominaAlumnosService, NominaAlumnosService>();
        services.AddScoped<ISesionService, SesionService>();
        services.AddScoped<IAsistenciaService, AsistenciaService>();
        services.AddScoped<IHorarioCarreraGuard, HorarioCarreraGuard>();
        services.AddScoped<IFranjaZGuard, FranjaZGuard>();
        services.AddScoped<IFranjaService, FranjaService>();
        services.AddScoped<IConflictoHorarioService, ConflictoHorarioService>();
        services.AddScoped<IEscrituraSerializable, EscrituraSerializable>();
        services.AddScoped<IHorarioService, HorarioService>();

        return services;
    }

    /// <summary>
    /// Configura la autenticación JWT con los parámetros de
    /// <c>JWTSettings</c>. El claim <c>codigo_sistema</c> se valida en la
    /// fallback policy (ver <c>AddApplicationLayer</c>).
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["JWTSettings:Secret"]
            ?? throw new InvalidOperationException("Falta JWTSettings:Secret en la configuración.");
        var issuer   = configuration["JWTSettings:Issuer"]   ?? "leccionario_conduccion";
        var audience = configuration["JWTSettings:Audience"] ?? "cplec";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                // Sin esto, ASP.NET Core mapea `sub` → ClaimTypes.NameIdentifier
                // (y `role` → ClaimTypes.Role, etc.) antes de poblar `User`. Eso
                // rompe `User.FindFirstValue("sub")` que usamos en DistributivoGuard
                // y SesionesController para tomar el idProfesor del token.
                // Ver https://learn.microsoft.com/aspnet/core/security/authentication/jwt
                o.MapInboundClaims = false;

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidIssuer              = issuer,
                    ValidateAudience         = true,
                    ValidAudience            = audience,
                    ValidateLifetime         = true,
                    ClockSkew                = TimeSpan.FromMinutes(5),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    // Los claims personalizados del sistema cplec viajan con su
                    // nombre original (no se mapean a URI de esquema). Importante:
                    // `role` sí se queda con nombre "role", pero `Authorize(Roles=...)`
                    // usa `ClaimTypes.Role` por defecto — por eso lo declaramos
                    // explícitamente aquí.
                    RoleClaimType            = "role",
                    NameClaimType            = "nombre"
                };
            });

        return services;
    }

    /// <summary>
    /// Rate limiting nativo de .NET 8 (<c>Microsoft.AspNetCore.RateLimiting</c>).
    /// Las policies se aplican por endpoint con
    /// <c>[EnableRateLimiting("nombre")]</c>. Ver ADR-005 decisión (e).
    /// </summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var loginVentana    = configuration.GetValue("RateLimiting:LoginVentanaMinutos", 5);
        var loginIntentos   = configuration.GetValue("RateLimiting:LoginIntentosPorVentana", 5);
        var reportesPorMin  = configuration.GetValue("RateLimiting:ReportesPorMinuto", 10);
        var generalPorMin   = configuration.GetValue("RateLimiting:GeneralPorMinuto", 120);

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Login: por IP. 5 intentos cada 5 minutos.
            o.AddPolicy("login", ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ip,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit          = loginIntentos,
                        Window               = TimeSpan.FromMinutes(loginVentana),
                        QueueLimit           = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            // Reportes: por usuario autenticado (claim uid). 10 por minuto.
            o.AddPolicy("reportes", ctx =>
            {
                var userId = ctx.User?.FindFirst("uid")?.Value ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = reportesPorMin,
                        Window      = TimeSpan.FromMinutes(1)
                    });
            });

            // General: por IP. 120 por minuto. Política global opcional.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ip,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = generalPorMin,
                        Window      = TimeSpan.FromMinutes(1)
                    });
            });
        });

        return services;
    }

    /// <summary>Swagger + JWT bearer security definition.</summary>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Leccionario e Inspección (cplec)",
                Version = "v1",
                Description = "API del leccionario y la inspección de la Escuela de Conducción del ISTPET."
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT emitido por POST /api/auth/login"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });
        return services;
    }

    /// <summary>CORS: una sola policy llamada <c>AllowFrontend</c>, orígenes desde <c>AllowedOrigins</c>.</summary>
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (origins.Length == 0)
            throw new InvalidOperationException("Falta AllowedOrigins en la configuración. La API no se puede iniciar sin un origen definido.");

        services.AddCors(o => o.AddPolicy("AllowFrontend", p =>
        {
            p.WithOrigins(origins)
             .AllowAnyHeader()
             .AllowAnyMethod()
             .AllowCredentials();
        }));
        return services;
    }
}
