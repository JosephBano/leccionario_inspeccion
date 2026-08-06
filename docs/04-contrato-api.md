# 04 — Contrato de API

Base: `/api`. Todo JSON, `UTF-8`. Autenticación `Authorization: Bearer <jwt>` salvo
donde se indique.

---

## Convenciones

- **Rutas** en `kebab-case` plural: `/api/asistencias`, `/api/mis-paralelos`.
- **Cuerpos** en `camelCase` (el frontend es TypeScript).
- **Fechas** en ISO-8601 (`2026-08-06`, `2026-08-06T14:30:00`). Nunca `dd/mm/yyyy`.
- **Paginación** por `?page=1&pageSize=50`; respuesta `{ items, total, page, pageSize }`.
  `pageSize` máximo 200.
- **Idempotencia**: `PUT` y el registro de asistencia son idempotentes. Reenviar la
  misma petición produce el mismo estado.

---

## Formato de error

Uniforme para toda la API, generado por `ApiExceptionMiddleware`:

```json
{
  "codigo": "DISTRIBUTIVO_AJENO",
  "mensaje": "No tienes asignado este paralelo.",
  "detalles": null,
  "traceId": "0HN7…"
}
```

| HTTP | `codigo` | Cuándo |
|---|---|---|
| 400 | `VALIDACION` | DTO inválido. `detalles` trae los errores por campo |
| 401 | `CREDENCIALES_INVALIDAS` | Login fallido. Mensaje **siempre** el mismo |
| 401 | `TOKEN_EXPIRADO` | El access token venció → el cliente hace refresh |
| 403 | `CUENTA_INACTIVA` | Credencial correcta, `usuarios.activo = 0` |
| 403 | `SIN_ACCESO_SISTEMA` | El usuario no tiene ningún rol `cplec_*` |
| 403 | `DISTRIBUTIVO_AJENO` | El docente pidió una asignación que no es suya |
| 403 | `SESION_CERRADA` | Intento de editar una sesión con `estado = 'cerrada'` |
| 403 | `FUERA_DE_PLAZO` | Venció la ventana de edición del docente |
| 404 | `NO_ENCONTRADO` | El recurso no existe |
| 409 | `SESION_DUPLICADA` | Ya existe una sesión para esa asignación/fecha/bloque |
| 422 | `MATRICULA_AJENA` | Un `idMatricula` enviado no pertenece a ese paralelo |
| 429 | `DEMASIADAS_PETICIONES` | Rate limiting |
| 500 | `ERROR_INTERNO` | Nunca expone el mensaje de la excepción al cliente |

**Regla:** el cliente nunca decide por el texto del mensaje, siempre por el `codigo`.
Los mensajes son para humanos y pueden cambiar.

---

## Autenticación

### `POST /api/auth/login` — anónimo

```json
// request
{ "username": "1804567890", "password": "••••••" }

// 200
{
  "accessToken": "eyJ…",
  "refreshToken": "base64…",
  "expiresIn": 28800,
  "usuario": {
    "idSigafi": "1804567890",
    "nombre": "MARIA ELENA TORRES",
    "email": "mtorres@istpet.edu.ec",
    "tipoUsuario": "profesor",
    "roles": ["cplec_docente"]
  }
}
```

### `POST /api/auth/refresh` — anónimo
`{ "refreshToken": "…" }` → mismos campos de token. El token anterior queda revocado.

### `POST /api/auth/logout` — anónimo
`{ "refreshToken": "…" }` → `204`.

### `GET /api/auth/me` — autenticado
Perfil + roles + cantidad de paralelos asignados en el período activo.

---

## Docente

### `GET /api/mis-paralelos?idPeriodo=SEE2023`
Rol: `cplec_docente`. El `idProfesor` sale del token.

```json
{
  "items": [{
    "idAsignacion": 18422,
    "idPeriodo": "SEE2023",
    "asignatura": "LEGISLACION DE TRANSITO",
    "paralelo": "A",
    "idNivel": 61,
    "nivel": "PRIMER NIVEL",
    "modalidad": "PRESENCIAL",
    "totalAlumnos": 28,
    "sesionesRegistradas": 12,
    "ultimaSesion": "2026-08-05"
  }],
  "total": 4, "page": 1, "pageSize": 50
}
```

Si se omite `idPeriodo` se usa el período activo (`periodos.activo = 1`).

### `GET /api/paralelos/{idAsignacion}/alumnos`
Roles: `cplec_docente` (solo suyos) · `cplec_inspector` (cualquiera).
Devuelve la nómina: `idMatricula`, `idAlumno`, `apellidos`, `nombres`, `retirado`.

`idMatricula` es la clave con la que se registra asistencia — no `idAlumno`.

---

## Sesiones de clase

### `POST /api/paralelos/{idAsignacion}/sesiones`
Rol: `cplec_docente`. Crea la clase del día.

```json
// request
{
  "fecha": "2026-08-06",
  "numeroBloque": 1,
  "idHora": null,
  "horaInicio": "08:00",
  "horaFin": "09:30",
  "tipoBloque": "teorico",
  "tema": "Señalización vertical y horizontal"
}
```

- `fecha` se traduce internamente a `fechas_horarios.idFecha`. Si el día no existe en el
  calendario → `400 VALIDACION`.
- `201` con la sesión creada y la nómina precargada en `presente`.
- Si ya existe esa (`idAsignacion`, `fecha`, `numeroBloque`) → `409 SESION_DUPLICADA`
  con el `idSesion` existente en `detalles`, para que el cliente redirija en vez de fallar.

### `GET /api/sesiones/{idSesion}`
La sesión con su lista completa de asistencia.

### `PUT /api/sesiones/{idSesion}`
Edita `tema`, `observacion`, horas. No cambia `estado`.

### `POST /api/sesiones/{idSesion}/cerrar` · `POST /api/sesiones/{idSesion}/reabrir`
Cerrar: rol `cplec_docente` o `cplec_inspector`.
**Reabrir: solo `cplec_inspector`.** Requiere `{ "motivo": "…" }`, que va al historial.

---

## Asistencia

### `POST /api/sesiones/{idSesion}/asistencias`
Rol: `cplec_docente`. Registra o corrige la lista. **Idempotente**
(`INSERT … ON DUPLICATE KEY UPDATE` sobre `uq_cplec_asistencias_sesion_matricula`).

```json
{
  "marcas": [
    { "idMatricula": 90211, "estado": "presente" },
    { "idMatricula": 90212, "estado": "ausente" },
    { "idMatricula": 90213, "estado": "atraso", "minutosAtraso": 12 },
    { "idMatricula": 90214, "estado": "justificado", "observacion": "Certificado médico" }
  ],
  "motivo": "Corrección: llegó tarde, no ausente"
}
```

Validaciones, en este orden:

1. La sesión existe y `activo = 1` → si no, `404`.
2. El `idAsignacion` de la sesión pertenece al distributivo del token → si no, `403 DISTRIBUTIVO_AJENO`.
3. `estado != 'cerrada'` → si no, `403 SESION_CERRADA`.
4. Ventana de edición vigente → si no, `403 FUERA_DE_PLAZO`.
5. Cada `idMatricula` pertenece al paralelo de esa asignación → si no, `422 MATRICULA_AJENA`
   (con la lista de ids ofensores en `detalles`).
6. `minutosAtraso` solo se acepta con `estado = 'atraso'`.

Todo en **una transacción**, con una fila en `cplec_asistencias_historial` por cada
marca cuyo `estado` cambió.

### Ventana de edición

| Rol | Puede editar |
|---|---|
| `cplec_docente` | Hasta **72 h** después de la fecha de la sesión, y solo si `estado = 'borrador'` |
| `cplec_inspector` | Siempre, incluso sesiones cerradas (queda en el historial) |

Las 72 h son configurables: `Leccionario:VentanaEdicionHoras`. Es un valor de
configuración, no una constante en el código.

### `GET /api/asistencias/historial/{idAsistencia}`
Rol: `cplec_inspector`. Devuelve el rastro completo de cambios de una marca.

---

## Reportes (inspector)

| Endpoint | Descripción |
|---|---|
| `GET /api/reportes/por-paralelo?idAsignacion=&desde=&hasta=` | Matriz alumnos × sesiones con totales |
| `GET /api/reportes/por-estudiante?idAlumno=&idPeriodo=` | Historial del alumno en todas sus asignaturas |
| `GET /api/reportes/por-docente?idProfesor=&idPeriodo=` | Sesiones dictadas, % de registro, sesiones sin cerrar |
| `GET /api/reportes/resumen?idPeriodo=` | Tablero: % asistencia por paralelo |

Descarga: mismo endpoint con `?formato=xlsx` o `?formato=pdf`, respuesta
`application/vnd.openxmlformats-…` / `application/pdf` con `Content-Disposition`.
Por defecto (`formato` ausente) devuelve JSON — así el frontend pinta y descarga con
la misma consulta.

Un `cplec_docente` puede llamar `por-paralelo` y `por-estudiante`, pero el backend
**fuerza el filtro a su propio distributivo**, ignorando lo que venga en el query string.

---

## Rate limiting

| Endpoint | Límite |
|---|---|
| `/api/auth/login` | 5 intentos / 5 min por IP |
| Reportes con `formato=` | 10 / min por usuario |
| Resto | 120 / min por usuario |

---

## Swagger

Habilitado solo en `Development`, en `/swagger`. Cada endpoint declara sus
`ProducesResponseType` con el DTO real — un `IActionResult` sin anotar no pasa review.
