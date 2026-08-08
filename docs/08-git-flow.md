# 08 — Git Flow y protección de ramas

---

## Ramas

| Rama | Vida | Quién escribe | Contenido |
|---|---|---|---|
| `main` | permanente | **nadie directo** | Lo que está en producción. Cada merge es una release etiquetada |
| `develop` | permanente | **nadie directo** | Integración. Siempre compila y pasa tests |
| `dv_<iniciales>` | permanente por persona | su dueño | Rama personal de trabajo |
| `feature/<slug>` | temporal | su autor | Trabajo grande que conviene aislar |
| `hotfix/<slug>` | temporal | su autor | Corrección urgente sobre `main` |
| `release/<version>` | temporal | responsable de release | Estabilización antes de `main` |

Ramas personales activas:

| Rama | Dispositivo / persona |
|---|---|
| `dev_jb` | Andrés (este equipo) |

Cada persona que se sume crea la suya (`dv_ml`, `dv_ac`, …) y la agrega a esta tabla en
su primer PR.

```
main ──────●────────────────────●──────────────▶  (producción, tags v1.0.0…)
            \                  /
develop ─────●────●────●──────●───────────────▶  (integración)
              \    \    \
               dev_jb  feature/reportes-excel
```

---

## Flujo diario

```bash
# 1. Partir siempre de develop actualizado
git checkout develop && git pull origin develop
git checkout dev_jb && git rebase develop

# 2. Trabajar y commitear
git add -p
git commit -m "feat(asistencia): registrar lista por sesion"

# 3. Antes de subir: verificar (el hook lo hace igual)
cd Backend && dotnet test && cd ../Frontend && npm test

# 4. Subir y abrir PR hacia develop
git push origin dev_jb
```

**`dev_jb` se rebasea sobre `develop`, no se mergea desde `develop`.** Mantiene el
historial lineal y hace los PR legibles.

---

## Mensajes de commit — Conventional Commits

```
<tipo>(<alcance>): <descripción en imperativo, minúscula, sin punto final>

[cuerpo opcional: el porqué, no el qué]

[Refs: #ticket]
```

Tipos: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`, `build`, `ci`, `revert`.
Alcances: `auth`, `asistencia`, `sesiones`, `reportes`, `rbac`, `db`, `frontend`,
`backend`, `docs`, `ci`.

```
feat(asistencia): registrar lista de una sesion de forma idempotente
fix(auth): validar activo despues de la credencial para no filtrar el padron
db(rbac): agregar sistema cplec y roles inspector/docente
docs(modelo): documentar por que no se reutiliza horario_detalle
```

El hook `commit-msg` rechaza lo que no cumpla el formato.

---

## Pull Requests

### Requisitos para poder mergear a `develop`

1. Al menos **1 aprobación** de otra persona.
2. **CI en verde**: `dotnet test`, `npm test`, `npm run build`, lint.
3. Sin conflictos; rama rebaseada sobre `develop`.
4. Descripción que responda: qué cambia, por qué, cómo se probó.
5. Si toca el esquema → migración **y** rollback en `database/`, y la tabla de estado
   de `database/README.md` actualizada.
6. Si agrega configuración → `appsettings.example.json` actualizado.
7. Sin secretos, sin `TODO` sin ticket, sin `Console.WriteLine` / `console.log`.

### Requisitos extra para `develop → main`

8. Aprobación del responsable técnico.
9. Migraciones probadas en un entorno con datos reales.
10. Tag de versión (`v1.0.0`) y notas de release.

---

## Protección de ramas

Hay **dos niveles**. El local es el que funciona hoy; el del servidor es el que
realmente no se puede saltar.

### Nivel 1 — Hooks locales (activos ya)

```bash
./scripts/setup-hooks.sh    # una vez por clon
```

Configura `core.hooksPath = .githooks`, con lo cual los hooks quedan **versionados**
(a diferencia de `.git/hooks`, que no se comparte).

| Hook | Qué bloquea |
|---|---|
| `pre-commit` | Commit directo sobre `main` o `develop` · secretos (cadenas de conexión, claves, tokens) · archivos > 5 MB · `appsettings.Development.json` |
| `commit-msg` | Mensajes que no cumplen Conventional Commits |
| `pre-push` | Push directo a `main`/`develop` · `dotnet test` en rojo · `npm test` en rojo · migración sin rollback |

**Limitación honesta:** un hook local se salta con `--no-verify` y no existe hasta que
alguien corre `setup-hooks.sh`. Es una red de seguridad contra el error, no contra la
intención. La protección real es el nivel 2.

### Nivel 2 — Protección en el servidor (pendiente de configurar)

Cuando el repositorio esté en GitHub/GitLab, configurar para `main` y `develop`:

- [ ] Prohibir push directo (incluidos administradores).
- [ ] Exigir Pull Request con ≥ 1 aprobación.
- [ ] Exigir que pasen los status checks: `backend-tests`, `frontend-tests`, `build`.
- [ ] Exigir la rama actualizada respecto de la base antes de mergear.
- [ ] Prohibir force-push y borrado de la rama.
- [ ] Descartar aprobaciones obsoletas al llegar commits nuevos.

El workflow de CI ya está en `.github/workflows/ci.yml`; solo falta activar las reglas
en la configuración del repositorio remoto. **Hasta que eso esté hecho, `main` y
`develop` están protegidas por convención y por hooks, no por el servidor.**

---

## Releases

```bash
git checkout develop && git pull
git checkout -b release/v1.0.0
# estabilizar: solo fixes, nada de features
git checkout main && git merge --no-ff release/v1.0.0
git tag -a v1.0.0 -m "Leccionario v1.0.0 — asistencia y reportes"
git push origin main --tags
git checkout develop && git merge --no-ff release/v1.0.0   # devolver los fixes
git branch -d release/v1.0.0
```

Versionado semántico: `MAJOR.MINOR.PATCH`.

## Hotfix

```bash
git checkout main && git pull
git checkout -b hotfix/asistencia-duplicada
# corregir + test que reproduce el bug
git checkout main   && git merge --no-ff hotfix/… && git tag -a v1.0.1 -m "…"
git checkout develop && git merge --no-ff hotfix/…          # nunca olvidar este paso
```

Un hotfix **siempre** lleva un test que falla antes del arreglo. Si no se puede
escribir ese test, no se entiende el bug todavía.

---

## Qué nunca se hace

- `git push --force` sobre `main` o `develop`.
- `git commit --no-verify` para esquivar un hook.
- Commitear `appsettings.Development.json`, `.env`, dumps de base o `node_modules/`.
- Mergear un PR propio sin revisión.
- Subir a `develop` sabiendo que un test está en rojo.
