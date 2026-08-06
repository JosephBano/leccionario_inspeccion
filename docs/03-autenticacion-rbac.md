# 03 — Autenticación y RBAC

Este sistema **no tiene su propio padrón de usuarios**. Reutiliza el de `sigafi_es`,
con el mismo mecanismo que `BienestarInstitucional.Api`, para que un docente entre con
las credenciales que ya conoce.

Referencia del original:
`digitalizacion-istpet/Bienestar_Institucional/Backend/BienestarInstitucional.Api/Application/Services/AuthService.cs`

---

## 1. Configuración

`appsettings.json` (los valores reales van en `appsettings.Development.json`, que
**no se commitea**):

```jsonc
{
  "ConnectionStrings": {
    "SigafiDb": "Server=<host>;Port=3307;Database=sigafi_es;User=<user>;Password=<pass>"
  },
  "SistemaCodigo": "cplec",
  "JWTSettings": {
    "Secret":   "<clave simétrica compartida ISTPET — mínimo 32 bytes>",
    "Issuer":   "leccionario_conduccion",
    "Audience": "cplec"
  },
  "AllowedOrigins": [ "http://localhost:4200" ]
}
```

`SistemaCodigo` **debe** coincidir con `rbac_sistema.codigo`. Si no coincide, ningún
usuario podrá entrar (y es el fallo más común al desplegar).

> **Secreto JWT.** El secreto es el mismo que usan los demás sistemas ISTPET
> (`ISTPET_Sistemas_Seguridad_ClaveCompartidaSecretSymmetricKey2026!` en Bienestar).
> Compartirlo permite SSO entre portales, pero implica que **un token emitido por
> cualquier sistema ISTPET valida en este**. Por eso la autorización real no puede
> apoyarse solo en la firma: se valida además `codigo_sistema == "cplec"` y el rol.
> Ver [ADR-003](adr/ADR-003-autenticacion-local-sigafi.md) y la sección 5.

---

## 2. Flujo de login

`POST /api/auth/login` con `{ "username": "<cédula>", "password": "<clave>" }`

```
1. Buscar en `usuarios` por idSigafi  (SIN filtrar por activo)
   │
   ├── Existe → verificar credencial:
   │      a) si `contrasenia` parece bcrypt ($2…, len ≥ 50) → BCrypt.Verify
   │      b) si no → comparación directa (legacy en texto plano)
   │      c) fallback: comparar contra `profesores.clave` (solo tablaSigafi='profesor')
   │
   │    ¿credencial incorrecta? → 401 "Credenciales inválidas."   ← mensaje ÚNICO
   │    ¿credencial correcta pero usuario.activo = 0? → 403 CUENTA_INACTIVA
   │    ¿hash centinela + credencial legacy correcta? → migrar a bcrypt(12) y guardar
   │
   └── No existe → auto-registro:
          verificar `profesores.clave` (esReal = 1)
          ¿coincide? → crear fila en `usuarios` + hashear la clave
          ¿no?      → 401 "Credenciales inválidas."
2. Cargar roles activos del usuario (rbac_usuario_rol → rbac_rol)
3. Filtrar: ¿tiene algún rol con grants sobre el sistema `cplec`?
   No → 403 "No tienes acceso a esta aplicación."
4. Emitir access token (JWT HS256, 8 h) + refresh token (rotativo, en BD)
```

### Reglas de seguridad heredadas — no se relajan

| Regla | Por qué |
|---|---|
| **Un solo mensaje de error de credencial** (`"Credenciales inválidas."`) | Distinguir "no existe" de "clave incorrecta" permite enumerar el padrón de cédulas |
| **El estado `activo` se valida DESPUÉS de la credencial** | Si se valida antes, un atacante descubre qué cuentas existen aunque estén desactivadas |
| **No se persiste ninguna migración de contraseña antes de validar `activo`** | Una cuenta desactivada no debe poder "completar registro" |
| **`profesores.esReal = 1`** en el fallback legacy | Hay filas de profesores ficticias en `sigafi_es` |
| **BCrypt work factor 12** | Es el usado en el resto de la plataforma |
| **Hash centinela aleatorio** (nunca constante) para cuentas creadas sin clave | Un centinela compartido y filtrado permitiría entrar a cualquier cuenta nueva |
| **`idSigafi` enmascarado en los logs** | Es PII (cédula) |

### Diferencia deliberada con Bienestar

Bienestar acepta login de `alumno` **y** `profesor`. En `cplec` **los alumnos no
inician sesión**: no hay caso de uso para ellos en la v1. El fallback legacy se limita
a `tablaSigafi = 'profesor'`, y el auto-registro solo consulta `profesores`. Un alumno
que intente entrar recibe el mismo `401` genérico.

---

## 3. Estructura del JWT

| Claim | Contenido | Ejemplo |
|---|---|---|
| `sub` | `usuarios.idSigafi` (cédula) | `"1804567890"` |
| `uid` | `usuarios.idUsuario` (interno) | `"412"` |
| `nombre` | Nombre completo | `"MARIA ELENA TORRES"` |
| `email` | `profesores.emailInstitucional` o `.email` | `"mtorres@istpet.edu.ec"` |
| `tipo_usuario` | `alumno` / `profesor` / `otros` | `"profesor"` |
| `role` (×N) | Un claim por rol RBAC activo | `"cplec_docente"` |
| `codigo_rol` | Primer rol (compatibilidad) | `"cplec_docente"` |
| `codigo_sistema` | **Siempre `"cplec"`** | `"cplec"` |
| `jti` | GUID del token | — |

- Algoritmo **HS256**, expiración **8 h**, `ClockSkew` de 5 minutos.
- El refresh token es un valor aleatorio de 64 bytes; en BD se guarda solo su
  **SHA-256** (`rbac_refresh_tokens.tokenHash`), con rotación y detección de reuso.

---

## 4. Roles

| `codigo_rol` | Nombre | Módulos y operaciones (`cplec`) |
|---|---|---|
| `cplec_docente` | Docente Leccionario | `asistencia`: ver, crear, editar · `reportes`: ver |
| `cplec_inspector` | Inspector Leccionario | `asistencia`: ver, editar · `reportes`: ver, crear |

Notas:

- El docente **no tiene `eliminar`** sobre asistencia: una marca no se borra, se corrige
  (y queda en `cplec_asistencias_historial`).
- El inspector **no tiene `crear`** sobre asistencia: no pasa lista por un docente. Sí
  tiene `editar` para corregir y reabrir sesiones cerradas.
- Ambos roles se crean en `database/migrations/001_cplec_rbac_seed.sql`.
- La asignación a personas concretas se hace con
  `database/queries/asignar-roles-cplec.sql`.

---

## 5. Autorización: dos capas, ambas obligatorias

### Capa 1 — Rol (declarativa)

```csharp
[Authorize(Roles = "cplec_docente")]
[Authorize(Roles = "cplec_inspector")]
[Authorize(Roles = "cplec_docente,cplec_inspector")]   // cualquiera de los dos
```

Además, un filtro global valida que `codigo_sistema == "cplec"`. Sin él, un token
emitido por Bienestar (mismo secreto compartido) pasaría la validación de firma.

### Capa 2 — Alcance de datos (imperativa)

**Esta es la que realmente protege.** Un docente con rol válido sigue sin poder tocar
el paralelo de otro docente. Se verifica en el servicio, contra la BD, en **cada**
petición que reciba un `idAsignacion` o un `idSesion`:

```csharp
// Application/Asistencia/Services/DistributivoGuard.cs
public async Task<bool> DocenteTieneAsignacionAsync(string idProfesor, int idAsignacion)
    => await _db.AsignacionesProfesores
        .AnyAsync(ap => ap.IdAsignacion == idAsignacion
                     && ap.IdProfesor   == idProfesor
                     && (ap.Activo == null || ap.Activo == true)
                     && (ap.EsActivaAsignacion == null || ap.EsActivaAsignacion == true));
```

Reglas:

- El `idProfesor` sale **del claim `sub` del token**, nunca del body ni del query string.
- Si falla → `403 Forbidden` con código `DISTRIBUTIVO_AJENO`. **No** `404`: el recurso
  existe, simplemente no es suyo.
- El inspector salta esta capa (por diseño), pero toda consulta suya queda auditada.

Cualquier endpoint nuevo que reciba un identificador de clase **debe** pasar por este
guard. Es el punto que más tests unitarios concentra — ver
[`07-pruebas.md`](07-pruebas.md).

---

## 6. Endpoints de autenticación

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| POST | `/api/auth/login` | anónimo | Devuelve access + refresh token y datos del usuario |
| POST | `/api/auth/refresh` | anónimo | Rota el refresh token y emite un access token nuevo |
| POST | `/api/auth/logout` | anónimo | Revoca el refresh token |
| GET | `/api/auth/me` | autenticado | Perfil, roles y paralelos del usuario actual |

`/api/auth/me` es propio de `cplec` (Bienestar no lo tiene): el frontend lo llama al
arrancar para reconstruir el estado sin decodificar el JWT en el cliente.

---

## 7. Lado frontend

- El **access token vive en memoria** (signal de `AuthService`), no en `localStorage`:
  reduce la superficie de XSS.
- El **refresh token va en `localStorage`** — es el compromiso aceptado para sobrevivir
  a un F5. Si se habilita cookie `HttpOnly` en el backend, migrar a eso.
- `AuthInterceptor` adjunta `Authorization: Bearer …` y, ante un `401`, intenta un
  refresh **una sola vez** antes de expulsar a login (sin bucle de reintentos).
- `roleGuard(['cplec_inspector'])` protege las rutas. Es **conveniencia de UX, no
  seguridad**: el backend vuelve a validar todo.

---

## 8. Checklist de puesta en marcha

- [ ] Aplicar `001_cplec_rbac_seed.sql` → verificar con `verificar-esquema-cplec.sql` (consultas 6–9).
- [ ] Configurar `SistemaCodigo = "cplec"` y el secreto JWT en `appsettings.Development.json`.
- [ ] Asignar `cplec_inspector` a la persona de inspección.
- [ ] Asignar `cplec_docente` a los docentes con distributivo en la carrera 6.
- [ ] Probar: docente entra y ve solo sus paralelos.
- [ ] Probar: docente pide un `idAsignacion` ajeno → `403 DISTRIBUTIVO_AJENO`.
- [ ] Probar: usuario sin rol `cplec_*` → `403` en el login.
