# Leccionario e Inspección — Escuela de Conducción Profesional (ISTPET)

Portal de **toma de asistencia** (leccionario) para la Escuela de Conducción del ISTPET.

- **Docentes** registran la asistencia de los alumnos de los paralelos asignados en su distributivo.
- **Inspectores** consultan y descargan reportes de asistencia por docente o por estudiante.

Código de sistema en RBAC: **`cplec`**.

---

## Stack

| Capa | Tecnología | Versión fijada |
|---|---|---|
| Frontend | Angular | 21.2.x (CLI 21.2.12) |
| Backend | ASP.NET Core Web API | **net8.0** |
| ORM | EF Core + Pomelo MySQL | 8.0.x |
| Base de datos | MySQL | **5.7.21** (`sigafi_es`) |
| Tests backend | MSTest + Moq + FluentAssertions | — |
| Tests frontend | Vitest | 4.x |

> **Sobre .NET 10:** en esta máquina solo está instalado el SDK `8.0.421`. El proyecto
> se fija en `net8.0`, que además es la versión que corre `BienestarInstitucional.Api`
> y permite compartir código de autenticación sin fricción. Ver
> [`docs/adr/ADR-002-target-framework-net8.md`](docs/adr/ADR-002-target-framework-net8.md).

---

## Estructura del repositorio

```text
leccionario_inspeccion/
├── src/
│   ├── Leccionario.Api/             # Web API (Clean Architecture por capas)
│   │   ├── Domain/Entities/         # PLANO — generado por EF Core Power Tools
│   │   ├── Application/            # Casos de uso, DTOs, interfaces
│   │   ├── Infrastructure/         # DbContext, repos, servicios externos
│   │   ├── Controllers/             # Endpoints HTTP
│   │   ├── Extensions/              # DI, JWT, CORS, Swagger
│   │   └── Middlewares/             # Excepciones, auditoría, headers, rate limit
│   ├── Leccionario.Tests/           # Unitarias + integración
│   └── Leccionario.sln
├── client/                          # Angular 21 — login, mis paralelos, pasar lista (M4)
├── database/
│   ├── migrations/                  # DDL versionado, replicable en producción (MySQL 5.7)
│   │   └── README.md                #   ↳ paso a paso para ejecutar y hacer rollback
│   ├── rollback/                    # Un rollback por cada migración
│   └── queries/                     # Consultas de verificación/diagnóstico
├── docs/                            # Documentación y lineamientos (leer antes de codear)
├── .githooks/                       # Hooks que bloquean commits/pushes que rompen reglas
└── scripts/                         # Utilidades de desarrollo
```

El estado y la secuencia de entrega están en [`docs/00-roadmap.md`](docs/00-roadmap.md).

---

## Documentación — orden de lectura

| # | Documento | Para qué |
|---|---|---|
| 00 | [Roadmap](docs/00-roadmap.md) | Estado actual, hitos, dependencias y próximos pasos |
| 01 | [Visión y alcance](docs/00-vision-alcance.md) | Qué se construye y qué **no** |
| 01 | [Arquitectura](docs/01-arquitectura.md) | Client–server, capas, reglas de dependencia |
| 02 | [Modelo de datos](docs/02-modelo-datos.md) | Tablas legacy reutilizadas y tablas nuevas `cplec_*` |
| 03 | [Autenticación y RBAC](docs/03-autenticacion-rbac.md) | Login, JWT, roles `cplec_inspector` / `cplec_docente` |
| 04 | [Contrato de API](docs/04-contrato-api.md) | Endpoints, DTOs, códigos de error |
| 05 | [Lineamientos backend](docs/05-lineamientos-backend.md) | Convenciones .NET, Clean Code, Power Tools |
| 06 | [Lineamientos frontend](docs/06-lineamientos-frontend.md) | Convenciones Angular 21, signals, guards |
| 07 | [Estrategia de pruebas](docs/07-pruebas.md) | Qué se testea, cobertura mínima, TDD |
| 08 | [Git Flow y protección de ramas](docs/08-git-flow.md) | Ramas, PRs, hooks, CI |
| 09 | [Base de datos y despliegue](docs/09-base-datos-despliegue.md) | Cómo se escriben y aplican los `.sql` |
| 10 | [Navegación del distributivo](docs/10-navegacion-distributivo.md) | **El camino de consulta**: de `asignaciones_profesores` a los alumnos |
| — | [ADRs](docs/adr/) | Decisiones de arquitectura con su justificación |

---

## Arranque rápido

```bash
# 1. Clonar y situarse en la rama personal
git clone <url> && cd leccionario_inspeccion
git checkout dv_jb

# 2. Activar los hooks de protección (OBLIGATORIO, una sola vez por clon)
./scripts/setup-hooks.sh

# 3. Backend
cp src/Leccionario.Api/appsettings.example.json src/Leccionario.Api/appsettings.Development.json
# editar credenciales reales (ver docs/03) — este archivo NUNCA se commitea
cd src && dotnet restore && dotnet test && dotnet run --project Leccionario.Api

# 4. Frontend
cd ../client
npm ci
npm test          # 24 tests
npm run lint      # ESLint + Angular ESLint
npm run build     # build de producción, sin warnings
npm start         # ng serve, con proxy /api → http://localhost:5093
```

---

## Reglas no negociables

1. **`main` y `develop` están protegidas.** No se commitea ni se pushea directo a ellas.
   Todo entra por Pull Request desde una rama personal, con tests en verde.
2. **Ningún secreto en el repositorio.** `appsettings.json` con credenciales reales está
   en `.gitignore`; se commitea únicamente `appsettings.example.json`.
3. **Todo cambio de esquema va como `.sql` versionado** en `database/migrations/`, con su
   rollback, compatible con **MySQL 5.7**. Nunca se usan migraciones EF contra `sigafi_es`.
4. **`Domain/Entities/` es código generado.** No se edita a mano — se regenera con
   EF Core Power Tools. La lógica va en `Application/`.
5. **Sin tests, no hay merge.**
