# 09 — Base de datos y despliegue

Complementa [`database/README.md`](../database/README.md), que tiene las reglas de
escritura de los `.sql`. Este documento cubre el ciclo de vida: de desarrollo a
producción.

---

## Entornos

| Entorno | Base | Host | Quién aplica el DDL |
|---|---|---|---|
| Desarrollo | `sigafi_es` | `100.121.210.68:3307` (Tailscale) | cada desarrollador |
| Producción | `sigafi_es` | por definir con el cliente | responsable de BD, con backup previo |

> **Advertencia importante:** hoy desarrollo apunta a la **misma base con datos reales**
> que usa Bienestar. Contiene cédulas, nombres y correos de personas. Mientras siga así:
> no se ejecutan `DELETE`/`UPDATE` masivos sin `WHERE`, no se extraen dumps al repositorio
> y no se usan datos reales en los tests. La recomendación es levantar una réplica de
> desarrollo cuanto antes.

---

## Aplicar migraciones

```bash
# 0. Confirmar contra qué base se está trabajando
mysql -h <host> -P 3307 -u <user> -p -e "SELECT DATABASE(), VERSION(), @@hostname" sigafi_es

# 1. Backup — SIEMPRE, también en desarrollo
mysqldump -h <host> -P 3307 -u <user> -p --single-transaction --routines --triggers \
  sigafi_es > backup_sigafi_es_$(date +%Y%m%d_%H%M).sql

# 2. Aplicar en orden, deteniéndose al primer error
set -e
for f in database/migrations/*.sql; do
  echo ">>> $f"
  mysql -h <host> -P 3307 -u <user> -p sigafi_es < "$f"
done

# 3. Verificar
mysql -h <host> -P 3307 -u <user> -p sigafi_es < database/queries/verificar-esquema-cplec.sql

# 4. Marcar como aplicado en la tabla de estado de database/README.md
```

Sin cliente `mysql` local:

```bash
docker run --rm -i mysql:5.7 mysql -h <host> -P 3307 -u <user> -p<pass> sigafi_es < archivo.sql
```

---

## Orden de despliegue de una versión

El orden importa: el objetivo es que en ningún momento haya una API pidiendo columnas
que aún no existen.

```
1. Backup de la base
2. Aplicar migraciones (.sql)           ← el esquema va PRIMERO
3. Verificar el esquema
4. Desplegar el backend
5. Comprobar salud: GET /api/auth/me con un token de prueba
6. Desplegar el frontend
7. Prueba de humo: login docente → ver paralelos → pasar lista → cerrar sesión
```

Para que ese orden funcione, **toda migración debe ser retrocompatible** con la versión
del backend que ya está corriendo: agregar tablas y columnas nullable, sí; renombrar o
eliminar, no. Un cambio destructivo se parte en dos despliegues (agregar → migrar datos
→ desplegar código → eliminar en la versión siguiente).

---

## Rollback

```
1. Revertir el frontend a la versión anterior
2. Revertir el backend
3. Solo si el esquema es incompatible: aplicar los rollbacks .sql en orden INVERSO
   (003 → 002 → 001)
4. Si algo se corrompió: restaurar el backup
```

Los rollbacks de `cplec_asistencias` y `cplec_sesiones` son **destructivos**: borran la
asistencia registrada. Antes de correrlos en producción hay que extraer los datos
(el comando exacto está comentado en cada archivo de rollback).

---

## Configuración por entorno

Nada de secretos en el repositorio. En producción, variables de entorno:

```bash
ConnectionStrings__SigafiDb="Server=…;Port=3307;Database=sigafi_es;User=…;Password=…"
JWTSettings__Secret="…"
JWTSettings__Issuer="leccionario_conduccion"
JWTSettings__Audience="cplec"
SistemaCodigo="cplec"
AllowedOrigins__0="https://leccionario.istpet.edu.ec"
ASPNETCORE_ENVIRONMENT="Production"
```

El doble guion bajo (`__`) es el separador de secciones que entiende
`IConfiguration` en .NET.

---

## Checklist previo a producción

**Base de datos**

- [ ] Backup verificado (se puede restaurar, no solo se creó el archivo).
- [ ] Migraciones aplicadas y verificadas con `verificar-esquema-cplec.sql`.
- [ ] Tabla de estado de `database/README.md` actualizada.
- [ ] Roles `cplec_inspector` y `cplec_docente` asignados a personas reales.
- [ ] Al menos un docente y un inspector pueden iniciar sesión.

**Backend**

- [ ] `ASPNETCORE_ENVIRONMENT=Production` (apaga Swagger, activa HTTPS redirect).
- [ ] Secretos en variables de entorno, no en archivos.
- [ ] `AllowedOrigins` con el dominio real, sin `localhost` ni `*`.
- [ ] `SistemaCodigo = "cplec"`.
- [ ] Rate limiting activo.
- [ ] Logs sin PII: verificar que `idSigafi` sale enmascarado.

**Frontend**

- [ ] `environment.prod.ts` apunta a la API real por HTTPS.
- [ ] `npm run build --configuration production` sin warnings.
- [ ] Probado en móvil: pasar lista es el flujo crítico y se usa desde el teléfono.

**Seguridad**

- [ ] Un docente no puede acceder al paralelo de otro (probado en el entorno real).
- [ ] Un usuario sin rol `cplec_*` recibe `403`.
- [ ] Un token de otro sistema ISTPET (mismo secreto) es rechazado por
      `codigo_sistema`. **Esta prueba no es opcional** — ver
      [ADR-003](adr/ADR-003-autenticacion-local-sigafi.md).

---

## Monitoreo mínimo

- Errores `500` por endpoint (deberían ser cero).
- `401`/`403` anómalos: un pico puede ser un intento de acceso indebido o un rol mal
  asignado tras el despliegue.
- Latencia de `POST /api/sesiones/{id}/asistencias`: es la operación que el docente
  hace en clase; si tarda, no se usa el sistema.
- Sesiones en estado `borrador` con más de 72 h: indican docentes que no cerraron la
  lista. Es un dato de gestión para inspección, no un error técnico.
