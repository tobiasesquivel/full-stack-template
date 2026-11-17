# Progreso del plan de evolución

Registro vivo del [plan](plan.md). El agente lo actualiza al cerrar cada paso.

## Paso actual

**Fase 5, paso 16: health por dependencia.** Estado: pendiente. El paso 15 cerró el 2026-11-17 en modo autónomo (PR #69) (sección "Modo autónomo" de `AGENTS.md`); se apoya en [investigacion/paso-15-correlacion-log-traza.md](investigacion/paso-15-correlacion-log-traza.md). No hubo elección de herramienta: el control es un script de Node sobre los archivos que el `file` exporter del collector ya escribía; las elecciones son las recomendaciones de la investigación, anotadas en `docs/decisions.tsv`. El paso 14 cerró el 2026-11-17 en modo autónomo (PR #68) (sección "Modo autónomo" de `AGENTS.md`); se apoya en [investigacion/paso-14-metricas-negocio.md](investigacion/paso-14-metricas-negocio.md). No hubo elección de herramienta: `System.Diagnostics.Metrics` viene con .NET y OpenTelemetry ya estaba; las elecciones son las recomendaciones de la investigación, anotadas en `docs/decisions.tsv`. El paso 13 cerró el 2026-11-17 en modo autónomo (PR #67) (sección "Modo autónomo" de `AGENTS.md`); se apoya en [investigacion/paso-13-cabeceras-seguridad.md](investigacion/paso-13-cabeceras-seguridad.md) y [investigacion/paso-13-escaneo-continuo.md](investigacion/paso-13-escaneo-continuo.md). Tobi eligió el 2026-11-17 las herramientas (regla 5): NetEscapades.AspNetCore.SecurityHeaders para las cabeceras, gitleaks como binario para los secretos, NuGetAudit y `npm audit` para las dependencias, y no prender las alertas de Dependabot por ahora; las demás elecciones están en `docs/decisions.tsv`. El paso 12 cerró el 2026-11-16 en modo autónomo (PR #66) (sección "Modo autónomo" de `AGENTS.md`); se apoya en [investigacion/paso-12-rate-limiting.md](investigacion/paso-12-rate-limiting.md). No hubo elección de herramienta: el limiter viene con ASP.NET Core y el repo ya lo usaba; las elecciones son las recomendaciones de la investigación, anotadas en `docs/decisions.tsv`. El paso 11 cerró el 2026-11-16 en modo autónomo (PR #65) (sección "Modo autónomo" de `AGENTS.md`); se apoya en [investigacion/paso-11-permisos-por-request.md](investigacion/paso-11-permisos-por-request.md). No hubo elección de herramienta: todas las elecciones son las recomendaciones de la investigación, anotadas en `docs/decisions.tsv`. El paso 10 cerró el 2026-11-16 en modo autónomo (PR #64) (sección "Modo autónomo" de `AGENTS.md`); se apoya en [investigacion/paso-10-auditoria-cambios.md](investigacion/paso-10-auditoria-cambios.md). Tobi eligió el 2026-11-16 el interceptor propio, sin librería (regla 5); las demás elecciones son las recomendaciones de la investigación, anotadas en `docs/decisions.tsv`. El paso 9 cerró el 2026-11-16 y Tobi mergeó el PR #62; se apoya en [investigacion/paso-09-idempotencia-escritura.md](investigacion/paso-09-idempotencia-escritura.md). Tobi eligió el 2026-11-16: `Idempotency-Key` opcional con UUID, 422 `Idempotency.KeyReused` para la misma clave con otro cuerpo, DUNE0016 dentro del paso, y las otras seis recomendaciones (el pedido simultáneo espera, solo se guardan respuestas 2xx, `Idempotent-Replayed: true`, retención de 24 h sin borrado con cascada al borrar la cuenta, filtro propio sin librería). El paso 8 cerró el 2026-11-16 (PR #61); se apoya en [investigacion/paso-08-concurrencia-optimista.md](investigacion/paso-08-concurrencia-optimista.md). Tobi eligió el 2026-11-16 las seis recomendaciones de la investigación: `xmin` como versión, campo `version` en el JSON con 409 `Task.Conflict`, 400 si falta la versión, reintento tolerante, mensaje y recarga ante el 409, y `DeleteTask` fuera del paso. El paso 7 cerró el 2026-11-04 (PR #60); se apoya en [investigacion/paso-07-paginacion-keyset.md](investigacion/paso-07-paginacion-keyset.md). Tobi decidió el 2026-11-03 cambiar v1 en el lugar, dejar el índice para el paso 18, quitar `totalCount` y usar `FakeTimeProvider`. El paso 6.3 cerró el 2026-11-03 (PR #58, apilado sobre el #57). El paso 6.2 cerró el 2026-11-03 (PR #57). El paso 6.1 cerró el 2026-11-03; Tobi mergeó los PRs #53 y #54. El paso 6 cerró el 2026-10-18 y Tobi mergeó el PR #52. El paso 5.1 cerró el 2026-10-18. Los pasos 3, 4 y 5 y las reglas 11 y 12 entraron a `main` con el PR #47 el 2026-10-18; Tobi pidió integrar todo y borrar las demás ramas. El paso 2 cerró el 2026-10-17 y Tobi lo mergeó. La Fase 0 cerró con el paso 0.28 (reglas de la sesión en Dune). Tobi eligió el BFF del paso 0.26 y el volumen del 0.27 el 2026-10-17. Los pasos 0.10 a 0.28 cerraron. Tobi pidió el 2026-10-17 separar la vista de los componentes (0.10), un archivo por tipo (0.11), Tailwind (0.12), ordenar los archivos sueltos de cada feature (0.13), investigar y aplicar el manejo de sesión en Angular (0.14 a 0.16, [informe-sesion-angular.md](informe-sesion-angular.md)) y aplicar lo que no necesita decisión de herramienta de [informe-componentes-angular.md](informe-componentes-angular.md) y [informe-frontend-para-agentes.md](informe-frontend-para-agentes.md) (0.17 a 0.20). Después eligió Vitest, Prettier, Playwright Test, el MCP del CLI y los ajustes visuales (0.21 a 0.25). Los pasos 0.6 a 0.28 esperan el merge de Tobi. El Stop hook esperaba hasta después del paso 2: ya se puede retomar.

La Fase 0 va antes del paso 2. El paso 1 (línea base) ya cerró y sirve de referencia para la Fase 0.

## Estado por paso

Estados: `pendiente`, `en curso`, `cerrado`, `bloqueado (⏸ decisión de Tobi)`.

| Paso | Título | Estado | Evidencia |
|---|---|---|---|
| 0.0 | Inicialización concurrente de la base | cerrado | `artifacts/verify-template/fase0-00-seed-race/`, `artifacts/verify-template/fase0-00-runtime/` |
| 0.1 | Docs y reglas de trabajo | cerrado | `artifacts/verify-template/fase0-01-docs/` |
| 0.2 | Arreglos de Dune | cerrado | `artifacts/verify-template/fase0-02-dune/` |
| 0.3 | Reparto del código | cerrado | `artifacts/verify-template/fase0-03-reparto/`, `artifacts/verify-template/fase0-03-runtime/` |
| 0.4 | Slices | cerrado | `artifacts/verify-template/fase0-04-slices/`, `artifacts/verify-template/fase0-04-runtime/` |
| 0.5 | Límite entre Auth y Users | cerrado | `artifacts/verify-template/fase0-05-auth-users/`, `artifacts/verify-template/fase0-05-runtime/` |
| 0.6 | Frontend por feature | cerrado | `artifacts/verify-template/fase0-06-frontend/`, `artifacts/verify-template/fase0-06-runtime/` (fallo), `artifacts/verify-template/fase0-06-runtime-2/` |
| 0.7 | Servicios y nombres de Angular v22 | cerrado | `artifacts/verify-template/fase0-07-services/`, `artifacts/verify-template/fase0-07-runtime/` |
| 0.8 | Signal Forms | cerrado | `artifacts/verify-template/fase0-08-signal-forms/`, `artifacts/verify-template/fase0-08-runtime/` |
| 0.9 | Arranque con `provideAppInitializer()` | cerrado | `artifacts/verify-template/fase0-09-app-initializer/`, `artifacts/verify-template/fase0-09-runtime/` |
| 0.10 | Componentes en archivos separados | cerrado | `artifacts/verify-template/fase0-10-component-files/`, `artifacts/verify-template/fase0-10-runtime/` |
| 0.11 | Un archivo, un tipo | cerrado | `artifacts/verify-template/fase0-11-one-type-per-file/`, `artifacts/verify-template/fase0-11-runtime/` |
| 0.12 | Estilos con Tailwind | cerrado | `artifacts/verify-template/fase0-12-tailwind/`, `artifacts/verify-template/fase0-12-runtime/` |
| 0.13 | Carpetas por propósito en cada feature del frontend | cerrado | `artifacts/verify-template/fase0-13-feature-folders/`, `artifacts/verify-template/fase0-13-runtime/` |
| 0.14 | Forma de la sesión | cerrado | `artifacts/verify-template/fase0-14-session-shape/`, `artifacts/verify-template/fase0-14-runtime/` |
| 0.15 | Interceptor y vuelta a la página pedida | cerrado | `artifacts/verify-template/fase0-15-session-interceptor/`, `artifacts/verify-template/fase0-15-runtime-red/` (rojo), `artifacts/verify-template/fase0-15-runtime/` |
| 0.16 | Varias pestañas | cerrado | `artifacts/verify-template/fase0-16-session-tabs/`, `artifacts/verify-template/fase0-16-runtime-red/` (rojo), `artifacts/verify-template/fase0-16-runtime/` |
| 0.17 | El CLI genera código que pasa el gate | cerrado | `artifacts/verify-template/fase0-17-cli-defaults/`, `artifacts/verify-template/fase0-17-runtime/` |
| 0.18 | Componente de ruta y componente de presentación: `TaskPager` | cerrado | `artifacts/verify-template/fase0-18-task-pager/`, `artifacts/verify-template/fase0-18-runtime/` |
| 0.19 | Estado local en el hijo: `TaskRow` | cerrado | `artifacts/verify-template/fase0-19-task-row/`, `artifacts/verify-template/fase0-19-runtime-baseline/` (línea base), `artifacts/verify-template/fase0-19-runtime/` |
| 0.20 | Diálogo de presentación: `TaskDeleteDialog` | cerrado | `artifacts/verify-template/fase0-20-task-delete-dialog/`, `artifacts/verify-template/fase0-19-runtime-baseline/` (línea base), `artifacts/verify-template/fase0-20-runtime/` |
| 0.21 | Diálogo centrado y error bajo su campo | cerrado | `artifacts/verify-template/fase0-21-dialog-and-edit-error/`, `artifacts/verify-template/fase0-21-runtime-red/` (rojo), `artifacts/verify-template/fase0-21-runtime/` (fallo del test), `artifacts/verify-template/fase0-21-runtime-2/` |
| 0.22 | Primera suite de Vitest | cerrado | `artifacts/verify-template/fase0-22-vitest/` |
| 0.23 | Formato verificado con Prettier | cerrado | `artifacts/verify-template/fase0-23-prettier-check/`, `artifacts/verify-template/fase0-23-runtime/` |
| 0.24 | E2E con Playwright Test | cerrado | `artifacts/verify-template/fase0-24-playwright-e2e/`, `artifacts/verify-template/fase0-24-e2e-try1/` (fallo de red local), `artifacts/verify-template/fase0-24-e2e-red/` (rojo), `artifacts/verify-template/fase0-24-e2e/` |
| 0.25 | MCP del CLI de Angular, solo lectura | cerrado | `artifacts/verify-template/fase0-25-angular-mcp/` |
| 0.26 | Sesión en el servidor (BFF) | cerrado | `artifacts/verify-template/fase0-26-backend-red/` (rojo), `artifacts/verify-template/fase0-26-backend/`, `artifacts/verify-template/fase0-26-gates/`, `artifacts/verify-template/fase0-26-runtime/`, `artifacts/verify-template/fase0-26-e2e/`, `artifacts/verify-template/fase0-26-browser/` |
| 0.27 | Claves de Data Protection en un volumen | cerrado | `artifacts/verify-template/fase0-27-backend-red/` (rojo), `artifacts/verify-template/fase0-27-compose-red/` (rojo), `artifacts/verify-template/fase0-27-compose/`, `artifacts/verify-template/fase0-27-runtime/`, `artifacts/verify-template/fase0-27-gates/` |
| 0.28 | Reglas de la sesión en Dune | cerrado | `artifacts/verify-template/fase0-28-rules-red/` (rojo), `artifacts/verify-template/fase0-28-gates/` |
| 1 | Línea base: qué funciona hoy, con evidencia | cerrado | [linea-base.md](linea-base.md); `artifacts/verify-template/baseline-20261016/` |
| 2 | Un solo comando de verificación local | cerrado | `artifacts/verify-template/fase1-02-check-all/`, `artifacts/verify-template/fase1-02-check-all-red/` (rojo), `artifacts/verify-template/fase1-02-loose/`, `artifacts/verify-template/fase1-02-loose-runtime/`, `artifacts/verify-template/fase1-02-compose/` (NOT_RUN por puertos) |
| 3 | CI verde de verdad | cerrado | `artifacts/verify-template/fase1-03-ci/` (runs `39091828498` verde y `39092222604` rojo, artifact `otel-export` descargado) |
| 4 | Versionado de API declarado | cerrado | `artifacts/verify-template/fase2-04-characterization/`, `fase2-04-red/` (rojo), `fase2-04-backend/`, `fase2-04-metadata-red/` (rojo), `fase2-04-rule-red/` (rojo), `fase2-04-check-all/` |
| 5 | Validación de configuración al arranque | cerrado | `artifacts/verify-template/fase2-05-config/` (rojo y manual), `fase2-05-check-all/` (sin e2e) |
| 5.1 | Inicialización de la base como hosted service | cerrado | `artifacts/verify-template/fase2-05-1-red/` (rojo), `fase2-05-1-check-all/` (sin e2e) |
| 5.2 | Las herramientas de diseño no registran un arranque fallido | cerrado | `artifacts/verify-template/fase2-05-2/`, `fase2-05-2-check-all/` (sin e2e) |
| 5.3 | La configuración se lee solo con options validadas | cerrado | `artifacts/verify-template/fase2-05-3/` (collector), `fase2-05-3-check-all/` (sin e2e) |
| 5.4 | Regla de Dune para la lectura de configuración | cerrado | `artifacts/verify-template/fase2-05-4-rule-red/` (rojo), `fase2-05-4-check-all/` (sin e2e) |
| 6 | Catálogo de errores estable | cerrado | `artifacts/verify-template/fase2-06-red/` (rojo), `fase2-06-log-red/` (rojo del log), `fase2-06-green/`, `fase2-06-check-all/` (sin e2e; Tobi decidió no correr las e2e del paso) |
| 6.1 | Todo error sin cuerpo sale como ProblemDetails | cerrado | `artifacts/verify-template/fase2-06-1-red/` (rojo), `fase2-06-1-green/`, `fase2-06-1-check-all/` (FAIL por restos de la e2e cortada), `fase2-06-1-check-all-2/` (sin e2e), `fase2-06-1-cierre-2/` (sin e2e, quita un `using` que sobra en `ApiResults.cs`) |
| 6.2 | Regla de Dune para el catálogo de errores (DUNE0014) | cerrado | `artifacts/verify-template/fase2-06-2-rule-red/` (rojo de los tests y del backend de antes del paso 6), `fase2-06-2-rule-green/`, `fase2-06-2-check-all/` (FAIL: Docker apagado), `fase2-06-2-check-all-2/` (sin e2e) |
| 6.3 | Convención de nombres para los contratos entre features (DUNE0015) | cerrado | `artifacts/verify-template/fase2-06-3-rule-red/` (rojo de los tests y del backend de antes del cambio), `fase2-06-3-rule-green/`, `fase2-06-3-check-all/` (sin e2e) |
| 7 | Paginación por keyset | cerrado | `artifacts/verify-template/fase2-07-red/` (rojo de los tests y de Vitest), `fase2-07-green/`, `fase2-07-check-all/` (FAIL: faltaba el navegador de Playwright), `fase2-07-check-all-3/` y `-4/` (FAIL por restos de la corrida 2 cortada: trazas a medias y un volumen de PostgreSQL con otra contraseña), `fase2-07-check-all-5/` (con e2e) |
| 8 | Concurrencia optimista en `TaskItem` | cerrado | `artifacts/verify-template/fase2-08-red/` (rojo: 2 de 3 tests de `UpdateTaskTests`), `fase2-08-green/` (backend, migración generada y aplicada sin DDL), `fase2-08-check-all/` (sin e2e), `fase2-08-check-all-e2e/` (FAIL: la e2e nueva tomaba la primera fila, que otro test ya había marcado), `fase2-08-check-all-e2e-2/` (con e2e) |
| 9 | Idempotencia de escritura | cerrado | `artifacts/verify-template/fase3-09-red/` (rojo: 7 de 12 casos de `CreateTaskTests`), `fase3-09-green/` (backend 90/90, migración generada por CLI, aplicada y sin editar), `fase3-09-rule-red/` (rojo de DUNE0016 sobre el `CreateTaskEndpoint` de antes del paso), `fase3-09-check-all/` (sin e2e), `fase3-09-check-all-e2e/` (con e2e: 18 etapas PASS, .NET 178/178, Vitest 20/20, e2e 35/35) |
| 10 | Auditoría de cambios | cerrado | `artifacts/verify-template/fase3-10-red/` (rojo: 6 de 6 casos de `AuditTrailTests`, `42P01` sin la tabla), `fase3-10-red-table/` (rojo: 6 de 6 con la tabla y sin interceptor, cero filas), `fase3-10-green/` (6/6, suite 184/184, migración generada por CLI y sin cambios pendientes), `fase3-10-rule/` (Dune 90/90 y la violación plantada en `DeleteTask` rompe el build con DUNE0017), `fase3-10-check-all/` (sin e2e) |
| 11 | Permisos memorizados por request | cerrado | `artifacts/verify-template/fase4-11-red/` (rojo: 3 de 3 casos de `PermissionMemoTests`), `fase4-11-green/` (suite de producto 99/99), `fase4-11-rule/` (rojo: 7 de 32 tests del gate con el `check-structure.mjs` de antes; verde 32/32; `AddMemoryCache()` plantado rompe el gate), `fase4-11-check-all/` (sin e2e) |
| 12 | Rate limiting por usuario y por tipo de endpoint | cerrado | `artifacts/verify-template/fase4-12-red/` (rojo: 8 casos de .NET y 1 de Vitest), `fase4-12-green/` (34/34 de las clases tocadas; el orden de middlewares de antes hace fallar 3 casos), `fase4-12-rule/` (rojo: 4 de 37 tests del gate con el `check-structure.mjs` de antes; verde 37/37; `DisableRateLimiting` plantado en Tasks rompe el gate), `fase4-12-check-all/` (sin e2e), `fase4-12-check-all-e2e/` (con e2e: 18 etapas PASS, .NET 197/197, Vitest 21/21, e2e 35/35) |
| 13 | Cabeceras de seguridad y escaneo continuo | cerrado | `artifacts/verify-template/fase4-13-red/` (rojo: 9 de 10 casos de `SecurityHeadersTests`), `fase4-13-green/` (10/10; con el middleware detrás del limiter falla el 429), `fase4-13-rule/` (gitleaks sin allowlist: 2 hallazgos; `check-secrets.test.mjs` 6/6; rojo del caso sin commitear sin el arreglo de CRLF), `fase4-13-deps/` (npm audit 1 con el lockfile de antes y 0 con el nuevo; NuGet 0 hoy y 1 con `Newtonsoft.Json` 12.0.1), `fase4-13-plant/` (CI rojo con la clave plantada, run `44010593133`), `fase4-13-check-all/` (FAIL: comentarios en `ci.yml`), `fase4-13-check-all-2/` (sin e2e), `fase4-13-check-all-e2e/` (con e2e: 22 etapas PASS, .NET 207/207, Vitest 21/21, e2e 35/35) |
| 14 | Métricas de negocio reales | cerrado | `artifacts/verify-template/paso14-rojo/` (rojo: los 3 tests de métricas sin medición; `MeterConstructionBanTests` sin la regla), `paso14-verde/` (3/3 y 2/2), `paso14-check1/` (21 etapas PASS sin e2e, .NET 212/212), `paso14-compose/` (smoke `--compose` PASS con las 3 métricas en el export OTLP), `paso14-compose-rojo/` (sin `AddMeter` el smoke falla en el contador de tareas) |
| 15 | Correlación log↔traza automatizada | cerrado | `artifacts/verify-template/paso15-rojo/` (rojo: 11 de 11 pruebas del control sin el script), `paso15-verde/` (11/11 y el export real del paso 14 pasa), `paso15-compose/` (smoke `--compose` PASS y control PASS sobre el export completo), `paso15-compose-rojo/` (sampler al 50 %: smoke PASS, la aserción vieja PASS, control FAIL con 29 logs colgados), `paso15-check1/` (22 etapas PASS sin e2e), `paso15-revision/` (arreglos de la revisión: rojo y verde del caso de línea cortada, mutaciones del lector), `paso15-check2/` (sin e2e, después de la revisión) |
| 16 | Health por dependencia | pendiente | |
| 17 | SLOs y runbooks | pendiente | |
| 18 | Índices medidos, no supuestos | pendiente | |
| 19 | Migraciones expand/contract ensayadas | pendiente | |
| 20 | Lectura dedicada por módulo (CQRS liviano) | pendiente | |
| 21 | Contrato público por feature, y gate que lo haga cumplir | pendiente (gran parte absorbida por 0.5) | |
| 22 | Un `DbContext` por feature | pendiente | |
| 23 | Extraer un servicio con gateway adelante | pendiente | |
| 24 | Eventos de dominio + outbox transaccional, sin broker | pendiente | |
| 25 | Broker real con consumidor idempotente y DLQ | pendiente | |
| 26 | Saga coreografiada (3 pasos con compensación) | pendiente | |
| 27 | Saga orquestada con máquina de estados persistida | pendiente | |
| 28 | Durable execution para un proceso largo con auditoría | pendiente | |
| 29 | Primer cliente HTTP saliente con resiliencia | pendiente | |
| 30 | Pasarela de pago detrás de un puerto propio | pendiente | |
| 31 | Máquina de estados de pago + conciliación programada | pendiente | |
| 32 | Integración por archivo (SFTP o CSV) con cuarentena | pendiente | |
| 33 | Caché distribuida con invalidación medida | pendiente | |
| 34 | Cadena de suministro | pendiente | |
| 35 | Despliegue progresivo con rollback probado | pendiente | |
| 36 | Gobernanza final | pendiente | |

## Pendientes fuera de alcance

Cosas vistas durante un paso que no le corresponden. Cada una espera su paso.

- El build Release muestra 218 warnings de analyzers (CA2007, CA1707, RS2008, RS1036, CS8602 y otros). Nadie decidió si cada uno se corrige o se baja a `suggestion`. Sin paso asignado: proponer uno.
- `docs/evolucion/informe-vsa-deep-modules.md` §2.1 propone `AppIdentityUser` en `Auth/Domain`. Choca con `architecture.md` (Domain sin ASP.NET). Desde el paso 0.5, Users ya no lo usa; sigue en `Auth/Contracts` solo porque `AppDbContext` hereda de `IdentityDbContext<AppIdentityUser>`. Paso 22.
- Las dos FKs entre features (`UserProfiles → AspNetUsers`, `TaskItems → UserProfiles`) están en `AppDbContext.ConfigureCrossFeatureRelationships`. Con un `DbContext` por feature hay que elegir: quitarlas y reemplazar la cascada por un evento (Grzybek, Wolverine) o dejarlas solo en SQL (Jovanović). Paso 22.
- `RegisterUser` (Auth) y `UserProfiles` (Users) usan el mismo `AppDbContext` del request: la atomicidad del registro depende de eso. Con contextos separados hace falta compartir conexión y transacción, o un outbox. Pasos 22 y 24.
- Desde el paso 12 la política `auth` es por IP, pero en `verify-template` el proxy de `ng serve` y el smoke llegan a la API desde `127.0.0.1`: comparten el balde de 20 por minuto. Un recorrido de navegador seguido del smoke da 429. Por eso siguen la espera de 65 s de `check-all.mjs` y `waitForAuthRoom()`. Salidas: un límite de auth alto para `verify-template` por variable de entorno (`AuthRateLimiting__PermitLimit`), o forwarded headers. Va con el pendiente de e2e en CI. Sin paso asignado.
- Hay una carpeta vacía y sin versionar, `backend/Features/Health/Test/`, de antes de este paso. Git la ignora. Borrarla es decisión de Tobi.
- `backend/AGENTS.md` y `backend/docs/agents/` describen un issue tracker en `.scratch/`, que `.gitignore` excluye: los issues nunca se versionan.
- `Profile` vuelve a pedir `/users/me` al abrirse, aunque `Session` ya tiene el usuario. Podría leerlo con `httpResource()`. Es de la feature `users`, fuera del paso 0.14.
- Al restaurar, un error de red (`status 0`) deja la sesión `closed`, igual que un 401. Distinguir "sin conexión" de "sin sesión" queda pendiente.
- Una regla que limite `HttpClient` a los archivos de `api/` haría ejecutable la división del paso 0.13 (`api/` hace el HTTP). Hoy FRONT002 solo lo prohíbe en componentes. Sin paso asignado.

- FRONT006 solo mira el import de `@angular/forms`. Un input con `[value]`, `(input)` y `$any($event.target)`, como el que tenía la edición en línea antes del paso 0.19, sigue pasando el gate. Sin paso asignado.
- Las claves de Data Protection se guardan sin cifrar en el volumen (`No XML encryptor configured`); el archivo es del usuario `app` con permisos 600. Cifrarlas en reposo (`ProtectKeysWithCertificate` o similar) pide decidir dónde vive ese certificado. Con varias instancias, el volumen local no alcanza: las claves tendrían que vivir en PostgreSQL (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, un paquete nuevo) o en un volumen compartido. Tobi decidió el 2026-10-18 no cifrarlas por ahora (ver `docs/decisions.tsv`). Se revisa si hay varias instancias o un disco compartido: encaja con el paso 13 o el 35.
- DUNE0011 reconoce un `*Endpoint.Map` por el nombre del tipo y su único parámetro: una slice que mapea desde un método con otro nombre queda fuera de la regla. Además, una slice que crea un subgrupo sobre el `IEndpointRouteBuilder` que recibe (`tasks.MapGroup("/{id}/comments")`) da un falso positivo, porque para el analyzer ese parámetro puede ser el host. Hoy ninguna slice lo hace. Sin paso asignado.
- Ninguna regla de Dune verifica que `Program.cs` registre `UseAuthAntiforgery`. Si se borra, fallan `IssueCsrfTokenTests` y `smoke-api.mjs`. Nadie probó qué pasa si queda antes de `UseAuthentication`. Sin paso asignado.
- Desde el paso 2 de la Fase 1, `smoke-api.mjs` espera la readiness antes de su primera request. Falta verlo contra un `docker compose up --wait` recién creado: en esta máquina no corrió, porque otro stack ocupa los puertos (ver abajo). En CI la carrera es real: en el run `39091828498` el primer `curl` a `/health/ready` dio `Connection reset by peer` y el segundo, 5 segundos después, 200. CI sigue esperando con `curl` antes del smoke: el paso 3 no tocó `ci.yml` porque ningún job falla, y ese loop además vuelca `docker compose logs` si la API no levanta. Sin paso asignado.
- Las filas de `Sessions` vencidas o abandonadas se borran solo cuando su cookie vuelve. Falta una limpieza periódica. Sin paso asignado.
- Abrir sesión con una sesión ya abierta en el mismo navegador deja la fila anterior hasta que vence por inactividad. `OpenSession` podría cerrarla. Sin paso asignado.
- CI no corre las e2e. El job `compose` necesitaría `npm ci` del frontend, un navegador (`npx playwright install chromium --with-deps`) y 65 segundos de espera después del smoke por el limiter de `/auth`; suma unos 7 minutos. Tobi decidió el 2026-10-18 que no entra en el paso 3: es un gate nuevo y pide su propio paso. Sin paso asignado.
- El generador de `[OptionsValidator]` (SDK 10.0.401) ignora `ErrorMessage` en `[Range]`: su copia del atributo devuelve siempre "The field {0} must be between {1} and {2}.". Por eso los mensajes de `SessionOptions` nombran el tipo y la propiedad, no la clave `Session:...`. Es el bug [dotnet/runtime#123137](https://github.com/dotnet/runtime/issues/123137); el arreglo está en el PR [#135927](https://github.com/dotnet/runtime/pull/135927).
- CI no frena un merge. `main` no tiene protección de rama: en un repo privado del plan gratis, GitHub responde 403 a la API de protección y de rulesets. Tobi decidió el 2026-10-18 dejarlo así: revisa los checks antes de mergear. Se revisa si el repo pasa a público o a GitHub Pro.
- `informe-dotnet-empresarial.md:110` recomienda `Asp.Versioning.Mvc` / `.ApiExplorer`, que son para controllers. El paso 4 usa `Asp.Versioning.Http` (Minimal APIs). El informe no se corrigió. Sin paso asignado.
- La librería acepta `/api/v1.0/...` como alias de `/api/v1/...`. Es una URL nueva que nadie usa. Sin paso asignado.
- `check-comments.mjs` revisa `artifacts/`, aunque está en `.gitignore`. Una corrida de e2e cortada deja `.playwright-artifacts-0/` con `.css` de los traces y el gate falla con NOCOMMENT (paso 4). Sin paso asignado.
- El plan dice que `check-structure.mjs` prueba "sin herramientas duplicadas". Ninguna regla lo hace: nada impide un segundo script de smoke. Hoy lo cumple el diseño, porque `check-all.mjs` llama a `verify.mjs api`. Sin paso asignado.
- `compose.yaml` fija los puertos 4317 y 4318 del collector. Otro stack con el mismo compose (`hermes-bot-omp`) los ocupa y este no arranca. `POSTGRES_PORT`, `API_PORT` y `WEB_PORT` sí se pueden cambiar. Sin paso asignado.
- El `title` de un error lleva la descripción de cada caso ("Task with ID '…' not found."). El RFC 9457 §3.1.3 pide un `title` fijo por tipo y el texto variable en `detail`. Tobi decidió el 2026-10-18 dejarlo así: moverlo cambia los textos que ve el usuario. Va junto con que el frontend traduzca por `code` en vez de por status. Sin paso asignado.
- Las claves del diccionario `errors` salen en PascalCase (`"Title"`, `"DisplayName"`), aunque el JSON es camelCase. Cambiarlas rompe el contrato. Sin paso asignado.
- `AuthErrors.IdentityRejected` usa el código de Identity (por ejemplo `PasswordTooShort`) como clave de `errors`, no el nombre del campo. Hoy no se ve: el largo se valida antes y `AuthFeature` relaja las demás reglas de contraseña. Sin paso asignado.
- OpenAPI no describe las respuestas de error: los endpoints usan `Results.*`, que no aporta metadata. `ProducesProblem` por grupo lo resolvería; los valores posibles de `code` pedirían un transformer. Sin paso asignado.
- La lista de tareas pagina por keyset desde el paso 7, pero no tiene el índice `(OwnerId, CreatedAt, Id)`: PostgreSQL lee todas las tareas del dueño y las ordena. Tobi decidió el 2026-11-03 dejarlo para el paso 18, que mide el antes y el después.
- `Tasks` lee la lista con `load()` y una `Promise`. Pasarla a `resource()` o `httpResource()` (`informe-componentes-angular.md`, opción D) puede cambiar el comportamiento. Sin paso asignado.
- El `createdAt` de la respuesta 201 de `POST /tasks` sale del objeto en memoria con 7 decimales; el mismo dato leído de la lista tiene 6, porque PostgreSQL guarda microsegundos. No afecta al keyset. Truncarlo en `CreateTask` sería otro paso. Sin paso asignado.
- Si una corrida de `check-all` se corta a mitad, deja restos que rompen la siguiente: `check-comments` lee las trazas de Playwright a medias en `artifacts/` como código, y el volumen `verify-pgdata-runtime` sobrevive con la contraseña de la corrida cortada, entonces la API de la corrida nueva no puede entrar a PostgreSQL. Pasó en el paso 7 (`fase2-07-check-all-3/` y `-4/`). Sin paso asignado.
- `smoke-api.mjs` no comprueba a qué API le habla. Si `API_BASE_URL` apunta a otro servicio, registra usuarios ahí. Pasó en el paso 2 de la Fase 1: dos registros `bob<sufijo>` quedaron en la API de `hermes-bot-omp` (`localhost:18080`). Borrarlos es decisión de Tobi. Sin paso asignado.

- Dos `DELETE` de la misma tarea que cargan la fila antes de que el primero borre: el segundo recibe `DbUpdateConcurrencyException` y sale como 500 `Http.Unexpected`. Lo mismo para un `PUT` que pierde contra un `DELETE` antes de guardar. Tobi decidió el 2026-11-16 dejarlo fuera del paso 8 (investigación del paso 8, sección 8). Sin paso asignado.
- Las filas de `IdempotencyKeys` no se borran: la política publicada es "al menos 24 h" y falta una limpieza periódica, junto con la de `Sessions`. Pide un paso con trabajo programado (investigación del paso 9, sección 7). Sin paso asignado.
- El header `Idempotency-Key` no aparece en el documento OpenAPI. Va con el pendiente de OpenAPI y las respuestas de error. Sin paso asignado.
- Un segundo pedido simultáneo con la misma clave espera al primero; el borrador IETF sugiere 409 con un tope de espera (`lock_timeout`). Se revisa si aparece un cliente que reintenta en paralelo. Sin paso asignado.
- La clave de idempotencia del frontend vive en memoria del componente: recargar la página la pierde (FRONT015 prohíbe el storage del navegador). Sin paso asignado.
- `RegisterUser` abre su propia transacción, así que el filtro de idempotencia no se le puede poner sin cambiar esa slice. Hoy Auth queda fuera de DUNE0016. Sin paso asignado.
- DUNE0016 mira solo `MapPost`: un `MapMethods(..., ["POST"], ...)` de una feature queda fuera de la regla. Hoy ninguna slice lo usa. Sin paso asignado.

- `AuditEntries` no es append-only: un `UPDATE` o `DELETE` por SQL puede reescribir la historia. Quitar permisos no alcanza porque la API se conecta como el superusuario `postgres`. Las salidas son un trigger en una migración (choca con "las migraciones no se editan a mano" de `conventions.md`) o un rol de base sin `UPDATE`/`DELETE` para la API (investigación del paso 10, sección 8). Sin paso asignado; encaja con la Fase 4.
- Las filas de `AuditEntries` no se borran. Falta una política de retención y una limpieza periódica, junto con las de `Sessions` e `IdempotencyKeys`. Sin paso asignado.
- Al borrar la cuenta, las tareas las borra la base en cascada y no dejan fila de auditoría; solo queda la fila `Deleted` del perfil. Tener una fila por tarea obliga a cargar las tareas desde Users (DUNE0004 lo prohíbe) o a un trigger. Sin paso asignado.
- SQL crudo (`ExecuteSql*`) sobre una tabla auditada no deja fila, y DUNE0017 no lo ve. Hoy ningún código lo hace. Sin paso asignado.
- La auditoría no cubre Identity (roles, bloqueo, contraseña). Cada login fallido escribe `AccessFailedCount`, así que pide decidir qué cambios de cuenta se auditan. Fase 4.
- Tres lugares leen `ClaimTypes.NameIdentifier` por separado: `GetCallerId` (Auth), `IdempotencyFilter.OwnerOf` y `AuditInterceptor`. Sin paso asignado.
- Nadie puede leer la auditoría desde la API ni desde un tablero. Sería otro contrato HTTP. Sin paso asignado.

- `AccessPolicy` lee los permisos con `2 + 2N` comandos para un usuario con N roles. Una sola consulta con join `AspNetUserRoles` + `AspNetRoleClaims` lo baja a 1 (investigación del paso 11, sección 3). Es otro concepto (forma de la consulta). Sin paso asignado; encaja con el paso 18.
- `AccessPolicy` vuelve a cargar la cuenta con una consulta `AsNoTracking` aunque `OpenSession`, `RegisterUser` y `FindAccountAsync` ya la tienen. `UserManager.FindByIdAsync` ahorraría un comando, a cambio de rastrear la cuenta en los requests de Tasks. Sin paso asignado.
- Ninguna regla de Dune impide usar `UserManager` o `RoleManager` fuera de `Features/Auth`; DUNE0002 solo lo frena en endpoints. Hoy se cumple. Sin paso asignado.
- El entorno `Testing` de `ApiFixture` no activa `ValidateScopes` ni `ValidateOnBuild`: una dependencia cautiva (un singleton que toma un scoped) solo se ve en Development. Sin paso asignado.
- Si un endpoint futuro cambia roles y después comprueba permisos en el mismo request, verá el valor del principio del request. Ese endpoint tendrá que sumar una invalidación del memo de `AccessPolicy` (está escrito en `architecture.md`). Sin paso asignado.

- Cabeceras del documento de la SPA en nginx: CSP de Angular (`autoCsp`, `inlineCritical: false` o `'unsafe-inline'` en estilos), `frame-ancestors`, COOP, `Permissions-Policy`, `Referrer-Policy`, `nosniff` y `server_tokens off`, con `always` y sin duplicar las de `/api/v1/`. nginx 1.28 no tiene `add_header_inherit`. Pide una prueba en navegador contra Compose (investigación del paso 13, sección 5). Sin paso asignado.
- HSTS donde se termine TLS, cuando el repo sume HTTPS, con `max-age` bajo al principio; la API necesitaría forwarded headers para ver `https` y que la cookie del antiforgery salga `Secure`. Sin paso asignado.
- `Clear-Site-Data: "cookies"` en el logout (`CloseSession`), de la lista de OWASP. Pide revisar qué borra en la SPA. Sin paso asignado.
- Las respuestas que Kestrel arma antes del pipeline (400 de request mal formada, 431) salen sin las cabeceras de seguridad. Sin paso asignado.
- NetEscapades permite una política por endpoint (`WithSecurityHeadersPolicy`); `conventions.md` lo prohíbe, pero ninguna regla de Dune lo frena. Hoy nadie lo usa. Sin paso asignado.
- `npm audit` sin `--omit=dev` da 8 avisos en herramientas de desarrollo (`vite`, `vitest`, `@modelcontextprotocol/server`, `undici` y otros). Varios piden subir `@angular/build` y `@angular/cli` fuera del rango declarado. Encaja con el paso 34.
- Los jobs que ya existían en `ci.yml` usan acciones por tag (`@v4`) y no declaran `permissions`. Solo el job `security` las fija por SHA. Paso 34.
- No hay hook local antes del commit (`gitleaks git --pre-commit --staged`): sin push protection de GitHub, un secreto llega al remoto antes de que el CI lo vea. Sin paso asignado.
- No hay escaneo programado (`schedule`): un aviso nuevo en una dependencia de `main` se ve recién en el próximo PR. Tobi decidió el 2026-11-17 no prender las alertas de Dependabot por ahora. Sin paso asignado.
- `conventions.md` dice que no se versionan connection strings con credenciales, y `backend/appsettings.json`, `appsettings.Development.json` y `ci.yml` las tienen con contraseñas de desarrollo y de CI. gitleaks no las marca. Lo decide Tobi. Sin paso asignado.
- npm 12 de esta máquina (`allow-remote=none`) rechaza reescribir `frontend/package-lock.json` porque `@tailwindcss/oxide-wasm32-wasi` trae dependencias empaquetadas. En el paso 13 la entrada de `zod` se escribió a mano. Sin paso asignado.
- `POST /tasks` con `Idempotency-Key` suma 1 a `templateapp.tasks.created` aunque después falle el commit de la transacción que abre `IdempotencyFilter` (entonces la tarea se deshace y el contador ya sumó). Se aceptó por raro (`docs/decisions.tsv`). Sin paso asignado.
- Las métricas de negocio no tienen tablero ni alerta. Encaja con el paso 17 (SLOs y runbooks).
- La tabla "Estado real de partida" de `plan.md` nombra los meters `TemplateApp.Api` y `TemplateApp.Business`, que el código nunca registró como métricas (`TemplateApp.Api` solo aparece como fuente de trazas). Es la foto de partida; corregirla es decisión de Tobi. Sin paso asignado.

- `ActivityTraceEnricher` es redundante para OTLP: Serilog ya toma `TraceId` y `SpanId` de `Activity.Current` y el sink los escribe en los campos del LogRecord. El enricher solo agrega los atributos `TraceId` y `SpanId` duplicados en cada log (investigación del paso 15, sección 3). Sin paso asignado.
- El sink de Serilog no escribe `flags` (TraceFlags) en el LogRecord: desde el log solo no se sabe si su traza se grabó (investigación del paso 15, sección 3). Sin paso asignado.
- `check-otel-export.mjs` solo corre sobre el export de Compose (job `compose` y a mano). `check-all` lanza la API sin collector, así que en local la correlación se ve solo levantando Compose. Sin paso asignado.
- La búsqueda de secretos de `smoke-api.mjs` corre con el stack andando, así que no ve lo que se exporta al apagar; `check-otel-export.mjs` no busca secretos (revisión independiente del paso 15). Sin paso asignado.
- Riesgo sin observar: Compose arranca la API con `otel` en `service_started`, no listo. Si las primeras requests de readiness llegan antes de que el collector escuche, el exporter de trazas reintenta y el sink de Serilog no: quedaría un span SERVER sin su log y `check-otel-export.mjs` daría un falso rojo en `server-span-logged`. En las dos corridas locales no pasó. Si aparece, la muestra de `otel-check.json` muestra `/health/ready` (revisión independiente del paso 15). Encaja con el paso 16.

## Cierres

Una entrada por paso cerrado, con este formato:

```md
### Paso N — AAAA-MM-DD
- Enseñado: qué concepto quedó claro.
- Preguntas: qué preguntó Tobi.
- Evidencia: artifacts/verify-template/<runId>/
```

### Paso 15 — 2026-11-17
- Enseñado: modo autónomo: sin explicación previa.
- Preguntas: ninguna (modo autónomo). No hubo pregunta de herramienta: el control es un script de Node sobre los archivos que el `file` exporter del collector ya escribía. La investigación describe OTel Weaver `live-check` y Tracetest y no los suma.
- Qué cambió: `.agents/skills/verify-template/scripts/check-otel-export.mjs` lee `logs.json`, `traces.json` y `metrics.json` del export y falla si no hay logs de request (los que llevan `RequestId`, del scope de hosting de ASP.NET Core), si uno no trae `traceId` y `spanId` válidos en los campos del LogRecord, si apunta a un span que el collector no recibió o que es de otra traza, si no hay spans SERVER, si un span SERVER no tiene log de request, si no llegan métricas o una llega sin puntos, o si una línea del export quedó cortada. Escribe `otel-check.json` en la carpeta del export. `check-otel-export.test.mjs` tiene la fixture válida y once violaciones deliberadas. El job `compose` de CI corre las pruebas al principio, apaga el stack con `docker compose down --timeout 30 --volumes` (la API y el collector vacían sus buffers) y después corre el control; el artifact `otel-export` lleva el informe. `check-all` suma la etapa `check-otel-export-test`. `smoke-api.mjs` deja la aserción vieja ("algún log correlacionado") y conserva las métricas y la búsqueda de secretos. README, `architecture.md` y la skill `verify-template` describen el control. Ningún contrato HTTP cambia y el backend no se toca.
- Decisiones: cinco filas en `docs/decisions.tsv`. Una aclara la regla 4: el control vive en `verify-template`, como dice el plan, con su fixture válida y sus violaciones, y no en `dune/`. PR #69.
- Evidencia:
  - Rojo (`paso15-rojo/`): las 11 pruebas del control fallan sin el script.
  - Verde (`paso15-verde/`): 11/11. El export real del paso 14 pasa: 63 logs de request, 60 spans SERVER, 183 métricas.
  - Compose (`paso15-compose/`): proyecto `paso15`, API en 18580 y collector sin puertos publicados, porque 8080, 8081, 5432 y 4317-4318 los usan otros stacks de la máquina. Smoke `--compose` PASS; después de `down`, el control da PASS con 77 logs, 63 de request, 371 spans, 60 SERVER y 92 métricas.
  - Rojo real (`paso15-compose-rojo/`): el mismo stack con `OTEL_TRACES_SAMPLER=parentbased_traceidratio` y `OTEL_TRACES_SAMPLER_ARG=0.5` en la API, sin tocar código. El smoke pasa y la aserción vieja del smoke también (`old-weak-check.log`). El control falla en `request-log-span-exported` con 29 de 63 logs de request que apuntan a spans que el collector nunca recibió.
  - `check-all` sin e2e (`paso15-check1/`): PASS en las 22 etapas, .NET 212/212. e2e: NOT_RUN, porque el paso no cambia el frontend ni un contrato HTTP.
  - Revisión independiente (un subagente sin mi conclusión): nada grave y cinco hallazgos bajos. Se arreglaron dos. Las fixtures pasan a varias líneas, resources y scopes: con un lector que lee solo la primera línea fallan 10 de 12 pruebas, y con uno que lee solo el primer resource fallan 7 (`paso15-revision/`). Una línea cortada ahora deja informe con la regla `export-json-valid` (rojo 11/12, verde 12/12). El README pide una carpeta vacía por corrida, porque el collector agrega al final. Los otros dos quedan en Pendientes.
  - `check-all` sin e2e después de la revisión (`paso15-check2/`): PASS en las 22 etapas, .NET 212/212, con la etapa `check-otel-export-test` en 12/12. e2e: NOT_RUN.
  - Sin comprobar: el job `compose` de CI (se ve en el PR); una métrica vacía en un export real (solo la cubre la fixture).

### Paso 14 — 2026-11-17
- Enseñado: modo autónomo: sin explicación previa.
- Preguntas: ninguna (modo autónomo). No hubo pregunta de herramienta: `Meter`, `IMeterFactory`, `MeterListener` e `InstrumentAdvice` vienen con .NET 10 y OpenTelemetry ya estaba en el repo. El paquete `Microsoft.Extensions.Diagnostics.Testing` (`MetricCollector<T>`) no entró.
- Qué cambió: dos Meters de negocio. `Features/Tasks/TaskMetrics.cs` (Meter `TemplateApp.Tasks`) cuenta `templateapp.tasks.created` en `CreateTask` y registra `templateapp.tasks.completion.duration` (segundos desde la creación, buckets de 1 min a 30 días) en `UpdateTask` cuando una tarea pasa a completa. `Features/Auth/AuthMetrics.cs` (Meter `TemplateApp.Auth`) cuenta `templateapp.auth.login.attempts` en `OpenSession` con la etiqueta `templateapp.auth.login.outcome`, que sale del enum `LoginOutcome` (`success`, `invalid_credentials`, `locked_out`). Las dos clases son singleton y crean su Meter con `IMeterFactory`. Se mide después del `SaveChangesAsync`, así que el replay de idempotencia, el replay de `PUT` y el 409 no cuentan. `ObservabilityExtensions` suma `.AddMeter("TemplateApp.*")`. `backend/Tests/Support/RecordedMeasurements.cs` escucha con `MeterListener` solo el host del test (filtra por su `IMeterFactory`). `dune/BannedSymbols.txt` prohíbe los cuatro constructores de `Meter`, con `MeterConstructionBanTests` (fixture válida y violación deliberada). `smoke-api.mjs` falla si el export OTLP no trae las tres métricas. `conventions.md` tiene una sección "Métricas" y `architecture.md` nombra los Meters. Ningún contrato HTTP cambia: el frontend no se toca.
- Decisiones: seis filas en `docs/decisions.tsv`. Ninguna se aparta de la investigación. PR #68.
- Evidencia:
  - Rojo (`paso14-rojo/`): `Create_CountsTheSavedTask_ButNotItsReplayNorARejectedOne`, `Update_RecordsTimeToComplete_OnlyWhenTheTaskBecomesCompleted` y `Login_CountsEachAttemptByOutcome` fallan sin la instrumentación (ninguna medición). `MeterConstructionBanTests` falla sin la regla (esperaba diagnósticos en las líneas 6 a 9 y no hubo ninguno).
  - Verde (`paso14-verde/`): los 3 tests pasan; el de `UpdateTask` lee exactamente 1500 s con `FakeTimeProvider`, y el de login lee la secuencia `invalid_credentials`, `success`, cinco `invalid_credentials` y `locked_out`. La regla de Dune pasa 2/2.
  - `check-all` sin e2e (`paso14-check1/`): PASS en las 21 etapas, .NET 212/212. e2e: NOT_RUN, porque el paso no cambia el frontend ni un contrato HTTP.
  - Compose (`paso14-compose/`): `smoke-api.mjs --compose` PASS con `OTEL_EXPORT_DIR`. El `metrics.json` trae `templateapp.tasks.created` = 5 (el smoke manda 6 POST y uno es replay), el histograma con count 1 y los buckets propios, y `templateapp.auth.login.attempts` con `outcome=success` (`business-metrics.json`). Corrió con el proyecto `paso14` y otros puertos porque 8080, 8081, 5432 y 4317-4318 los usaban otros stacks de la máquina.
  - Rojo del smoke (`paso14-compose-rojo/`): sin `.AddMeter("TemplateApp.*")` el smoke falla con "Debe exportarse el contador de tareas creadas del Meter TemplateApp.Tasks".
  - Sin comprobar: un revisor independiente; el job `compose` de CI (se ve en el PR).

### Paso 13 — 2026-11-17
- Enseñado: modo autónomo: sin explicación previa.
- Preguntas: la pregunta de herramientas (regla 5). Tobi eligió NetEscapades.AspNetCore.SecurityHeaders 1.3.1 para las cabeceras (el agente recomendaba un middleware propio), gitleaks 8.30.1 como binario para los secretos, NuGetAudit más `npm audit` para las dependencias, y no prender las alertas de Dependabot por ahora.
- Qué cambió: toda respuesta de la API lleva `nosniff`, CSP `default-src 'none'; frame-ancestors 'none'`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cross-Origin-Resource-Policy: same-origin` y `Cache-Control: no-store`. La política está en `backend/Infrastructure/Http/ApiSecurityHeaders.cs` y `UseApiSecurityHeaders()` es el primer middleware: escribe en `OnStarting`, así cubre el 500, el 404, el 400 del antiforgery y el 429. Kestrel ya no manda `Server`. La cookie interna del antiforgery usa `SameAsRequest`. El pendiente decía que el valor por defecto era `SameAsRequest` y que `Always` era una línea; las dos cosas eran incorrectas: el valor por defecto es `None` y `Always` rompe toda request HTTP. En el CI, el job nuevo `security` corre `dune/check-secrets.mjs` (gitleaks sobre el historial alcanzable desde `HEAD` y los cambios sin commitear), sus tests, la auditoría NuGet (`NU1903`, `NU1904`) y `npm audit --omit=dev --audit-level=high`. `check-all.mjs` suma las mismas cuatro etapas. `zod` sube a 4.6.7 en el lockfile para producción. `architecture.md`, `conventions.md` y el README describen las cabeceras y el escaneo.
- Decisiones: quince filas en `docs/decisions.tsv`. Una se aparta de la investigación: el rojo del CI se mostró en la rama del paso, con el commit sacado por force-push antes del merge, y no en una rama descartable, porque la regla 6 permite push solo de la rama del paso. El JSON de la API no cambia; cambian las cabeceras de toda respuesta. PR #67.
- Evidencia:
  - Rojo (`fase4-13-red/`): 9 de 10 casos de `backend/Tests/SecurityHeaders/SecurityHeadersTests.cs` fallan con el código de antes. El caso de la cookie por HTTP ya pasaba.
  - Verde (`fase4-13-green/`): 10/10. Con `UseApiSecurityHeaders()` movido detrás de `UseRateLimiter()` falla solo el caso 429.
  - Regla (`fase4-13-rule/`): gitleaks sin `.gitleaks.toml` encuentra 2 hallazgos (el ejemplo de Stripe en las investigaciones de los pasos 9 y 13); con la allowlist, 0. `dune/check-secrets.test.mjs` 6/6 con una clave AWS falsa generada en el momento, así que ningún secreto falso queda versionado. Sin el arreglo de CRLF falla el caso del cambio sin commitear: en Windows, gitleaks `--pre-commit` aborta con código 0 cuando git avisa de CRLF.
  - Dependencias (`fase4-13-deps/`): `npm audit --omit=dev --audit-level=high` da código 1 con el lockfile de antes (`zod` 4.6.5, GHSA-mrhx-c7xc-m3w5) y 0 con el nuevo. La auditoría NuGet da 0 hoy y 1 con `Newtonsoft.Json` 12.0.1 plantado (`error NU1903`).
  - CI con el secreto plantado (`fase4-13-plant/`): el commit `5200b41` agregó `backend/Tests/Fixtures/planted-secret.txt` con una clave AWS falsa y aleatoria, nunca válida. El job `security` del run `44010593133` falló en "Secretos en el historial alcanzable desde HEAD" con `aws-access-token` y la clave tapada por `--redact`. El commit salió de la rama con un force-push; el run `44010785494` sobre `22396f1` dio verde en los 5 jobs.
  - `check-all` (`fase4-13-check-all/`): FAIL en `check-comments` por los `# vX` al lado de los SHA en `ci.yml`; la versión pasó al `name:` del paso, sin tocar el gate. `fase4-13-check-all-2/`: PASS en las 21 etapas sin e2e.
  - `check-all` con e2e (`fase4-13-check-all-e2e/`): PASS en las 22 etapas, .NET 207/207, Vitest 21/21, smoke de la API (con las seis cabeceras y sin `Server` en 200, 404, 401 y 400 contra Kestrel real) y e2e 35/35.
  - Sin comprobar: un revisor independiente; las cabeceras vistas a través de nginx en Compose (en local no corrió `smoke-api.mjs --compose`; en CI el job `compose` pasó con el smoke contra el puerto de la API); el documento de la SPA, que todavía no lleva cabeceras.

### Paso 12 — 2026-11-16
- Enseñado: modo autónomo: sin explicación previa.
- Preguntas: ninguna (modo autónomo). El paso no tuvo que elegir herramienta: `Microsoft.AspNetCore.RateLimiting` viene con el framework y el repo ya lo usaba.
- Qué cambió: un limiter global (`backend/Infrastructure/Http/ApiRateLimiting.cs`) cuenta toda request de la API en ventanas fijas de un minuto. Con sesión, cada usuario tiene un balde de lecturas (300) y otro de escrituras (60); sin sesión, el balde es por IP (120). La política `auth` de login, registro y logout pasa de un balde único para toda la API a uno por IP (20). Health queda exento. `UseRateLimiter()` va ahora después de `UseAuthentication()`, porque la partición lee al usuario. El rechazo pasa de 503 `Http.ServiceUnavailable` a 429 `Http.TooManyRequests` con `Retry-After`, y el frontend muestra "Demasiadas solicitudes. Vuelve a intentarlo en N segundos.". Los límites salen de las options `RateLimiting` y `AuthRateLimiting`. `check-structure.mjs` permite `DisableRateLimiting` solo en `backend/Features/Health/`. `architecture.md`, `conventions.md` y el README describen el limiter.
- Decisiones: siete filas en `docs/decisions.tsv`. Una se aparta de la investigación: los valores por defecto viven en el código de las options y no en `appsettings.json`, porque `ConfigurationValidationTests` arranca la API sin ese archivo. El contrato HTTP cambia (status, `code` y header): backend, frontend y tests van juntos (regla 3). PR #66.
- Evidencia:
  - Rojo (`fase4-12-red/`): con el código de antes fallan 8 casos de .NET: 5 de `backend/Tests/RateLimiting/RateLimitingTests.cs` (201 o 200 donde se espera 429; una segunda IP recibe el 429 de la primera), el contrato de `ErrorContractTests` (503 en vez de 429) y 2 de `ConfigurationValidationTests`. Vitest: el caso del 429 muestra "Error inesperado del servidor".
  - Verde (`fase4-12-green/`): 34/34 en las tres clases. Con `UseRateLimiter()` otra vez antes de `UseAuthentication()` fallan 3 casos: los tests atrapan el orden. Vitest 21/21.
  - Regla (`fase4-12-rule/`): con el `check-structure.mjs` de antes fallan las 4 violaciones deliberadas nuevas (33/37); con el nuevo, 37/37. Un `DisableRateLimiting` plantado en `ListTasksEndpoint.cs` da `limiter global: DisableRateLimiting solo en backend/Features/Health`; se quitó.
  - `check-all` (`fase4-12-check-all/`): PASS en las 17 etapas sin e2e.
  - `check-all` con e2e (`fase4-12-check-all-e2e/`): PASS en las 18 etapas, .NET 197/197, Vitest 21/21, smoke de la API y e2e 35/35.
  - Sin comprobar: un revisor independiente; la IP que ve la API detrás del proxy de `ng serve` (se dedujo `127.0.0.1` de la configuración, no se midió); el limiter detrás de nginx en Compose (no corrió `smoke-api.mjs --compose`). El recorrido de navegador seguido del smoke sigue compartiendo el balde de `auth` en `127.0.0.1`: queda como pendiente, con la espera de 65 s.

### Paso 11 — 2026-11-16
- Enseñado: modo autónomo: sin explicación previa.
- Preguntas: ninguna (modo autónomo). El paso no tuvo que elegir herramienta ni librería.
- Qué cambió: `AccessPolicy` (`backend/Features/Auth/Access/`) guarda en un campo un `AccessGrant` (roles y permisos) por `userId`. Como `AccessPolicy` es scoped, el memo dura un request: la primera comprobación lee Identity y las siguientes del mismo request lo reusan. El request siguiente arranca sin memo, entonces un cambio de rol vale desde ahí. `AuthModule.DescribeAsync` usa el mismo `AccessGrant` y deja de leer los roles dos veces al registrarse, abrir sesión o leer un usuario. `check-structure.mjs` rechaza la caché entre requests (paquetes, namespaces y registros de caché) hasta el paso 33. `architecture.md` y `conventions.md` dicen el alcance del memo y su límite: un cambio de roles dentro del mismo request no se ve hasta el siguiente.
- Decisiones: cinco filas en `docs/decisions.tsv`. El contrato HTTP no cambia: no rige la regla 3. PR #65.
- Evidencia:
  - Rojo (`fase4-11-red/`): los 3 casos de `backend/Tests/Authorization/PermissionMemoTests.cs` fallan con el código de antes. Cuatro comprobaciones en un request mandan 16 comandos a tablas `AspNet*` en vez de 4; abrir sesión lee `AspNetUserRoles` 2 veces en vez de 1; un rol agregado se ve dentro del mismo scope.
  - Verde (`fase4-11-green/`): suite de producto 99/99, con los 3 casos nuevos.
  - Regla (`fase4-11-rule/`): con el `check-structure.mjs` de antes fallan las 7 violaciones deliberadas nuevas (25/32); con el nuevo, 32/32. Un `services.AddMemoryCache()` plantado en `AuthFeature.cs` da `caché entre requests antes del paso 33`; se quitó y el gate vuelve a OK.
  - `check-all` (`fase4-11-check-all/`): PASS en las 17 etapas, `dotnet test` 189/189, Vitest 20/20 y smoke de la API. E2E: NOT_RUN, el paso no cambia el frontend ni el contrato HTTP.
  - Sin comprobar: un revisor independiente y el costo medido en la app lanzada por `check-all` (el smoke solo comprueba que no hay regresión). `GetTaskTests` sigue probando por HTTP que dar y quitar `Admin` vale en el request siguiente.

### Paso 10 — 2026-11-16
- Enseñado: modo autónomo: sin explicación previa.
- Preguntas: ninguna (modo autónomo). Tobi eligió el interceptor propio, sin librería, cuando se le preguntó por la herramienta.
- Qué cambió: `AuditInterceptor` (`backend/Infrastructure/Persistence/`) es un `SaveChangesInterceptor`. En `SavingChanges` agrega una fila de `AuditEntries` por cada entidad `IAuditable` (`TaskItem` y `UserProfile`) agregada, modificada o borrada. La fila viaja en el mismo `SaveChanges`, entonces queda en la transacción del cambio: un 409 o un 400 dentro del filtro de idempotencia no la dejan. Guarda el tipo de actor (`user`, `anonymous`, `system`), el id opaco, la entidad, la operación, los nombres de las propiedades cambiadas, la hora del `TimeProvider` y el `TraceId`; nunca valores. DUNE0017 rechaza `ExecuteUpdate` y `ExecuteDelete` sobre una entidad `IAuditable`, porque no pasan por el interceptor. `CONTEXT.md` define qué cuenta como dato personal en la auditoría.
- Decisiones: ocho filas en `docs/decisions.tsv`. El contrato HTTP no cambia: no rige la regla 3. PR #64.
- Evidencia:
  - Rojo (`fase3-10-red/`): los 6 casos de `backend/Tests/Auditing/AuditTrailTests.cs` fallan con `42P01: relation "AuditEntries" does not exist`.
  - Rojo con la tabla (`fase3-10-red-table/`): con la entidad y la migración y sin el interceptor, los 6 fallan porque no hay filas.
  - Verde (`fase3-10-green/`): 6/6 y la suite completa 184/184. La migración `AddAuditEntries` se generó por CLI y no se editó; `has-pending-model-changes` no ve cambios pendientes.
  - Regla (`fase3-10-rule/`): Dune 90/90 (fixture válida y violación deliberada). Un `ExecuteDeleteAsync` plantado en `DeleteTask` rompe el build con DUNE0017; se quitó y el build vuelve a compilar.
  - `check-all` (`fase3-10-check-all/`): PASS en las 17 etapas, `dotnet test` 186/186, Vitest 20/20 y smoke de la API. E2E: NOT_RUN, el paso no cambia el frontend ni el contrato HTTP.
  - Sin comprobar: un revisor independiente, las filas de auditoría en la app lanzada por `check-all` (el smoke solo comprueba que no hay regresión) y el costo de rendimiento de `IHttpContextAccessor`.

### Paso 9 — 2026-11-16
- Enseñado: un `POST` no es idempotente por naturaleza: cada uno crea algo nuevo, entonces reintentar "Crear" después de perder la respuesta dejaba dos tareas. El cliente manda una clave por intención en `Idempotency-Key`. `IdempotencyFilter` abre una transacción y anota la clave con `INSERT ... ON CONFLICT DO NOTHING`: 1 fila es clave nueva y 0 es repetición, y PostgreSQL hace esperar a un segundo pedido simultáneo hasta que el primero termina. `CreateTask` guarda dentro de esa misma transacción porque comparte el `AppDbContext` del request, entonces la clave existe si y solo si la tarea existe: un 2xx se guarda y confirma, un error deshace las dos. La huella (SHA-256 de método, ruta y JSON) decide entre repetir los bytes guardados con `Idempotent-Replayed: true` y responder 422 `Idempotency.KeyReused`. La slice solo suma `.WithIdempotency<CreateTaskRequest>()`, y DUNE0016 lo exige en todo `MapPost` de una feature salvo Auth. En el frontend la clave es por intención (`nextCreateAttempt`), no por pedido: por eso no hay interceptor.
- Preguntas: ninguna. Tobi eligió clave opcional, 422 para otro cuerpo, DUNE0016 dentro del paso y las otras seis recomendaciones, y respondió "Entendido, cerralo".
- Decisiones: siete filas en `docs/decisions.tsv`. El paso 9 del plan nombra el frontend, el smoke, los e2e, el 422 y DUNE0016.
- Evidencia:
  - Rojo (`fase3-09-red/`): 7 de 12 casos de `CreateTaskTests` en rojo: la misma clave creaba otro `id`, dos pedidos simultáneos creaban dos tareas, otro título daba 201 en vez de 422 y una clave mal formada daba 201 en vez de 400.
  - Verde (`fase3-09-green/`): backend 90/90; la migración `AddIdempotencyKeys` se generó por CLI, aparece aplicada y no se editó.
  - Regla (`fase3-09-rule-red/`): el `CreateTaskEndpoint` de antes del paso falla con DUNE0016 en `(9,60)`; el actual compila.
  - `check-all`: `fase3-09-check-all/` PASS sin e2e; `fase3-09-check-all-e2e/` PASS en las 18 etapas: `dotnet test` 178/178, Vitest 20/20, smoke de la API y e2e 35/35, con la e2e que pierde el primer 201 y reintenta. Sin comprobar: un revisor independiente, la caída del proceso de la API a mitad de un pedido y el header en OpenAPI.

### Paso 8 — 2026-11-16
- Enseñado: el lost update pasa cuando un cliente guarda el estado completo de una lectura vieja: la pestaña B marcaba el checkbox y su `PUT` mandaba el título viejo, que pisaba el renombre de la pestaña A. La concurrencia optimista no bloquea al leer: cada tarea lleva una versión (`xmin` de PostgreSQL, `uint Version` con `IsRowVersion()`) y el `PUT` la devuelve. `UpdateTask` frena el conflicto con dos barreras. La comparación en memoria (`item.Version != request.Version`) frena a la pestaña vieja. `OriginalValue = request.Version` hace que EF Core ponga la versión del cliente en el `WHERE xmin` y frena la carrera entre la carga y el guardado (`DbUpdateConcurrencyException` → `Task.Conflict`, 409). Hace falta porque EF Core compara contra el valor que leyó él, no contra el del cliente. El reintento es tolerante: versión vieja con el estado ya guardado da 200 sin escribir. `Tasks` muestra un mensaje propio ante `Task.Conflict` y recarga. La migración se generó por CLI y no emite DDL: `xmin` ya existe.
- Preguntas: ninguna. Tobi eligió las seis recomendaciones de la investigación y respondió "Entendido, cerrá el paso".
- Decisiones: cinco filas en `docs/decisions.tsv` (`xmin`, campo `version` con 409, versión obligatoria, reintento tolerante, mensaje y recarga). `DeleteTask` quedó fuera, con un pendiente. El paso 8 del plan nombra el frontend, el smoke y los e2e.
- Evidencia:
  - Rojo (`fase2-08-red/`): 2 de 3 tests de `UpdateTaskTests` en rojo. El secuencial terminó con "Comprar pan" en vez de "Comprar pan integral"; el paralelo dio `[OK, OK]` en vez de `[OK, Conflict]`.
  - Verde (`fase2-08-green/`): `dotnet test` 79/79 en esa corrida; `UpdateTaskTests` 3/3 en 5 corridas seguidas. `migrations list` muestra `AddTaskVersion` aplicada; `migrations script` tiene solo el `INSERT` del historial.
  - `check-all`: `fase2-08-check-all/` PASS sin e2e; `fase2-08-check-all-e2e/` FAIL (la e2e nueva tomaba la primera fila, ya marcada por otro test); `fase2-08-check-all-e2e-2/` PASS en todas las etapas: `dotnet test` 165/165, Vitest 17/17, smoke de la API y e2e 34/34. Sin comprobar: un revisor independiente, y que un borrado entre la carga y el guardado da 409 en vez de 404.

### Paso 7 — 2026-11-04
- Enseñado: paginar por keyset es marcar dónde quedaste en vez de contar filas. `ListTasks` ordena por `CreatedAt DESC, Id DESC` (el `Id` desempata el mismo instante) y filtra con la comparación de pares `(CreatedAt, Id) < (cursor)` (`EF.Functions.LessThan` sobre `ValueTuple`), entonces una tarea creada entre páginas no empuja filas ni se repite. Pide `pageSize + 1` filas para saber si hay otra página sin contar, por eso se fue `totalCount`. `TaskCursor` empaqueta versión, ticks UTC e `Id` en base64url opaco; un cursor roto da 400 con `errors.Cursor`. El servidor solo va hacia adelante: `Tasks` guarda la pila de cursores visitados para "Anterior" y la página es el largo de la pila. Se pierde el salto directo a una página y el total.
- Preguntas: ninguna; Tobi pidió un resumen corto y respondió "listo".
- Decisiones: cinco filas en `docs/decisions.tsv` (v1 cambia en el lugar, se quita `totalCount`, índice en el paso 18, formato del cursor, `FakeTimeProvider`). `architecture.md`, `conventions.md` y el paso 7 del plan describen la paginación nueva.
- Evidencia:
  - Rojo (`fase2-07-red/`): 7 de 9 tests de la lista en rojo por la forma vieja de la respuesta (los 2 de `pageSize` ya pasaban); el spec de `TaskPager` no compilaba. La repetición con offset la mostró el experimento de la investigación ({5,4} y después {4,3}).
  - Verde (`fase2-07-green/`): 9/9 tests de la lista, Vitest 14/14. El SQL generado se revisó con el `AppDbContext` real: `(t."CreatedAt", t."Id") < (@at, @id) ORDER BY ... LIMIT`.
  - `check-all` con e2e: `fase2-07-check-all/` FAIL (faltaba el navegador de Playwright), `-3/` y `-4/` FAIL por restos de la corrida 2 cortada, `fase2-07-check-all-5/` PASS en todas las etapas: `dotnet test` 163/163, Vitest 14/14, smoke de la API (sigue `nextCursor` con una tarea creada entre páginas y un cursor roto da 400) y e2e 33/33. Sin comprobar: el recorrido manual en el navegador con dos pestañas.

### Paso 6.3 — 2026-11-03
- Enseñado: la interfaz de un módulo profundo es una puerta que se encuentra por su nombre. `IAuthModule` junta `IAccessPolicy` e `IUserAccounts` con métodos que llevan el sustantivo (`FindAccountAsync`), y `AuthModule` delega los permisos en `AccessPolicy`, que queda interno. `IUsersModule` reemplaza a `IUserProfiles`. Los datos dicen su propósito (`UserAccountDetails`, `UserProfileDetails`) y `Contracts/` guarda solo lo que usa otra feature. DUNE0015 reconoce cada tipo por forma y nombre, recorre la firma de la puerta para que no salga un `*Response` ni una entidad, y con un análisis al final de la compilación rechaza el contrato o el método que ninguna otra feature usa; sigue los métodos de extensión hasta su clase (`ReducedFrom`) para no dar un rojo falso con `GetCallerId`. DUNE0008 acepta solo la puerta y su implementación.
- Preguntas: ninguna. Tobi decidió ajustar DUNE0008 (opción A), sacar de `Contracts/` las responses propias y cambiar ADR-0004 (opción A), y no partir el paso.
- Decisiones: cuatro filas en `docs/decisions.tsv` (DUNE0015, DUNE0008, `Contracts/` solo para otra feature, forma de las puertas y nombres). ADR-0004 suma una entrada en "Cambios"; `conventions.md`, `architecture.md` y el paso 11 del plan nombran la puerta nueva.
- Evidencia:
  - Rojo de los tests (`fase2-06-3-rule-red/tests.txt`): 4 en rojo con el analyzer vacío.
  - Rojo real (`fase2-06-3-rule-red/build.txt`): el backend antes del cambio da 8 errores DUNE0015, los 8 que predijo la investigación (5 tipos no admitidos y 3 responses que solo usa su feature).
  - Tests de Dune (`fase2-06-3-rule-green/`): 86/86.
  - `check-all --no-e2e` (`fase2-06-3-check-all/`): PASS en todas las etapas, `dotnet test` 156/156, smoke de la API PASS. E2E: NOT_RUN, sin cambios de frontend ni del JSON. Sin comprobar: el documento OpenAPI generado y cómo muestra el editor el error del final de la compilación.

### Paso 6.2 — 2026-11-03
- Enseñado: una convención escrita se vuelve regla ejecutable. DUNE0014 (`ErrorCatalogAnalyzer`) mira cada invocación del backend, salvo tests y migraciones, y se queda con los métodos estáticos de `ErrorOr.Error` por símbolo, no por texto. Fuera de un `*Errors.cs` marca la llamada aunque el código no sea constante (`Error.Validation(code: e.Code, …)`). En `backend/Features/`, el código constante empieza con el nombre de la clase sin `Errors` (`TaskErrors` → `Task.`, no la carpeta `Tasks`); `Validation` queda fuera porque su código es el campo, y un `Error.Failure()` sin código se marca por su valor por defecto `General.Failure`.
- Preguntas: ninguna; Tobi pidió seguir con el 6.3.
- Decisiones: una fila en `docs/decisions.tsv` (no se prohíbe la clave `"code"` a mano: `ApiResults` y `CustomizeProblemDetails` la escriben a propósito). `Dune.props` y `.editorconfig` suman DUNE0014; `AnalyzerHost.All()` suma `ErrorCatalogAnalyzer`. `conventions.md` nombra la regla.
- Evidencia:
  - Rojo de los tests (`fase2-06-2-rule-red/tests.txt`): con el analyzer vacío, 2 de 3 en rojo (las dos violaciones).
  - Rojo real (`fase2-06-2-rule-red/build.txt`): el backend de antes del paso 6 (`fc91aeb`) con la regla da 13 errores DUNE0014, los 13 que el paso 6 movió a mano.
  - Tests de Dune (`fase2-06-2-rule-green/`): 80/80.
  - `check-all --no-e2e`: `fase2-06-2-check-all/` FAIL porque Docker estaba apagado; `fase2-06-2-check-all-2/` PASS en todas las etapas, `dotnet test` 150/150. E2E: NOT_RUN, sin cambios de frontend ni de contrato HTTP.

### Paso 6.1 — 2026-11-03
- Enseñado: ningún error sale mudo. `UseStatusCodePages` (`Program.cs`, después de `UseExceptionHandler`) le escribe un ProblemDetails a toda respuesta 400-599 sin cuerpo, también al 503 del limiter. `CustomizeProblemDetails` completa el `code` con `TryAdd`, que no pisa el código que ya puso `ApiResults` (`Task.NotFound`). `HttpErrors.CodeFor` sale de una tabla explícita de cinco status y, para los demás, de la frase estándar del status. `ApiVersioningTests` cambió a propósito: `/api/v9` responde 404 con `Http.NotFound`.
- Preguntas: ninguna; Tobi pidió seguir con los pasos 6.2 y 6.3.
- Evidencia: `fase2-06-1-red/` (9 tests en rojo por falta de `code`, 14 en verde), `fase2-06-1-green/`, `fase2-06-1-cierre-2/` (gates, launch, doctor, smoke y cleanup en PASS). E2E: NOT_RUN, el paso no toca el frontend.

### Paso 6 — 2026-10-18
- Enseñado: el contrato de error también es API. El `code` es el identificador estable y sale de un catálogo: `TaskErrors`, `UserErrors`, `AuthErrors` y `Shared/Http/HttpErrors` absorbieron los 13 errores escritos en línea. `ApiResults` es el único mapeo de `Error` a HTTP (`StatusFor` más un solo `Results.Problem`), y lo usan también la cookie y el antiforgery. El 500 sale con `Http.Unexpected`, sin `detail`. Desde .NET 10 el middleware no registra una excepción manejada por un `IExceptionHandler`; `SuppressDiagnosticsCallback = _ => false` la devuelve al log.
- Preguntas: si se trabajaba sobre `main` (no, sobre la rama del paso) y si las e2e no estaban deshabilitadas. Tobi decidió no correrlas en este paso; un primer intento falló porque faltaba el navegador de Playwright, y el segundo, con Edge, se cortó.
- Decisiones: siete filas nuevas en `docs/decisions.tsv` (partir el paso en 6, 6.1 y 6.2; `code` como identificador; descripción en `title`; catálogos; cookie; 500; `Auth.AccountDeleteFailed`). `conventions.md` documenta el catálogo y el mapeo.
- Evidencia: `fase2-06-red/` (5 de 11 en rojo), `fase2-06-log-red/` (sin la línea del log, el test del 500 falla), `fase2-06-green/` (11/11), `fase2-06-check-all/` (13 gates, launch, doctor, smoke y cleanup en PASS; `dotnet test` 141/141; Vitest 14/14). E2E: NOT_RUN por decisión de Tobi. CI del PR #52: 4 checks en verde.

### Paso 5.4 — 2026-10-18
- Enseñado: una decisión escrita se vuelve regla ejecutable (regla 4 de AGENTS.md). DUNE0013 (`ConfigurationAccessAnalyzer`) mira cada identificador del backend, salvo tests y migraciones. Marca si el símbolo o el tipo de la expresión es `IConfiguration` o algo que la implementa, y si es `Environment.GetEnvironmentVariable(s)` o `ExpandEnvironmentVariables`. Compara por interfaz implementada, no por nombre, porque `builder.Configuration` es `ConfigurationManager`: la investigación mostró que `BannedSymbols.txt` (RS0030) no lo ve. No hace falta excepción para `Program.cs` porque el paso 5.3 sacó todas las lecturas.
- Preguntas: ninguna; Tobi pidió resolver los pendientes.
- Decisiones: `Dune.props` y `.editorconfig` suman DUNE0010 a DUNE0013 (la lista terminaba en DUNE0011 y DUNE0009), y `AnalyzerHost.All()` suma `VersionedApiAnalyzer`, que faltaba. `conventions.md` gana la sección "Configuración". `informe-vsa-deep-modules.md` deja de proponer `Add<X>Feature(services, configuration)`.
- Evidencia:
  - Tests de Dune: 77/77, con los dos nuevos de `ConfigurationAccessAnalyzerTests` (fixture válida vacía; violación en las líneas 7, 9-15: parámetro, indexador, `GetValue`, `GetConnectionString`, `GetSection`, `new ConfigurationManager()[...]`, `Configure<IConfiguration>` y `Environment.GetEnvironmentVariable`).
  - Rojo real (`fase2-05-4-rule-red/build.txt`): el backend de antes del paso 5.3 (`1c5473d`) con la regla nueva da 13 errores DUNE0013 en `Program.cs:11,12,44,49`, `BackendInfrastructure.cs:13,26,31,35`, `ObservabilityExtensions.cs:20,21` y `HealthFeature.cs:7,9`.
  - `check-all --no-e2e` (`fase2-05-4-check-all/`): 13 gates, `launch`, `doctor`, smoke y `cleanup` en PASS; `dotnet test` 130/130 (53 de producto y 77 de Dune). E2E: NOT_RUN, sin cambios de frontend ni de contrato HTTP.

### Paso 5.3 — 2026-10-18
- Enseñado: la configuración entra por una sola puerta, las options validadas. `DataProtectionKeysOptions` exige ruta absoluta (`Path.IsPathFullyQualified`; `/keys` en Windows es "rooted" pero relativa) y configura `KeyManagementOptions.XmlRepository` de forma diferida, que es lo que hace `PersistKeysToFileSystem` por dentro. `TelemetryOptions` liga `OTEL_EXPORTER_OTLP_ENDPOINT` con `[ConfigurationKeyName]` y un segundo `BindConfiguration(string.Empty)`: la variable sigue ganándole a `Otel:Endpoint`, sin indexador. Serilog arranca en dos etapas: el logger estático escribe solo a consola, y el final (consola + OTLP con `ignoreEnvironment: true`) sale de los servicios con `AddSerilog(..., preserveStaticLogger: true)`.
- Dos trampas encontradas al probar: `CreateBootstrapLogger()` se congela una vez por proceso, y los tests levantan varios hosts en paralelo ("The logger is already frozen"); y sin `preserveStaticLogger`, el host cierra el logger estático al descartarse, entonces el `Log.Fatal` del `catch` no escribía nada. Por eso el log de requests recibe el logger de DI (`options.Logger`), porque por defecto usa el estático.
- Preguntas: Tobi pidió resolver los pendientes y dejó las decisiones de diseño en manos del agente. Se tomaron las recomendaciones de [investigacion/paso-05-3-lectura-configuracion.md](investigacion/paso-05-3-lectura-configuracion.md): dos claves con prioridad, bootstrap solo de consola, y la regla en un paso aparte (5.4).
- Evidencia:
  - `ConfigurationValidationTests`: 9/9, con dos casos nuevos (`DataProtection__KeysDirectory=relative/keys` y `OTEL_EXPORTER_OTLP_ENDPOINT=collector:4318`).
  - Collector falso (`fase2-05-3/`): con `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4319`, llegaron `/v1/logs` 3, `/v1/traces` 2 y `/v1/metrics` 3; la consola muestra `HTTP GET /api/v1/health/live responded 200`.
  - `check-all --no-e2e` (`fase2-05-3-check-all/`): 13 gates, `launch`, `doctor`, smoke y `cleanup` en PASS; `dotnet test` 128/128. E2E: NOT_RUN, sin cambios de frontend ni de contrato HTTP.

### Paso 5.2 — 2026-10-18
- Enseñado: `dotnet ef` no tiene otra forma de obtener el `DbContext` que ejecutar `Program.cs`. Cuando la app llega a `Build()`, la herramienta la corta con `HostAbortedException`. El `catch (Exception ex)` de `Program.cs` lo tomaba por un fallo y escribía "Application terminated unexpectedly during startup". Ahora el `catch` deja pasar esa excepción (`when (ex is not HostAbortedException)`), que es la forma que documenta Microsoft.
- Preguntas: Tobi pidió resolver los pendientes del paso 5.1.
- Evidencia:
  - Antes (`fase2-05-2/before.txt`): `dotnet ef migrations list --no-connect` lista las 2 migraciones y deja `[FTL] Application terminated unexpectedly during startup` con `HostAbortedException`.
  - Después (`fase2-05-2/after.txt`): lista las mismas 2 migraciones, sin log Fatal.
  - Sin test automático: reproducir `HostAbortedException` pide el `HostFactoryResolver` de las herramientas, que es interno. Queda la prueba manual.
  - `check-all --no-e2e` (`fase2-05-2-check-all/`): 13 gates, `launch`, `doctor`, smoke y `cleanup` en PASS; `dotnet test` 126/126. E2E: NOT_RUN, sin cambios de frontend ni de contrato HTTP.

### Paso 5.1 — 2026-10-18
- Enseñado: el host ordena el arranque. `Host.StartAsync` valida las options, después arranca los hosted services en orden de registro, y Kestrel va último. El código entre `Build()` y `RunAsync()` queda fuera de ese orden: por eso el paso 5 necesitó `IStartupValidator.Validate()` a mano, una API pensada para hosts que .NET 11 deja obsoleta. Ahora `DatabaseInitialization` (`Infrastructure/Persistence`) migra desde `StartAsync`, y `Program.cs` no sabe nada de la base. Los registros pasan a `AddOptionsWithValidateOnStart<T, V>().BindConfiguration(...)`, y `AddAuthFeature` y `AddTemplateObservability` ya no reciben `IConfiguration`.
- Preguntas: Tobi dejó la decisión en manos del agente ("como lo consideres") y pidió avisar al terminar.
- Decisiones: la fila del paso 5 sobre `IStartupValidator` queda reemplazada en `docs/decisions.tsv`. La investigación del paso 5, hecha después de implementarlo, está en [investigacion/paso-05-validacion-configuracion.md](investigacion/paso-05-validacion-configuracion.md). `informe-dotnet-empresarial.md` §4.4 nombraba `Options.DataAnnotations`; ahora nombra el validador generado.
- Evidencia:
  - Rojo (`fase2-05-1-red/`): con la migración otra vez antes de `RunAsync`, fallan los 3 casos de `Session` y `Otel` de `ConfigurationValidationTests`. Se revirtió.
  - `check-all --no-e2e` (`fase2-05-1-check-all/`): 13 gates, `launch`, `doctor`, smoke y `cleanup` en PASS; `dotnet test` 126/126, con la base migrada por el hosted service.
  - E2E: NOT_RUN. El paso no cambia frontend ni contrato HTTP.

### Paso 5 — 2026-10-18
- Enseñado: fail-fast; la configuración es un contrato. `SessionOptions`, `DatabaseOptions` y `TelemetryOptions` llevan sus reglas (`[Range]`, `[Required]`, `IValidatableObject`) y las aplica un validador generado con `[OptionsValidator]`. `ValidateOnStart` solo corre en `RunAsync`, después de migrar la base: `Program.cs` llama a `IStartupValidator.Validate()` apenas construye la app. La cookie lee `IOptions<SessionOptions>` en vez de una segunda lectura sin validar. El test corre la API como proceso porque el `catch` de `Program.cs` devuelve 1 sin relanzar y `WebApplicationFactory` no ve el error.
- Preguntas: qué es un host. Tobi respondió que quedó claro y que no hacía falta más explicación.
- Decisiones: dos filas en `docs/decisions.tsv`. Tobi dejó en manos del agente cómo apagar las e2e: `check-all` corre con `--no-e2e` hasta que se necesiten, sin cambiar el script.
- Evidencia:
  - Tests nuevos (`ConfigurationValidationTests`): 7/7. Seis valores inválidos salen con código 1 y el mensaje de la clave, sin `NpgsqlException`; la configuración válida pasa la validación y llega a la base.
  - Rojo (`fase2-05-config/violation-no-early-validate.txt`): sin `IStartupValidator.Validate()` fallan los 3 casos de `Session` y `Otel` (la API intenta la base primero). Se revirtió.
  - Manual (`fase2-05-config/manual-invalid-session.txt`): `Session__IdleTimeoutMinutes=0` sale con código 1 y `OptionsValidationException`.
  - `check-all --no-e2e` (`fase2-05-check-all/`): 13 gates, `launch`, `doctor`, smoke y `cleanup` en PASS; `dotnet test` 126/126.
  - E2E: NOT_RUN por pedido de Tobi. Una corrida previa con e2e quedó cortada (`fase2-05-config/summary-interrumpida.json`); su contenedor se cerró con `verify.mjs cleanup`.

### Paso 4 — 2026-10-18
- Enseñado: la versión de la API pasa de texto repetido a versión declarada. Un solo grupo, `/api/v{version:apiVersion}` con `HasApiVersion(1.0)`, vive en `Infrastructure/Http/VersionedApi.cs`, y cada feature cuelga de él (`api.MapTasksEndpoints()` en `Program.cs`). La librería informa `api-supported-versions`, rechaza `/api/v9/...` con 404 y completa la versión en los links. Los `Location` salen del nombre del endpoint con `LinkGenerator.GetPathByName`, no de la ruta escrita. La trampa: un grupo con `{version}` sin versiones declaradas acepta cualquiera (`/api/v9/raw` dio 200); la frenan DUNE0012 al compilar y `EveryApiEndpoint_HasVersionMetadata` al ejecutar.
- Preguntas: qué es lo más profesional para la versión (URL, header o query) y qué dice la regla 3. Tobi eligió la URL y respondió "entendí" a la explicación final.
- Decisiones: seis filas en `docs/decisions.tsv`. Tobi eligió `Asp.Versioning.Http` y `Asp.Versioning.OpenApi`, la versión en la URL y corregir ADR-0004 punto 4. La investigación previa (regla 12) está en [investigacion/paso-04-versionado-api.md](investigacion/paso-04-versionado-api.md).
- Evidencia:
  - Caracterización (`fase2-04-characterization/`): con el código viejo, los tests nuevos de `Location` en `CreateTaskTests` y `RegisterUserTests` pasan, 38/38.
  - Rojo (`fase2-04-red/`): sin la librería falta `api-supported-versions`; falla 1 de 43. Los 404 y el query ignorado ya pasan: son el contrato de hoy.
  - Verde (`fase2-04-backend/`): `dotnet test TemplateApp.slnx` 116/116. Los `Location` no cambiaron.
  - Rojo de metadata (`fase2-04-metadata-red/`): `app.MapGet("/api/v1/raw")` sobre el host da 1 de 44 en rojo. Se revirtió.
  - Rojo de DUNE0012 (`fase2-04-rule-red/`): con el analyzer vacío fallan 2 de 75 tests de Dune (las dos violaciones; la fixture válida pasa). En el producto, `app.MapGroup("/api/v1/legacy")` y una feature que recibe el host frenan el build con DUNE0012. Se revirtió.
  - `check-all` (`fase2-04-check-all/`): 13 gates, `launch`, `doctor` y smoke en PASS (16/16). El smoke comprueba `api-supported-versions: 1.0` y `/api/v9/health/live` → 404.
  - E2E: NOT_RUN. Tobi pidió cortarlas mientras se desarrolla; el frontend no cambió. La corrida cortada dejó restos que hicieron fallar `check-comments`; se borraron (pendiente anotado).
  - Documento OpenAPI de Development: NOT_RUN en el repo; lo vio el experimento de la investigación.

### Paso 3 — 2026-10-18
- Enseñado (explicado el 2026-10-18, después del cierre): un gate vale si prueba algo real (conteos de tests distintos de cero), si se pone rojo con una violación deliberada en el step correcto (PR #42, una falla por job) y si bloquea. El tercer punto no se cumple: sin protección de rama, CI avisa pero no frena un merge.
- Preguntas: Tobi preguntó qué entendía el agente que estaban haciendo. Con eso marcó que los pasos 2 y 3 se habían cerrado sin enseñar; de ahí salió la regla 11 de `AGENTS.md`. No preguntó sobre el concepto.
- Decisiones: Tobi decidió dejar `main` sin protección (el repo privado del plan gratis no la permite) y dejar las e2e de CI para otro paso. Dos filas en `docs/decisions.tsv`.
- Evidencia:
  - Verde (run `39091828498`, push del merge de #41): los 4 jobs en `success`. Backend 38 tests de producto y 72 de Dune; `check-structure` corre; `check-frontend` 32/32; `check-comments` 18/18; smoke `--compose` con `"result": "PASS"`. Mismos conteos que `check-all.mjs` en el paso 2.
  - Artifact: `otel-export` (31 991 bytes) se descargó con `gh run download`: `logs.json`, `metrics.json` y `traces.json` con contenido.
  - Rojo (PR de prueba #42, run `39092222604`): cada job falla en su step. Backend: `CloseSessionTests` (`failed: 1`). Frontend: Prettier sobre `app.config.ts`. Comentarios: `NOCOMMENT dune/check-structure.mjs:2`. Compose: `GET tasks devolvió 401, se esperaba 200`. El artifact `otel-export` se publicó igual (`if: always()`). El PR se cerró sin mergear y su rama se borró.
  - Protección de `main`: NOT_RUN. La API devuelve 403 (`Upgrade to GitHub Pro or make this repository public`).
  - E2E en CI: NOT_RUN, por decisión de Tobi.
  - `ci.yml`: sin cambios. El plan lo cambia solo si un job falla, y ninguno falla.

### Paso 2 — 2026-10-17
- Enseñado (explicado el 2026-10-18, después del cierre): un pipeline local reproducible. `check-all.mjs` corre en orden los 13 gates del README y el runtime; el primer `FAIL` corta con `break` (`check-all.mjs:201-203`) y lo siguiente queda `NOT_RUN`; `cleanup` corre siempre después de `launch` (`:251`). El smoke HTTP sigue con una sola fuente: el wrapper llama a `verify.mjs api`.
- Preguntas: ninguna sobre el concepto.
- Decisiones: una fila en `docs/decisions.tsv` (el wrapper corta en el primer fallo y guarda el runtime en una subcarpeta).
- Evidencia:
  - Comando nuevo (`fase1-02-check-all/`): 19 de 19 etapas en PASS. `dotnet test` 110/110, `check-structure` 24/24, `check-comments` 18/18, `check-frontend` 32/32, Vitest 13/13, smoke HTTP PASS, e2e 33/33 con Edge, `cleanup` PASS y sin contenedores ni volúmenes propios después.
  - Comandos sueltos (`fase1-02-loose/`, `fase1-02-loose-runtime/`): los mismos 13 gates con exit 0 y los mismos conteos; `launch`, `doctor`, `api`, e2e (33/33) y `cleanup`: PASS. Las advertencias del build cambian (412 líneas frente a 24) porque el segundo build es incremental, como ya dice [linea-base.md](linea-base.md).
  - Rojo (`fase1-02-check-all-red/`): un espacio de más en `playwright.config.ts` da `FAIL` en `format-check`, exit 1, y deja `NOT_RUN` las 8 etapas siguientes. Se revirtió.
  - `E2E_OUTPUT_DIR=../artifacts/...` desde `frontend` dejó `report.json` en `artifacts/verify-template/fase1-02-loose-runtime/e2e/`; `frontend/artifacts/` no existe.
  - `check-structure`: PASS dentro del comando nuevo y suelto.
  - Compose (`fase1-02-compose/`): NOT_RUN. `docker compose up` falla porque `hermes-bot-omp`, otro stack, ocupa 55437, 4317, 18080 y 18081. Con `POSTGRES_PORT` cambiado sigue chocando en 4317, que `compose.yaml` fija. No se paró el stack ajeno.

### Paso 0.28 — 2026-10-17
- Enseñado: un test prueba los endpoints que existen hoy; una regla estática frena el cambio que todavía no tiene test. `UseAuthAntiforgery` cubre solo las escrituras bajo `/api`: un `app.MapPost("/webhooks/x")` quedaría sin antiforgery y ningún test lo notaría. DUNE0011 (`ApiRouteAnalyzer`) mira cada llamada de ruteo de ASP.NET Core: un `MapGroup` sobre el host necesita un prefijo literal `/api` o `/api/...`; `MapPost`, `MapPut`, `MapPatch`, `MapDelete`, `MapMethods` y `Map` sobre el host solo valen en un `*Endpoint.cs`; y `XEndpoint.Map(arg)` necesita que `arg` sea un `RouteGroupBuilder`, así una slice no recibe el host. `BannedSymbols.txt` prohíbe las tres salidas del framework: `DisableAntiforgery()`, `[RequireAntiforgeryToken(false)]` y `UseAntiforgery()`, que solo valida endpoints con formularios. En el frontend, FRONT015 extiende a todos los archivos la prohibición de `localStorage` y `sessionStorage` que FRONT003 tenía solo en componentes, y FRONT016 rechaza un `Authorization` escrito como nombre de propiedad (`setHeaders: { Authorization }`) o como texto exacto (`headers.set('authorization', …)`).
- Preguntas: ninguna todavía.
- Decisiones: seis filas nuevas en `docs/decisions.tsv`: una por regla (FRONT015, FRONT016, DUNE0011 y las prohibiciones de antiforgery) y dos por lo que no se hizo (sin regla de registro del middleware y sin prohibir JwtBearer).
- Evidencia:
  - Rojo, `check-frontend` (`fase0-28-rules-red/check-frontend-test.log`): sin las reglas fallan 2 de 32 tests, los de la violación deliberada de FRONT015 y FRONT016. Las fixtures válidas pasan.
  - Rojo, Dune (`fase0-28-rules-red/dune-tests.log`): con el analyzer vacío y sin las entradas nuevas en `BannedSymbols.txt` fallan 4 de 72 tests: los tres de DUNE0011 que esperan diagnósticos y el que lee el `BannedSymbols.txt` real. Las fixtures válidas pasan. El test de las prohibiciones lee el archivo real, así un id mal escrito lo pone en rojo.
  - Rojo, producto frontend (`fase0-28-rules-red/check-frontend-product-red.log`): un `sessionStorage.setItem` en `session.ts` y un `Authorization` en `auth-interceptor.ts` dan FRONT015 y FRONT016 en la línea exacta. Se revirtió.
  - Rojo, producto backend (`fase0-28-rules-red/product-red-build.log`): `MapGroup("/v1/tasks")`, `ListTasksEndpoint.Map(app)`, `app.MapPost("/webhooks/demo")`, `.DisableAntiforgery()` y `app.UseAntiforgery()` frenan el build: tres DUNE0011 y dos RS0030. Se revirtió.
  - Verde (`fase0-28-gates/`): `dotnet build TemplateApp.slnx --configuration Release --no-incremental` 0 errores (195 warnings, del pendiente sobre warnings de analyzers); `dotnet test TemplateApp.slnx --configuration Release` 110/110 (38 de producto y 72 de Dune); `CommentVerifier`, `check-structure` (y sus 24 tests), `check-comments` (y sus 18 tests), `check-frontend` (y sus 32 tests): PASS. `ng build`, `format:check` y Vitest (13/13): PASS. `check-structure` corrió bien dentro del worktree del agente.
  - Runtime, smoke HTTP, e2e y navegador: NOT_RUN. El paso solo agrega reglas; no cambia el producto ni el frontend.

### Paso 0.27 — 2026-10-17
- Enseñado: Data Protection es el servicio de ASP.NET Core que cifra la cookie `__Host-session` y el token antiforgery. Usa un anillo de claves (archivos `key-*.xml`). Sin configuración, en el contenedor las guarda en `/home/app/.aspnet/DataProtection-Keys`, dentro del disco del contenedor, y avisa en el log: `Storing keys in a directory ... that may not be persisted outside of the container`. Recrear el contenedor borra ese disco: la API arranca con claves nuevas, no puede descifrar las cookies viejas y todas las sesiones dan 401, aunque su fila siga en `Sessions`. Ahora `BackendInfrastructure.AddDataProtectionKeys` fija `SetApplicationName("TemplateApp")` (las claves no dependen de la carpeta de la app) y, si existe `DataProtection:KeysDirectory`, llama a `PersistKeysToFileSystem`. El `Dockerfile` define esa variable, crea la carpeta `/var/lib/templateapp/data-protection-keys` con dueño `app` (así el volumen vacío hereda ese dueño) y `compose.yaml` monta ahí el volumen `dataprotection`. Fuera de Docker, sin la variable, quedan en el perfil del usuario, como antes.
- Preguntas: Tobi eligió un volumen de Docker, sin paquete nuevo. PostgreSQL queda como opción para varias instancias.
- Decisiones: tres filas nuevas en `docs/decisions.tsv` (volumen, configuración en `Infrastructure`, recrear en el smoke).
- Evidencia:
  - Rojo, backend (`fase0-27-backend-red/dotnet-test.log`): `DataProtectionKeysTests` falla con el código del 0.26: la carpeta configurada no existe, porque nadie guarda claves ahí (`DirectoryNotFoundException`).
  - Rojo, Compose (`fase0-27-compose-red/`): con el código del 0.26 y el smoke nuevo, `smoke-api.mjs --compose` recrea la API (`docker compose up --detach --no-deps --force-recreate api`) y la misma cookie de alice da 401 (`smoke-compose.log`). El log de la API muestra la advertencia de claves dentro del contenedor (`api.log`).
  - Verde, backend: el test nuevo pasa (`fase0-27-gates/dotnet-test-green-probe.log`). Otra instancia con la misma carpeta acepta la cookie (200) y una con otra carpeta la rechaza (401).
  - Verde, Compose (`fase0-27-compose/`): el contenedor corre como `app` (uid 1654) y la carpeta es de `app` (`keys-dir.log`). Primer intento del smoke: FAIL por `ECONNRESET` en la primera request, porque la API todavía no escuchaba (`smoke-compose-try1.log`; queda como pendiente). Después de esperar la readiness: `smoke-api.mjs --compose` PASS, con la sesión viva después de recrear la API y la exportación OTLP observada. `recreate-probe.log`: la misma cookie da 200 después de `--force-recreate` y después de `docker compose down` sin `-v` y `up`; en el volumen hay un solo `key-*.xml`.
  - verify-template (`fase0-27-runtime/`): prereqs, build, tests (38/38), PostgreSQL, API, Angular, ready y smoke HTTP: PASS. Navegador: NOT_RUN, el paso no cambia el frontend. Cleanup PASS.
  - Gates (`fase0-27-gates/`): `dotnet build TemplateApp.slnx --configuration Release` 0 errores; `dotnet test TemplateApp.slnx --configuration Release` 104/104; `CommentVerifier`, `check-structure` (y sus 24 tests), `check-comments` (y sus 18 tests): PASS. `check-structure` corrió bien dentro del worktree del agente. `ng build`, Vitest, `check-frontend` y e2e: NOT_RUN, el paso no toca el frontend.

### Paso 0.26 — 2026-10-17
- Enseñado: la sesión vive en el servidor (BFF). `OpenSession` y `RegisterUser` crean una fila en `Sessions` (`Auth/Domain/Session`) y `SessionHttpPolicy.SignInAsync` emite la cookie `__Host-session` de la autenticación por cookie de ASP.NET Core: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, con el id del usuario y el de la sesión dentro del ticket cifrado. En cada request, `OnValidatePrincipal` llama a `Sessions.IsActiveAsync`: sin fila, vencida (7 días) o inactiva (30 minutos), rechaza la sesión y borra la cookie. `LastSeenAt` se escribe como mucho una vez por minuto. `CloseSession` borra la fila: la misma cookie da 401 en la request siguiente, igual que si un admin borra la fila. Como la cookie viaja sola, `UseAuthAntiforgery` exige antiforgery en toda escritura bajo `/api` (`Auth.AntiforgeryFailed`), y abrir o cerrar sesión renueva `XSRF-TOKEN` para la identidad nueva. Angular ya no guarda tokens: `Session.restore()` pide `XSRF-TOKEN` y `GET /users/me`; el interceptor solo termina la sesión ante un 401 con la sesión abierta. Se fueron JWT, `TokenService`, `Jwt/`, `RefreshSession`, `RefreshToken`, `TokenResponse`, `AuthResponse`, el paquete JwtBearer, la renovación y `navigator.locks`. Un cliente móvil sumaría un esquema Bearer sobre los mismos endpoints.
- Preguntas: Tobi eligió BFF. Las decisiones del paso (cookie, vencimientos, borrar la fila, antiforgery en un middleware, `/users/me` como "quién soy", migración sin backfill, interceptor, Dune sin cambios) son ocho filas nuevas en `docs/decisions.tsv`.
- Evidencia:
  - Rojo: `fase0-26-backend-red/dotnet-test.log`. Con el código del 0.25, 31 de 37 tests fallan: 30 en el primer contrato nuevo (`register` todavía devuelve `accessToken` en el body) y `Register_RotatesTheCsrfToken_And_TheNewOneAcceptsWrites` porque el registro no renovaba `XSRF-TOKEN`.
  - Verde: `fase0-26-backend/dotnet-test-try1.log`, 37/37 al primer intento. Tests nuevos: `CloseSessionTests` (logout y la misma cookie da 401; borrar la fila corta el acceso y la otra sesión sigue; vencida e inactiva dan 401; `LastSeenAt` una vez por minuto), `IssueCsrfTokenTests` (400 `Auth.AntiforgeryFailed` en `/auth` y en `POST`, `PUT` y `DELETE` de Tasks y Users sin cambiar nada; el registro renueva `XSRF-TOKEN`), `OpenSessionTests` (sin token en el body, `XSRF-TOKEN` como única cookie legible, sin cookie con credenciales malas) y `GetUserTests` (la cookie sola restaura la sesión). Se borró `RefreshSessionTests` con su slice.
  - Gates (`fase0-26-gates/`): `dotnet build TemplateApp.slnx` 0 errores; `dotnet test TemplateApp.slnx` 103/103 (backend y Dune); `CommentVerifier`, `check-structure`, `check-comments` y sus tests, `ng build`, `format:check`, Vitest 13/13, `check-frontend` y sus 28 tests: PASS. `check-structure` corrió bien dentro del worktree del agente.
  - Runtime (`fase0-26-runtime/`): launch con build, tests, PostgreSQL, API y Angular en PASS; `smoke-api.mjs` PASS: `__Host-session` con sus atributos, `XSRF-TOKEN` única cookie legible, ningún body con token, antiforgery en una escritura, restauración con la cookie sola, 401 de la cookie de una sesión cerrada.
  - E2E (`fase0-26-e2e/`): 33/33 con Edge al primer intento (sin `ERR_NO_BUFFER_SPACE`). `session.spec` nueva: cookie HttpOnly que JavaScript no lee, recarga, 404, 401 que lleva a `/login?returnUrl`, la sesión cerrada desde otro contexto corta esta página en su request siguiente, ninguna request con `Authorization`.
  - Navegador a mano (`fase0-26-browser/`, `playwright-cli` con Edge): después de recargar sigue `/tasks` con el usuario; `document.cookie` solo ve `XSRF-TOKEN`; `localStorage` y `sessionStorage` vacíos; `__Host-session` `HttpOnly`, `Secure` y `Strict` sobre `http://127.0.0.1` (Edge acepta `Secure` ahí); `Salir` en una pestaña deja la otra en `/login` sin `__Host-session`.

### Paso 0.25 — 2026-10-17
- Enseñado: un servidor MCP es un proceso que el agente arranca y al que le pide herramientas por JSON-RPC sobre stdin y stdout. `.mcp.json`, en la raíz, es la configuración de proyecto de Claude Code. Declara `angular-cli` con `node frontend/node_modules/@angular/cli/bin/ng.js mcp --read-only --root frontend`. Usa el CLI 22.2.1 del proyecto, no el último de npm. `node` evita el envoltorio `cmd /c` que pide `npx` en Windows. `--root frontend` limita la búsqueda de workspaces y el acceso a archivos. `--read-only` quita las tres herramientas que ejecutan algo: `run_target`, `devserver_start` y `devserver_stop`.
- Preguntas: Tobi decidió sumar el MCP en solo lectura.
- Decisiones: una fila nueva en `docs/decisions.tsv`. `frontend/.vscode/mcp.json` (VS Code, del scaffold) pasa a `@angular/cli@22.2.1` con `--read-only`. Codex y Cursor quedan sin configurar: su formato de proyecto no es trivial de probar acá. `AGENTS.md` (Verificación) lo nombra.
- Evidencia: artifacts/verify-template/fase0-25-angular-mcp/.
  - `mcp-probe.mjs` lee `.mcp.json`, arranca el servidor como lo haría el agente y habla MCP. Respuestas: `initialize` devuelve `angular-cli-server` 22.2.1; `tools/list` da 6 herramientas (`ai_tutor`, `get_best_practices`, `search_documentation`, `list_projects`, `onpush_zoneless_migration`, `devserver_wait_for_build`); `list_projects` encuentra `frontend/angular.json` con el proyecto `frontend` (application, `build`, `serve` y `test`) (`mcp-probe.log`).
  - Comparación: sin `--read-only`, el mismo servidor da 9 herramientas, con `run_target`, `devserver_start` y `devserver_stop` (`mcp-probe-without-read-only.log`). Es la prueba de que la opción hace lo que dice.
  - `claude mcp list` ve el servidor del proyecto como "Pending approval" (`claude-mcp-list.log`). Aprobarlo es de Tobi, la primera vez que abra Claude Code en el repo.
  - Build de Angular, `format:check`, `check-frontend` (28/28 tests), `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - Navegador: NOT_RUN. El paso no cambia código de la app.

### Paso 0.24 — 2026-10-17
- Enseñado: el recorrido del navegador como código que corre igual cada vez. Los 32 pasos del flujo de `playwright-cli` pasaron a specs de Playwright Test en `frontend/e2e/`, por feature: `auth` (9), `session` (5), `tasks` (12), `profile` (3) y `tabs` (3). Buscan por rol y por label (`getByRole('navigation', { name: 'Paginación' })`, `getByLabel('Nueva tarea')`). Las aserciones esperan solas (`toHaveText`, `expect.poll`), sin pausas fijas salvo para probar que no salió una request. Cada archivo usa una página y un usuario propios, en modo serie. El limiter de `/auth` (20 por minuto para toda la API) se respeta con `waitForAuthRoom()`: cuenta las requests a `/api/v1/auth/` de los últimos 61 segundos y, antes de cada test, espera hasta que queden 6 o menos. Un solo worker. `E2E_BASE_URL` apunta a una instancia de `verify-template` y `E2E_CHANNEL=msedge` usa el Edge instalado.
- Preguntas: Tobi pidió comparar Playwright con tester-army/e2e. El coordinador eligió Playwright Test: tester-army/e2e es pre-1.0, pide un modelo de IA y manda telemetría por defecto.
- Decisiones: una fila nueva en `docs/decisions.tsv`. `@playwright/test` queda fijo en 1.64.1, la misma versión menor que el `playwright-core` 1.64 de `playwright-cli`. `README.md`, la skill `verify-template` y `conventions.md` dicen cómo correrlo. CI queda pendiente para el paso 3 (ver Pendientes).
- Evidencia: artifacts/verify-template/fase0-24-playwright-e2e/, artifacts/verify-template/fase0-24-e2e-try1/, artifacts/verify-template/fase0-24-e2e-red/ y artifacts/verify-template/fase0-24-e2e/.
  - Primera corrida (`fase0-24-e2e-try1`): 31 de 32. En "abrir sesión en una pestaña", el registro dio 201 pero el chunk lazy de `/tasks` no cargó: `net::ERR_NO_BUFFER_SPACE`, un error de sockets de Windows, no de la app. La página mostró "Error inesperado". El trace quedó en la carpeta.
  - Rojo: con el label "Nueva tarea" cambiado a "Tarea nueva" en `tasks.html`, `tasks.spec.ts` falla en el primer test: `getByLabel('Nueva tarea')` no encuentra el elemento, y los 11 tests siguientes del archivo no corren (`fase0-24-e2e-red/e2e/playwright.log`). Después se restauró el label.
  - Verde (`fase0-24-e2e`): 32 passed en 8,2 minutos contra una instancia real, después del smoke HTTP y 65 segundos de espera (`e2e/playwright.log`, `e2e/results/report.json`). Ningún test chocó con el limiter: cada login y registro esperaba su 200 o 201.
  - `tsc --strict` sobre `e2e/`: PASS. Build de Angular, Vitest 13/13, `format:check`, `check-frontend` (28/28 tests), `check-structure`, `check-comments` y `CommentVerifier`: PASS.

### Paso 0.23 — 2026-10-17
- Enseñado: un formato que decide una herramienta. `frontend/.prettierrc` ya existía (100 columnas, comillas simples, parser `angular` para los `.html`), pero nadie lo corría: 31 archivos no lo cumplían. `npm run format:check` (`prettier --check .`) es ahora un gate del README y del job `frontend` de CI, y `npm run format` arregla. Prettier 3 respeta `.gitignore`: no mira `dist/`, `.angular/cache` ni `node_modules`. En los templates, el parser `angular` cuida los espacios: corta líneas dentro de las etiquetas (`>Página … </span
>`) para no agregar espacios visibles.
- Preguntas: Tobi decidió sumar el gate.
- Decisiones: una fila nueva en `docs/decisions.tsv`. Alcance: todo `frontend/`, porque ahí vive la configuración. Los `.mjs` de `dune/` quedan fuera: ninguna configuración los cubre, y sumarla sería otra regla. `README.md`, `conventions.md` y CI actualizados.
- **Este paso tiene dos commits**: primero `style: format the frontend with prettier`, que solo formatea, y después el gate. Así el diff del gate no se mezcla con 31 archivos de formato.
- Evidencia: artifacts/verify-template/fase0-23-prettier-check/ y artifacts/verify-template/fase0-23-runtime/.
  - Antes: `prettier --check .` falla con 31 archivos (`prettier-check-before.log`). Después del formateo pasa (`prettier-check-after-format.log`).
  - Rojo antes de verde: con `http-error.ts` mal formateado a propósito (`import {HttpErrorResponse} from "@angular/common/http"`), `npm run format:check` falla y nombra ese archivo (`format-check-red.log`). Restaurado, pasa (`format-check-green.log`).
  - El formateo no cambia comportamiento: build PASS, Vitest 13/13, `check-frontend` (28/28 tests), `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Navegador PASS con los 33 pasos del 0.21 sobre los templates formateados. Cleanup PASS.

### Paso 0.22 — 2026-10-17
- Enseñado: pruebas unitarias y de componente con Vitest y jsdom. El builder `@angular/build:unit-test` compila los `*.spec.ts` y los corre en Node, sin navegador ni zone.js. Las funciones puras se prueban con entradas y salidas: `returnTarget` (rutas internas, `//`, `/\`, URLs absolutas) y `describeHttpError` (sin red, 400 con ProblemDetails, textos fijos de 403 y 404, `detail` en 401, 409 y 500). `TaskPager` se prueba con TestBed y "Act, Wait, Assert": `fixture.componentRef.setInput()` cambia las entradas, `await fixture.whenStable()` deja que Angular dibuje y el test lee el DOM. La salida se lee con `pageChange.subscribe()`. No hace falta `HttpClient` ni dobles: es la ganancia de separar el componente de presentación en el 0.18.
- Preguntas: Tobi decidió adoptar la suite.
- Decisiones: una fila nueva en `docs/decisions.tsv`. Sin regla nueva de `check-frontend`: exigir un spec por componente sería ceremonia sin valor claro hoy. `AGENTS.md` (Verificación), `README.md` (Ejecutar gates), `frontend/README.md`, `conventions.md` (Pruebas), `plan.md` (estado del frontend) y el job `frontend` de CI actualizados. Se quita el pendiente del `.spec.ts` que generaba el CLI: ahora la suite lo usa.
- Evidencia: artifacts/verify-template/fase0-22-vitest/.
  - Verde: `ng test --watch=false` corre 3 archivos y 13 tests en 1,4 s (`ng-test.log`). El comando de CI, `npm test -- --watch=false` con `CI=true`, da lo mismo (`npm-test-ci-command.log`).
  - Rojo: cada spec falla contra una implementación rota a propósito, y después se restauró el archivo. `returnTarget` sin el control de `//` devuelve `//evil.example` (`ng-test-red-return-url.log`). `describeHttpError` con el `detail` en el 403 devuelve `internal` (`ng-test-red-http-error.log`). `TaskPager` con `page() > totalPages()` deja `Siguiente` habilitado en la última página (`ng-test-red-task-pager.log`). `TaskPager` que emite `page()` en lugar de `page() + 1` da `[2, 1]` en vez de `[3, 1]` (`ng-test-red-task-pager-output.log`). En cada caso falla 1 test y pasan 12.
  - Build de Angular: PASS. `check-frontend` (28/28 tests), `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - Navegador: NOT_RUN. El paso no cambia código que llegue al navegador: solo suma `*.spec.ts` (fuera de `tsconfig.app.json`), docs y CI. El build lo confirma.

### Paso 0.21 — 2026-10-17
- Enseñado: dos clases de error, dos lugares. Un error de campo vive junto al campo: `TaskRow` muestra "El título es obligatorio (máximo 200 caracteres)." justo después del input, en rojo como en login, registro y perfil, y el input lo nombra con `aria-describedby`. Un lector de pantalla lo lee al volver al campo. Un error de la acción (la API respondió mal) sigue en el `role="alert"` bajo la lista. El mensaje aparece cuando el campo está tocado e inválido: `submit()` marca el campo como tocado. Se van `renameRejected` y `Tasks.rejectTitle()`. El diálogo: el preflight de Tailwind pone `margin: 0` y le quitaba el `margin: auto` con que el navegador centra un `<dialog>` modal; `m-auto` lo devuelve.
- Preguntas: Tobi decidió centrar el diálogo y llevar el error bajo su campo ("hacé lo que creas mejor"), y mantener los errores rojos de campo y el tachado.
- Decisiones: tres filas nuevas en `docs/decisions.tsv` (incluye la de los pasos 0.21 a 0.25). `plan.md` (pasos 0.21 a 0.25), `conventions.md`, `architecture.md` y la receta `features/tasks.md` de `verify-template` actualizados. Se quita el pendiente del diálogo arriba a la izquierda.
- Evidencia: artifacts/verify-template/fase0-21-dialog-and-edit-error/, artifacts/verify-template/fase0-21-runtime-red/, artifacts/verify-template/fase0-21-runtime/ y artifacts/verify-template/fase0-21-runtime-2/.
  - Sin regla nueva de Dune: el paso cambia comportamiento visible y lo prueba el navegador. `check-frontend` sigue con 28/28 tests.
  - Rojo antes de verde, navegador (`fase0-21-runtime-red`, código del 0.20): fallan las 2 comprobaciones nuevas. El mensaje no aparece dentro de la fila (estaba bajo la lista). El centro del diálogo queda a dx=448 y dy=271 px del centro del viewport: la caja empieza en (0, 0).
  - Primera corrida verde (`fase0-21-runtime`): 32 de 33 PASS. Falló "abrir Editar en otra fila", por una carrera del test: miraba si había un input de edición antes de que Angular quitara el que acababa de cerrar `Cancelar`. La captura muestra la lista bien. El test ahora espera a que el input desaparezca.
  - Verde (`fase0-21-runtime-2`): 33 pasos PASS. El mensaje es el elemento que sigue al input, `aria-describedby` apunta a su id, no hay alerta bajo la lista y no sale `PUT` (`10-inline-edit-invalid.png`). El centro del diálogo coincide con el del viewport, con 2 px de tolerancia (`12a-delete-dialog-centered.png`). Las 31 comprobaciones restantes no cambian.
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Cleanup PASS.

### Paso 0.20 — 2026-10-17
- Enseñado: un hijo maneja su propio DOM y el padre decide el foco. `TaskDeleteDialog` recibe `task: input<TaskResponse | null>()`. Un `afterRenderEffect` abre su `<dialog>` con `showModal()` cuando llega una tarea y lo cierra con `null`. Lo hace sobre su propio `viewChild`, después de que Angular dibujó la vista. Al cancelar (botón o `Escape`) y al confirmar, cierra el diálogo y recién después avisa `cancelled` o `confirmed`. Así `Tasks` encuentra el diálogo cerrado, igual que antes del paso, y mueve el foco: al `Eliminar` de la fila al cancelar, al encabezado "Mis tareas" después de borrar. `Tasks` pierde `confirmDialog` y el `showModal()`. Para saber si el foco quedó en el diálogo usa `active.closest('dialog')`.
- Preguntas: ninguna nueva; aplica [informe-componentes-angular.md](informe-componentes-angular.md), paso C.
- Decisiones: una fila nueva en `docs/decisions.tsv`. `conventions.md` y `architecture.md` actualizados. El diálogo sigue arriba a la izquierda: centrarlo es decisión de Tobi (pendiente del 0.12).
- Evidencia: artifacts/verify-template/fase0-20-task-delete-dialog/, artifacts/verify-template/fase0-19-runtime-baseline/ y artifacts/verify-template/fase0-20-runtime/.
  - Sin regla nueva de Dune. FRONT013 sigue en verde con `TaskDeleteDialog`, que solo usa `viewChild`. `check-frontend` sigue con 28/28 tests.
  - Línea base (`fase0-19-runtime-baseline`, código del 0.18): las 3 comprobaciones del diálogo ya pasaban. Es el comportamiento que el paso debe conservar.
  - Verde (`fase0-20-runtime`): 32 pasos PASS. Borrar la única tarea de la página 2 manda `DELETE` 204, pide `page=1` y muestra "Página 1 de 1 (10)" con 10 filas. `Eliminar` abre el diálogo con «título» y el foco en `Cancelar` (`12-delete-dialog-open.png`). `Escape` lo cierra sin `DELETE` y el foco vuelve al `Eliminar` de esa misma fila. `Confirmar` manda `DELETE` 204, la fila desaparece y el foco va al encabezado `#tasks-title` "Mis tareas".
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Cleanup PASS.

### Paso 0.19 — 2026-10-17
- Enseñado: el estado vive en quien lo usa. El borrador del título es estado de la fila: `TaskRow` lo guarda en un `linkedSignal` cuya fuente es el título y `editing`. Cuando llega una tarea nueva o se abre la edición, el borrador vuelve solo al título guardado, sin `effect` y sin `startEdit` que lo copie. Quién edita es estado de la página: `editingId` queda en `Tasks`, así abrir otra fila cierra la primera. La edición usa Signal Forms aunque no tenga `<form>`: `form(draft, …)` con `required`, `maxLength(200)` y un `validate` que rechaza el blanco, `[formField]` en el input y `submit()` en `Guardar`. Se van el `$any($event.target)` y la validación a mano de `saveTitle`. `TaskRow` es el propio `<li>` (`li[app-task-row]`), así el DOM y las clases de Tailwind quedan iguales.
- Preguntas: ninguna nueva; aplica [informe-componentes-angular.md](informe-componentes-angular.md), paso B.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. El mensaje sigue en el mismo lugar y con el mismo texto: `TaskRow` avisa `renameRejected` y `Tasks` escribe `actionError`. Moverlo bajo el campo es decisión de Tobi. Los outputs se nombran como hechos (`completedChange`, `renamed`, `removeRequested`…), no `toggle` ni `cancel`, que son eventos del DOM. `conventions.md` y `architecture.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-19-task-row/, artifacts/verify-template/fase0-19-runtime-baseline/ y artifacts/verify-template/fase0-19-runtime/.
  - Sin regla nueva de Dune: el paso cambia la forma, no una regla. FRONT013 sigue en verde con `TaskRow`, que no inyecta nada. `check-frontend` sigue con 28/28 tests.
  - Línea base (`fase0-19-runtime-baseline`, código del 0.18): las 7 comprobaciones nuevas de los pasos 0.19 y 0.20 pasan también con el código viejo. Prueba que miden el comportamiento de hoy y que el paso no lo cambia. No hay rojo posible: el paso es solo forma. Dos corridas anteriores se cayeron porque Docker Desktop se detuvo (Postgres cortó la conexión con `57P01` y después el daemon no respondía). Quedaron en `fase0-19-runtime-baseline-flaky-db/` y `fase0-19-runtime-baseline-docker-down/`.
  - Verde (`fase0-19-runtime`): 32 pasos PASS: los 24 del 0.16, la paginación del 0.18 y las 7 nuevas. Renombrar da `PUT` 200 con el título nuevo e `isCompleted=false`. Un título vacío y uno en blanco muestran "El título es obligatorio (máximo 200 caracteres)." bajo la lista y no mandan `PUT` (`10-inline-edit-invalid.png`). `Cancelar` restaura el título, y reabrir muestra el guardado. Abrir `Editar` en otra fila deja un solo input y la primera fila vuelve a su título. El checkbox manda `PUT` con `isCompleted=true`, y después de recargar la fila sigue hecha.
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Cleanup PASS.

### Paso 0.18 — 2026-10-17
- Enseñado: componente de ruta frente a componente de presentación. `Tasks` es el destino de `/tasks`: guarda la lista y es el único que llama a `TasksApi`. `TaskPager` no inyecta nada de la app: recibe `page`, `totalPages` y `totalCount` con `input.required()`, más `disabled`, y pide otra página con `pageChange: output<number>()`. Así su contrato se lee en su firma: lo que entra y lo que sale. Con OnPush por defecto, Angular revisa el paginador solo cuando cambia una entrada. FRONT013 hace cumplir el límite. Calcula qué componentes son de ruta: los que nombran `loadComponent` o `component` en un `*.routes.ts` (también un `import()` con export default) y el que recibe `bootstrapApplication`. En los demás, pide al type checker el tipo de cada `inject(...)` y de cada parámetro del constructor. Falla si ese tipo se declara bajo `frontend/src/app/` o si es `Router` o `ActivatedRoute`. Como mira el tipo y no el texto, `const api = TasksApi; inject(api)` no lo esquiva.
- Preguntas: ninguna nueva; aplica [informe-componentes-angular.md](informe-componentes-angular.md), paso A.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. `conventions.md` (de ruta y de presentación, una carpeta por componente, sin `components/`) y `architecture.md` (árbol de tasks) actualizados.
- Evidencia: artifacts/verify-template/fase0-18-task-pager/ y artifacts/verify-template/fase0-18-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.17 falla el test de violaciones (`check-frontend-test-red.log`: 27 pasan, 1 falla). La fixture válida pasa también con el gate viejo, como debe. Con el nuevo pasan 28/28 (`check-frontend-test-green.log`). Los stubs ganan `inject<T>()`, `ElementRef`, `input()`, `@angular/router` y `@angular/platform-browser`. La violación tiene 4 casos: `inject(TasksApi)`, el alias, `inject(Router)` y un constructor. La válida tiene un componente de ruta por `loadComponent`, otro por `component`, otro por export default, la raíz `App` con `Session` y `Router`, y `TaskPager` con `ElementRef`.
  - Producto: el código del 0.17 ya cumplía FRONT013 (`check-frontend-product-before.log`: OK), porque todos sus componentes eran de ruta. Para probarla con los tipos reales de Angular se agregó a `TaskPager` `inject(api)` con `const api = TasksApi` e `inject(Router)`, y se revirtió: FRONT013 frena las dos líneas (`check-frontend-product-probe-red.log`).
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Navegador PASS: los 24 pasos del 0.16 y la comprobación nueva. Con 11 tareas creadas desde la UI: "Página 1 de 2 (11)", 10 filas, `Anterior` deshabilitado y `Siguiente` habilitado; `Siguiente` pide `page=2` y muestra "Página 2 de 2 (11)" con 1 fila y `Siguiente` deshabilitado; `Anterior` pide `page=1` y vuelve a 10 filas. Capturas `08-pager-page-1.png` y `09-pager-page-2.png`. Cleanup PASS.

### Paso 0.17 — 2026-10-17
- Enseñado: la herramienta oficial y el gate tienen que decir lo mismo. `ng generate component` creaba un `.css` y un `styleUrl`, y FRONT009 los rechaza: cada componente nuevo costaba una corrección a mano. `angular.json` fija ahora `"style": "none"` para `@schematics/angular:component`, y FRONT014 exige esa línea. La regla lee `angular.json` con el parser JSON de TypeScript y marca la línea exacta que falta o que está mal.
- Hallazgo: `frontend/README.md` decía que `npm test` no tenía runner. Sí lo tiene: el builder `@angular/build:unit-test` usa Vitest con jsdom. Falla solo porque no hay ningún `*.spec.ts` (`ng-test-no-specs.log`). Adoptar la suite sigue siendo decisión de Tobi.
- Preguntas: Tobi pidió aplicar lo que no necesita decisión de herramienta de los dos informes nuevos.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. La regla se llama FRONT014: el informe ya reservaba FRONT013 para el límite de componentes del paso 0.18. `conventions.md`, `plan.md` (pasos 0.17 a 0.20), `README.md` y `frontend/README.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-17-cli-defaults/ y artifacts/verify-template/fase0-17-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.16 fallan los 2 tests nuevos (`check-frontend-test-red.log`: 24 pasan, 2 fallan). Con el nuevo pasan 26/26 (`check-frontend-test-green.log`). Las fixtures cubren `angular.json` válido, `"schematics": {}`, `"style": "css"`, un proyecto sin `schematics` y un frontend sin `angular.json`.
  - Rojo antes de verde, producto: FRONT014 frena el `angular.json` del 0.16 en la línea 11, `"schematics": {}` (`check-frontend-product-red.log`).
  - CLI antes: `ng generate component features/tasks/probe --dry-run` lista `probe.css` (`ng-generate-dry-run-before.log`). Un componente generado de verdad trae `styleUrl` y FRONT009 lo frena (`check-frontend-generated-before.log`).
  - CLI después: el dry-run ya no lista `.css` (`ng-generate-dry-run-after.log`). Un componente generado de verdad pasa `check-frontend` (`check-frontend-generated-after.log`) y se borró sin versionarlo. El CLI sigue creando `probe.spec.ts`: depende de la decisión sobre la suite.
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS con el recorrido del 0.16 (24 pasos), sin cambios: el paso no cambia la app. Cleanup PASS.

### Paso 0.16 — 2026-10-17
- Enseñado: coordinar pestañas del mismo origen con dos APIs del navegador. Las pestañas comparten la cookie de refresh, pero cada una tiene su access token en memoria. Si dos recargan a la vez, las dos presentan el mismo refresh token; el backend ve un reuso y revoca la familia, y las dos quedan sin sesión. `navigator.locks.request('session-refresh', …)` pone las renovaciones en fila: la segunda sale cuando la primera ya dejó la cookie nueva. `BroadcastChannel('session')` avisa `opened` y `closed`: el logout en una pestaña cierra las otras al instante, y un login o registro las abre con el mismo usuario.
- Preguntas: ninguna nueva; aplica la investigación del 0.14.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. `conventions.md` y `architecture.md` actualizados. El margen de rotación en el backend sigue como decisión de Tobi: con el lock no hace falta para un mismo navegador.
- Evidencia: artifacts/verify-template/fase0-16-session-tabs/, artifacts/verify-template/fase0-16-runtime-red/ y artifacts/verify-template/fase0-16-runtime/.
  - Sin regla nueva de Dune. `check-frontend` sigue con 24/24 tests.
  - Rojo antes de verde, navegador (`fase0-16-runtime-red`, código del 0.15): las 3 comprobaciones nuevas fallan. Con dos páginas del mismo contexto y las dos renovaciones retenidas hasta llegar juntas, las renovaciones dan `[401, 200, 401]` y las dos páginas terminan en `/login?returnUrl=%2Ftasks`. El logout en A deja a B en `/tasks`. El registro en A deja a B en `/login`.
  - Verde (`fase0-16-runtime`): 24 pasos PASS: los 15 del 0.14, las 6 del 0.15 y las 3 nuevas. Las dos recargas dan dos `POST /auth/refresh`, los dos 200, una después de la otra, y las dos páginas siguen abiertas. El logout en A lleva a B a `/login` sin tocar B. El registro de otro usuario en A abre B con ese usuario y la lleva a `/tasks`.
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Las comprobaciones corren en tres tandas separadas por 61 segundos, bajo 20 requests de `/auth` por minuto. Cleanup PASS.

### Paso 0.15 — 2026-10-17
- Enseñado: un interceptor funcional con renovación. El token va solo a `/api/`. Las peticiones de `/auth` llevan un `HttpContextToken` (`SKIP_SESSION`): es un dato de la petición que lee el interceptor y que el backend nunca ve, más firme que comparar el texto de la URL. Ante un 401, varias peticiones esperan la misma renovación y cada una reintenta una vez. El `catchError` va antes del `switchMap`: atrapa solo la renovación fallida, no el error del reintento. `returnUrl` vuelve a la página pedida y solo acepta rutas internas, para que nadie arme un link de login que lleve a otro sitio.
- Hallazgo: el informe marcaba un riesgo sin probar, y el rojo lo confirmó. Con el código del 0.14, `/auth/refresh` viajaba con el JWT todavía válido. El token antiforgery se había emitido como anónimo antes del login, así que el backend respondía 400 y la sesión se cerraba. Pasaba en cada 401 con el token vigente (por ejemplo, una familia revocada en otra pestaña). Sin el JWT en `/auth`, todas esas peticiones son anónimas y el token coincide.
- Preguntas: ninguna nueva; aplica la investigación del 0.14.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. `conventions.md` y `architecture.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-15-session-interceptor/, artifacts/verify-template/fase0-15-runtime-red/ y artifacts/verify-template/fase0-15-runtime/.
  - Sin regla nueva de Dune: el paso cambia comportamiento y lo prueba el navegador. `check-frontend` sigue con 24/24 tests.
  - Rojo antes de verde, navegador (`fase0-15-runtime-red`, código del 0.14): 5 de 6 comprobaciones nuevas fallan. Sin `returnUrl` en `/login` (dos casos). Un 401 seguido de un 404 termina en `/login`. Dos 401 a la vez terminan en `/login` porque la renovación da 400. 9 de 39 peticiones de `/auth` llevan `Authorization`. La única que pasa es la del `returnUrl` externo: antes también iba a `/tasks`.
  - Verde (`fase0-15-runtime`): los 15 pasos del 0.14 y las 6 comprobaciones pasan. Un 401 y un 404 en el reintento dejan la sesión abierta y muestran "El recurso solicitado no existe o ya fue eliminado." Dos 401 retenidos y soltados juntos dan un solo `POST /auth/refresh` y dos reintentos. Una renovación fallida lleva a `/login?returnUrl=%2Ftasks`. La contraseña mala no dispara renovación.
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Las comprobaciones nuevas corren después de 61 segundos más, para quedar bajo 20 requests de `/auth` por minuto. Cleanup PASS.

### Paso 0.14 — 2026-10-17
- Enseñado: un solo estado para la sesión. Antes eran tres piezas sueltas: `currentUser`, `initialized` y una `Promise` con `readyResolve` armada a mano. Ahora `Session` guarda una unión discriminada en un `signal` privado: `restoring`, `open` (con el usuario) o `closed`. TypeScript no deja escribir un usuario con la sesión cerrada. Lo público es `computed`: `status`, `currentUser` e `isOpen`. Los guards esperan con `toObservable(session.status).pipe(first(...))`, sin una segunda fuente de verdad. El HTTP de Auth pasó a `AuthApi`, como `UsersApi`; `Session` solo decide el estado. Las acciones usan los verbos del backend: `open`, `refresh`, `close`.
- Preguntas: Tobi pidió investigar la forma más profesional de manejar la sesión en Angular y aplicarla.
- Decisiones: tres filas nuevas en `docs/decisions.tsv`. `conventions.md`, `architecture.md`, `plan.md` (pasos 0.14 a 0.16) y `AGENTS.md` (el informe nuevo) actualizados.
- Evidencia: artifacts/verify-template/fase0-14-session-shape/ y artifacts/verify-template/fase0-14-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.13 falla el test nuevo (`check-frontend-red.log`: 23 pasan, 1 falla). Con el nuevo pasan 24/24 (`check-frontend-green.log`).
  - Producto: el código del 0.13 ya cumplía FRONT012 (`check-frontend-product-before.log`: OK), porque `Session` exponía `asReadonly()`. La regla es preventiva. Para probarla con los tipos reales de Angular se cambió una línea de `Session` a `readonly currentUser = this.currentUserSignal` y se revirtió: FRONT012 la frena (`check-frontend-product-probe-red.log`).
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS con el recorrido del 0.13 (15 pasos), sin cambios: el paso no cambia comportamiento. Cleanup PASS.

### Paso 0.13 — 2026-10-17
- Enseñado: agrupar por propósito, no por tipo técnico. La raíz de cada feature muestra solo su puerta (`index.ts`) y sus rutas. Lo demás vive en una carpeta que dice para qué sirve: `auth/session/` junta todo lo que mantiene la sesión; `tasks/api/` junta el cliente HTTP con sus modelos. Es la misma idea que las slices del backend, y la guía de estilo de Angular desaconseja carpetas como `services/` o `models/`.
- Preguntas: Tobi vio muchos archivos sueltos en `auth/` y pidió agruparlos.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. `conventions.md` y `architecture.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-13-feature-folders/ y artifacts/verify-template/fase0-13-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.12 falla el test nuevo (`check-frontend-red.log`). Con el nuevo pasan 23/23. Cuatro tests viejos tenían fixtures en la raíz de la feature: pasaron a `api/` o `session/`.
  - Rojo antes de verde, producto: FRONT011 frena 13 archivos sueltos en auth, tasks y users (`check-frontend-product-red.log`).
  - Build de Angular: PASS. `check-frontend`, `check-structure` y `check-comments`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS con el recorrido del 0.12 (15 pasos): incluye restaurar la sesión al recargar, los dos guards y el interceptor en cada request. Cleanup PASS.

### Paso 0.12 — 2026-10-17
- Enseñado: CSS utility-first. El estilo de cada elemento está en su `class`: `rounded-lg border border-slate-200 bg-white p-5` reemplaza la regla `section { … }` de `styles.css`. Ya no hay que buscar qué selector pinta un elemento. `styles.css` pasó de 234 líneas a 14: `@import 'tailwindcss'`, un breakpoint `xs` (32rem) en `@theme` y el foco visible en `@layer base`. Se repiten las clases en lugar de usar `@apply`, como recomienda Tailwind.
- Hallazgo: `.field-error` y `.done` vivían en `app.css`. Angular encapsula los estilos de cada componente, así que esas reglas solo valían dentro de `App` y nunca llegaron a los formularios ni a la lista. Con Tailwind la clase está en el mismo elemento: ahora los errores de campo se ven en rojo y las tareas hechas, tachadas.
- Preguntas: Tobi pidió "migrá de CSS a Tailwind".
- Decisiones: cuatro filas nuevas en `docs/decisions.tsv`. `conventions.md` actualizado.
- Evidencia: artifacts/verify-template/fase0-12-tailwind/ y artifacts/verify-template/fase0-12-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.10 falla el test nuevo (`check-frontend-red.log`). Con el nuevo pasan 21/21.
  - Rojo antes de verde, producto: FRONT009 frena `app.ts` y `tasks.ts` (`check-frontend-product-red.log`).
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - Capturas: 13 por versión en `before/` y `after/`, con la API simulada. La comparación píxel a píxel (`pixel-diff-threshold150.json`) da 0 diferencias de forma salvo los errores en rojo y el texto tachado. Con umbral bajo (`pixel-diff-threshold24.json`) aparecen los tonos: la paleta de Tailwind v4 usa OKLCH y, por ejemplo, `blue-700` no es exactamente `#1d4ed8`.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS (15 pasos): el recorrido del 0.10 con dos comprobaciones cambiadas. La fila tiene `border-left-width` 3px; al marcarla hecha (PUT 200) gana borde verde y texto tachado; el fondo del diálogo tiene alfa 0.4. Cleanup PASS.

### Paso 0.11 — 2026-10-17
- Enseñado: un archivo, un tipo. Buscar `TaskListResponse` lleva a `task-list-response.ts`, sin abrir un archivo de modelos. La excepción de Tobi (un tipo que usa solo otro queda junto a él) la hace cumplir el compilador: en C#, un tipo `file` no puede usarse fuera de su archivo; en TypeScript, un tipo sin `export` tampoco. Por eso `SessionCookie` y `SessionResponse` no entran en la excepción: los usan los tests de Auth.
- Preguntas: Tobi pidió la regla, con la excepción del tipo auxiliar.
- Decisiones: tres filas nuevas en `docs/decisions.tsv`. `conventions.md` y `architecture.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-11-one-type-per-file/ y artifacts/verify-template/fase0-11-runtime/.
  - Rojo antes de verde, Dune: con un `OneTypePerFileAnalyzer` vacío falla `SecondTopLevelTypeIsRejected` (`dune-red.log`). Con el analyzer pasan 66/66.
  - Rojo antes de verde, producto backend: DUNE0010 frena `SessionCookie` y `SessionResponse` en `TestSession.cs` (`product-red.log`), y ningún otro archivo.
  - Rojo antes de verde, gate frontend: con el `check-frontend` del 0.10 falla el test nuevo (`check-frontend-red.log`). Con el nuevo pasan 21/21.
  - Rojo antes de verde, producto frontend: FRONT010 frena 5 tipos en `auth-models.ts`, `tasks-models.ts` y `users-models.ts` (`check-frontend-product-red.log`).
  - Build Release: PASS (0 errores). Tests `TemplateApp.slnx`: PASS (96/96: 30 de producto y 66 de Dune). Build de Angular: PASS. `check-frontend`, `check-comments` y `CommentVerifier`: PASS. `check-structure`: PASS en una copia limpia de la rama; en la carpeta de trabajo falla por el worktree de otro agente en `.claude/worktrees/`.
  - `TestReferences` también salió de `AnalyzerHost.cs` a su archivo. Dune no analiza sus propios proyectos: ahí la regla se cumple a mano.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS con el recorrido del 0.10 (15 pasos). Cleanup PASS.

### Paso 0.10 — 2026-10-17
- Enseñado: cada archivo cambia por un solo motivo. Un cambio de vista toca `tasks.html`, uno de estilo toca `tasks.css` y uno de comportamiento toca `tasks.ts`. `tasks.ts` pasó de 333 a 199 líneas. Angular compila `templateUrl` igual que `template`: el resultado en el navegador no cambia.
- Preguntas: Tobi marcó `tasks.ts` como un archivo que junta todo.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. `conventions.md` actualizado.
- Evidencia: artifacts/verify-template/fase0-10-component-files/ y artifacts/verify-template/fase0-10-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.9 falla el test nuevo (`check-frontend-red.log`). Con el nuevo pasan 20/20. Las fixtures de los tests viejos pasaron de `template: ''` a `templateUrl`.
  - Rojo antes de verde, producto: FRONT008 frena login, registro, tareas (vista y estilos) y perfil (`check-frontend-product-red.log`).
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS (15 pasos): el recorrido del 0.8 más un paso nuevo. Ese paso comprueba que `tasks.css` se aplica (borde de la fila y fondo del diálogo) y borra una tarea con el diálogo (DELETE 204). Cleanup PASS.

### Paso 0.9 — 2026-10-17
- Enseñado: providers funcionales. `provideAppInitializer(() => inject(Session).bootstrap())` reemplaza un token multi con `useFactory` y `deps` escritos a mano: la función corre en un contexto de inyección y pide lo que necesita con `inject()`. `bootstrap()` devuelve `void`, así que Angular no lo espera, igual que antes; los guards siguen esperando `session.ready()`.
- Preguntas: ninguna nueva.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. `conventions.md` actualizado.
- Evidencia: artifacts/verify-template/fase0-09-app-initializer/ y artifacts/verify-template/fase0-09-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.8 falla el test nuevo (`check-frontend-red.log`). Con el nuevo pasan 19/19.
  - Rojo antes de verde, producto: FRONT007 frena `app.config.ts` (`check-frontend-product-red.log`).
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS con el recorrido del 0.8 (14 pasos), que incluye la restauración de sesión al recargar y el guard sin sesión. Cleanup PASS.

### Paso 0.8 — 2026-10-17
- Enseñado: el formulario como signals. El modelo es un `signal` (`credentials`, `account`, `newTask`, `profile`), `form()` le agrega reglas y estado (`touched`, `invalid`, `submitting`), y `[formRoot]` llama a `submit()`: marca todo como tocado y corre la acción solo si el formulario es válido. Eso reemplaza el `markAllAsTouched()` y los signals `loading`, `creating` y `saving` escritos a mano. Las reglas escriben `required` y `maxlength` en el HTML: ponerlos a mano es un error de compilación (NG8022).
- Preguntas: ninguna nueva; Tobi aprobó la modernización.
- Decisiones: dos filas nuevas en `docs/decisions.tsv`. `conventions.md` actualizado.
- Evidencia: artifacts/verify-template/fase0-08-signal-forms/ y artifacts/verify-template/fase0-08-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.7 falla el test nuevo (`check-frontend-red.log`). Con el nuevo pasan 18/18.
  - Rojo antes de verde, producto: FRONT006 frena los cuatro formularios del código del 0.7 (`check-frontend-product-red.log`).
  - Build de Angular: PASS. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS (build y tests .NET incluidos), smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS (14 pasos): los formularios inválidos de registro, tareas, perfil y login muestran los mismos mensajes y no mandan la request; el input de tarea conserva `maxlength=200`; crear limpia el formulario sin error; el registro con nombre visible vacío usa el usuario; y el recorrido del 0.6 completo. Cleanup PASS.

### Paso 0.7 — 2026-10-17
- Enseñado: *Intent over Role*. El nombre dice para qué sirve la clase y la carpeta dice de qué feature es: `session.ts` / `Session` maneja la sesión, `tasks-api.ts` / `TasksApi` es el cliente HTTP de Tasks. `@Service()` reemplaza a `@Injectable({ providedIn: 'root' })` en v22. Los otros nombres solo cambian en la puerta (`index.ts`) de cada feature.
- Preguntas: si Angular moderno sigue usando `.service` (no: desde v20 el CLI no genera sufijos). Tobi pidió la investigación, instalar `angular/skills` y modernizar el frontend.
- Decisiones: tres filas nuevas en `docs/decisions.tsv`. `conventions.md` y `architecture.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-07-services/ y artifacts/verify-template/fase0-07-runtime/.
  - Rojo antes de verde, gate: con el `check-frontend` del 0.6 fallan los 2 tests de rechazo nuevos (`check-frontend-red.log`). Con el nuevo pasan 17/17. La fixture válida del adapter HTTP pasó a `tasks-api.ts` / `TasksApi` con `@Service()`.
  - Rojo antes de verde, producto: FRONT004 y FRONT005 frenan 11 casos en los tres servicios, el guard y el interceptor (`check-frontend-product-red.log`).
  - Build de Angular: PASS, con los cinco componentes en chunks lazy. `check-frontend`, `check-structure`, `check-comments` y `CommentVerifier`: PASS.
  - verify-template: launch PASS (build y tests .NET incluidos), smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS con el recorrido del 0.6 (11 pasos). Cleanup PASS.

### Paso 0.6 — 2026-10-17
- Enseñado: el mismo límite de módulo, en el cliente. Cada feature de Angular publica su puerta en `index.ts`, como `Contracts/` en el backend. La raíz de la app compone las features: aplica los guards con `canActivateChild` sobre las rutas que cada feature exporta. Así se evita un ciclo de imports entre `auth` y `users` al cargar la app.
- Preguntas: cómo se hacía siempre lo de las entidades y sus relaciones, comparado con el diseño de features. Tobi delegó la ubicación del guard y del interceptor, y aceptó la puerta por feature.
- Decisiones: cinco filas nuevas en `docs/decisions.tsv`. `architecture.md` y `conventions.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-06-frontend/, artifacts/verify-template/fase0-06-runtime/ y artifacts/verify-template/fase0-06-runtime-2/.
  - Rojo antes de verde, gate: con el `check-frontend` viejo fallan 3 de 14 tests (`check-frontend-red.log`). Con el nuevo pasan 14/14.
  - Rojo antes de verde, producto: el FRONT001 nuevo frena las 4 rutas de `app.routes.ts` que entraban al interior de una feature (`check-frontend-product-red.log`).
  - Build de Angular: PASS. Los cinco componentes siguen en chunks lazy. `check-frontend`: PASS.
  - `check-structure` (24/24), `check-comments` (18/18) y `CommentVerifier`: PASS. Build y tests .NET: PASS dentro de launch (30/30 de producto). El paso no toca el backend.
  - Primera corrida (`fase0-06-runtime`): navegador PASS, smoke HTTP FAIL con 503 en `POST auth/logout`. El navegador acababa de hacer 24 requests de auth y el limiter `auth` permite 20 por minuto para toda la API. Queda como pendiente del paso 12.
  - Segunda corrida (`fase0-06-runtime-2`), smoke primero: smoke HTTP PASS (45 requests). Después de 65 segundos, navegador PASS (11 pasos): registro, recarga, tarea persistida, perfil persistido, zona inválida 400, logout, guard sin sesión, login, guard de invitado, contraseña incorrecta 401 y storage vacío. Capturas y requests en `ui/`. Cleanup PASS.

### Paso 0.5 — 2026-10-17
- Enseñado: dependencia en una sola dirección entre módulos y contrato público. Auth crea y lee el perfil con `Users.Contracts.IUserProfiles`; Users lee y borra la cuenta con `Auth.Contracts.IUserAccounts`. Entre features, una entidad guarda solo el Id del otro lado, sin navegación. Un gate que se esquiva con `var` no hace cumplir la regla: DUNE0004 ahora mira también el tipo de cada expresión.
- Preguntas: cómo resuelve la industria las FKs entre módulos (Tobi pidió investigarlo); si las entidades no deberían tener FKs y navegaciones; si convenía juntar todo el dominio; cómo se ve "crear usuario" cuando la identidad y el perfil son de features distintas.
- Decisiones: Tobi eligió la postura pragmática para las FKs (quedan en la base, declaradas en `AppDbContext`), `IUserAccounts` en Auth.Contracts y validar el DisplayName al crear el perfil. Cinco filas nuevas en `docs/decisions.tsv`. `architecture.md` y `conventions.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-05-auth-users/ y artifacts/verify-template/fase0-05-runtime/.
  - Rojo antes de verde, Dune: con el analyzer viejo fallan `CrossFeatureRejectsForeignDomain` y `CrossFeatureRejectsForeignDomainReachedWithoutNamingIt` (`dune-red.log`). Con el nuevo pasan 63/63.
  - Rojo antes de verde, producto: el DUNE0004 nuevo frena el código del paso 0.4 en `RegisterUser.cs`, `OpenSession.cs` y `TaskItemConfiguration.cs` (`product-red.log`), y en ningún otro archivo.
  - EF `has-pending-model-changes`: PASS (sin cambios de modelo; las FKs se movieron sin migración). Sin la FK de `TaskItems` falla, como debe (`ef-pending-red.log`).
  - Test nuevo `Register_WithBlankDisplayName_Returns400_WithoutPartialRows`: PASS. Con un commit forzado en la rama de error falla con `Assert.Null() Failure` (`register-rollback-red.log`).
  - Build Release: PASS (0 errores, 206 warnings; el paso 0.4 compilado igual da 203). La diferencia es +2 CA1707 y +4 CA2007, del pendiente sobre warnings de analyzers. Tests `TemplateApp.slnx`: PASS (93/93: 30 de producto y 63 de Dune). `CommentVerifier`, `check-structure` (24/24) y `check-comments` (18/18): PASS.
  - verify-template: launch PASS (build, tests, PostgreSQL, API y front), smoke HTTP PASS (45 requests, igual que en el paso 0.4), cleanup PASS.
  - Navegador: NOT_RUN. El paso no cambia contratos HTTP ni el frontend.

### Paso 0.4 — 2026-10-17
- Enseñado: la profundidad se mide en el borde de la feature, no en la slice. Cada slice declara solo sus dependencias: `ListTasks` recibe solo `AppDbContext`, mientras `TasksModule` cargaba `IAccessPolicy` y `TimeProvider` para todas. Lo que se repetía entre slices (regla del título, traducción de validación) baja al `Domain` o a `Shared`. Una interfaz con una sola implementación y un solo uso es un seam hipotético (`IDatabaseReadinessProbe`).
- Preguntas: por qué la slice era un archivo y no una carpeta. Tobi eligió una carpeta con un archivo por pieza. También eligió que lo interno compartido viva en la raíz de la feature y no en `Contracts/`.
- Decisiones: ocho filas nuevas en `docs/decisions.tsv`. ADR-0004 (puntos 2 y 5), `architecture.md` y `conventions.md` actualizados.
- Evidencia: artifacts/verify-template/fase0-04-slices/ y artifacts/verify-template/fase0-04-runtime/.
  - Rojo antes de verde, Dune: con el `NamingAnalyzer` viejo fallan 8 de 61 tests (`dune-red.log`), entre ellos `SharedModuleAtFeatureRootIsRejected` y `SliceInItsFolderIsAccepted`. Con el nuevo pasan 61/61.
  - Rojo antes de verde, `check-structure`: con el script viejo fallan 8 de 24 fixtures (`check-structure-red.log`). Con el nuevo pasan 24/24.
  - Primer intento de tests: 7 de 29 FAIL con 503. Una sola app compartida agotaba el rate limiter `auth`. Se volvió a una app por clase con un solo contenedor (`AssemblyFixture`).
  - Build Release: PASS (0 errores, 203 warnings; antes 217). Tests `TemplateApp.slnx`: PASS (90/90: 29 de producto y 61 de Dune). `CommentVerifier`, `check-structure` (24/24), `check-comments` (18/18): PASS. EF `has-pending-model-changes`: PASS (sin cambios de modelo).
  - verify-template: launch PASS (build, tests, PostgreSQL, API y front), smoke HTTP PASS (45 requests, igual que en el paso 0.3), cleanup PASS.
  - Navegador: NOT_RUN. El paso no cambia contratos HTTP ni el frontend.

### Paso 0.3 — 2026-10-17
- Enseñado: lo que nombra una feature vive en esa feature; cada feature es su propio composition root. Information leakage (el hook de sesión en Infrastructure, `GetCallerId` lejos del JWT) y change amplification (permisos de todas las features en un archivo). Juntar dos interfaces en una más profunda (`IAccessPolicy`). Invertir una dependencia con `IDatabaseSeeder`.
- Preguntas: Tobi aclaró que Dune no es la fuente de verdad: mandan ADR-0004, `architecture.md` y los informes. Por eso las rutas pasaron a `<Feature>Feature.cs`, como dice `architecture.md`.
- Decisiones: cinco filas nuevas en `docs/decisions.tsv`; la del código muerto pasa a `aceptada`.
- Evidencia: artifacts/verify-template/fase0-03-reparto/ y artifacts/verify-template/fase0-03-runtime/.
  - DUNE0001 frenó `AppIdentityUser` en `Auth/Domain`. `architecture.md` dice lo mismo: Domain sin ASP.NET. Quedó en `Auth/Contracts`.
  - Rojo antes de verde: el código de `main` con el DUNE0003 nuevo falla en `BackendInfrastructure.cs` (`TokenService`, `AuthModule`, `UsersModule`, `TasksModule`, `SessionHttpPolicy`).
  - EF `has-pending-model-changes`: PASS (sin cambios de modelo). Con una propiedad de prueba en `AppIdentityUser` falla, como debe.
  - Build Release: PASS (0 errores, 217 warnings). Tests `TemplateApp.slnx`: PASS (79/79: 29 de producto y 50 de Dune). `CommentVerifier`, `check-structure` (23/23) y `check-comments` (18/18): PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests, igual que en el paso 0.0), cleanup PASS. Navegador: NOT_RUN, el paso no toca el frontend ni el contrato HTTP.

### Paso 0.2 — 2026-10-16
- Enseñado: un gate que no se aplica, o que elige por vos, es peor que no tenerlo. Una regla de configuración también necesita un gate que la vigile.
- Preguntas: ninguna todavía.
- Decisiones: tres filas nuevas en `docs/decisions.tsv`; la del gate de bus pasa a `aceptada`.
- Evidencia: artifacts/verify-template/fase0-02-dune/.
  - `.editorconfig`: build Release antes 330 warnings, después 218, 0 errores. Desaparecen CA1062 y CA1861 (configurados en `none`) y los que van a `suggestion`.
  - Rojo antes de verde: con el código de `main`, 8 de 23 tests de `check-structure` fallan, 3 de 18 de `check-comments` fallan y `FeatureMigrationDirectoryIsExempt` falla (47/48). El `.editorconfig` de `main` hace fallar el gate nuevo: severidades bajo `[*.md]`.
  - Build Release: PASS (0 errores). Tests `TemplateApp.slnx`: PASS (77/77). `CommentVerifier`: PASS. `check-structure`: PASS (23/23). `check-comments`: PASS (18/18).
  - Smoke HTTP, navegador y `check-frontend`: NOT_RUN. El paso no toca producto ni frontend.

### Paso 1 — 2026-10-16
- Enseñado: línea base; "compila" no es "funciona"; PASS/FAIL/NOT_RUN; verificación reproducible con run ID y evidencia que sobrevive al cleanup.
- Preguntas: qué concepto enseña el paso.
- Evidencia: artifacts/verify-template/baseline-20261016/ y [linea-base.md](linea-base.md).

### Paso 0.1 — 2026-10-16
- Enseñado: una sola fuente de verdad por regla; las decisiones van en un ADR con alternativas y consecuencias.
- Preguntas: si la arquitectura ya estaba definida; si una clase por slice es compatible con los módulos profundos de Ousterhout; dónde va el código compartido.
- Decisiones: ADR-0004 y `docs/decisions.tsv`.
- Evidencia: artifacts/verify-template/fase0-01-docs/. Links relativos: PASS (57 links en 27 archivos, 0 rotos). Referencias viejas: PASS (12 coincidencias, todas intencionales: ADR-0004 nombra lo que reemplaza, `informe-deep-mediators.md` conserva su texto con nota). `check-structure`: PASS (14/14). `check-comments`: PASS (12/12). Build y tests .NET: NOT_RUN, el paso no toca código.

### Paso 0.0 — 2026-10-16
- Enseñado: inicialización concurrente. Varias instancias que preparan la misma base se pisan; un advisory lock de PostgreSQL las hace entrar de a una. Un test que no puede ponerse rojo no prueba nada: el primer intento (4 APIs completas) pasaba sin el arreglo.
- Preguntas: si convenía adelantar el arreglo de CI.
- Evidencia: artifacts/verify-template/fase0-00-seed-race/ y artifacts/verify-template/fase0-00-runtime/.
  - Test nuevo sin el lock: FAIL (`Optimistic concurrency failure`, igual que en CI). Con el lock: PASS, 4 corridas seguidas.
  - Suite con una sola base compartida, como en CI: código de `main` FAIL (24 de 28 tests caen); con el arreglo PASS (29/29).
  - Build Release: PASS (0 errores). Tests `TemplateApp.slnx`: PASS (73/73). `CommentVerifier`, `check-structure` (14/14) y `check-comments` (12/12): PASS.
  - verify-template: launch PASS, smoke HTTP PASS (45 requests), cleanup PASS. Navegador: NOT_RUN, el paso no toca la UI. Frontend: NOT_RUN, sin cambios.

- En Compose, la API no lee `X-Forwarded-For`: todo el tráfico anónimo del navegador llega con la IP de nginx y comparte un balde del limiter (el anónimo y el de `auth`). Configurar `ForwardedHeaders` pide confiar solo en la IP exacta de nginx (`KnownProxies`), fijada en `compose.yaml`: el puerto publicado de la API entra por el gateway de la misma red y podría falsificar la IP. Lleva su test de spoofing (investigación del paso 12, sección 2). Sin paso asignado; encaja con el paso 13.
- `/health/ready` queda anónimo, sin límite y consultando la base, y nginx lo publica bajo `/api/v1/`. Sin paso asignado; encaja con el paso 13 o el 16.
- Las métricas nativas del limiter (`aspnetcore.rate_limiting.*`) no se verifican en el export OTLP. Paso 14.
- Los headers `RateLimit` y `RateLimit-Policy` siguen en borrador IETF (`draft-ietf-httpapi-ratelimit-headers`). Se revisan cuando sean RFC. Sin paso asignado.
- Con IPv6, una clave por dirección deja rotar direcciones dentro de un mismo /64. Partir por prefijo /64. Sin paso asignado.
- Los contadores del limiter viven en memoria de cada instancia: con varias instancias el límite efectivo se multiplica. Un store compartido (Redis) o un gateway es decisión de herramienta (regla 5). Paso 33 o 35.
- En .NET 10, el `Retry-After` de la ventana fija vale la ventana entera, no lo que falta; el frontend muestra ese número. Se corrige solo con .NET 11. Sin paso asignado.
- Cuando la política `auth` rechaza, el limiter global gasta 2 permisos en vez de 1 (`RateLimitingMiddleware` vuelve a pedir el global). No cambia ningún resultado visible con los límites de hoy. Sin paso asignado.
