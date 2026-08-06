# ADR-003 — Autenticación local contra el padrón de `sigafi_es`

- **Estado:** Aceptada
- **Fecha:** 2026-08-06
- **Autor:** dv_jb

## Contexto

El requerimiento es "una forma rápida de login", usando **las mismas credenciales** que
`BienestarInstitucional.Api`.

Existen dos caminos en la plataforma ISTPET:

1. **`auth_global_istpet`** — un SSO centralizado, con ADRs propios
   (`ADR-001-autenticacion-sso-centralizada.md`,
   `ADR-002-refresh-token-db-rotation.md`) y un playbook de migración. Bienestar lo
   tiene configurado en `AuthGlobal:Url = http://localhost:5000`.
2. **Autenticación local** contra `usuarios` de `sigafi_es`, que es lo que Bienestar
   **efectivamente ejecuta hoy** en `AuthService.LoginAsync`, con fallback a las tablas
   legacy `alumnos.password` / `profesores.clave`.

## Decisión

Implementar **autenticación local** replicando el mecanismo de Bienestar, con el
sistema `cplec` y el mismo secreto JWT compartido.

## Razones

1. **Es lo que realmente está en producción.** Bienestar declara `AuthGlobal` en
   configuración pero su `AuthService` resuelve el login contra `sigafi_es`. Copiar el
   camino probado da un login funcionando en un sprint.
2. **Mismas credenciales, literalmente.** No hay sincronización de padrón ni proceso de
   alta: es la misma tabla `usuarios`, la misma fila.
3. **El auto-registro ya resuelve el arranque.** Un docente que nunca entró a ningún
   portal ISTPET no existe en `usuarios`, pero sí en `profesores`. En el primer login
   válido contra `profesores.clave`, la API crea su fila y hashea la contraseña. Sin
   este mecanismo habría que dar de alta a 41 docentes a mano.
4. **No cierra la puerta al SSO.** `IAuthService` queda como puerto: migrar a
   `auth_global_istpet` es cambiar la implementación registrada en DI, sin tocar
   controllers ni frontend.

## Consecuencias y riesgos asumidos

**El secreto JWT es compartido entre todos los sistemas ISTPET.** Un token emitido por
Bienestar tiene firma válida en `cplec`. Mitigación obligatoria, no opcional:

- Validar `codigo_sistema == "cplec"` en un filtro global, además de la firma.
- Exigir rol `cplec_*` en cada endpoint.
- Validar el alcance de datos (distributivo) en cada petición.

Sin las tres, un docente de otro portal entraría acá. Es el punto más delicado del
diseño y está cubierto por tests obligatorios en
[`07-pruebas.md`](../07-pruebas.md).

**Contraseñas legacy en texto plano.** `profesores.clave` guarda la clave sin hashear.
No es algo que este proyecto pueda arreglar (es de otro sistema), pero:

- `cplec` **nunca escribe** en `profesores.clave`.
- Al validar contra ella, migra la contraseña a bcrypt(12) en `usuarios.contrasenia`.
  Cada login legacy exitoso reduce la superficie del problema.

**Alumnos excluidos.** A diferencia de Bienestar, `cplec` no permite login de alumnos:
no hay caso de uso en la v1 y reduce la superficie de ataque.

## Cuándo revisar este ADR

- Si `auth_global_istpet` pasa a producción y los demás sistemas migran.
- Si se necesita SSO real entre portales (hoy cada uno pide login por separado).
- Si se decide rotar el secreto compartido por claves asimétricas por sistema — que
  sería la solución correcta al riesgo descrito arriba.
