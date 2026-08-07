# feature/auth-login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout` and `GET /api/auth/me` for `Leccionario.Api`, with TDD coverage of every scenario in `docs/07-pruebas.md`.

**Architecture:** Port of `BienestarInstitucional.Api/Application/Services/AuthService.cs`, cut down to what cplec actually needs — only `profesor` logs in, no automatic role assignment. `AuthService` implements a new `IAuthService`; `RefreshTokenService` implements the `IRefreshTokenService` contract that already exists in the scaffold. `AuthController` is a thin pass-through with no `try/catch` — exceptions propagate to the already-existing `ApiExceptionMiddleware`.

**Tech Stack:** ASP.NET Core net8.0, EF Core 8 + Pomelo (MySQL 5.7.21), MSTest + Moq + FluentAssertions, EF Core InMemory for unit tests.

**Spec:** `docs/superpowers/specs/2026-08-06-auth-login-design.md` — read it first if anything below is unclear on *why*, not just *what*.

## Global Constraints

- Target framework `net8.0`, build with `dotnet build src/Leccionario.sln --warnaserror` — zero warnings, zero errors, always.
- **Every "boolean" column in this schema is `sbyte`/`sbyte?` (MySQL `tinyint`), NOT `bool`**, except `cplec_sesiones.activo` which is a real `bool?`. Compare with `== 1` / `!= 1`, never `== true`. This trips people up constantly — `usuarios.activo`, `rbac_rol.esActivo`, `rbac_usuario_rol.esActivo`, `rbac_modulos_operaciones.esActivo`, `rbac_rol_modulo_operacion.esActivo`, `profesores.esReal` are all `sbyte`/`sbyte?`.
- `Domain/Entities/*.cs` is generated code — **never edit it by hand**. Nothing in this plan touches it.
- Never wrap EF writes in `Database.BeginTransactionAsync()` in this plan — the InMemory provider used by unit tests throws `InvalidOperationException` on it (`TransactionIgnoredWarning`). Every write in this plan touches a single table, so a single `SaveChangesAsync()` call is already atomic — that satisfies `docs/05 sección Transacciones` (its rule is about writes spanning *multiple tables*).
- MSTest style: `[TestClass]` / `[TestMethod]`, method names `Metodo_Escenario_ResultadoEsperado`, assertions via FluentAssertions (`.Should()`).
- No secrets in test code — reuse the existing test constant pattern (`"this-is-a-test-secret-32-bytes-min!"` for JWT secrets, already used in `JwtTokenServiceTests.cs`).
- Run `dotnet build src/Leccionario.sln --warnaserror && dotnet test src/Leccionario.sln` after every task and confirm it is green before committing.

---

### Task 1: `SinAccesoSistemaException`

**Files:**
- Create: `src/Leccionario.Api/Application/Common/Exceptions/SinAccesoSistemaException.cs`
- Modify: `src/Leccionario.Tests/AppExceptionTests.cs`
- Modify: `src/Leccionario.Tests/ExceptionClassifierTests.cs`

**Interfaces:**
- Produces: `SinAccesoSistemaException` (no-arg constructor), in namespace `Leccionario.Api.Application.Common.Exceptions`, `: ProhibidoException`, `Codigo = "SIN_ACCESO_SISTEMA"`, `HttpStatus = 403`.

`docs/04-contrato-api.md` and `docs/07-pruebas.md` both reference `SIN_ACCESO_SISTEMA` (403 — "el usuario no tiene ningún rol `cplec_*`") but the exception class was never created in `feature/scaffold-base`. It slots into the existing `ProhibidoException` hierarchy, so `ExceptionClassifier` needs **no changes** — it already reads `Codigo`/`HttpStatus` off any `AppException` polymorphically.

- [ ] **Step 1: Write the failing tests**

Append to `src/Leccionario.Tests/AppExceptionTests.cs` (inside the existing `AppExceptionTests` class, after `ConflictoException_ConCodigo_DevuelveCustom`):

```csharp
    [TestMethod]
    public void SinAccesoSistemaException_DevuelveCodigo403()
    {
        var ex = new SinAccesoSistemaException();
        ex.Codigo.Should().Be("SIN_ACCESO_SISTEMA");
        ex.HttpStatus.Should().Be(403);
    }
```

Append to `src/Leccionario.Tests/ExceptionClassifierTests.cs` (inside `ExceptionClassifierTests`, after `GetCodigo_CuentaInactiva_DevuelveCodigoEstable`):

```csharp
    [TestMethod]
    public void GetCodigo_SinAccesoSistema_DevuelveCodigoEstable()
    {
        ExceptionClassifier.GetCodigo(new SinAccesoSistemaException())
            .Should().Be("SIN_ACCESO_SISTEMA");
        ExceptionClassifier.GetHttpStatus(new SinAccesoSistemaException())
            .Should().Be(403);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Leccionario.sln --filter "FullyQualifiedName~AppExceptionTests|FullyQualifiedName~ExceptionClassifierTests"`
Expected: build error — `SinAccesoSistemaException` does not exist.

- [ ] **Step 3: Create the exception**

```csharp
namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 403 SIN_ACCESO_SISTEMA — el usuario autenticó correctamente pero no tiene
/// ningún rol activo con permisos sobre el sistema <c>cplec</c>. Ver docs/03 sección 2
/// paso 3 y docs/04 sección "Formato de error".
/// </summary>
public sealed class SinAccesoSistemaException : ProhibidoException
{
    public SinAccesoSistemaException()
        : base("SIN_ACCESO_SISTEMA", "No tienes acceso a esta aplicación.") { }
}
```

Save to `src/Leccionario.Api/Application/Common/Exceptions/SinAccesoSistemaException.cs`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build src/Leccionario.sln --warnaserror && dotnet test src/Leccionario.sln --filter "FullyQualifiedName~AppExceptionTests|FullyQualifiedName~ExceptionClassifierTests"`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Leccionario.Api/Application/Common/Exceptions/SinAccesoSistemaException.cs \
        src/Leccionario.Tests/AppExceptionTests.cs \
        src/Leccionario.Tests/ExceptionClassifierTests.cs
git commit -m "feat(auth): agregar SinAccesoSistemaException (403 SIN_ACCESO_SISTEMA)"
```

---

### Task 2: Exponer `ExpiryHours` en `JwtTokenService`

**Files:**
- Modify: `src/Leccionario.Api/Application/Authenticacion/Auth/JwtTokenService.cs`
- Modify: `src/Leccionario.Tests/JwtTokenServiceTests.cs`

**Interfaces:**
- Produces: `IJwtTokenService.ExpiryHours` (`int`, get-only). `AuthService` (Task 6) reads this to compute `LoginResponseDto.ExpiresIn` without re-parsing `JWTSettings:ExpiryHours` from config a second time.

- [ ] **Step 1: Write the failing test**

Append to `src/Leccionario.Tests/JwtTokenServiceTests.cs` (inside `JwtTokenServiceTests`):

```csharp
    [TestMethod]
    public void ExpiryHours_SinParametro_DevuelveDefaultDe8()
    {
        var svc = new JwtTokenService(Secret);
        svc.ExpiryHours.Should().Be(8);
    }

    [TestMethod]
    public void ExpiryHours_ConParametro_DevuelveElValorPasado()
    {
        var svc = new JwtTokenService(Secret, expiryHours: 4);
        svc.ExpiryHours.Should().Be(4);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Leccionario.sln --filter "FullyQualifiedName~JwtTokenServiceTests"`
Expected: build error — `IJwtTokenService` does not contain `ExpiryHours`.

- [ ] **Step 3: Add the property**

In `src/Leccionario.Api/Application/Authenticacion/Auth/JwtTokenService.cs`, add to the `IJwtTokenService` interface (after the `ValidateToken` declaration):

```csharp
    /// <summary>Horas de vigencia del access token — usado para calcular ExpiresIn en segundos.</summary>
    int ExpiryHours { get; }
```

And in the `JwtTokenService` class, add the implementation (after the private fields, before the constructor is fine — as a property near `_expiryHours`):

```csharp
    public int ExpiryHours => _expiryHours;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build src/Leccionario.sln --warnaserror && dotnet test src/Leccionario.sln --filter "FullyQualifiedName~JwtTokenServiceTests"`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Leccionario.Api/Application/Authenticacion/Auth/JwtTokenService.cs \
        src/Leccionario.Tests/JwtTokenServiceTests.cs
git commit -m "feat(auth): exponer ExpiryHours en IJwtTokenService"
```

---

### Task 3: DTOs de autenticación

**Files:**
- Create: `src/Leccionario.Api/Application/Authenticacion/Auth/AuthDtos.cs`

**Interfaces:**
- Produces (all in namespace `Leccionario.Api.Application.Authenticacion.Auth`):
  - `LoginDto { string Username, string Password }`
  - `RefreshTokenRequestDto { string RefreshToken }`
  - `LogoutRequestDto { string RefreshToken }`
  - `UsuarioDto { string IdSigafi, string Nombre, string? Email, string TipoUsuario, IReadOnlyList<string> Roles }`
  - `LoginResponseDto { string AccessToken, string RefreshToken, int ExpiresIn, UsuarioDto Usuario }`
  - `RefreshTokenResponseDto { string AccessToken, string RefreshToken, int ExpiresIn }`
  - `ParaleloResumenDto { int IdAsignacion, string IdPeriodo, string Asignatura, string TipoLicencia, string Jornada, string Modalidad, string Paralelo, DateOnly? FechaInicial, DateOnly? FechaFin, int TotalAlumnos }`
  - `PermisosDto { bool PuedeEditarAsistencia, bool PuedeCerrarSesion, bool PuedeReabrirSesion, bool PuedeEliminarAsistencia, bool PuedeDescargarReportes }`
  - `MiPerfilDto { UsuarioDto Usuario, IReadOnlyList<ParaleloResumenDto> Paralelos, PermisosDto Permisos }`

No dedicated test file — plain data holders, consistent with how `JwtTokenClaims` (same folder) has no standalone tests either. They get exercised indirectly through `AuthServiceTests` and `AuthControllerTests` in later tasks.

- [ ] **Step 1: Create the file**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Leccionario.Api.Application.Authenticacion.Auth;

public sealed class LoginDto
{
    [Required(ErrorMessage = "El usuario es requerido.")]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "El usuario debe tener entre 1 y 20 caracteres.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es requerida.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "La contraseña debe tener entre 1 y 100 caracteres.")]
    public string Password { get; init; } = string.Empty;
}

public sealed class RefreshTokenRequestDto
{
    [Required(ErrorMessage = "El refresh token es requerido.")]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class LogoutRequestDto
{
    [Required(ErrorMessage = "El refresh token es requerido.")]
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>Perfil devuelto en login, refresh y <c>/api/auth/me</c>.</summary>
public sealed class UsuarioDto
{
    public required string IdSigafi { get; init; }
    public required string Nombre { get; init; }
    public string? Email { get; init; }
    public required string TipoUsuario { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}

public sealed class LoginResponseDto
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required int ExpiresIn { get; init; }
    public required UsuarioDto Usuario { get; init; }
}

public sealed class RefreshTokenResponseDto
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required int ExpiresIn { get; init; }
}

/// <summary>
/// Resumen de un paralelo del docente. Shape fijo desde este PR aunque
/// <c>/api/auth/me</c> siempre devuelva la lista vacía hasta que exista el
/// DistributivoGuard (ver docs/superpowers/specs/2026-08-06-auth-login-design.md sección 3).
/// </summary>
public sealed class ParaleloResumenDto
{
    public required int IdAsignacion { get; init; }
    public required string IdPeriodo { get; init; }
    public required string Asignatura { get; init; }
    public required string TipoLicencia { get; init; }
    public required string Jornada { get; init; }
    public required string Modalidad { get; init; }
    public required string Paralelo { get; init; }
    public DateOnly? FechaInicial { get; init; }
    public DateOnly? FechaFin { get; init; }
    public required int TotalAlumnos { get; init; }
}

/// <summary>Permisos derivados puramente de los roles del JWT — ver docs/03 sección 4.</summary>
public sealed class PermisosDto
{
    public required bool PuedeEditarAsistencia { get; init; }
    public required bool PuedeCerrarSesion { get; init; }
    public required bool PuedeReabrirSesion { get; init; }
    public required bool PuedeEliminarAsistencia { get; init; }
    public required bool PuedeDescargarReportes { get; init; }
}

public sealed class MiPerfilDto
{
    public required UsuarioDto Usuario { get; init; }
    public required IReadOnlyList<ParaleloResumenDto> Paralelos { get; init; }
    public required PermisosDto Permisos { get; init; }
}
```

Save to `src/Leccionario.Api/Application/Authenticacion/Auth/AuthDtos.cs`.

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Leccionario.sln --warnaserror`
Expected: 0 warnings, 0 errors (nothing references these types yet, so nothing can fail — this step just confirms the file itself is syntactically and semantically valid).

- [ ] **Step 3: Commit**

```bash
git add src/Leccionario.Api/Application/Authenticacion/Auth/AuthDtos.cs
git commit -m "feat(auth): DTOs de login, refresh, logout y perfil"
```

---

### Task 4: `IAuthService`

**Files:**
- Create: `src/Leccionario.Api/Application/Authenticacion/Auth/IAuthService.cs`

**Interfaces:**
- Consumes: `LoginResponseDto`, `RefreshTokenResponseDto`, `MiPerfilDto` (Task 3).
- Produces: `IAuthService` with `LoginAsync`, `RefreshTokenAsync`, `LogoutAsync`, `ObtenerMiPerfilAsync` — the exact signatures below are what `AuthController` (Task 8) calls and what `AuthService` (Tasks 6-7) implements.

- [ ] **Step 1: Create the file**

```csharp
namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// Contrato del servicio de autenticación. Implementado por <c>AuthService</c>
/// (puerto ajustado de <c>BienestarInstitucional.Api/.../AuthService.cs</c> —
/// ver docs/superpowers/specs/2026-08-06-auth-login-design.md).
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Autentica y, si no existe una fila en <c>usuarios</c> para un profesor
    /// real, la crea (auto-registro). Lanza <see cref="Common.Exceptions.SinAccesoSistemaException"/>
    /// si el usuario no tiene ningún rol <c>cplec_*</c>.
    /// </summary>
    Task<LoginResponseDto> LoginAsync(string username, string password, string? deviceInfo, string? ipAddress, CancellationToken ct = default);

    /// <summary>Rota el refresh token y emite un access token nuevo.</summary>
    Task<RefreshTokenResponseDto> RefreshTokenAsync(string refreshToken, string? deviceInfo, string? ipAddress, CancellationToken ct = default);

    /// <summary>Revoca el refresh token. No lanza si el token ya no existe.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Perfil + permisos del usuario autenticado. <c>Paralelos</c> siempre
    /// vacío hasta que exista el DistributivoGuard.
    /// </summary>
    Task<MiPerfilDto> ObtenerMiPerfilAsync(int idUsuario, CancellationToken ct = default);
}
```

Save to `src/Leccionario.Api/Application/Authenticacion/Auth/IAuthService.cs`.

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Leccionario.sln --warnaserror`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Leccionario.Api/Application/Authenticacion/Auth/IAuthService.cs
git commit -m "feat(auth): interfaz IAuthService"
```

---

### Task 5: `RefreshTokenService`

**Files:**
- Create: `src/Leccionario.Api/Application/Authenticacion/Auth/RefreshTokenService.cs`
- Create: `src/Leccionario.Tests/RefreshTokenServiceTests.cs`

**Interfaces:**
- Consumes: `IRefreshTokenService`, `RefreshTokenStatus`, `RefreshTokenValidationResult` (already exist in `src/Leccionario.Api/Application/Authenticacion/Auth/IRefreshTokenService.cs` — do not modify that file except to add the reason constants below). Entity `Leccionario.Api.Domain.Entities.rbac_refresh_tokens` (fields: `idRefreshToken` `ulong`, `idUsuario` `int`, `tokenHash` `string`, `deviceInfo` `string?`, `ipAddress` `string?`, `createdAt`/`expiresAt`/`revokedAt` `DateTime`, `replacedByTokenId` `ulong?`, `familyId` `string?`, `sequence` `uint?`, `revokedReason` `string?`). `sigafi_esContext.rbac_refresh_tokens` (`DbSet<rbac_refresh_tokens>`).
- Produces: `RefreshTokenService : IRefreshTokenService`. `RefreshTokenRevokedReason` static class with `Rotation`, `Logout`, `ReuseDetected` constants — **Task 7 (`AuthService.LogoutAsync`) consumes `RefreshTokenRevokedReason.Logout`**.

**Design notes carried over from the spec:**
- Reuse detection: a presented token whose row has `revokedAt != null && revokedReason == Rotation` means a newer token already superseded it — that specific case revokes the whole family and returns `ReuseDetected`. Any other already-revoked state (`Logout`, or already `ReuseDetected`) just returns `Invalid` — no cascading revoke needed twice.
- `replacedByTokenId` is intentionally left `null` — setting it would require a second `SaveChangesAsync` after the new row's generated ID is known, breaking the single-transaction guarantee for no real benefit (reuse detection works off `revokedReason`, not off walking this chain).
- Every method does exactly one `SaveChangesAsync()` — see Global Constraints on transactions.

- [ ] **Step 1: Write the failing tests**

Create `src/Leccionario.Tests/RefreshTokenServiceTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Leccionario.Tests;

[TestClass]
public sealed class RefreshTokenServiceTests
{
    private static sigafi_esContext NewDb()
    {
        var options = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(databaseName: $"refreshtokentest-{Guid.NewGuid()}")
            .Options;
        return new sigafi_esContext(options);
    }

    private static RefreshTokenService NewService(sigafi_esContext db) =>
        new(db, NullLogger<RefreshTokenService>.Instance);

    [TestMethod]
    public async Task IssueAsync_CreaTokenYLoPersisteHasheado()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var (token, expiresAt) = await svc.IssueAsync(idUsuario: 1, deviceInfo: "Chrome/Win", ipAddress: "10.0.0.1");

        token.Should().NotBeNullOrEmpty();
        expiresAt.Should().BeAfter(DateTime.UtcNow);

        var fila = await db.rbac_refresh_tokens.SingleAsync();
        fila.idUsuario.Should().Be(1);
        fila.tokenHash.Should().NotBe(token); // nunca se guarda el token en claro
        fila.deviceInfo.Should().Be("Chrome/Win");
        fila.ipAddress.Should().Be("10.0.0.1");
        fila.revokedAt.Should().BeNull();
        fila.familyId.Should().NotBeNullOrEmpty();
        fila.sequence.Should().Be(1u);
    }

    [TestMethod]
    public async Task ValidateAndRotateAsync_TokenDesconocido_DevuelveInvalid()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var result = await svc.ValidateAndRotateAsync("token-que-no-existe", null, null);

        result.Status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [TestMethod]
    public async Task ValidateAndRotateAsync_TokenValido_RotaYDevuelveNuevoToken()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var (token, _) = await svc.IssueAsync(idUsuario: 7, null, null);

        var result = await svc.ValidateAndRotateAsync(token, "Chrome/Win", "10.0.0.2");

        result.Status.Should().Be(RefreshTokenStatus.Ok);
        result.IdUsuario.Should().Be(7);
        result.NewRefreshToken.Should().NotBeNullOrEmpty().And.NotBe(token);

        var filas = await db.rbac_refresh_tokens.OrderBy(t => t.idRefreshToken).ToListAsync();
        filas.Should().HaveCount(2);
        filas[0].revokedAt.Should().NotBeNull();
        filas[0].revokedReason.Should().Be(RefreshTokenRevokedReason.Rotation);
        filas[1].revokedAt.Should().BeNull();
        filas[1].familyId.Should().Be(filas[0].familyId);
        filas[1].sequence.Should().Be(2u);
    }

    [TestMethod]
    public async Task ValidateAndRotateAsync_TokenExpirado_DevuelveInvalid()
    {
        using var db = NewDb();
        db.rbac_refresh_tokens.Add(new Leccionario.Api.Domain.Entities.rbac_refresh_tokens
        {
            idUsuario = 3,
            tokenHash = "hash-de-prueba",
            familyId = Guid.NewGuid().ToString("N"),
            sequence = 1,
            createdAt = DateTime.UtcNow.AddDays(-10),
            expiresAt = DateTime.UtcNow.AddDays(-3)
        });
        await db.SaveChangesAsync();
        var svc = NewService(db);

        // El servicio hashea el token que se le pasa; para simular un token
        // expirado tenemos que hashear el mismo valor "en claro" que produciría
        // el hash sembrado arriba. Más simple: emitir uno real y luego
        // envejecerlo directamente en la fila.
        using var db2 = NewDb();
        var svc2 = NewService(db2);
        var (token, _) = await svc2.IssueAsync(idUsuario: 3, null, null);
        var fila = await db2.rbac_refresh_tokens.SingleAsync();
        fila.expiresAt = DateTime.UtcNow.AddMinutes(-10);
        await db2.SaveChangesAsync();

        var result = await svc2.ValidateAndRotateAsync(token, null, null);

        result.Status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [TestMethod]
    public async Task ValidateAndRotateAsync_TokenYaRotado_DevuelveReuseDetectedYRevocaFamilia()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var (tokenOriginal, _) = await svc.IssueAsync(idUsuario: 9, null, null);
        var primeraRotacion = await svc.ValidateAndRotateAsync(tokenOriginal, null, null);
        primeraRotacion.Status.Should().Be(RefreshTokenStatus.Ok);

        // Alguien presenta el token ORIGINAL de nuevo — ya fue rotado. Reuso.
        var reuso = await svc.ValidateAndRotateAsync(tokenOriginal, null, null);

        reuso.Status.Should().Be(RefreshTokenStatus.ReuseDetected);

        var todas = await db.rbac_refresh_tokens.ToListAsync();
        todas.Should().AllSatisfy(t => t.revokedAt.Should().NotBeNull());
        todas.Should().Contain(t => t.revokedReason == RefreshTokenRevokedReason.ReuseDetected);
    }

    [TestMethod]
    public async Task RevokeAsync_TokenValido_LoMarcaRevocado()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var (token, _) = await svc.IssueAsync(idUsuario: 5, null, null);

        await svc.RevokeAsync(token, RefreshTokenRevokedReason.Logout);

        var fila = await db.rbac_refresh_tokens.SingleAsync();
        fila.revokedAt.Should().NotBeNull();
        fila.revokedReason.Should().Be(RefreshTokenRevokedReason.Logout);
    }

    [TestMethod]
    public async Task RevokeAsync_TokenDesconocido_NoLanza()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var act = async () => await svc.RevokeAsync("no-existe", RefreshTokenRevokedReason.Logout);

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Leccionario.sln --filter "FullyQualifiedName~RefreshTokenServiceTests"`
Expected: build error — `RefreshTokenService` and `RefreshTokenRevokedReason` do not exist yet.

- [ ] **Step 3: Add `RefreshTokenRevokedReason` to the existing interface file**

In `src/Leccionario.Api/Application/Authenticacion/Auth/IRefreshTokenService.cs`, add this at the end of the file (after the `IRefreshTokenService` interface closing brace):

```csharp

/// <summary>Valores válidos para el parámetro <c>reason</c> de <c>RevokeAsync</c> y para <c>rbac_refresh_tokens.revokedReason</c>.</summary>
public static class RefreshTokenRevokedReason
{
    public const string Rotation = "Rotation";
    public const string Logout = "Logout";
    public const string ReuseDetected = "ReuseDetected";
}
```

- [ ] **Step 4: Implement `RefreshTokenService`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// Implementación de <see cref="IRefreshTokenService"/>. Adapta la lógica de
/// rotación + detección de reuso de
/// <c>BienestarInstitucional.Api/.../RefreshTokenService.cs</c> al contrato
/// propio de cplec (ver docs/superpowers/specs/2026-08-06-auth-login-design.md sección 2).
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    private const int ExpiryDays = 7;
    private const int ClockSkewMinutes = 5;

    private readonly sigafi_esContext _db;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(sigafi_esContext db, ILogger<RefreshTokenService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(string Token, DateTime ExpiresAt)> IssueAsync(int idUsuario, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var token = GenerarTokenSeguro();
        var expiresAt = DateTime.UtcNow.AddDays(ExpiryDays);

        _db.rbac_refresh_tokens.Add(new rbac_refresh_tokens
        {
            idUsuario = idUsuario,
            tokenHash = ComputarHash(token),
            familyId = Guid.NewGuid().ToString("N"),
            sequence = 1,
            deviceInfo = deviceInfo,
            ipAddress = ipAddress,
            createdAt = DateTime.UtcNow,
            expiresAt = expiresAt
        });
        await _db.SaveChangesAsync(ct);

        return (token, expiresAt);
    }

    public async Task<RefreshTokenValidationResult> ValidateAndRotateAsync(string token, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var hash = ComputarHash(token);
        var existente = await _db.rbac_refresh_tokens.FirstOrDefaultAsync(t => t.tokenHash == hash, ct);

        if (existente is null)
        {
            _logger.LogWarning("Refresh token desconocido presentado.");
            return new RefreshTokenValidationResult { Status = RefreshTokenStatus.Invalid };
        }

        if (existente.revokedAt is not null)
        {
            if (existente.revokedReason == RefreshTokenRevokedReason.Rotation)
            {
                _logger.LogWarning("Reuso de refresh token rotado detectado para familia {FamilyId}.", existente.familyId);
                if (existente.familyId is not null)
                    await RevocarFamiliaAsync(existente.familyId, RefreshTokenRevokedReason.ReuseDetected, ct);

                return new RefreshTokenValidationResult { Status = RefreshTokenStatus.ReuseDetected };
            }

            return new RefreshTokenValidationResult { Status = RefreshTokenStatus.Invalid };
        }

        if (existente.expiresAt.AddMinutes(-ClockSkewMinutes) < DateTime.UtcNow)
        {
            return new RefreshTokenValidationResult { Status = RefreshTokenStatus.Invalid };
        }

        var nuevoToken = GenerarTokenSeguro();
        var nuevaExpiracion = DateTime.UtcNow.AddDays(ExpiryDays);

        _db.rbac_refresh_tokens.Add(new rbac_refresh_tokens
        {
            idUsuario = existente.idUsuario,
            tokenHash = ComputarHash(nuevoToken),
            familyId = existente.familyId,
            sequence = (existente.sequence ?? 0) + 1,
            deviceInfo = deviceInfo,
            ipAddress = ipAddress,
            createdAt = DateTime.UtcNow,
            expiresAt = nuevaExpiracion
        });

        existente.revokedAt = DateTime.UtcNow;
        existente.revokedReason = RefreshTokenRevokedReason.Rotation;

        await _db.SaveChangesAsync(ct); // una sola llamada: revocación + alta nueva, atómico

        return new RefreshTokenValidationResult
        {
            Status = RefreshTokenStatus.Ok,
            IdUsuario = existente.idUsuario,
            NewRefreshToken = nuevoToken,
            NewRefreshTokenExpiresAt = nuevaExpiracion
        };
    }

    public async Task RevokeAsync(string token, string reason, CancellationToken ct = default)
    {
        var hash = ComputarHash(token);
        var existente = await _db.rbac_refresh_tokens.FirstOrDefaultAsync(t => t.tokenHash == hash, ct);
        if (existente is null || existente.revokedAt is not null)
            return;

        existente.revokedAt = DateTime.UtcNow;
        existente.revokedReason = reason;
        await _db.SaveChangesAsync(ct);
    }

    private async Task RevocarFamiliaAsync(string familyId, string reason, CancellationToken ct)
    {
        var tokens = await _db.rbac_refresh_tokens
            .Where(t => t.familyId == familyId && t.revokedAt == null)
            .ToListAsync(ct);

        foreach (var t in tokens)
        {
            t.revokedAt = DateTime.UtcNow;
            t.revokedReason = reason;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Revocados {Count} tokens de la familia {FamilyId} por {Reason}.", tokens.Count, familyId, reason);
    }

    private static string GenerarTokenSeguro()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string ComputarHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

Save to `src/Leccionario.Api/Application/Authenticacion/Auth/RefreshTokenService.cs`. Add `using Microsoft.Extensions.Logging;` if your editor doesn't auto-resolve `ILogger<T>`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build src/Leccionario.sln --warnaserror && dotnet test src/Leccionario.sln --filter "FullyQualifiedName~RefreshTokenServiceTests"`
Expected: PASS (7 tests), 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Leccionario.Api/Application/Authenticacion/Auth/IRefreshTokenService.cs \
        src/Leccionario.Api/Application/Authenticacion/Auth/RefreshTokenService.cs \
        src/Leccionario.Tests/RefreshTokenServiceTests.cs
git commit -m "feat(auth): implementar RefreshTokenService con rotacion y deteccion de reuso"
```

---

### Task 6: `AuthService` — `LoginAsync`

**Files:**
- Create: `src/Leccionario.Api/Application/Authenticacion/Auth/AuthService.cs`
- Create: `src/Leccionario.Tests/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `IAuthService` (Task 4), `AuthDtos` types (Task 3), `IRefreshTokenService`/`RefreshTokenValidationResult`/`RefreshTokenStatus` (existing + Task 5), `IJwtTokenService`/`JwtTokenClaims`/`ExpiryHours` (existing + Task 2), `PasswordService` (existing: `Hash`, `Verify`, `IsHashed`), `CuentaInactivaException` (existing), `SinAccesoSistemaException` (Task 1). Entities: `usuarios` (`idUsuario` int, `idSigafi` string, `tablaSigafi` string, `nombre` string?, `contrasenia` string, `activo` sbyte, `administrador` sbyte, `emailInstitucional` string?), `profesores` (`idProfesor` string, `apellidos` string?, `nombres` string?, `clave` string?, `esReal` sbyte?, `emailInstitucional` string?, `email` string?), `rbac_rol`/`rbac_usuario_rol`/`rbac_rol_modulo_operacion`/`rbac_modulos_operaciones`/`rbac_modulos`/`rbac_sistema` navigation chain (all confirmed present in `Domain/Entities/`, see spec sección 2). `sigafi_esContext` (`DbSet<usuarios> usuarios`, `DbSet<profesores> profesores`, `DbSet<rbac_rol> rbac_rol`).
- Produces (this task): `AuthService` class implementing `LoginAsync` fully. `RefreshTokenAsync`/`LogoutAsync`/`ObtenerMiPerfilAsync` are added in Task 7 as `NotImplementedException` stubs for now so the class compiles against `IAuthService`.

**Note on `sbyte` fields:** re-read the Global Constraints section before writing any comparison against `activo`, `esActivo`, `esReal`.

- [ ] **Step 1: Write the failing tests**

Create `src/Leccionario.Tests/AuthServiceTests.cs`:

```csharp
using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Leccionario.Tests;

[TestClass]
public sealed class AuthServiceTests
{
    private const string JwtSecret = "this-is-a-test-secret-32-bytes-min!";
    private const string Password = "clave-correcta-123";

    private static sigafi_esContext NewDb() => new(
        new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(databaseName: $"authservicetest-{Guid.NewGuid()}")
            .Options);

    private static IConfiguration NewConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SistemaCodigo"] = "cplec" })
            .Build();

    private static Mock<IRefreshTokenService> NewMockRefreshTokens()
    {
        var mock = new Mock<IRefreshTokenService>();
        mock.Setup(r => r.IssueAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("token-de-refresh-de-prueba", DateTime.UtcNow.AddDays(7)));
        return mock;
    }

    private static AuthService NewService(sigafi_esContext db, Mock<IRefreshTokenService>? refreshTokens = null) =>
        new(db, new JwtTokenService(JwtSecret), (refreshTokens ?? NewMockRefreshTokens()).Object, NewConfig(), NullLogger<AuthService>.Instance);

    /// <summary>Siembra sistema/modulo/operacion/rol-modulo-operacion para que un rol quede "con grants sobre cplec". Devuelve idRol.</summary>
    private static async Task<int> CrearRolCplecAsync(sigafi_esContext db, string codigoRol)
    {
        var sistema = new rbac_sistema { codigo = "cplec", detalle = "Leccionario" };
        db.rbac_sistema.Add(sistema);
        await db.SaveChangesAsync();

        var modulo = new rbac_modulos { id_sistema = sistema.idSistema, Nombre = "asistencia", esActivo = 1 };
        db.rbac_modulos.Add(modulo);
        await db.SaveChangesAsync();

        var operacion = new rbac_operaciones { NombreOperacion = "ver" };
        db.rbac_operaciones.Add(operacion);
        await db.SaveChangesAsync();

        var moduloOperacion = new rbac_modulos_operaciones { idModulos = modulo.idModulos, idOperaciones = operacion.idOperaciones, esActivo = 1 };
        db.rbac_modulos_operaciones.Add(moduloOperacion);
        await db.SaveChangesAsync();

        var rol = new rbac_rol { Nombre = codigoRol, codigo_rol = codigoRol, esActivo = 1 };
        db.rbac_rol.Add(rol);
        await db.SaveChangesAsync();

        db.rbac_rol_modulo_operacion.Add(new rbac_rol_modulo_operacion
        {
            idRol = rol.idRol,
            idModulosOperaciones = moduloOperacion.idModulosOperaciones,
            esActivo = 1
        });
        await db.SaveChangesAsync();

        return rol.idRol;
    }

    private static async Task AsignarRolAsync(sigafi_esContext db, int idUsuario, int idRol)
    {
        db.rbac_usuario_rol.Add(new rbac_usuario_rol { idUsuario = idUsuario, idRol = idRol, esActivo = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task<usuarios> CrearUsuarioActivoAsync(sigafi_esContext db, string idSigafi = "1804567890", string tablaSigafi = "profesor", string? passwordPlano = null)
    {
        var usuario = new usuarios
        {
            idSigafi = idSigafi,
            tablaSigafi = tablaSigafi,
            nombre = "PEREZ, JUAN",
            contrasenia = passwordPlano is null ? PasswordService.Hash(Password) : passwordPlano,
            activo = 1,
            administrador = 0
        };
        db.usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    [TestMethod]
    public async Task LoginAsync_UsuarioInexistenteYSinProfesorMatching_MensajeGenerico()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("9999999999", Password, null, null);

        var ex = await act.Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.Message.Should().Be("Credenciales inválidas.");
    }

    [TestMethod]
    public async Task LoginAsync_PasswordIncorrecta_MismoMensajeQueUsuarioInexistente()
    {
        using var db = NewDb();
        await CrearUsuarioActivoAsync(db);
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("1804567890", "clave-equivocada", null, null);

        var ex = await act.Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.Message.Should().Be("Credenciales inválidas.");
    }

    [TestMethod]
    public async Task LoginAsync_CuentaInactivaConCredencialCorrecta_LanzaYNoMigraPassword()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        usuario.activo = 0;
        await db.SaveChangesAsync();
        var hashOriginal = usuario.contrasenia;
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("1804567890", Password, null, null);

        await act.Should().ThrowAsync<CuentaInactivaException>();
        var recargado = await db.usuarios.AsNoTracking().SingleAsync(u => u.idSigafi == "1804567890");
        recargado.contrasenia.Should().Be(hashOriginal);
    }

    [TestMethod]
    public async Task LoginAsync_ProfesorNoEsReal_NoAutoRegistra()
    {
        using var db = NewDb();
        db.profesores.Add(new profesores { idProfesor = "0102030405", clave = Password, esReal = 0 });
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("0102030405", Password, null, null);

        var ex = await act.Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.Message.Should().Be("Credenciales inválidas.");
        (await db.usuarios.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task LoginAsync_UsuarioSinNingunRolCplec_LanzaSinAccesoSistema()
    {
        using var db = NewDb();
        await CrearUsuarioActivoAsync(db);
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("1804567890", Password, null, null);

        await act.Should().ThrowAsync<SinAccesoSistemaException>();
    }

    [TestMethod]
    public async Task LoginAsync_ConRolCplecDocente_EmiteAccessTokenYRefreshToken()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        var svc = NewService(db);

        var respuesta = await svc.LoginAsync("1804567890", Password, "Chrome/Win", "10.0.0.5");

        respuesta.AccessToken.Should().NotBeNullOrEmpty();
        respuesta.RefreshToken.Should().Be("token-de-refresh-de-prueba");
        respuesta.ExpiresIn.Should().Be(8 * 3600);
        respuesta.Usuario.IdSigafi.Should().Be("1804567890");
        respuesta.Usuario.TipoUsuario.Should().Be("profesor");
        respuesta.Usuario.Roles.Should().ContainSingle().Which.Should().Be("cplec_docente");
    }

    [TestMethod]
    public async Task LoginAsync_CredencialPlanaEnUsuarios_ValidaSinMigrarHash()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db, passwordPlano: Password); // sin hashear
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        var svc = NewService(db);

        await svc.LoginAsync("1804567890", Password, null, null);

        var recargado = await db.usuarios.AsNoTracking().SingleAsync(u => u.idSigafi == "1804567890");
        recargado.contrasenia.Should().Be(Password); // docs/03: solo migra el caso "hash centinela"
    }

    [TestMethod]
    public async Task LoginAsync_HashCentinelaConCredencialLegacyProfesor_MigraABcrypt()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db, passwordPlano: PasswordService.GenerarHashCentinela());
        db.profesores.Add(new profesores { idProfesor = "1804567890", clave = Password, esReal = 1 });
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        await svc.LoginAsync("1804567890", Password, null, null);

        var recargado = await db.usuarios.AsNoTracking().SingleAsync(u => u.idSigafi == "1804567890");
        PasswordService.IsHashed(recargado.contrasenia).Should().BeTrue();
        PasswordService.Verify(Password, recargado.contrasenia).Should().BeTrue();
    }

    [TestMethod]
    public async Task LoginAsync_ProfesorNuevoSinRolPreasignado_AutoRegistraPeroRechazaPorSinAcceso()
    {
        using var db = NewDb();
        db.profesores.Add(new profesores { idProfesor = "0708091011", clave = Password, esReal = 1, apellidos = "TORRES", nombres = "MARIA" });
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("0708091011", Password, null, null);

        await act.Should().ThrowAsync<SinAccesoSistemaException>();
        var creado = await db.usuarios.AsNoTracking().SingleAsync(u => u.idSigafi == "0708091011");
        creado.tablaSigafi.Should().Be("profesor");
        PasswordService.Verify(Password, creado.contrasenia).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Leccionario.sln --filter "FullyQualifiedName~AuthServiceTests"`
Expected: build error — `AuthService` does not exist.

- [ ] **Step 3: Implement `AuthService.LoginAsync` and its helpers**

```csharp
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// Puerto ajustado de <c>BienestarInstitucional.Api/Application/Services/AuthService.cs</c>:
/// solo <c>profesor</c> inicia sesión, sin asignación automática de rol. Ver
/// docs/superpowers/specs/2026-08-06-auth-login-design.md.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly sigafi_esContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly ILogger<AuthService> _logger;
    private readonly string _sistemaCodigo;

    public AuthService(
        sigafi_esContext db,
        IJwtTokenService jwt,
        IRefreshTokenService refreshTokens,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _db = db;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _logger = logger;
        _sistemaCodigo = config["SistemaCodigo"] ?? "cplec";
    }

    public async Task<LoginResponseDto> LoginAsync(string username, string password, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var usuario = await ObtenerOCrearUsuarioAsync(username, password, ct);

        var roles = await CargarRolesCplecAsync(usuario.idUsuario, ct);
        if (roles.Count == 0)
            throw new SinAccesoSistemaException();

        var tipoUsuario = ResolverTipoUsuario(usuario.tablaSigafi);
        var email = await ResolverEmailAsync(usuario, ct);

        var (refreshToken, _) = await _refreshTokens.IssueAsync(usuario.idUsuario, deviceInfo, ipAddress, ct);

        var accessToken = _jwt.GenerateAccessToken(new JwtTokenClaims
        {
            IdSigafi = usuario.idSigafi,
            IdUsuario = usuario.idUsuario,
            Nombre = usuario.nombre ?? usuario.idSigafi,
            Email = email,
            TipoUsuario = tipoUsuario,
            Roles = roles,
            CodigoSistema = _sistemaCodigo
        });

        _logger.LogInformation("Login exitoso para {IdSigafiMask}.", EnmascararIdSigafi(usuario.idSigafi));

        return new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _jwt.ExpiryHours * 3600,
            Usuario = new UsuarioDto
            {
                IdSigafi = usuario.idSigafi,
                Nombre = usuario.nombre ?? usuario.idSigafi,
                Email = email,
                TipoUsuario = tipoUsuario,
                Roles = roles
            }
        };
    }

    public Task<RefreshTokenResponseDto> RefreshTokenAsync(string refreshToken, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
        => throw new NotImplementedException("Se implementa en la Tarea 7.");

    public Task LogoutAsync(string refreshToken, CancellationToken ct = default)
        => throw new NotImplementedException("Se implementa en la Tarea 7.");

    public Task<MiPerfilDto> ObtenerMiPerfilAsync(int idUsuario, CancellationToken ct = default)
        => throw new NotImplementedException("Se implementa en la Tarea 7.");

    // ------------------------------------------------------------------
    // Helpers privados
    // ------------------------------------------------------------------

    private async Task<usuarios> ObtenerOCrearUsuarioAsync(string username, string password, CancellationToken ct)
    {
        var usuario = await _db.usuarios.FirstOrDefaultAsync(u => u.idSigafi == username, ct);

        if (usuario is not null)
        {
            if (!await VerificarCredencialAsync(usuario, password, ct))
                throw new UnauthorizedAccessException("Credenciales inválidas.");

            // Crítico: activo se valida DESPUÉS de la credencial (docs/03 sección 2).
            if (usuario.activo != 1)
                throw new CuentaInactivaException();

            await MigrarPasswordSiCorrespondeAsync(usuario, password, ct);

            return usuario;
        }

        return await AutoRegistrarProfesorAsync(username, password, ct);
    }

    private async Task<bool> VerificarCredencialAsync(usuarios usuario, string password, CancellationToken ct)
    {
        if (PasswordService.IsHashed(usuario.contrasenia))
        {
            if (PasswordService.Verify(password, usuario.contrasenia))
                return true;
        }
        else if (usuario.contrasenia.Trim() == password)
        {
            return true;
        }

        if (EsProfesor(usuario.tablaSigafi))
            return await MatcheaProfesorLegacyAsync(usuario.idSigafi, password, ct);

        return false;
    }

    private async Task MigrarPasswordSiCorrespondeAsync(usuarios usuario, string password, CancellationToken ct)
    {
        // Solo migra el caso "hash centinela + credencial legacy correcta" —
        // NO el caso "contrasenia en texto plano ya matcheaba" (docs/03 sección 2).
        if (PasswordService.IsHashed(usuario.contrasenia)
            && EsProfesor(usuario.tablaSigafi)
            && await MatcheaProfesorLegacyAsync(usuario.idSigafi, password, ct))
        {
            usuario.contrasenia = PasswordService.Hash(password);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cuenta {IdSigafiMask} migró su contraseña a bcrypt.", EnmascararIdSigafi(usuario.idSigafi));
        }
    }

    private async Task<bool> MatcheaProfesorLegacyAsync(string idSigafi, string password, CancellationToken ct)
    {
        var profesor = await _db.profesores.AsNoTracking()
            .FirstOrDefaultAsync(p => p.idProfesor == idSigafi && p.esReal == 1, ct);
        return profesor is not null && !string.IsNullOrEmpty(profesor.clave) && profesor.clave.Trim() == password;
    }

    private async Task<usuarios> AutoRegistrarProfesorAsync(string username, string password, CancellationToken ct)
    {
        var profesor = await _db.profesores.AsNoTracking()
            .FirstOrDefaultAsync(p => p.idProfesor == username && p.esReal == 1, ct);

        if (profesor is null || string.IsNullOrEmpty(profesor.clave) || profesor.clave.Trim() != password)
            throw new UnauthorizedAccessException("Credenciales inválidas.");

        var nuevo = new usuarios
        {
            idSigafi = username,
            tablaSigafi = "profesor",
            nombre = FormatearNombreProfesor(profesor),
            contrasenia = PasswordService.Hash(password),
            activo = 1,
            administrador = 0
        };

        _db.usuarios.Add(nuevo);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Profesor {IdSigafiMask} auto-registrado en usuarios (sin rol asignado).", EnmascararIdSigafi(username));

        return nuevo;
    }

    private async Task<List<string>> CargarRolesCplecAsync(int idUsuario, CancellationToken ct)
    {
        return await _db.rbac_rol
            .Where(r => r.rbac_usuario_rol.Any(ur => ur.idUsuario == idUsuario && ur.esActivo == 1))
            .Where(r => r.esActivo == 1 || r.esActivo == null)
            .Where(r => r.rbac_rol_modulo_operacion.Any(rmo =>
                rmo.esActivo == 1
                && rmo.idModulosOperacionesNavigation.idModulosNavigation.id_sistemaNavigation.codigo == _sistemaCodigo))
            .Select(r => r.codigo_rol)
            .Distinct()
            .ToListAsync(ct);
    }

    private async Task<string?> ResolverEmailAsync(usuarios usuario, CancellationToken ct)
    {
        if (!EsProfesor(usuario.tablaSigafi))
            return usuario.emailInstitucional;

        var profesor = await _db.profesores.AsNoTracking()
            .FirstOrDefaultAsync(p => p.idProfesor == usuario.idSigafi, ct);

        return profesor?.emailInstitucional ?? profesor?.email ?? usuario.emailInstitucional;
    }

    private static bool EsProfesor(string tablaSigafi) => tablaSigafi.Trim().Equals("profesor", StringComparison.OrdinalIgnoreCase);

    private static string ResolverTipoUsuario(string tablaSigafi) => tablaSigafi.Trim().ToLowerInvariant() switch
    {
        "alumno" => "alumno",
        "profesor" => "profesor",
        _ => "otros"
    };

    private static string FormatearNombreProfesor(profesores p)
    {
        var apellidos = p.apellidos?.Trim() ?? string.Empty;
        var nombres = p.nombres?.Trim() ?? string.Empty;
        if (apellidos.Length == 0) return nombres;
        if (nombres.Length == 0) return apellidos;
        return $"{apellidos}, {nombres}";
    }

    private static string EnmascararIdSigafi(string idSigafi) =>
        idSigafi.Length <= 4 ? "****" : $"{idSigafi[..2]}****{idSigafi[^2..]}";
}
```

Save to `src/Leccionario.Api/Application/Authenticacion/Auth/AuthService.cs`. The `RefreshTokenAsync`/`LogoutAsync`/`ObtenerMiPerfilAsync` stubs above throw `NotImplementedException` on purpose — Task 7 replaces them. This keeps the class compiling against `IAuthService` right now without lying about what's finished.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build src/Leccionario.sln --warnaserror && dotnet test src/Leccionario.sln --filter "FullyQualifiedName~AuthServiceTests"`
Expected: PASS (9 tests), 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Leccionario.Api/Application/Authenticacion/Auth/AuthService.cs \
        src/Leccionario.Tests/AuthServiceTests.cs
git commit -m "feat(auth): implementar AuthService.LoginAsync con auto-registro de profesor"
```

---

### Task 7: `AuthService` — `RefreshTokenAsync`, `LogoutAsync`, `ObtenerMiPerfilAsync`

**Files:**
- Modify: `src/Leccionario.Api/Application/Authenticacion/Auth/AuthService.cs`
- Modify: `src/Leccionario.Tests/AuthServiceTests.cs`

**Interfaces:**
- Consumes: everything from Task 6 (`AuthService` private helpers `CargarRolesCplecAsync`, `ResolverTipoUsuario`, `ResolverEmailAsync`, `EnmascararIdSigafi` — all still `private`, reused within the same class), `RefreshTokenRevokedReason.Logout` (Task 5), `RefreshTokenStatus`/`RefreshTokenValidationResult` (existing).
- Produces: `MiPerfilDto`/`PermisosDto`/`ParaleloResumenDto` construction logic — nothing new is exposed to other classes; `IAuthService` (Task 4) already declared these method signatures.

- [ ] **Step 1: Write the failing tests**

Append to `src/Leccionario.Tests/AuthServiceTests.cs` (inside `AuthServiceTests`, after `LoginAsync_ProfesorNuevoSinRolPreasignado_AutoRegistraPeroRechazaPorSinAcceso`):

```csharp
    [TestMethod]
    public async Task RefreshTokenAsync_TokenInvalido_LanzaUnauthorized()
    {
        using var db = NewDb();
        var mockRefresh = new Mock<IRefreshTokenService>();
        mockRefresh.Setup(r => r.ValidateAndRotateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenValidationResult { Status = RefreshTokenStatus.Invalid });
        var svc = NewService(db, mockRefresh);

        var act = async () => await svc.RefreshTokenAsync("token-invalido", null, null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task RefreshTokenAsync_TokenValido_EmiteNuevoAccessTokenConRolesActuales()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_inspector");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);

        var mockRefresh = new Mock<IRefreshTokenService>();
        mockRefresh.Setup(r => r.ValidateAndRotateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenValidationResult
            {
                Status = RefreshTokenStatus.Ok,
                IdUsuario = usuario.idUsuario,
                NewRefreshToken = "nuevo-refresh-token",
                NewRefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7)
            });
        var svc = NewService(db, mockRefresh);

        var respuesta = await svc.RefreshTokenAsync("token-viejo", null, null);

        respuesta.AccessToken.Should().NotBeNullOrEmpty();
        respuesta.RefreshToken.Should().Be("nuevo-refresh-token");
        respuesta.ExpiresIn.Should().Be(8 * 3600);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_UsuarioInactivoTrasRotacion_LanzaUnauthorized()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        usuario.activo = 0;
        await db.SaveChangesAsync();

        var mockRefresh = new Mock<IRefreshTokenService>();
        mockRefresh.Setup(r => r.ValidateAndRotateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenValidationResult { Status = RefreshTokenStatus.Ok, IdUsuario = usuario.idUsuario, NewRefreshToken = "x" });
        var svc = NewService(db, mockRefresh);

        var act = async () => await svc.RefreshTokenAsync("token", null, null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task RefreshTokenAsync_UsuarioSinRolCplec_LanzaSinAccesoSistema()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);

        var mockRefresh = new Mock<IRefreshTokenService>();
        mockRefresh.Setup(r => r.ValidateAndRotateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenValidationResult { Status = RefreshTokenStatus.Ok, IdUsuario = usuario.idUsuario, NewRefreshToken = "x" });
        var svc = NewService(db, mockRefresh);

        var act = async () => await svc.RefreshTokenAsync("token", null, null);

        await act.Should().ThrowAsync<SinAccesoSistemaException>();
    }

    [TestMethod]
    public async Task LogoutAsync_DelegaEnRevokeAsyncConRazonLogout()
    {
        using var db = NewDb();
        var mockRefresh = NewMockRefreshTokens();
        var svc = NewService(db, mockRefresh);

        await svc.LogoutAsync("token-a-revocar");

        mockRefresh.Verify(r => r.RevokeAsync("token-a-revocar", RefreshTokenRevokedReason.Logout, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ObtenerMiPerfilAsync_Docente_PuedeReabrirSesionEsFalse()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        var svc = NewService(db);

        var perfil = await svc.ObtenerMiPerfilAsync(usuario.idUsuario);

        perfil.Usuario.Roles.Should().Contain("cplec_docente");
        perfil.Permisos.PuedeEditarAsistencia.Should().BeTrue();
        perfil.Permisos.PuedeCerrarSesion.Should().BeTrue();
        perfil.Permisos.PuedeReabrirSesion.Should().BeFalse();
        perfil.Permisos.PuedeEliminarAsistencia.Should().BeFalse();
        perfil.Permisos.PuedeDescargarReportes.Should().BeTrue();
        perfil.Paralelos.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ObtenerMiPerfilAsync_Inspector_PuedeReabrirSesionEsTrue()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_inspector");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        var svc = NewService(db);

        var perfil = await svc.ObtenerMiPerfilAsync(usuario.idUsuario);

        perfil.Permisos.PuedeReabrirSesion.Should().BeTrue();
        perfil.Paralelos.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ObtenerMiPerfilAsync_UsuarioSinRolCplec_LanzaSinAccesoSistema()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var svc = NewService(db);

        var act = async () => await svc.ObtenerMiPerfilAsync(usuario.idUsuario);

        await act.Should().ThrowAsync<SinAccesoSistemaException>();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Leccionario.sln --filter "FullyQualifiedName~AuthServiceTests"`
Expected: the 9 tests from Task 6 still PASS; the new ones FAIL with `NotImplementedException`.

- [ ] **Step 3: Replace the three stub methods**

In `src/Leccionario.Api/Application/Authenticacion/Auth/AuthService.cs`, replace the three `throw new NotImplementedException(...)` stubs with:

```csharp
    public async Task<RefreshTokenResponseDto> RefreshTokenAsync(string refreshToken, string? deviceInfo, string? ipAddress, CancellationToken ct = default)
    {
        var resultado = await _refreshTokens.ValidateAndRotateAsync(refreshToken, deviceInfo, ipAddress, ct);

        if (resultado.Status != RefreshTokenStatus.Ok || resultado.IdUsuario is null || resultado.NewRefreshToken is null)
            throw new UnauthorizedAccessException("Credenciales inválidas.");

        var usuario = await _db.usuarios.FirstOrDefaultAsync(u => u.idUsuario == resultado.IdUsuario, ct);
        if (usuario is null || usuario.activo != 1)
            throw new UnauthorizedAccessException("Credenciales inválidas.");

        var roles = await CargarRolesCplecAsync(usuario.idUsuario, ct);
        if (roles.Count == 0)
            throw new SinAccesoSistemaException();

        var accessToken = _jwt.GenerateAccessToken(new JwtTokenClaims
        {
            IdSigafi = usuario.idSigafi,
            IdUsuario = usuario.idUsuario,
            Nombre = usuario.nombre ?? usuario.idSigafi,
            Email = await ResolverEmailAsync(usuario, ct),
            TipoUsuario = ResolverTipoUsuario(usuario.tablaSigafi),
            Roles = roles,
            CodigoSistema = _sistemaCodigo
        });

        return new RefreshTokenResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = resultado.NewRefreshToken,
            ExpiresIn = _jwt.ExpiryHours * 3600
        };
    }

    public Task LogoutAsync(string refreshToken, CancellationToken ct = default) =>
        _refreshTokens.RevokeAsync(refreshToken, RefreshTokenRevokedReason.Logout, ct);

    public async Task<MiPerfilDto> ObtenerMiPerfilAsync(int idUsuario, CancellationToken ct = default)
    {
        var usuario = await _db.usuarios.FirstOrDefaultAsync(u => u.idUsuario == idUsuario, ct)
            ?? throw new UnauthorizedAccessException("Credenciales inválidas.");

        var roles = await CargarRolesCplecAsync(usuario.idUsuario, ct);
        if (roles.Count == 0)
            throw new SinAccesoSistemaException();

        return new MiPerfilDto
        {
            Usuario = new UsuarioDto
            {
                IdSigafi = usuario.idSigafi,
                Nombre = usuario.nombre ?? usuario.idSigafi,
                Email = await ResolverEmailAsync(usuario, ct),
                TipoUsuario = ResolverTipoUsuario(usuario.tablaSigafi),
                Roles = roles
            },
            // Vacío hasta que exista el DistributivoGuard — ver
            // docs/superpowers/specs/2026-08-06-auth-login-design.md sección 3.
            Paralelos = Array.Empty<ParaleloResumenDto>(),
            Permisos = CalcularPermisos(roles)
        };
    }

    private static PermisosDto CalcularPermisos(IReadOnlyList<string> roles)
    {
        var esInspector = roles.Contains("cplec_inspector");
        return new PermisosDto
        {
            PuedeEditarAsistencia = true,
            PuedeCerrarSesion = true,
            PuedeReabrirSesion = esInspector,
            PuedeEliminarAsistencia = false,
            PuedeDescargarReportes = true
        };
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build src/Leccionario.sln --warnaserror && dotnet test src/Leccionario.sln --filter "FullyQualifiedName~AuthServiceTests"`
Expected: PASS (17 tests total in this file), 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Leccionario.Api/Application/Authenticacion/Auth/AuthService.cs \
        src/Leccionario.Tests/AuthServiceTests.cs
git commit -m "feat(auth): completar AuthService con refresh, logout y perfil"
```

---

### Task 8: `AuthController` + wiring

**Files:**
- Create: `src/Leccionario.Api/Controllers/Auth/AuthController.cs`
- Create: `src/Leccionario.Tests/AuthControllerTests.cs`
- Modify: `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`
- Modify: `src/Leccionario.Api/Program.cs`

**Interfaces:**
- Consumes: `IAuthService` (Task 4), all `AuthDtos` (Task 3). Existing `"login"` rate limiting policy (already registered in `AddRateLimiting`, `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`).
- Produces: `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/me`. Nothing downstream consumes this task's output — it's the last piece of the wiring chain.

- [ ] **Step 1: Write the failing tests**

Create `src/Leccionario.Tests/AuthControllerTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Controllers.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Leccionario.Tests;

[TestClass]
public sealed class AuthControllerTests
{
    private static AuthController NewController(Mock<IAuthService> mock, DefaultHttpContext? httpContext = null)
    {
        var controller = new AuthController(mock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext ?? new DefaultHttpContext() }
        };
        return controller;
    }

    private static LoginResponseDto SampleLoginResponse() => new()
    {
        AccessToken = "access-token",
        RefreshToken = "refresh-token",
        ExpiresIn = 28800,
        Usuario = new UsuarioDto { IdSigafi = "1804567890", Nombre = "PEREZ, JUAN", TipoUsuario = "profesor", Roles = new[] { "cplec_docente" } }
    };

    [TestMethod]
    public async Task Login_ExtraeDeviceInfoYForwardedFor_LosPasaAlService()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.LoginAsync("1804567890", "clave", "Chrome/Win", "203.0.113.5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleLoginResponse());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Device-Info"] = "Chrome/Win";
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.5, 10.0.0.1";
        var controller = NewController(mock, httpContext);

        var result = await controller.Login(new LoginDto { Username = "1804567890", Password = "clave" }, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<LoginResponseDto>().Which.AccessToken.Should().Be("access-token");
        mock.VerifyAll();
    }

    [TestMethod]
    public async Task Login_SinXForwardedFor_UsaRemoteIpAddress()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), "127.0.0.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleLoginResponse());

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        var controller = NewController(mock, httpContext);

        await controller.Login(new LoginDto { Username = "x", Password = "y" }, CancellationToken.None);

        mock.VerifyAll();
    }

    [TestMethod]
    public async Task Login_SiAuthServiceLanza_LaExcepcionSePropagaSinCapturar()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Credenciales inválidas."));
        var controller = NewController(mock);

        var act = async () => await controller.Login(new LoginDto { Username = "x", Password = "y" }, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task Refresh_DelegaEnAuthServiceYDevuelveOk()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.RefreshTokenAsync("token-viejo", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenResponseDto { AccessToken = "nuevo", RefreshToken = "nuevo-refresh", ExpiresIn = 28800 });
        var controller = NewController(mock);

        var result = await controller.Refresh(new RefreshTokenRequestDto { RefreshToken = "token-viejo" }, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<RefreshTokenResponseDto>().Which.AccessToken.Should().Be("nuevo");
    }

    [TestMethod]
    public async Task Logout_DelegaEnAuthServiceYDevuelveNoContent()
    {
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.LogoutAsync("token", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var controller = NewController(mock);

        var result = await controller.Logout(new LogoutRequestDto { RefreshToken = "token" }, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        mock.VerifyAll();
    }

    [TestMethod]
    public async Task Me_LeeClaimUidYDelegaEnAuthService()
    {
        var perfil = new MiPerfilDto
        {
            Usuario = new UsuarioDto { IdSigafi = "1804567890", Nombre = "PEREZ, JUAN", TipoUsuario = "profesor", Roles = new[] { "cplec_docente" } },
            Paralelos = Array.Empty<ParaleloResumenDto>(),
            Permisos = new PermisosDto { PuedeEditarAsistencia = true, PuedeCerrarSesion = true, PuedeReabrirSesion = false, PuedeEliminarAsistencia = false, PuedeDescargarReportes = true }
        };
        var mock = new Mock<IAuthService>();
        mock.Setup(s => s.ObtenerMiPerfilAsync(412, It.IsAny<CancellationToken>())).ReturnsAsync(perfil);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", "412") }, "TestAuth"))
        };
        var controller = NewController(mock, httpContext);

        var result = await controller.Me(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(perfil);
    }

    [TestMethod]
    public async Task Me_ClaimUidAusente_LanzaUnauthorized()
    {
        var mock = new Mock<IAuthService>();
        var controller = NewController(mock); // sin claims

        var act = async () => await controller.Me(CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Leccionario.sln --filter "FullyQualifiedName~AuthControllerTests"`
Expected: build error — `Leccionario.Api.Controllers.Auth.AuthController` does not exist.

- [ ] **Step 3: Create `AuthController`**

```csharp
using Leccionario.Api.Application.Authenticacion.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Leccionario.Api.Controllers.Auth;

/// <summary>
/// Login, refresh, logout y perfil del usuario autenticado. Sin
/// <c>try/catch</c> a propósito: las excepciones (<see cref="UnauthorizedAccessException"/>,
/// <c>CuentaInactivaException</c>, <c>SinAccesoSistemaException</c>) las
/// traduce <c>ApiExceptionMiddleware</c>. Ver
/// docs/superpowers/specs/2026-08-06-auth-login-design.md sección 0e.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginDto dto, CancellationToken ct)
    {
        var respuesta = await _authService.LoginAsync(dto.Username, dto.Password, ObtenerDeviceInfo(), ObtenerIpAddress(), ct);
        return Ok(respuesta);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<RefreshTokenResponseDto>> Refresh([FromBody] RefreshTokenRequestDto dto, CancellationToken ct)
    {
        var respuesta = await _authService.RefreshTokenAsync(dto.RefreshToken, ObtenerDeviceInfo(), ObtenerIpAddress(), ct);
        return Ok(respuesta);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto dto, CancellationToken ct)
    {
        await _authService.LogoutAsync(dto.RefreshToken, ct);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<MiPerfilDto>> Me(CancellationToken ct)
    {
        var uidClaim = User.FindFirst("uid")?.Value;
        if (!int.TryParse(uidClaim, out var idUsuario))
            throw new UnauthorizedAccessException("Token inválido.");

        var perfil = await _authService.ObtenerMiPerfilAsync(idUsuario, ct);
        return Ok(perfil);
    }

    private string? ObtenerDeviceInfo() => Request.Headers["X-Device-Info"].FirstOrDefault();

    private string ObtenerIpAddress()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
            return forwarded.Split(',')[0].Trim();

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
```

Save to `src/Leccionario.Api/Controllers/Auth/AuthController.cs`.

- [ ] **Step 4: Wire `AuthService`/`RefreshTokenService` into DI**

In `src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs`, inside `AddInfrastructureLayer` (after the `services.AddDbContext<sigafi_esContext>(...)` call, before `return services;`), add:

```csharp

        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthService, AuthService>();
```

- [ ] **Step 5: Make `LoginDto` validation errors match the API's error contract**

In `src/Leccionario.Api/Program.cs`, after the existing block:

```csharp
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy   = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
```

add (this MUST come after `AddControllers()`, not before — `AddControllers()` registers its own default `InvalidModelStateResponseFactory` via `Configure<ApiBehaviorOptions>`, and the last registration wins):

```csharp

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
```

(Fully-qualified names used inline to avoid adding more top-level `using`s to a file that currently has none — matches the existing minimal style of `Program.cs`.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet build src/Leccionario.sln --warnaserror && dotnet test src/Leccionario.sln`
Expected: PASS, **entire suite** (not just the filtered subset) — this step also re-validates `ApiSmokeTests` and `DbContextSmokeTests` still pass now that `AddInfrastructureLayer` registers two more scoped services. 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Leccionario.Api/Controllers/Auth/AuthController.cs \
        src/Leccionario.Api/Extensions/DependencyInjectionExtensions.cs \
        src/Leccionario.Api/Program.cs \
        src/Leccionario.Tests/AuthControllerTests.cs
git commit -m "feat(auth): AuthController (login/refresh/logout/me) y wiring en DI"
```

---

### Task 9: Corregir `docs/04-contrato-api.md`

**Files:**
- Modify: `docs/04-contrato-api.md`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Fix the table**

In `docs/04-contrato-api.md`, find this line in the error-code table (sección "Formato de error"):

```
| 403 | `CUENTA_INACTIVA` | Credencial correcta, `usuarios.activo = 0` |
```

Replace with:

```
| 401 | `CUENTA_INACTIVA` | Credencial correcta, `usuarios.activo = 0` |
```

Move the row up to sit with the other `401` entries (right after `TOKEN_EXPIRADO`) so the table stays sorted by HTTP status — the section should read:

```
| 400 | `VALIDACION` | DTO inválido. `detalles` trae los errores por campo |
| 401 | `CREDENCIALES_INVALIDAS` | Login fallido. Mensaje **siempre** el mismo |
| 401 | `TOKEN_EXPIRADO` | El access token venció → el cliente hace refresh |
| 401 | `CUENTA_INACTIVA` | Credencial correcta, `usuarios.activo = 0` |
| 403 | `SIN_ACCESO_SISTEMA` | El usuario no tiene ningún rol `cplec_*` |
| 403 | `DISTRIBUTIVO_AJENO` | El docente pidió una asignación que no es suya |
```

(the remaining rows — `SESION_CERRADA` through `ERROR_INTERNO` — are unchanged, just keep them below as they already are.)

- [ ] **Step 2: Verify**

Run: `grep -n "CUENTA_INACTIVA" docs/04-contrato-api.md`
Expected: the line shows `| 401 | \`CUENTA_INACTIVA\` |`.

- [ ] **Step 3: Commit**

```bash
git add docs/04-contrato-api.md
git commit -m "docs(api): corregir CUENTA_INACTIVA a 401 (coincide con ExceptionClassifier)"
```

---

### Task 10: Verificación final

**Files:** none — this task only runs commands.

- [ ] **Step 1: Full clean build**

Run: `dotnet build src/Leccionario.sln --warnaserror`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Full test suite**

Run: `dotnet test src/Leccionario.sln`
Expected: every test passes — this now includes the ~34 new tests from Tasks 1, 2, 5, 6, 7, 8 on top of the ~60 that existed before this plan.

- [ ] **Step 3: Confirm coverage bar for the new code**

Run: `dotnet test src/Leccionario.sln /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:Exclude="[*]Leccionario.Api.Domain.Entities.*"`

Open the generated `coverage.cobertura.xml` (or the console summary) and confirm `Application/Authenticacion/Auth/AuthService.cs` and `RefreshTokenService.cs` are at or above 80% line coverage, per `docs/07-pruebas.md sección Cobertura`. If either is below 80%, identify the uncovered branch and add a test for it before moving on — don't lower the bar.

- [ ] **Step 4: Sanity-check the git log**

Run: `git log --oneline dv_jb..HEAD`
Expected: 9 commits (Tasks 1-9), each with a `feat(auth):` or `docs(api):` message, no `WIP` or fixup commits left dangling.

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin feature/auth-login
gh pr create --base dv_jb \
  --title "feat(auth): login, refresh, logout y /api/auth/me" \
  --body "Implementa feature/auth-login siguiendo docs/superpowers/specs/2026-08-06-auth-login-design.md. Ver ese archivo para las decisiones de diseño (por qué no se porta IEnsureUsuarioService, por qué CUENTA_INACTIVA quedó en 401, por qué /me entra en este PR con paralelos:[] ). Test plan: dotnet test en verde (build --warnaserror limpio), cobertura >=80% en AuthService y RefreshTokenService."
```

This is the only step in the whole plan that touches anything outside the local repo — confirm with the user before running it if you're executing this plan unattended.
