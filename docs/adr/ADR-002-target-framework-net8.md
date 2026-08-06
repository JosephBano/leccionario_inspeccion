# ADR-002 — Target framework `net8.0`

- **Estado:** Aceptada
- **Fecha:** 2026-08-06
- **Autor:** dv_jb

## Contexto

El brief pedía ".NET la 8 o la 10", dejando la elección abierta.

Estado del entorno verificado:

```
$ dotnet --list-sdks
8.0.421 [/home/joeman/.dotnet/sdk]
```

Solo hay SDK 8. Además, `BienestarInstitucional.Api` —de donde se porta el mecanismo de
autenticación, RBAC y middlewares— es `net8.0`, con `Pomelo.EntityFrameworkCore.MySql
8.0.*` y `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.*`.

## Decisión

Fijar `<TargetFramework>net8.0</TargetFramework>` en la API y en el proyecto de tests.

## Razones

1. **Es el único SDK instalado.** Elegir net10 obliga a instalar el SDK en cada equipo
   del proyecto antes de escribir la primera línea.
2. **Paridad con el resto de la plataforma ISTPET.** Los cinco sistemas de
   `digitalizacion-istpet` corren sobre net8. Compartir versión permite copiar
   `AuthService`, `JwtTokenService`, `PasswordService` y los middlewares sin adaptarlos,
   y que una corrección de seguridad en uno se aplique igual en el otro.
3. **net8 es LTS.** Soporte hasta noviembre de 2026. Suficiente para la v1.
4. **Nada de lo que necesita el proyecto es exclusivo de net10.** Es un CRUD con JWT,
   EF Core y generación de Excel.

## Consecuencias

- No se dispone de las mejoras de rendimiento ni de las APIs nuevas de net9/net10.
  Ninguna hace falta hoy.
- **Antes de noviembre de 2026** hay que planificar el salto a la siguiente LTS. Como el
  proyecto no usa APIs exóticas, se espera un cambio de `TargetFramework` y de versiones
  de paquetes, sin migración de código.
- Si el cliente exige net10 explícitamente, revisar este ADR: la decisión cambia
  instalando el SDK y subiendo `TargetFramework` y los paquetes `8.0.*` → `10.0.*`.
  El código de autenticación portado desde Bienestar debería compilar sin cambios.
