---
title: Diseño del scaffold — Leccionario.Api (cplec)
status: draft
date: 2026-08-06
branch: feature/scaffold-base
target_pr: <TBD>
spec: docs/01-arquitectura.md · docs/03-autenticacion-rbac.md · docs/05-lineamientos-backend.md
author: arquitecto
---

# Diseño del scaffold — Leccionario.Api (cplec)

## Resumen ejecutivo

Scaffold base del backend `Leccionario.Api` que produce una solución que **compila
limpio (`--warnaserror`), arranca, expone un `/api/health` y un `/swagger` en
Development, y tiene todos los middlewares y políticas de seguridad cableados**, pero
**sin lógica de negocio**. La lógica llega en PRs siguientes con TDD
(`feature/auth-login` primero, después `feature/rbac-distributivo-guard`).

La estrategia es **portar verbatim todo lo que es seguro reutilizar de
`BienestarInstitucional.Api`** (auth plumbing, middlewares, jerarquía de errores,
extensions) y **diseñar desde cero solo las piezas que Bienestar no tiene o que
cplec hace distinto** (filtro de `codigo_sistema`, `DistributivoGuard`,
`/api/auth/me`, rate limiting nativo de .NET 8).

Tres decisiones son no triviales y se documentan en detalle abajo:

1. **Filtro `codigo_sistema == "cplec"`** → fallback policy de autorización
   (AuthorizationHandler), no `IAuthorizationFilter`, no middleware manual.
2. **Rate limiting** → `Microsoft.AspNetCore.RateLimiting` nativo de .NET 8
   (particionado por IP/ruta), no middleware in-memory de Bienestar.
3. **Auditoría en scaffold** → versión reducida que escribe solo a `ILogger` (no a
   BD). La persistencia en `gest_audit_registros` llega cuando EF Core Power Tools
   scaffoldee las entidades (PR posterior, fuera de scope aquí).

---

## 1. Estructura del scaffold (carpetas, archivos)

Toda ruta es relativa a `src/Leccionario.Api/` salvo donde se indique.

### 1.1 Archivos del proyecto

| Ruta | Estado | Origen | Tests | Notas |
|---|---|---|---|---|
| `Leccionario.Api.csproj` | Reescrito | NUEVO (port de Bienestar + paquetes del docs/05) | n/a | `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` |
| `Program.cs` | Reescrito | NUEVO (port de Bienestar, slim) | smoke (`ApiSmokeTests.cs`) | Cablea todas las extensions |
| `appsettings.json` | Extendido | NUEVO | n/a | Estructura completa, sin secretos |
| `appsettings.example.json` | Extendido | NUEVO | n/a | Placeholders con `<…>` |
| `appsettings.Development.json` | (no commiteado) | NUEVO | n/a | gitignored, con valores reales |
| `Properties/launchSettings.json` | Conservado | `dotnet new webapi` | n/a | Sin cambios |
| `Leccionario.Api.http` | Conservado | `dotnet new webapi` | n/a | Plantilla, se actualiza cuando existan endpoints reales |

### 1.2 Domain

| Ruta | Estado | Origen | Tests | Notas |
|---|---|---|---|---|
| `Domain/Entities/.gitkeep` | NUEVO | placeholder | n/a | Vacío hasta EF Power Tools |

> Las entidades las scaffoldea EF Core Power Tools en un PR posterior
> (`feature/ef-powertools-scaffold`). **No** se crea ninguna entidad a mano en este
> PR — el archivo `.gitkeep` evita que git borre el directorio.

### 1.3 Application

| Ruta | Estado | Origen | Tests | Notas |
|---|---|---|---|---|
| `Application/Common/Exceptions/AppException.cs` | NUEVO | docs/05 sección "Manejo de errores" | `AppExceptionTests.cs` | Base abstracta con `Codigo` y `HttpStatus` |
| `Application/Common/Exceptions/ValidacionException.cs` | NUEVO | docs/05 | (en `AppExceptionTests`) | `400 VALIDACION` |
| `Application/Common/Exceptions/NoEncontradoException.cs` | NUEVO | docs/05 | (en `AppExceptionTests`) | `404 NO_ENCONTRADO` |
| `Application/Common/Exceptions/ProhibidoException.cs` | NUEVO | docs/05 | (en `AppExceptionTests`) | `403` base |
| `Application/Common/Exceptions/DistributivoAjenoException.cs` | NUEVO | docs/03 sección 5 + docs/05 | (en `AppExceptionTests`) | `403 DISTRIBUTIVO_AJENO` |
| `Application/Common/Exceptions/SesionCerradaException.cs` | NUEVO | docs/05 | (en `AppExceptionTests`) | `403 SESION_CERRADA` |
| `Application/Common/Exceptions/FueraDePlazoException.cs` | NUEVO | docs/05 | (en `AppExceptionTests`) | `403 FUERA_DE_PLAZO` |
| `Application/Common/Exceptions/ConflictoException.cs` | NUEVO | docs/05 | (en `AppExceptionTests`) | `409` |
| `Application/Auth/Authorization/SistemaClaimRequirement.cs` | NUEVO | decisión (a) | `SistemaClaimHandlerTests.cs` | Requirement: el claim `codigo_sistema` debe ser `cplec` |
| `Application/Auth/Authorization/SistemaClaimAuthorizationHandler.cs` | NUEVO | decisión (a) | `SistemaClaimHandlerTests.cs` | Handler que satisface o rechaza el requirement |
| `Application/Authenticacion/Auth/JwtTokenService.cs` | Reescrito | **PORT** de `BienestarInstitucional.Api/Application/Authenticacion/Auth/JwtTokenService.cs` | `JwtTokenServiceTests.cs` (portado) | Solo cambia defaults: `Issuer="leccionario_conduccion"`, `Audience="cplec"` |
| `Application/Authenticacion/Auth/AuthDtos.cs` | Reescrito | **PORT** parcial | (junto a `AuthServiceTests` en auth-login) | Quitar `RegisterStudentDto`, `SistemaAccesoDto`, `LoginResponseDto.Sistemas`. Conservar `LoginDto`, `LoginResponseDto`, `RefreshTokenRequestDto`, `LogoutRequestDto`, `RefreshTokenResponseDto`, `UsuarioDto`, `RolCatalogoDto` |
| `Application/Authenticacion/Auth/PasswordService.cs` | **PORT verbatim** | Bienestar | `PasswordServiceTests.cs` (portado) | Sin cambios, es estático |
| `Application/Authenticacion/Auth/CuentaInactivaException.cs` | **PORT verbatim** | Bienestar | (en `AuthServiceTests` en auth-login) | Sin cambios |
| `Application/Authenticacion/Auth/IRefreshTokenService.cs` | NUEVO (interfaz) | docs/03 sección 2 | (tests en auth-login) | Definido acá para que DI compile. **No** se incluye impl — depende de `sigafi_esContext` (llega en PR posterior) |
| `Application/Authenticacion/Auth/IJwtTokenService.cs` | (incluido en `JwtTokenService.cs`) | port | (junto a `JwtTokenServiceTests`) | Mismo archivo, como en Bienestar |
| `Application/Authenticacion/Auth/IAuthService.cs` | **NO en scaffold** | n/a | n/a | Aparece en `feature/auth-login`. Sin él no se registra nada en DI |
| `Application/Asistencia/Services/IDistributivoGuard.cs` | NUEVO (interfaz) | docs/03 sección 5 + decisión (b) | (tests en rbac-distributivo-guard) | `Task<bool> DocenteTieneAsignacionAsync(string idProfesor, int idAsignacion)` |

### 1.4 Infrastructure

| Ruta | Estado | Origen | Tests | Notas |
|---|---|---|---|---|
| `Infrastructure/DbContexts/.gitkeep` | NUEVO | placeholder | n/a | Vacío hasta EF Power Tools |
| `Infrastructure/DbContexts/DesignTime/sigafi_esContextFactory.cs` | NUEVO | docs/01 sección "Capas" + docs/05 | n/a | Factory `IDesignTimeDbContextFactory<sigafi_esContext>` solo para que `dotnet ef`/Power Tools puedan scaffoldear. Sin implementación hasta tener entidades |

> **Decisión consciente:** `sigafi_esContext` no existe en el scaffold. Cualquier
> servicio que dependa de él (RefreshTokenService, AuditMiddleware persistido,
> DistributivoGuard) **se queda en este PR como interfaz o stub**. Sus
> implementaciones reales llegan cuando EF Power Tools scaffoldea las entidades
> (PR `feature/ef-powertools-scaffold`, posterior).

### 1.5 Controllers

| Ruta | Estado | Origen | Tests | Notas |
|---|---|---|---|---|
| `Controllers/HealthController.cs` | NUEVO | docs/05 sección "Estructura" | `HealthControllerTests.cs` | `GET /api/health` (anónimo), `GET /api/health/db` (verifica conexión, tolera fallo) |
| `Controllers/Auth/AuthController.cs` | **NO en scaffold** | n/a | n/a | Aparece completo en `feature/auth-login` |

### 1.6 Extensions

| Ruta | Estado | Origen | Tests | Notas |
|---|---|---|---|---|
| `Extensions/DependencyInjectionExtensions.cs` | NUEVO | **PORT** parcial de Bienestar | `DependencyInjectionExtensionsTests.cs` | Métodos: `AddApplicationLayer`, `AddInfrastructureLayer`, `AddJwtAuthentication`, `AddSwaggerDocumentation`, `AddCorsPolicy`. **No** incluye `AddFormOptions` (cplec v1 no recibe uploads), ni EmailQueue/Hosted services |
| `Extensions/ApplicationBuilderExtensions.cs` | NUEVO | **PORT** de Bienestar | (en `ApiSmokeTests`) | Métodos: `UseApiExceptionHandling`. **No** `EnsureDatabaseSetup` (prohibido por ADR-004: esquema por `.sql`, no `EnsureCreated`) |

### 1.7 Middlewares

| Ruta | Estado | Origen | Tests | Notas |
|---|---|---|---|---|
| `Middlewares/AppExceptions.cs` | Reescrito | docs/05 + decisión (d) | `ExceptionClassifierTests.cs` | Sustituye la jerarquía de Bienestar por la de `docs/05`. La vieja jerarquía (`EntityNotFoundException`, `BusinessRuleException`, etc.) **no se porta**: cada sistema ISTPET puede tener las suyas. Se conserva solo lo que cplec necesita |
| `Middlewares/ExceptionClassifier.cs` | Reescrito | NUEVO, basado en Bienestar | `ExceptionClassifierTests.cs` | Switch actualizado a las nuevas excepciones. La generación de código `XXX-NNNN` se mantiene pero con módulo fijo `APP` (cplec es un sistema chico, no necesita prefijos por módulo) |
| `Middlewares/ApiExceptionMiddleware.cs` | **PORT** de Bienestar, ajustado | Bienestar | `ApiExceptionMiddlewareTests.cs` | Misma estructura: lee `AppException`, `CuentaInactivaException`, etc., produce el JSON `{codigo, mensaje, detalles, traceId}` definido en docs/04 |
| `Middlewares/SecurityHeadersMiddleware.cs` | **PORT verbatim** | Bienestar | `SecurityHeadersMiddlewareTests.cs` | Sin cambios: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` |
| `Middlewares/AuditMiddleware.cs` | Reescrito como **STUB** | decisión (f) | `AuditMiddlewareTests.cs` | Versión scaffold: escribe un `LogInformation` con method, path, status, duration, userId, jti. **No** persiste en BD. La versión persistida llega cuando EF Power Tools scaffoldee `GestAuditRegistros` |

### 1.8 `src/Leccionario.Tests/`

| Ruta | Estado | Origen | Notas |
|---|---|---|---|
| `Leccionario.Tests.csproj` | Reescrito | NUEVO (port de Bienestar, mismo stack) | Paquetes: MSTest 3.2.0, Moq 4.20.70, FluentAssertions 6.12.0, EF InMemory 8.0.*, NET.Test.Sdk 17.9.0 |
| `UnitTest1.cs` (placeholder de `dotnet new`) | Eliminado | n/a | Borrar |
| `IntegrationTestConnection.cs` | NUEVO | **PORT** de Bienestar | Lee `SIGAFI_INTEGRATION_CONNECTION` o `appsettings.IntegrationTests.json` |
| `appsettings.IntegrationTests.example.json` | NUEVO | placeholder commitado | Plantilla, gitignored el `.json` real |
| `Builders/.gitkeep` | NUEVO | placeholder | Vacío hasta que existan builders |
| `ApiSmokeTests.cs` | NUEVO | smoke | Arranca `WebApplicationFactory<Program>`, verifica `/api/health` 200 y `/swagger` |
| `ExceptionClassifierTests.cs` | NUEVO | smoke | Cada subclase mapea al `(Codigo, HttpStatus)` esperado |
| `AppExceptionTests.cs` | NUEVO | smoke | Constructores setean `Codigo` y `HttpStatus` correctos |
| `ApiExceptionMiddlewareTests.cs` | NUEVO | smoke | Cada excepción produce el JSON esperado |
| `SecurityHeadersMiddlewareTests.cs` | NUEVO | smoke | Headers correctos en respuesta |
| `AuditMiddlewareTests.cs` | NUEVO | smoke | Stub escribe log con los campos esperados, salta `/api/auth/login` |
| `HealthControllerTests.cs` | NUEVO | smoke | `/api/health` 200 sin auth |
| `DependencyInjectionExtensionsTests.cs` | NUEVO | smoke | `AddApplicationLayer` + `AddInfrastructureLayer` no lanzan; resuelven las claves registradas |
| `JwtTokenServiceTests.cs` | **PORT** de Bienestar | port | `GenerateAccessToken` produce token válido, `ValidateToken` lo acepta, secret < 32 bytes lanza |
| `PasswordServiceTests.cs` | **PORT** de Bienestar | port | Hash/Verify, `GenerarHashCentinela` produce valores distintos en llamadas consecutivas, `IsHashed` correcto |
| `SistemaClaimHandlerTests.cs` | NUEVO | decisión (a) | Caso positivo (claim = `cplec`), caso negativo (otro sistema), caso sin claim |

---

## 2. Decisiones arquitectónicas

### a) Filtro `codigo_sistema == "cplec"`

**Contexto.** El secreto JWT es compartido entre todos los sistemas ISTPET
(ver [ADR-003](docs/adr/ADR-003-autenticacion-local-sigafi.md)). Sin una segunda
comprobación, un token emitido por Bienestar pasa la validación de firma y entra
a cplec. El check debe ser **global** (toda ruta autenticada), **declarativo**
(que el código de los controllers no lo olvide) y testeable de forma aislada.

**Opciones evaluadas.**

1. **Middleware manual** después de `UseAuthentication`. Lee
   `context.User.FindFirst("codigo_sistema")`. Si falta o es ≠ `"cplec"`, devuelve
   403. Simple, pero: (i) no aparece en la firma de los controllers; (ii) no se
   integra con `[AllowAnonymous]`; (iii) fácil de olvidar al agregar un endpoint.

2. **`IAuthorizationFilter` registrado globalmente**. Forma parte del pipeline
   MVC, corre por endpoint `[Authorize]`. Funciona, pero requiere un filtro por
   cada ruta o un global filter que matchee todas; menos idiomático en .NET 8
   que las policies.

3. **`AuthorizationHandler` + fallback policy** (recomendada).
   `services.AddAuthorization(o => o.FallbackPolicy = policyWithRequirement)`.
   El handler lee el claim; si no coincide, llama `context.Fail()`. La policy
   aplica a **toda ruta con `[Authorize]`** (incluyendo las nuevas por defecto).
   Los endpoints anónimos usan `[AllowAnonymous]` y se excluyen.

**Decisión: opción 3.**

```csharp
// Application/Auth/Authorization/SistemaClaimAuthorizationHandler.cs
public class SistemaClaimAuthorizationHandler
    : AuthorizationHandler<SistemaClaimRequirement>
{
    private const string CodigoSistema = "cplec";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SistemaClaimRequirement requirement)
    {
        var claim = context.User.FindFirst("codigo_sistema")?.Value;
        if (string.Equals(claim, CodigoSistema, StringComparison.Ordinal))
            context.Succeed(requirement);
        // Si no coincide, context.Fail() implícito → 403
        return Task.CompletedTask;
    }
}
```

Registro en `Extensions/DependencyInjectionExtensions.cs`:

```csharp
services.AddSingleton<IAuthorizationHandler, SistemaClaimAuthorizationHandler>();
services.AddAuthorization(o =>
{
    o.AddPolicy("CplecSystem", p =>
        p.RequireAuthenticatedUser().AddRequirements(new SistemaClaimRequirement()));
    o.FallbackPolicy = o.GetPolicy("CplecSystem");
});
```

**Consecuencias.**

- ✅ Cualquier controller con `[Authorize]` (sin policy explícita) queda protegido
  por el filtro de sistema automáticamente. Imposible olvidar.
- ✅ `[AllowAnonymous]` corta la policy, sin tener que tocar el handler.
- ✅ Testeable como `AuthorizationHandler<SistemaClaimRequirement>` puro, sin
  WebApplicationFactory.
- ❌ Si el handler falla (excepción), el pipeline devuelve 500, no 403. Aceptable:
  un fallo en la verificación de seguridad es un bug, no un éxito silencioso.
- ❌ Para casos puntuales que necesiten un comportamiento distinto
  (p. ej. un endpoint compartido entre dos sistemas en el futuro) hay que
  decorar con `[AllowAnonymous]` y reescribir — pero no aplica a cplec v1.

**Mitigaciones.**

- El test `SistemaClaimHandlerTests.SinClaim_LlamaContextFail` cubre el caso de
  token sin el claim (que pasa validación de firma pero no es nuestro).
- `ApiSmokeTests` valida que un token de Bienestar (claim `bien_proy062026`) sea
  rechazado en una ruta cualquiera (test con WebApplicationFactory).

---

### b) `DistributivoGuard`

**Contexto.** Cualquier endpoint que reciba `idAsignacion` o `idSesion` debe
verificar que el `idProfesor` del token es el dueño. Es la pieza con más riesgo
de seguridad del sistema y la que más tests debe tener (docs/07 sección "Autorización").

**Opciones evaluadas.**

1. **Lógica inline en cada service.** Cada service abre su propio
   `AnyAsync(ap => ap.IdAsignacion == …)`. Repetido, fácil de olvidar,
   imposible de cubrir con un solo set de tests.

2. **Método de extensión sobre `sigafi_esContext`**
   (`_db.DocenteTieneAsignacionAsync(...)`). Centraliza la consulta pero
   contamina el `DbContext` con semántica de negocio.

3. **Service dedicado en `Application/Asistencia/Services/DistributivoGuard.cs`**
   (recomendada). Vive en Application porque es lógica de negocio (no acceso a
   datos puro). El controller llama al service, el service llama al guard
   primero. Si el guard devuelve `false`, el service lanza
   `DistributivoAjenoException` → 403 `DISTRIBUTIVO_AJENO`.

**Decisión: opción 3.**

```csharp
// Application/Asistencia/Services/IDistributivoGuard.cs
public interface IDistributivoGuard
{
    Task<bool> DocenteTieneAsignacionAsync(string idProfesor, int idAsignacion);
}

// Application/Asistencia/Services/DistributivoGuard.cs (real impl, en PR posterior)
public class DistributivoGuard : IDistributivoGuard
{
    private readonly sigafi_esContext _db;
    public DistributivoGuard(sigafi_esContext db) => _db = db;

    public async Task<bool> DocenteTieneAsignacionAsync(string idProfesor, int idAsignacion)
        => await _db.AsignacionesProfesores
            .AsNoTracking()
            .AnyAsync(ap => ap.IdAsignacion == idAsignacion
                         && ap.IdProfesor   == idProfesor
                         && (ap.Activo == null || ap.Activo == true)
                         && (ap.EsActivaAsignacion == null || ap.EsActivaAsignacion == true));
}

// Uso típico (en un service, no en el controller):
if (!await _guard.DocenteTieneAsignacionAsync(idProfesorDelToken, idAsignacion))
    throw new DistributivoAjenoException();
```

**Consecuencias.**

- ✅ Una sola consulta canónica. Testeable con EF InMemory
  (`AsignacionBuilder` en `Leccionario.Tests/Builders/`).
- ✅ El service es el que lanza la excepción tipada, separando "decide" (guard)
  de "ejecuta la política" (service).
- ✅ El inspector **no** pasa por el guard (su rol ya se validó por `[Authorize]`).
- ❌ Una query a BD por cada endpoint. Para v1 es aceptable (índice sobre
  `IdAsignacion` en PK + `IdProfesor` en filtro).

**Cache:** no en v1. La consulta es indexada, fresca es más importante que
microsegundos, y un cache añadiría superficie de invalidación. Si la métrica
muestra hotspot, pasar a `IMemoryCache` con TTL de 60s por `(idProfesor, idAsignacion)`.

**Test de integración:** sí, pero self-skip si no hay MySQL
(vía `IntegrationTestConnection`). Cubre el caso "asignación existe en BD pero
`activo = 0`" — que EF InMemory no valida por constraint.

---

### c) Endpoint `GET /api/auth/me`

**Contexto.** El frontend reconstruye el estado al arrancar. No debe decodificar
el JWT en cliente (docs/03 sección 7). El endpoint devuelve el perfil + roles +
paralelos del usuario autenticado.

**Decisión sobre los datos devueltos.**

```jsonc
// GET /api/auth/me → 200 (rol: cualquier cplec_*)
{
  "usuario": {
    "idSigafi": "1804567890",
    "nombre":   "MARIA ELENA TORRES",
    "email":    "mtorres@istpet.edu.ec",
    "tipoUsuario": "profesor",
    "roles":    ["cplec_docente"]            // roles activos, refrescados de BD
  },
  "paralelos": [                              // [] para inspector
    {
      "idAsignacion": 23229,
      "idPeriodo":    "OCC2025",
      "asignatura":   "GEOGRAFÍA DEL ECUADOR",
      "tipoLicencia": "TIPO \"C\"",
      "jornada":      "NOCTURNA",
      "modalidad":    "PRESENCIAL",
      "paralelo":     "C",
      "fechaInicial": "2025-10-06",
      "fechaFin":     "2025-11-05",
      "totalAlumnos": 29
    }
  ],
  "permisos": {
    "puedeEditarAsistencia":   true,
    "puedeCerrarSesion":       true,
    "puedeReabrirSesion":      false,        // solo inspector
    "puedeEliminarAsistencia": false,
    "puedeDescargarReportes":  true
  }
}
```

**Cache: no en v1.** El endpoint es por-usuario, dinámico y de seguridad. Cachear
introduce race con revocaciones de rol. Si la latencia se vuelve problema, el
cache es por `(idUsuario, jti)` con TTL corto (≤ 60s) y se invalida al detectar
cambio de roles.

**Roles desincronizados entre JWT y consulta.** El endpoint consulta la BD
siempre, no confía en los claims del JWT. Si un usuario perdió un rol entre el
login y el `/me`, el endpoint refleja el estado actual. **El frontend debe
reaccionar** (logout automático si `roles` quedó vacío). El próximo refresh
token rotación reemitirá los claims correctos.

---

### d) Jerarquía de excepciones cplec vs Bienestar

**Contexto.** Bienestar tiene una jerarquía rica (`EntityNotFoundException`,
`BusinessRuleException`, `ValidationException`, etc.) con su clasificador.
docs/05 sección "Manejo de errores" propone una jerarquía más simple para cplec.

**Opciones evaluadas.**

1. **Portar la jerarquía completa de Bienestar.** Más clases, más switch en el
   classifier, mayor superficie a mantener. Innecesario: cplec tiene un solo
   dominio (asistencia), no necesita 30 subclases de `BusinessRuleException`.

2. **Usar solo la jerarquía de docs/05** (recomendada). 8 clases totales
   (`AppException` + 7 subclases). El `ExceptionClassifier` consume el `Codigo`
   y `HttpStatus` del `AppException` directamente — sin switch por tipo. Si
   aparece una nueva subclase, no se toca el classifier.

**Decisión: opción 2.**

```csharp
public abstract class AppException : Exception
{
    public string Codigo { get; }
    public int HttpStatus { get; }

    protected AppException(string codigo, int httpStatus, string mensaje)
        : base(mensaje)
    {
        Codigo    = codigo;
        HttpStatus = httpStatus;
    }
}

public sealed class ValidacionException : AppException
{
    public ValidacionException(string mensaje, object? detalles = null)
        : base("VALIDACION", 400, mensaje) { /* ... */ }
}
// … NoEncontradoException, ProhibidoException, DistributivoAjenoException,
//    SesionCerradaException, FueraDePlazoException, ConflictoException
```

`ExceptionClassifier` se simplifica:

```csharp
public static int GetHttpStatus(Exception ex) => ex switch
{
    AppException app     => app.HttpStatus,
    CuentaInactivaException => 403,
    UnauthorizedAccessException => 401,
    _ => 500
};

public static string GetCodigo(Exception ex) => ex switch
{
    AppException app     => app.Codigo,
    CuentaInactivaException => "CUENTA_INACTIVA",
    UnauthorizedAccessException => "CREDENCIALES_INVALIDAS",
    _ => "ERROR_INTERNO"
};
```

**Consecuencias.**

- ✅ El clasificador es trivial; añadir una excepción no lo modifica.
- ✅ El `Codigo` vive **en** la excepción, no en el clasificador. Coherente.
- ❌ Se pierde el `EntityNotFoundException` con campos `EntityName/FieldName`.
  Sustituido por `NoEncontradoException(mensaje, detalles: { entity, id })` —
  funcionalmente equivalente.

**Lo que NO se porta de Bienestar:**

- `BusinessRuleException` (→ usar `ProhibidoException` o `ValidacionException`).
- `EntityNotFoundException` (→ `NoEncontradoException`).
- `DatosExternoIncompletosException`, `ReferenciaInvalidaException`, y todas las
  excepciones específicas de los módulos de Bienestar (becas, casos, etc.) — son
  ruido en cplec.

**Lo que SÍ se porta de Bienestar:**

- `CuentaInactivaException` (sin cambios).
- `ApiExceptionMiddleware` (ajustado al nuevo clasificador).
- La estructura JSON `{codigo, mensaje, detalles, traceId}` de la respuesta.

---

### e) Rate limiting en login

**Contexto.** docs/04 sección "Rate limiting" exige:
- `/api/auth/login`: **5 / 5 min por IP**.
- Reportes con `formato=`: 10 / min por usuario.
- Resto: 120 / min por usuario.

Bienestar usa un `RateLimitingMiddleware` propio (in-memory,
`ConcurrentDictionary`). Limitaciones: (i) por IP, no por ruta; (ii) un solo
cubo global, no políticas; (iii) in-memory, no escala a multi-instancia.

**Decisión: usar `Microsoft.AspNetCore.RateLimiting`** (built-in .NET 8).

```csharp
// Extensions/DependencyInjectionExtensions.cs
services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 5,
                Window               = TimeSpan.FromMinutes(5),
                QueueLimit           = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
    o.AddPolicy("reportes", httpContext =>
    {
        var userId = httpContext.User.FindFirst("uid")?.Value ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1)
            });
    });
});

// Program.cs
app.UseRateLimiter();

// AuthController.Login
[EnableRateLimiting("login")]
public async Task<IActionResult> Login([FromBody] LoginDto dto) { ... }
```

**Consecuencias.**

- ✅ Política por ruta (`[EnableRateLimiting("...")]`), por IP o por usuario.
- ✅ 429 nativo con `Retry-After` correcto.
- ✅ Una línea para migrar a Redis: cambiar el `factory` por un particionador
  respaldado por Redis (e.g. `RedisRateLimiting.AspNetCore`).
- ❌ In-memory en v1 (mismo límite que Bienestar, pero ya con la forma
  correcta para migrar).

**Mitigación.** Documentar en ADR (futuro) el criterio de migración a Redis:
"cuando se despliegue a más de una instancia detrás de un balanceador". Mientras
tanto, el comportamiento es equivalente al de Bienestar.

**Por qué NO portar `RateLimitingMiddleware` de Bienestar:**

- No separa por ruta (Bienestar tiene un cubo por IP para todo).
- No soporta políticas por endpoint.
- Reescribirlo solo para mantener paridad es trabajo sin beneficio.

---

### f) `AuditMiddleware`

**Contexto.** cplec debe auditar todas las operaciones sobre asistencia. Las
reglas de BIenestar (skip `/api/auth/login`) se mantienen.

**Decisión: misma regla, pero ajustada.**

| Endpoint | ¿Se audita? | Por qué |
|---|---|---|
| `POST /api/auth/login` | NO | Recibe credenciales en el body. No se loguean |
| `POST /api/auth/refresh` | NO | El body trae un refresh token. Mismo motivo |
| `POST /api/auth/logout` | NO | El body trae un refresh token. Mismo motivo |
| `GET /api/auth/me` | **SÍ** | Útil para detectar anomalías (consultas repetidas, fuera de horario) |
| Todo lo demás | SÍ | |

**Implementación en scaffold: stub que escribe solo a `ILogger`.**

```csharp
// Middlewares/AuditMiddleware.cs (versión scaffold)
public async Task InvokeAsync(HttpContext context, ILogger<AuditMiddleware> logger)
{
    var sw = Stopwatch.StartNew();
    await _next(context);
    sw.Stop();

    if (EsEndpointSensibleSinAuditar(context)) return;

    logger.LogInformation(
        "AUDIT {Method} {Path} → {StatusCode} en {Ms}ms (user={User} jti={Jti})",
        context.Request.Method, context.Request.Path, context.Response.StatusCode,
        sw.ElapsedMilliseconds,
        context.User?.FindFirst("uid")?.Value ?? "anonymous",
        context.User?.FindFirst("jti")?.Value ?? "-");
}
```

**Consecuencias.**

- ✅ No depende de `sigafi_esContext` → compila y corre sin las entidades.
- ✅ Testeable con mocks de `ILogger`.
- � El rastro se pierde entre reinicios y no se puede consultar.

**Versión persistida (PR posterior):** cuando EF Power Tools scaffoldee
`GestAuditRegistros`, agregar `services.AddScoped<IAuditService, AuditService>()`
y cambiar el middleware para que persista + actualice con `StatusCode`. Misma
estructura que Bienestar.

**Por qué no portar verbatim el middleware de Bienestar ahora:**

- Depende de `sigafi_esContext` y de la entidad `GestAuditRegistros`.
- Ambas llegan con EF Power Tools (PR aparte).
- Hacer un port parcial ahora genera deuda que se sobreescribe igual.

---

### g) Tests In-Memory vs integración

**Contexto.** docs/07 sección "Tipos de prueba" define dos tipos: **unitaria**
(EF InMemory, siempre corre) y **integración** (MySQL real, corre solo si hay
conexión configurada). El patrón de Bienestar: helper
`IntegrationTestConnection` que lee env var o `appsettings.IntegrationTests.json`
y **se auto-skip** si no hay nada. Los tests de integración tienen un
`[TestInitialize]` que lanza `Assert.Inconclusive(...)` cuando falta la
conexión.

**Opciones evaluadas.**

1. **`[TestCategory("Integration")]` + filtro en CI.** Convención explícita,
   permite `dotnet test --filter "TestCategory!=Integration"`. Pero requiere
   disciplina (olvidar la categoría = test de integración ejecutándose en CI).

2. **Skip-on-missing-connection (portado de Bienestar).** Sin convención de
   nombres. El test **se ejecuta**, pero su `ClassInitialize` decide en
   milisegundos si skip o correr. Más robusto: cero mantenimiento.

3. **Ambos** (recomendada). Skip-on-missing-connection como mecanismo
   principal. `[TestCategory("Integration")]` como **etiqueta opcional** solo
   para filtrar en CI cuando se quiere una corrida rápida.

**Decisión: opción 3.**

```csharp
[TestClass]
[TestCategory("Integration")]
public class DistributivoGuardIntegracionTests
{
    [ClassInitialize]
    public static void Setup(TestContext ctx)
    {
        if (!IntegrationTestConnection.TryGetSigafiConnectionString(out _))
            Assert.Inconclusive("Integration tests requieren SIGAFI_INTEGRATION_CONNECTION o appsettings.IntegrationTests.json");
    }

    // tests reales...
}
```

**Convención de nombre de archivo:** libre. `XxxTests.cs` para unitarios;
`XxxIntegracionTests.cs` para integración. La categoría decide el filtro.

**Convención de nombre de método:** `Metodo_Escenario_ResultadoEsperado`
(como Bienestar). Ejemplos:
`DocenteTieneAsignacionAsync_CuandoAsignacionEsDeOtroProfesor_DevuelveFalse`,
`PasswordService_Hash_ProduceHashBcryptValido`.

**Consecuencias.**

- ✅ `dotnet test` corre todo lo que puede correr. Sin sorpresas.
- ✅ CI puede correr `dotnet test --filter "TestCategory!=Integration"` para
  feedback rápido.
- ✅ Integración corre cuando alguien con MySQL local lo pide (manual o via CI
  con secret).
- ❌ El test "se ejecuta" pero termina en `Inconclusive` si falta config. Eso
  cuenta como skipped, no como passed — bien.

---

## 3. Plan del primer PR (`feature/scaffold-base`)

### 3.1 Cambios a archivos existentes

- `src/Leccionario.sln`: agregar referencias a proyectos (ya existe).
- `src/Leccionario.Api/Leccionario.Api.csproj`: paquetes (ver sección 3.2).
- `src/Leccionario.Api/Program.cs`: rewrite (ver sección 3.3).
- `src/Leccionario.Api/appsettings.json`: agregar `ConnectionStrings`,
  `SistemaCodigo`, `JWTSettings` (estructura, sin secretos), `AllowedOrigins`,
  `Logging` extendido.
- `src/Leccionario.Api/appsettings.example.json`: igual que `appsettings.json`
  pero con placeholders `<…>`.
- `src/Leccionario.Tests/Leccionario.Tests.csproj`: paquetes.
- `src/Leccionario.Tests/UnitTest1.cs`: **eliminar** (placeholder de `dotnet new`).

### 3.2 Paquetes a agregar al `.csproj`

```xml
<!-- Leccionario.Api.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.*" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.*" PrivateAssets="all" />
  <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.*" />
  <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.0.*" />
  <PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
  <PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
</ItemGroup>
```

### 3.3 Forma de `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddInfrastructureLayer(builder.Configuration)
    .AddApplicationLayer();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRateLimiting(builder.Configuration);  // ver sección 3.4
builder.Services.AddSwaggerDocumentation();
builder.Services.AddCorsPolicy(builder.Configuration);

var app = builder.Build();

app.UseApiExceptionHandling();
app.UseCors("AllowFrontend");
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
else { app.UseHttpsRedirection(); }
app.UseSecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseAudit();      // stub que escribe a ILogger en scaffold
app.MapControllers();

app.Run();
```

### 3.4 Lista de archivos a crear (ordenada por dependencia)

| # | Archivo | Tipo | Test smoke |
|---|---|---|---|
| 1 | `Domain/Entities/.gitkeep` | placeholder | — |
| 2 | `Infrastructure/DbContexts/.gitkeep` | placeholder | — |
| 3 | `Application/Common/Exceptions/AppException.cs` | NUEVO | `AppExceptionTests` |
| 4 | `Application/Common/Exceptions/ValidacionException.cs` | NUEVO | (en `AppExceptionTests`) |
| 5 | `Application/Common/Exceptions/NoEncontradoException.cs` | NUEVO | (idem) |
| 6 | `Application/Common/Exceptions/ProhibidoException.cs` | NUEVO | (idem) |
| 7 | `Application/Common/Exceptions/DistributivoAjenoException.cs` | NUEVO | (idem) |
| 8 | `Application/Common/Exceptions/SesionCerradaException.cs` | NUEVO | (idem) |
| 9 | `Application/Common/Exceptions/FueraDePlazoException.cs` | NUEVO | (idem) |
| 10 | `Application/Common/Exceptions/ConflictoException.cs` | NUEVO | (idem) |
| 11 | `Application/Authenticacion/Auth/PasswordService.cs` | **PORT** | `PasswordServiceTests` |
| 12 | `Application/Authenticacion/Auth/AuthDtos.cs` | **PORT parcial** | (cubierto en auth-login) |
| 13 | `Application/Authenticacion/Auth/CuentaInactivaException.cs` | **PORT** | (en auth-login) |
| 14 | `Application/Authenticacion/Auth/JwtTokenService.cs` | **PORT** | `JwtTokenServiceTests` |
| 15 | `Application/Authenticacion/Auth/IRefreshTokenService.cs` | NUEVO (interfaz) | (en auth-login) |
| 16 | `Application/Asistencia/Services/IDistributivoGuard.cs` | NUEVO (interfaz) | (en rbac-distributivo-guard) |
| 17 | `Application/Auth/Authorization/SistemaClaimRequirement.cs` | NUEVO | `SistemaClaimHandlerTests` |
| 18 | `Application/Auth/Authorization/SistemaClaimAuthorizationHandler.cs` | NUEVO | (idem) |
| 19 | `Middlewares/AppExceptions.cs` | NUEVO | `ExceptionClassifierTests` |
| 20 | `Middlewares/ExceptionClassifier.cs` | NUEVO | (idem) |
| 21 | `Middlewares/ApiExceptionMiddleware.cs` | **PORT ajustado** | `ApiExceptionMiddlewareTests` |
| 22 | `Middlewares/SecurityHeadersMiddleware.cs` | **PORT** | `SecurityHeadersMiddlewareTests` |
| 23 | `Middlewares/AuditMiddleware.cs` | NUEVO (stub) | `AuditMiddlewareTests` |
| 24 | `Extensions/DependencyInjectionExtensions.cs` | **PORT parcial** | `DependencyInjectionExtensionsTests` |
| 25 | `Extensions/ApplicationBuilderExtensions.cs` | **PORT parcial** | (en `ApiSmokeTests`) |
| 26 | `Infrastructure/DbContexts/DesignTime/sigafi_esContextFactory.cs` | NUEVO (placeholder) | — |
| 27 | `Controllers/HealthController.cs` | NUEVO | `HealthControllerTests` |
| 28 | `Properties/launchSettings.json` | (sin cambios) | — |
| 29 | `Leccionario.Api.csproj` | (rewrite) | — |
| 30 | `Program.cs` | (rewrite) | `ApiSmokeTests` |
| 31 | `appsettings.json` | (extend) | — |
| 32 | `appsettings.example.json` | (extend) | — |
| 33 | `Leccionario.Tests/UnitTest1.cs` | **eliminar** | — |
| 34 | `Leccionario.Tests/Leccionario.Tests.csproj` | (rewrite) | — |
| 35 | `Leccionario.Tests/IntegrationTestConnection.cs` | **PORT** | — |
| 36 | `Leccionario.Tests/Builders/.gitkeep` | placeholder | — |
| 37 | `Leccionario.Tests/ApiSmokeTests.cs` | NUEVO (smoke) | sí |
| 38 | `Leccionario.Tests/ExceptionClassifierTests.cs` | NUEVO (smoke) | sí |
| 39 | `Leccionario.Tests/AppExceptionTests.cs` | NUEVO (smoke) | sí |
| 40 | `Leccionario.Tests/ApiExceptionMiddlewareTests.cs` | NUEVO (smoke) | sí |
| 41 | `Leccionario.Tests/SecurityHeadersMiddlewareTests.cs` | NUEVO (smoke) | sí |
| 42 | `Leccionario.Tests/AuditMiddlewareTests.cs` | NUEVO (smoke) | sí |
| 43 | `Leccionario.Tests/HealthControllerTests.cs` | NUEVO (smoke) | sí |
| 44 | `Leccionario.Tests/DependencyInjectionExtensionsTests.cs` | NUEVO (smoke) | sí |
| 45 | `Leccionario.Tests/JwtTokenServiceTests.cs` | **PORT** | sí |
| 46 | `Leccionario.Tests/PasswordServiceTests.cs` | **PORT** | sí |
| 47 | `Leccionario.Tests/SistemaClaimHandlerTests.cs` | NUEVO | sí |
| 48 | `Leccionario.Tests/appsettings.IntegrationTests.example.json` | NUEVO | — |

### 3.5 Criterios de "listo para mergear"

- [ ] `dotnet build src/Leccionario.sln --warnaserror` sin warnings ni errores.
- [ ] `dotnet test src/Leccionario.sln` pasa todos los smoke + unit (integración
      se auto-skipea si no hay MySQL).
- [ ] Cobertura: `Application/Common/Exceptions/*`, `Middlewares/*` y
      `Extensions/*` tienen tests smoke; `Application/Auth/*` ≥ 50% (lo demás
      llega con los PRs siguientes).
- [ ] `appsettings.Development.json` existe localmente (no commiteado) con
      secretos ficticios para que `Program.cs` arranque.
- [ ] `appsettings.example.json` está sincronizado con todas las claves nuevas.
- [ ] `git diff --check` no muestra espacios trailing ni líneas largas.
- [ ] El reviewer puede clonar, correr `./scripts/setup-hooks.sh`, hacer
      `dotnet test` y ver verde sin necesidad de MySQL.

---

## 4. Orden propuesto para los siguientes PRs

### 4.1 Secuencia

| # | Branch | Tipo merge | Qué incluye | Justificación del orden |
|---|---|---|---|---|
| 1 | `feature/scaffold-base` | **A (mecánico)** | Este PR. | Base para todo. |
| 2 | `feature/ef-powertools-scaffold` | **A (mecánico)** | Scaffolding de entidades desde `sigafi_es` con EF Power Tools. Genera `Domain/Entities/*` (≈80 archivos) y `Infrastructure/DbContexts/sigafi_esContext.cs`. | Desbloquea cualquier service que dependa de entidades. No toca lógica. |
| 3 | `feature/auth-login` | **B (lógica)** | `AuthService` (port de Bienestar, ajustada: solo `tablaSigafi='profesor'`, sin email/queue). `IAuthService` registrado en DI. `AuthController` con `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`. TDD completo: casos de docs/07 sección "Autenticación". `RefreshTokenService` impl real (ahora con DbContext disponible). | Es la pieza más crítica y la que más reglas de seguridad tiene. TDD estricto. |
| 4 | `feature/rbac-distributivo-guard` | **B (lógica)** | `SistemaClaimAuthorizationHandler` (si no entró en scaffold). `DistributivoGuard` real. Tests del guard (unit + integration). Wiring en DI. | Bloquea el endpoint `/me` y todos los de asistencia. |
| 5 | `feature/auth-me` | **B (lógica)** | `GET /api/auth/me` + service. DTO `MiPerfilDto`. Query con el `DistributivoGuard` para listar paralelos del docente. | Depende del guard (para docente) y del AuthService (para usuario + roles). |
| 6 | `feature/distributivo-periodos` | **B (lógica)** | `GET /api/mis-paralelos`, `GET /api/periodos/por-nivel`, `GET /api/paralelos/{id}/alumnos`. Toda query con `AsNoTracking()` y proyecciones a DTO. | Lee distributivo. Sin escrituras. Se puede testear 100% con InMemory. |
| 7 | `feature/asistencia-sesiones` | **B (lógica)** | CRUD de sesiones: `POST /api/paralelos/{id}/sesiones`, `GET /api/sesiones/{id}`, `PUT`, cerrar/reabrir. Transacciones + historial. | Empieza a escribir. Aquí aparece `IAsistenciaRepository`. |
| 8 | `feature/asistencia-marcas` | **B (lógica)** | `POST /api/sesiones/{id}/asistencias` (idempotente), `GET /api/asistencias/historial/{id}`. Validaciones de docs/04 sección "Asistencia" en orden. | El feature más sensible: reenvíos, constraints UNIQUE, ventana de edición. |
| 9 | `feature/reportes-core` | **B (lógica)** | Reportes JSON (`por-paralelo`, `por-estudiante`, `por-docente`, `resumen`). | Lectura pura, sin riesgo de escritura. |
| 10 | `feature/reportes-descarga` | **B (lógica)** | `?formato=xlsx` / `?formato=pdf` con ClosedXML + generador PDF. Rate limit `reportes` aplica acá. | Introduce el primer download binario — necesita tests de integración del Content-Type y Content-Disposition. |

### 4.2 Criterios de merge

- **Tipo A (mecánico):** mergea el dueño del repo (vos) cuando CI pasa. Cambios
  sin lógica: regeneraciones, refactors de tooling, agregados de paquetes.
- **Tipo B (lógica):** mergea el equipo técnico después de review + ejecución
  manual de los criterios de aceptación del spec. **TDD obligatorio**: el PR
  empieza con tests rojos y termina con verdes.

### 4.3 Acoplamiento entre PRs

```
[1 scaffold] ─┬─► [2 ef-powertools] ─┬─► [3 auth-login] ─► [4 rbac-distributivo]
               │                       │
               │                       └─► [5 auth-me]
               │                              │
               │                              └─► [6 distributivo-periodos]
               │                                       │
               │                                       └─► [7 asistencia-sesiones]
               │                                              │
               │                                              └─► [8 asistencia-marcas]
               │                                                     │
               │                                                     └─► [9 reportes-core]
               │                                                                  │
               │                                                                  └─► [10 reportes-descarga]
               │
               └─► (paralelo: cualquier PR que necesite un middleware nuevo)
```

Notas:

- El paso 2 (`ef-powertools-scaffold`) **no** se puede saltar: sin entidades no
  compila el `AuthService` ni el `DistributivoGuard`. Es un PR corto (≈30 min)
  pero obligatorio.
- Los pasos 4 y 5 están en ese orden porque `/me` para docente usa el guard. Si
  el guard se demora, `/me` puede salir primero con `paralelos: []` y el
  endpoint de mis-paralelos en paso 6.
- Cualquier PR puede agregar un endpoint a un middleware nuevo
  (e.g., `feature/rate-limit-reportes` se puede hacer en cualquier momento
  después del 1).

---

## 5. Riesgos y deuda técnica

| # | Riesgo | Cuándo duele | Mitigación / plan de salida |
|---|---|---|---|
| 1 | **Rate limiting in-memory** no escala a multi-instancia. | En el momento que haya 2 réplicas detrás de un balanceador, el límite real es `N_instancias × 5`. | Migrar a un particionador Redis-backed (`RedisRateLimiting.AspNetCore` o similar). Cambio aislado en `Extensions/DependencyInjectionExtensions.cs`; las policies `[EnableRateLimiting]` no cambian. **Trigger documentado:** "cuando se despliegue a más de una instancia". |
| 2 | **`AuditMiddleware` solo loguea**, no persiste. | No se puede reconstruir "qué hizo el docente tal día" sin parsear logs estructurados. | Reemplazar el middleware con la versión persistida (la misma que Bienestar) cuando EF Power Tools scaffoldee `GestAuditRegistros`. El cambio es interno al middleware; los consumidores no se enteran. |
| 3 | **`ExceptionClassifier` con prefijo fijo `APP`.** | Si en el futuro hay un módulo interno que quiera códigos con su prefijo (`ASI-`, `REP-`), el classifier no lo soporta. | Extender `ExceptionClassifier.GetCodigo(ex)` para que las subclases puedan sobreescribir el prefijo (override de `Prefijo => "APP"`). O agregar un `Dictionary<Tipo, Prefijo>` si crece. |
| 4 | **`JwtTokenService` con valores por defecto `leccionario_conduccion` / `cplec` hardcoded.** | Si alguien deploya sin configurar `appsettings`, el sistema funciona pero los tokens emitidos no se pueden validar contra ningún IdP de la plataforma. | El constructor ya **lee de config primero** y solo usa el default si falta. `appsettings.example.json` deja claro que esos valores deben venir del ambiente. |
| 5 | **`IAuthorizationHandler` registrado como singleton** mientras `AuthorizationHandler` accede a claims. | Ninguno en la práctica — son stateless. Pero si en el futuro un handler accede a BD, hay que cambiar a `Scoped`. | Documentado en el handler: "no acceder a scoped services". Si aparece la necesidad, cambiar a `services.AddScoped<IAuthorizationHandler, ...>`. |
| 6 | **`SigafiDbContextFactory` en el scaffold es placeholder.** | `dotnet ef` o Power Tools fallan hasta que se implemente. | Aceptable: el PR `feature/ef-powertools-scaffold` lo implementa como parte del scaffolding. Mientras tanto, nadie corre EF contra `sigafi_es` (todo son migraciones SQL, ver ADR-004). |
| 7 | **`Domain/Entities/.gitkeep` se commitea y queda como ruido.** | Después de EF Power Tools, el archivo sobra. | Se borra en el PR `feature/ef-powertools-scaffold`. No es bloqueante. |
| 8 | **`AddInfrastructureLayer` en scaffold no se usa todavía.** | El DbContext no existe; agregar `AddDbContext<sigafi_esContext>` en el scaffold no compila. | La función existe pero su cuerpo solo configura `AddMemoryCache` y otros servicios que no dependen del DbContext. La línea `AddDbContext` se agrega en `feature/ef-powertools-scaffold`. |
| 9 | **Tests de `SistemaClaimAuthorizationHandler` no prueban la integración con `UseAuthorization`.** | Un cambio en el orden del pipeline podría romper la política sin que ningún test lo note. | `ApiSmokeTests` debe incluir un caso: emitir un JWT con `codigo_sistema = "bien_proy062026"` (firmado con el mismo secreto) y verificar que una ruta autenticada devuelve 403. Eso cubre la integración. |
| 10 | **El scaffold no tiene `efpt.config.json`.** | Regenerar entidades sin el config implica re-marcar 80 tablas a mano. | Se agrega en `feature/ef-powertools-scaffold`, commitado. |

---

## Anexo A — Preguntas abiertas / huecos detectados

1. **¿`BienestarInstitucional.Api` también necesita el filtro `codigo_sistema`?**
   No. Es un gap de seguridad específico de cplec — Bienestar se autentica contra
   el mismo `sigafi_es` pero su `SistemaCodigo` es distinto y no comparte con
   nadie. ¿Es esto cierto? Confirmar con el equipo de Bienestar antes de
   promover este filtro como patrón compartido.

2. **¿El secreto JWT compartido se rota alguna vez?** En la doc dice
   "ISTPET_Sistemas_Seguridad_ClaveCompartidaSecretSymmetricKey2026!". Si cambia,
   hay que invalidar todos los tokens. Para cplec v1 no es problema, pero
   cualquier rotación coordinada toca `appsettings.json` de TODOS los sistemas
   ISTPET. **Acción:** confirmar con el equipo de plataforma cuál es el plan
   de rotación y si está documentado en algún ADR global.

3. **¿`gest_audit_registros` se puede consultar desde otros sistemas?** Si el
   inspector quiere ver un rastro consolidado de quién tocó qué en cplec, ¿se
   puede cruzar con Bienestar? Para cplec v1 no aplica (cplec no exporta), pero
   la pregunta queda abierta para v2.

4. **`matriculas_asistencias` (legacy) tiene 6018 filas. ¿Se migran a
   `cplec_asistencias`?** docs/03 sección "Modelo de datos" dice que no se toca. Pero
   ¿qué pasa con un docente que quiere ver la lista de julio (que ya estaba en
   legacy)? ¿O el reporte del inspector arranca en 2026-08-06? Confirmar con
   inspección el alcance temporal mínimo de los reportes.

5. **`SesionCerradaException` y `FueraDePlazoException` están en el scaffold
   pero no hay endpoint que las lance todavía.** ¿Es YAGNI? Justificación:
   están en docs/05 como parte de la jerarquía, y aparecen en el primer PR de
   asistencia (#7). Tener las clases desde el scaffold evita una migración
   intermedia. Aceptable, pero documentar.

6. **¿`IRefreshTokenService` se queda como interfaz sin impl en el scaffold?**
   Sí, intencional. El impl necesita DbContext + entidad `RbacRefreshTokens`,
   ambos llegan en `feature/ef-powertools-scaffold`. Mientras tanto, el
   `AuthController` (cuando llegue en auth-login) detecta con `?? throw new
   NotImplementedException(...)` si se invoca por error.

7. **`SistemaClaimAuthorizationHandler` con `string.Equals(..., Ordinal)` vs
   `OrdinalIgnoreCase`.** El `codigo_sistema` siempre es lowercase por
   convención. Usar `Ordinal` falla si alguien pone `CPLEC` en BD. Usar
   `OrdinalIgnoreCase` es más permisivo. **Decisión propuesta:** `Ordinal`,
   con un test que verifique el rechazo de `"CPLEC"`. Si llega un caso real
   donde se necesita tolerancia de mayúsculas, se cambia el handler (test
   incluido).

8. **`appsettings.Development.json` no está commiteado**, pero el archivo
   **debe existir** en el entorno local para que `dotnet run` arranque
   (porque `JWTSettings:Secret` se valida como no-vacío en el constructor de
   `JwtTokenService`). ¿Lo dejamos como responsabilidad de quien clone el repo,
   o agregamos un `appsettings.Development.example.json` con un secret
   ficticio claramente marcado como NO-PARA-PRODUCCIÓN? **Recomendado:** el
   segundo, para que el primer `dotnet run` no falle con un error críptico.

9. **¿`dotnet new webapi` agregó `WeatherForecast.cs` y `WeatherForecastController.cs`?**
   Sí (ver `git status`). ¿Se borran en el scaffold o en `feature/auth-login`?
   **Decisión:** en el scaffold — son ruido que no queremos ni siquiera en el
   primer commit.

---

> **Handoff sugerido:** `@documenter` — crear los ADRs que salen de este
> diseño (mínimo: `ADR-005-scaffold-arquitectura.md` con el resumen de
> decisiones a-g, y `ADR-006-filtro-codigo-sistema.md` con la justificación
> del fallback policy). Después: `@implementer` para `feature/scaffold-base`
> siguiendo el plan de sección 3.

> **Handoff (alternativo):** si querés saltarte la formalización de ADRs y
> ir directo a código, el siguiente agente es `@implementer` con la orden de
> ejecutar el plan de sección 3 al pie de la letra.
