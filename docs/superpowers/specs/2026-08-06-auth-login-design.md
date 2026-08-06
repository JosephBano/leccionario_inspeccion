---
title: Diseño — feature/auth-login (login, refresh, logout, /api/auth/me)
status: approved
date: 2026-08-06
branch: feature/auth-login
target_pr: <TBD>
spec: docs/03-autenticacion-rbac.md · docs/04-contrato-api.md · docs/07-pruebas.md
      · docs/superpowers/plans/001-scaffold-base-design.md §2c
author: arquitecto
---

# Diseño — feature/auth-login

## Resumen ejecutivo

PR #3 del roadmap (`docs/superpowers/plans/001-scaffold-base-design.md §4.1`). Implementa
`POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout` y
`GET /api/auth/me`, con TDD sobre los casos de `docs/07`. Es el puerto ajustado de
`BienestarInstitucional.Api/Application/Services/AuthService.cs`, recortado a lo que
cplec realmente necesita: **solo `profesor`** inicia sesión (no alumno, no "externos"),
y los roles **no se autoasignan** — se cargan a mano vía
`database/queries/asignar-roles-cplec.sql`.

`GET /api/auth/me` se adelanta a este PR (originalmente PR #5) con `paralelos: []`
fijo — el campo `permisos` sale puramente de los roles del JWT y no depende del
`DistributivoGuard` (PR #4, todavía no existe). El shape completo del DTO queda
definido ahora para que el PR del guard solo tenga que llenar la lista.

---

## 0. Decisiones resueltas en brainstorming

### a) `CUENTA_INACTIVA`: 401, no 403

`docs/04-contrato-api.md` documenta `CUENTA_INACTIVA` como 403, pero
`Middlewares/ExceptionClassifier.cs` (ya mergeado en `feature/scaffold-base`, con test
explícito `GetHttpStatus_CuentaInactivaException_Es401`) lo mapea a 401. **Se mantiene
el código tal cual está** — no se toca un PR ya cerrado — y se corrige `docs/04` para
que documente 401. Ese fix de documentación es parte de este PR.

### b) Alcance: `/api/auth/me` entra en este PR

Aunque el roadmap original lo separaba (dependía del guard de distributivo), se decidió
incluirlo ahora con `paralelos: []` hardcodeado. `permisos` no depende del guard — es
una función pura de los roles del JWT (ver §3). El PR que agregue
`DistributivoGuard` + la consulta de distributivo (`feature/rbac-distributivo-guard` o
`feature/distributivo-periodos`) solo reemplaza la lista vacía por la real; el contrato
del DTO no cambia.

### c) No se porta `IEnsureUsuarioService`

La versión de Bienestar resuelve alumno + profesor + "externos" (con generación de ids
`EXT-…`, reintentos ante colisión de PK, asignación automática de rol por defecto).
cplec solo tiene un camino: `profesor`, sin asignación automática de rol
(`docs/03 §2`: "auto-registro: verificar `profesores.clave`... no asigna rol"). Se
incorpora como método privado dentro de `AuthService` en vez de una interfaz aparte.

### d) Sin `IHttpContextAccessor` en la capa de Application

`AuthController` extrae `deviceInfo` (header `X-Device-Info`) e `ipAddress`
(`X-Forwarded-For` → `RemoteIpAddress`) y los pasa como parámetros explícitos a
`IAuthService`. Mantiene los services testeables sin mockear HTTP; ningún otro service
de cplec depende de `IHttpContextAccessor` hoy.

### e) `AuthController` no atrapa excepciones

A diferencia de Bienestar, cplec ya tiene `ApiExceptionMiddleware` +
`ExceptionClassifier` centralizando la traducción a JSON. El controller llama al
service y deja subir `UnauthorizedAccessException`, `CuentaInactivaException`,
`SinAccesoSistemaException` sin `try/catch`.

---

## 1. Archivos

```
Application/Authenticacion/Auth/
  AuthDtos.cs             NUEVO
  IAuthService.cs         NUEVO
  AuthService.cs          NUEVO
  RefreshTokenService.cs  NUEVO (implementa IRefreshTokenService, ya existente)

Application/Common/Exceptions/
  SinAccesoSistemaException.cs   NUEVO — 403 SIN_ACCESO_SISTEMA (: ProhibidoException)

Extensions/
  DependencyInjectionExtensions.cs   MODIFICADO — registra AuthService,
    RefreshTokenService, IAuthService en AddInfrastructureLayer (dependen de
    sigafi_esContext); agrega ApiBehaviorOptions.InvalidModelStateResponseFactory
    en AddApplicationLayer (ver §4)

Controllers/Auth/
  AuthController.cs       NUEVO

docs/04-contrato-api.md   MODIFICADO — corrige CUENTA_INACTIVA a 401

Leccionario.Tests/
  AuthServiceTests.cs          NUEVO
  RefreshTokenServiceTests.cs  NUEVO
  AuthControllerTests.cs       NUEVO
```

---

## 2. Flujo login / refresh / logout

Sigue `docs/03 §2` literal:

```
LoginAsync(username, password, deviceInfo, ipAddress):
  1. usuario = SELECT usuarios WHERE idSigafi = username   (sin filtrar activo)
  2. Si existe:
       a. VerificarCredencial (bcrypt si IsHashed, si no comparación directa;
          si falla, fallback contra profesores.clave con esReal=1)
       b. ¿credencial falla?          → UnauthorizedAccessException("Credenciales inválidas.")
       c. ¿credencial OK, activo=0?   → CuentaInactivaException
       d. ¿hash no-bcrypt y matchea profesores.clave? → migrar a bcrypt(12), SaveChanges
     Si no existe:
       a. profesor = SELECT profesores WHERE idProfesor = username AND esReal = 1
       b. ¿no existe o clave no matchea? → UnauthorizedAccessException("Credenciales inválidas.")
       c. Crear fila en usuarios (contrasenia = Hash(password), activo = 1). Sin rol.
  3. roles = SELECT rbac_rol WHERE activo Y tiene rbac_rol_modulo_operacion activo
             hacia un módulo cuyo rbac_sistema.codigo == config["SistemaCodigo"]
  4. ¿roles vacío? → SinAccesoSistemaException
  5. refreshToken = IRefreshTokenService.IssueAsync(idUsuario, deviceInfo, ipAddress)
  6. accessToken  = IJwtTokenService.GenerateAccessToken(JwtTokenClaims{...})
  7. return LoginResponseDto { AccessToken, RefreshToken, ExpiresIn=8*3600, Usuario }
```

`TipoUsuario` en el claim y en `UsuarioDto` **no se hardcodea a `"profesor"`** — se
deriva de `usuario.tablaSigafi.Trim().ToLower()` (igual que Bienestar), con
`"otros"` como default. `usuarios` es una tabla compartida entre todos los sistemas
ISTPET: nada impide, en teoría, que una fila con `tablaSigafi = 'alumno'` termine con
un rol `cplec_*` asignado a mano por error. Si eso pasa, el login igual debe reflejar
el tipo real, no mentir diciendo "profesor".

`UsuarioDto` (reutilizado de login, refresh y `/me`):

```csharp
public sealed class UsuarioDto
{
    public required string IdSigafi { get; init; }
    public required string Nombre { get; init; }
    public string? Email { get; init; }
    public required string TipoUsuario { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}
```

`RefreshTokenAsync` / `LogoutAsync` delegan en `IRefreshTokenService`
(`ValidateAndRotateAsync` / `RevokeAsync`), usando el contrato **ya existente** en el
scaffold (`RefreshTokenStatus.Ok/Invalid/ReuseDetected`), no el de Bienestar.
`RefreshTokenService` (impl nueva) adapta la lógica de familia + secuencia +
revocación-por-reuso de `BienestarInstitucional.Api/.../RefreshTokenService.cs`,
incluida la transacción con reintento ante colisión de hash.

`[EnableRateLimiting("login")]` en el endpoint de login (policy ya registrada).

---

## 3. `GET /api/auth/me`

```csharp
public sealed class MiPerfilDto
{
    public required UsuarioDto Usuario { get; init; }
    public required IReadOnlyList<ParaleloResumenDto> Paralelos { get; init; }
    public required PermisosDto Permisos { get; init; }
}

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

public sealed class PermisosDto
{
    public required bool PuedeEditarAsistencia { get; init; }
    public required bool PuedeCerrarSesion { get; init; }
    public required bool PuedeReabrirSesion { get; init; }
    public required bool PuedeEliminarAsistencia { get; init; }
    public required bool PuedeDescargarReportes { get; init; }
}
```

Shape tomado de `ADR-005 §2c` (más completo que el resumen de `docs/04`).

`Paralelos` = `[]` fijo en este PR (comentario en código explicando por qué, y
referenciando el PR que lo completa).

`Permisos` es función pura de los roles del JWT — no toca BD:

| Permiso | `cplec_docente` | `cplec_inspector` |
|---|---|---|
| `PuedeEditarAsistencia` | true | true |
| `PuedeCerrarSesion` | true | true |
| `PuedeReabrirSesion` | false | true |
| `PuedeEliminarAsistencia` | false | false |
| `PuedeDescargarReportes` | true | true |

El controller lee el claim `uid` (idUsuario) del `ClaimsPrincipal` autenticado y lo pasa
a `IAuthService.ObtenerMiPerfilAsync(idUsuario)`. Sin `[AllowAnonymous]` — cae bajo el
fallback policy normal, cualquier rol `cplec_*` puede llamarlo.

---

## 4. Validación de `ModelState`

`LoginDto` lleva `[Required]` en `Username`/`Password` (para que Swagger documente el
contrato), pero hoy un `ModelState` inválido cae en la respuesta automática de
`[ApiController]` (`ValidationProblemDetails` de ASP.NET), no en el JSON
`{codigo, mensaje, detalles, traceId, timestamp}` de `docs/04`. Nadie lo había notado
porque `HealthController` no valida body.

Se agrega un `ApiBehaviorOptions.InvalidModelStateResponseFactory` global (en
`AddApplicationLayer`) que arma `{"codigo":"VALIDACION", "mensaje":"...",
"detalles": {campo: [errores]}, "traceId":..., "timestamp":...}` — mismo shape que
`ValidacionException`. Mejora acotada, expuesta por primera vez en este PR.

---

## 5. Testing

- **`AuthServiceTests.cs`** (EF InMemory, sembrando `usuarios`/`profesores`/`rbac_*`):
  mismo mensaje para usuario inexistente y contraseña incorrecta; `CUENTA_INACTIVA` sin
  persistir migración de password; `esReal=0` no entra; alumno no entra (ni por
  fallback legacy ni por auto-registro); sin rol `cplec_*` → `SinAccesoSistemaException`;
  auto-registro de profesor nuevo persiste `contrasenia = Hash(password)` directamente
  (sin centinela — cplec verifica la credencial legacy *antes* de crear la fila, a
  diferencia de Bienestar, ver §0c); login con credencial legacy migra el hash a bcrypt
  sin duplicar la fila en un segundo login.
- **`RefreshTokenServiceTests.cs`**: emisión, rotación exitosa, expirado, revocado,
  reuso de token ya rotado → revoca toda la familia.
- **`AuthControllerTests.cs`**: controller delgado con `Moq` sobre `IAuthService`;
  verifica extracción de `deviceInfo`/`ipAddress`; verifica que las excepciones NO se
  atrapan (se propagan al middleware).
- Cobertura mínima `docs/07`: 80 % en `Application/**/Services`.

---

## Anexo — Alcance explícitamente fuera de este PR

- `DistributivoGuard` real y la consulta de paralelos del docente (PR
  `feature/rbac-distributivo-guard`).
- `GET /api/mis-paralelos`, `GET /api/periodos/por-nivel` (PR
  `feature/distributivo-periodos`).
- Cualquier lógica de asistencia.
