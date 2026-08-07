# 07 — Estrategia de pruebas

**Sin tests no hay merge.** Los hooks y el CI lo hacen cumplir; este documento explica
qué se espera que esos tests digan.

---

## Backend — `Leccionario.Tests`

Mismo stack que `BienestarInstitucional.Tests`:

| Paquete | Versión |
|---|---|
| `MSTest.TestAdapter` / `MSTest.TestFramework` | `3.2.0` |
| `Moq` | `4.20.70` |
| `FluentAssertions` | `6.12.0` |
| `Microsoft.EntityFrameworkCore.InMemory` | `8.0.*` |
| `Microsoft.NET.Test.Sdk` | `17.9.0` |

### Tipos de prueba

| Tipo | Dependencias | Cuándo corre |
|---|---|---|
| **Unitaria** | Ninguna real: mocks + EF InMemory | Siempre. Es el grueso |
| **Integración** | MySQL real | Solo si hay `appsettings.IntegrationTests.json`; si no, se saltan |

Las de integración se **saltan solas** cuando no hay conexión configurada (el patrón de
`IntegrationTestConnection.cs` en Bienestar). Así el CI y quien no tenga VPN pueden
correr la suite completa sin fallos falsos.

> **Advertencia sobre EF InMemory:** no aplica constraints. Un `UNIQUE` violado **no**
> falla ahí. Las pruebas de unicidad (`uq_cplec_asistencias_sesion_matricula`) tienen
> que ser de integración contra MySQL, o no prueban nada.

### Convención de nombres

`Metodo_Escenario_ResultadoEsperado`

```csharp
[TestMethod]
public async Task RegistrarAsync_CuandoLaAsignacionNoEsDelDocente_LanzaDistributivoAjeno()
```

Cuerpo en tres bloques marcados: `// Arrange`, `// Act`, `// Assert`.

### Qué hay que testear, sí o sí

Estas son las pruebas que justifican la existencia de la suite:

**Autorización — la más importante**

- [ ] Docente con `idAsignacion` ajeno → `DistributivoAjenoException`.
- [ ] Docente con asignación propia pero `activo = 0` → también rechaza.
- [ ] El `idProfesor` sale del claim `sub`; si el body trae otro, **se ignora**.
- [ ] Inspector puede leer cualquier asignación.
- [ ] Inspector **no** puede crear una sesión (no tiene la operación `crear`).
- [ ] Token con `codigo_sistema` distinto de `cplec` → rechazado.

**Registro de asistencia**

- [ ] Reenviar la misma lista no duplica filas (idempotencia).
- [ ] `idMatricula` de otro paralelo → `MatriculaAjenaException` con los ids en detalles.
- [ ] Sesión `cerrada` → `SesionCerradaException`.
- [ ] Fuera de la ventana de 72 h → `FueraDePlazoException`.
- [ ] Cambiar un estado escribe exactamente una fila en el historial.
- [ ] **No** cambiar un estado no escribe historial.
- [ ] `minutosAtraso` con `estado != 'atraso'` → error de validación.
- [ ] Si falla el historial, la transacción revierte también las asistencias.

**Sesiones**

- [ ] Sesión duplicada (misma asignación/fecha/bloque) → `409` con el `idSesion` existente.
- [ ] Fecha inexistente en `fechas_horarios` → `400`.
- [ ] Reabrir siendo docente → `403`.

**Autenticación** (portadas desde Bienestar, valen igual acá)

- [ ] Usuario inexistente y contraseña incorrecta devuelven **el mismo** mensaje.
- [ ] Cuenta inactiva con credencial correcta → `CUENTA_INACTIVA`, y **no** se persiste
      ninguna migración de contraseña.
- [ ] `profesores.esReal = 0` no puede iniciar sesión.
- [ ] Un alumno no puede iniciar sesión en `cplec`.
- [ ] Usuario sin rol `cplec_*` → `SIN_ACCESO_SISTEMA`.
- [ ] El hash centinela es distinto en cada llamada.
- [ ] Reuso de refresh token → revoca la familia completa.

**Consultas del distributivo**

- [ ] El join de paralelo tolera `paralelo` con espacios (`char(1)` vs `varchar(10)`).
- [ ] Los alumnos retirados no aparecen en la nómina.

### Cobertura

Mínimos exigidos en `Application/`:

| Área | Mínimo |
|---|---|
| `Application/**/Services` | **80 %** |
| Guards de autorización | **100 %** |
| `Controllers`, `Extensions`, `Domain/Entities` | excluidos |

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura \
            /p:Exclude="[*]Leccionario.Api.Domain.Entities.*"
```

La cobertura es un piso, no un objetivo. Un test que solo ejecuta líneas sin afirmar
nada útil es peor que no tenerlo: da confianza falsa.

---

## Frontend — Vitest

```bash
npm test              # una pasada
npm run test:watch
npm run test:coverage
```

### Qué se testea

| Sí | No |
|---|---|
| Servicios con lógica (transformaciones, cálculo de totales) | Que Angular renderice un `@if` |
| Guards (`authGuard`, `roleGuard`) | Estilos |
| Interceptor de auth (refresh, encolado, expiración) | Componentes puramente presentacionales |
| Componentes con lógica propia (pasar lista) | Getters triviales |
| Mapeo de errores de la API por `codigo` | — |

Mínimo **70 %** en `core/` y en los servicios de features. HTTP siempre mockeado con
`HttpTestingController`; ningún test toca la red.

### Casos obligatorios en la pantalla de pasar lista

- [ ] Arranca con todos en `presente`.
- [ ] El ciclo de estados al tocar es el esperado.
- [ ] Al fallar el guardado, el estado local **no** se pierde y se puede reintentar.
- [ ] Salir con cambios pendientes pide confirmación.

---

## Datos de prueba

**Prohibido usar datos personales reales en los tests.** `sigafi_es` contiene cédulas y
nombres de personas. Los fixtures usan cédulas ficticias (`0000000001`) y nombres
inventados.

Builders en `Leccionario.Tests/Builders/`:

```csharp
var asignacion = new AsignacionBuilder()
    .DelProfesor("0000000001")
    .EnPeriodo("SEE2023")
    .ConParalelo("A")
    .Build();
```

Evita 30 líneas de setup por test y hace evidente qué distingue a cada caso.

---

## Antes de abrir un PR

```bash
cd src    && dotnet test
cd client && npm test && npm run lint && npm run build   # cuando client/ exista (M4)
```

El hook `pre-push` corre lo mismo. No se sube nada en rojo, y **no se usa
`--no-verify`** para saltarlo: si el hook estorba, se arregla el hook.
