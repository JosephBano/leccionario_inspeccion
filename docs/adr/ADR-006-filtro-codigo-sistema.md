# ADR-006 — Filtro global de `codigo_sistema` mediante autorización

- **Estado:** Aceptada
- **Fecha:** 2026-08-06
- **Decisores:** arquitecto, dev

## Contexto

La autenticación local de cplec reutiliza el padrón de `sigafi_es` y el secreto JWT compartido por los sistemas ISTPET, según [[docs/adr/ADR-003-autenticacion-local-sigafi]]. Esto permite reutilizar credenciales y facilita la interoperabilidad, pero también significa que un token emitido por Bienestar tiene una firma válida cuando se presenta ante cplec.

La firma demuestra que el token fue emitido por una fuente que conoce el secreto; no demuestra que fue emitido para este sistema. Sin una segunda verificación, bastaría robar un token válido de Bienestar para intentar entrar a cplec. Bienestar no implementa actualmente este filtro: es un gap de seguridad conocido. cplec debe cerrarlo en su propia API.

## Decisión

Implementamos `AuthorizationHandler<SistemaClaimRequirement>` y lo registramos como singleton. La política de autorización se llama `CplecSystem` y exige un usuario autenticado además del requisito de sistema:

- `RequireAuthenticatedUser()`
- `AddRequirements(new SistemaClaimRequirement())`

La política se configura como fallback policy global. El handler lee el claim `codigo_sistema`: si su valor es exactamente `"cplec"`, ejecuta `context.Succeed()`; si falta o tiene cualquier otro valor, ejecuta `context.Fail()`, por lo que la solicitud protegida termina en 403.

El `appsettings.Development.json` (no commiteado) debe configurar `SistemaCodigo = "cplec"`, y ese valor debe coincidir con `rbac_sistema.codigo`. La configuración identifica el sistema que está desplegando la aplicación; la autorización sigue verificando el claim presentado por el token.

## Alternativas consideradas

- **Middleware manual después de `UseAuthentication`**: se descarta porque la protección no aparece en la firma del controller, no se integra naturalmente con `[AllowAnonymous]` y es fácil olvidar el middleware o su orden al agregar o modificar endpoints.
- **`IAuthorizationFilter` global**: se descarta porque es menos idiomático en .NET 8 y obliga a resolver la protección mediante filtros asociados a rutas, en vez de integrarla con el modelo estándar de policies y requirements.
- **`AuthorizationHandler` con fallback policy**: se adopta porque `[Authorize]` la aplica automáticamente, `[AllowAnonymous]` la corta explícitamente, el handler puede probarse de forma aislada y la cobertura no depende de que cada controller recuerde registrar un filtro.

## Consecuencias

### Positivas

- Cualquier controller que use `[Authorize]` queda protegido automáticamente por la validación de sistema.
- Los endpoints anónimos declaran `[AllowAnonymous]` y se excluyen sin modificar el handler.
- El requisito se prueba de forma aislada, sin levantar toda la aplicación.
- Si el handler falla durante la verificación, la aplicación produce un error 500: un fallo en una comprobación de seguridad se trata como bug y no como un éxito silencioso.
- Un token de otro sistema ISTPET, aunque esté firmado con el secreto compartido, no obtiene acceso a rutas protegidas de cplec.

### Negativas

- Si en el futuro existiera un endpoint compartido entre cplec y Bienestar, habría que excluirlo explícitamente con `[AllowAnonymous]` y reimplementar una autorización apropiada para ese caso. No es un escenario de cplec v1.
- Un JWT sin `codigo_sistema` es rechazado, incluso si la firma es válida. Puede ser un token legítimo emitido por un sistema externo, pero no debe entrar a cplec sin declarar su sistema destinatario.
- La política global exige disciplina al diseñar excepciones: un endpoint protegido por error no debe hacerse anónimo para solucionar un problema de integración sin revisar antes su amenaza.

## Testing

El handler debe contar con pruebas unitarias que verifiquen:

- claim `codigo_sistema = "cplec"` → `context.HasSucceeded == true`;
- claim ausente → `context.HasFailed == true`;
- claim con un valor diferente → `context.HasFailed == true`.

Además, se requiere un smoke test con `WebApplicationFactory`: emitir un JWT con `codigo_sistema = "bien_proy062026"`, firmado con el mismo secreto compartido, y comprobar que una ruta autenticada de cplec responde 403. El test debe demostrar que la firma válida no sustituye la validación del sistema.

## Referencias

- [[docs/adr/ADR-003-autenticacion-local-sigafi]] — autenticación local contra `sigafi_es` y secreto compartido.
- [[docs/03-autenticacion-rbac]] sección 5.1 — validación de `codigo_sistema` y capas de autorización.
- [[docs/adr/ADR-005-scaffold-arquitectura]] — decisiones generales del scaffold de `Leccionario.Api`.
