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
| 422 | `FUERA_DE_VENTANA` | La fecha cae fuera de `fecha_inicial .. fecha_fin` de la asignación |
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
    "idAsignacion": 23229,
    "idPeriodo": "OCC2025",
    "asignatura": "GEOGRAFÍA DEL ECUADOR",
    "tipoLicencia": "TIPO \"C\"",
    "jornada": "NOCTURNA",
    "modalidad": "PRESENCIAL",
    "paralelo": "C",
    "fechaInicial": "2025-10-06",
    "fechaFin": "2025-11-05",
    "totalAlumnos": 29,
    "sesionesRegistradas": 12,
    "ultimaSesion": "2025-10-28"
  }],
  "total": 4, "page": 1, "pageSize": 50
}
```

`tipoLicencia` viene de `cursos.nivel` y `jornada` de `secciones.seccion` — para la
carrera 6 eso es lo que significan. El cliente muestra la etiqueta, no los IDs.

Si se omite `idPeriodo`, el backend resuelve **un período por nivel: el más reciente**
(ver `GET /api/periodos/por-nivel` abajo) y devuelve las asignaciones del docente en
todos ellos. **No** se usa `periodos.activo`: casi todos lo tienen en 1, incluidos los
de 2022.

### `GET /api/periodos/por-nivel`

Roles: `cplec_docente` · `cplec_inspector`.

Devuelve **una fila por cada nivel (tipo de licencia) de la carrera 6**, con su período
más reciente. Los tipos de licencia corren en calendarios independientes: no existe "el
período activo" del sistema, existe uno por nivel.

```json
{
  "items": [
    { "idNivel": 35, "tipoLicencia": "TIPO \"C\"", "idPeriodo": "OCC2025",
      "detalle": "OCTUBRE 2025 - ABRIL 2026",
      "fechaInicial": "2025-10-04", "fechaFin": "2026-12-18",
      "vigencia": "VIGENTE", "asignaciones": 166, "docentes": 25 },
    { "idNivel": 37, "tipoLicencia": "TIPO \"E\"", "idPeriodo": "JUE2026",
      "detalle": "JULIO 2026 - ABRIL 2027",
      "fechaInicial": "2026-07-06", "fechaFin": "2026-07-31",
      "vigencia": "CERRADO", "asignaciones": 9, "docentes": 6 }
  ]
}
```

- `vigencia` ∈ `VIGENTE` · `FUTURO` · `CERRADO`, calculada contra la fecha del servidor.
- **El cliente debe mostrar `vigencia`.** "Más reciente" no es "vigente": medido el
  2026-08-06, solo el nivel 35 estaba en curso. Un docente que abra un período `CERRADO`
  tiene que verlo, no suponer que está pasando lista sobre el período actual.
- El desempate entre períodos que terminan el mismo día es
  `fecha_fin → fecha_inicial → idPeriodo`, determinista. Sin él, un nivel aparece
  duplicado (le pasa hoy al nivel 37).
- Para un docente, la respuesta se limita a los niveles en los que tiene distributivo.

Detalle e implementación en
[`10-navegacion-distributivo.md`](10-navegacion-distributivo.md) § Paso 5.

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
  "tema": "Señalización vertical y horizontal",
  "observacion": "Faltó el proyector; se usó pizarra",
  "numeroBloque": 1
}
```

- `tema` es **obligatorio** (máx. 250). Es el propósito del leccionario.
- `numeroBloque` es opcional y por defecto `1`. Solo se envía si hay dos clases de la
  misma asignación el mismo día.
- `fecha` se traduce internamente a `fechas_horarios.idFecha`. Si el día no existe en el
  calendario → `400 VALIDACION`.
- **La fecha debe caer dentro de `asignaciones_profesores.fecha_inicial .. fecha_fin`**
  de esa asignación → si no, `422 FUERA_DE_VENTANA` con el rango válido en `detalles`.
- `201` con la sesión creada y la nómina precargada en `presente`.
- Si ya existe esa (`idAsignacion`, `fecha`, `numeroBloque`) → `409 SESION_DUPLICADA`
  con el `idSesion` existente en `detalles`, para que el cliente redirija en vez de fallar.

No hay horas ni bloques horarios: el grano es el día. Ver
[`10-navegacion-distributivo.md`](10-navegacion-distributivo.md).

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
