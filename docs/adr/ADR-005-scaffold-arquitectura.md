# ADR-005 — Scaffold de la arquitectura de `Leccionario.Api`

- **Estado:** Aceptada
- **Fecha:** 2026-08-06
- **Decisores:** arquitecto, dev

## Contexto

`cplec` necesita un backend propio para el leccionario y la inspección de la Escuela de Conducción. El punto de partida disponible es `BienestarInstitucional.Api`, que comparte infraestructura de autenticación y acceso a `sigafi_es`, pero también contiene decisiones y código específicos de Bienestar. Portaremos únicamente lo seguro y estable; las reglas propias de cplec se diseñarán desde cero para no arrastrar semánticas ajenas.

El scaffold debe dejar explícitos los límites de seguridad, la estructura de capas, el contrato de errores, la observabilidad y la estrategia de pruebas antes de implementar los casos de uso.

## Decisión

Construimos la solución en `src/Leccionario.sln`, con la API bajo `src/Leccionario.Api/` y los tests bajo `src/Leccionario.Tests/`. La API se organiza en `Domain/Entities/`, `Application/`, `Infrastructure/`, `Controllers/`, `Extensions/` y `Middlewares/`. El frontend permanece en `client/`, en la raíz del repositorio. Las entidades de `Domain/Entities/` serán generadas por EF Core Power Tools y no contendrán lógica manual.

Adoptamos estas decisiones de scaffold:

1. **Autorización por sistema.** Validamos `codigo_sistema == "cplec"` mediante `AuthorizationHandler<SistemaClaimRequirement>` y una fallback policy global. No usamos `IAuthorizationFilter` ni middleware manual. Así, los endpoints protegidos quedan cubiertos de forma automática, `[AllowAnonymous]` conserva su semántica y un endpoint nuevo no puede omitir accidentalmente la validación. La razón de seguridad es que el secreto JWT es compartido entre sistemas ISTPET: la firma por sí sola no identifica al sistema destinatario. La decisión detallada está en [[docs/adr/ADR-006-filtro-codigo-sistema]].

2. **Alcance del distributivo.** `DistributivoGuard` vive en `Application/Asistencia/Services/` y lo invoca el service, no el controller. Ejecuta una única consulta canónica `AsNoTracking()` sobre `asignaciones_profesores`, validando `idAsignacion`, `idProfesor`, `activo` y `esActivaAsignacion`. En v1 no hay cache. Es la pieza de mayor riesgo de seguridad y, por tanto, la que concentra más pruebas.

3. **Perfil de sesión.** `/api/auth/me` es un endpoint propio de cplec. Devuelve `{ usuario, paralelos, permisos }`; roles y paralelos se consultan en la base de datos, no se aceptan como verdad del JWT. Si el token y la base están desincronizados, prevalece la base.

4. **Errores de aplicación.** Usamos únicamente la jerarquía de ocho clases definida en [[docs/05-lineamientos-backend]]: `AppException`, `ValidacionException`, `NoEncontradoException`, `ProhibidoException`, `DistributivoAjenoException`, `SesionCerradaException`, `FueraDePlazoException` y `ConflictoException`. No portamos las aproximadamente treinta excepciones de Bienestar (`EntityNotFoundException`, `BusinessRuleException`, entre otras). `ExceptionClassifier` lee directamente `app.Codigo` y `app.HttpStatus`, por lo que incorporar una subclase no requiere modificar el clasificador.

5. **Limitación de tasa.** Usamos `Microsoft.AspNetCore.RateLimiting` nativo de .NET 8, con policies por endpoint, por ejemplo `[EnableRateLimiting("login")]`. El middleware in-memory de Bienestar no es adecuado para varias instancias. Al escalar horizontalmente, la policy se migrará a Redis.

6. **Auditoría inicial.** El scaffold incluye un stub que registra eventos únicamente mediante `ILogger`; no persiste todavía. La escritura en `gest_audit_registros` se añadirá después de que EF Power Tools genere la entidad correspondiente. Se excluyen `/api/auth/login`, `/api/auth/refresh` y `/api/auth/logout`, porque reciben credenciales o refresh tokens en el body y no deben quedar registrados.

7. **Pruebas.** Las pruebas de integración usan `[TestCategory("Integration")]` y skip-on-missing-connection, ambas reglas obligatorias. Los unitarios siguen `XxxTests.cs`; las integraciones, `XxxIntegracionTests.cs`. Los métodos siguen `Metodo_Escenario_ResultadoEsperado`.

## Consecuencias

### Positivas

- La arquitectura separa lo reutilizable de Bienestar de las reglas propias del dominio cplec.
- La autorización de sistema y el alcance del distributivo se aplican en capas difíciles de omitir.
- La solución queda preparada para regenerar entidades legacy sin mezclar código generado con lógica de aplicación.
- `/api/auth/me` refleja el estado efectivo de la base, evitando confiar en claims potencialmente obsoletos.
- El contrato de excepciones es pequeño, predecible y extensible.
- El rate limiting funciona correctamente en una única instancia y deja definida la ruta de escalamiento.
- Las convenciones de tests hacen visibles las fronteras entre unitarios e integración sin bloquear el desarrollo cuando no existe una base local.

### Negativas y costos

- El port desde Bienestar no es una copia directa: requiere revisar y reescribir piezas, especialmente autorización, errores y auditoría.
- Sin persistencia de auditoría en el scaffold, los eventos no sobreviven a una rotación o reinicio de la aplicación.
- La solución in-memory de rate limiting deberá reemplazarse al desplegar más de una instancia.
- Consultar roles y paralelos en cada construcción de `/api/auth/me` añade una lectura de base, a cambio de consistencia.
- `DistributivoGuard` puede repetir consultas en v1, porque deliberadamente no se introduce cache antes de contar con evidencia de necesidad y un diseño seguro de invalidación.

## Alternativas consideradas

- **Copiar íntegramente `BienestarInstitucional.Api`**: se descartó porque arrastra excepciones, filtros y reglas de negocio que no representan cplec y aumenta la superficie de mantenimiento.
- **Resolver la autorización con middleware o filtros globales**: se descartó por las razones detalladas en [[docs/adr/ADR-006-filtro-codigo-sistema]]; son más fáciles de omitir o menos compatibles con la semántica de autorización de ASP.NET Core.
- **Hacer la auditoría persistente desde el primer scaffold**: se descartó temporalmente porque la entidad `gest_audit_registros` aún no está generada; el stub con `ILogger` permite instrumentar sin inventar un modelo.
- **Agregar cache al guard desde v1**: se descartó para mantener una única fuente canónica y evitar servir autorizaciones obsoletas mientras se estabilizan los flujos.
- **Rate limiting in-memory heredado de Bienestar**: se descartó porque no coordina límites entre instancias.

## Notas

- La autenticación local y el secreto compartido se decidieron en [[docs/adr/ADR-003-autenticacion-local-sigafi]].
- El modelo de sesiones propio se documenta en [[docs/adr/ADR-001-sesion-de-clase-propia]].
- El esquema se gestiona mediante scripts SQL, no migraciones de EF, según [[docs/adr/ADR-004-sin-migraciones-ef]].
- La especificación de implementación de referencia es [[docs/superpowers/plans/001-scaffold-base-design]].
- La autorización completa requiere las tres capas: firma JWT válida, sistema/rol cplec y alcance del distributivo. Véase [[docs/03-autenticacion-rbac]].
