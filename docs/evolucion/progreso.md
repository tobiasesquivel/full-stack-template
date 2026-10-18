# Progreso del plan de evolución

Registro vivo del [plan](plan.md). El agente lo actualiza al cerrar cada paso.

## Paso actual

**Fase 1, paso 2: un solo comando de verificación local.** Estado: pendiente. La Fase 0 cerró con el paso 0.26 (sesión en el servidor, BFF), que Tobi eligió el 2026-10-17. Los pasos 0.10 a 0.26 cerraron. Tobi pidió el 2026-10-17 separar la vista de los componentes (0.10), un archivo por tipo (0.11), Tailwind (0.12), ordenar los archivos sueltos de cada feature (0.13), investigar y aplicar el manejo de sesión en Angular (0.14 a 0.16, [informe-sesion-angular.md](informe-sesion-angular.md)) y aplicar lo que no necesita decisión de herramienta de [informe-componentes-angular.md](informe-componentes-angular.md) y [informe-frontend-para-agentes.md](informe-frontend-para-agentes.md) (0.17 a 0.20). Después eligió Vitest, Prettier, Playwright Test, el MCP del CLI y los ajustes visuales (0.21 a 0.25). Los pasos 0.6 a 0.26 esperan el merge de Tobi. El Stop hook espera hasta después del paso 2.

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
| 1 | Línea base: qué funciona hoy, con evidencia | cerrado | [linea-base.md](linea-base.md); `artifacts/verify-template/baseline-20261016/` |
| 2 | Un solo comando de verificación local | pendiente | |
| 3 | CI verde de verdad | pendiente | |
| 4 | Versionado de API declarado | pendiente | |
| 5 | Validación de configuración al arranque | pendiente | |
| 6 | Catálogo de errores estable | pendiente | |
| 7 | Paginación por keyset | pendiente | |
| 8 | Concurrencia optimista en `TaskItem` | pendiente | |
| 9 | Idempotencia de escritura | pendiente | |
| 10 | Auditoría de cambios | pendiente | |
| 11 | Permisos memorizados por request | pendiente | |
| 12 | Rate limiting por usuario y por tipo de endpoint | pendiente | |
| 13 | Cabeceras de seguridad y escaneo continuo | pendiente | |
| 14 | Métricas de negocio reales | pendiente | |
| 15 | Correlación log↔traza automatizada | pendiente | |
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

- Catálogo de errores: `TaskErrors`, `UserErrors` y `AuthErrors` usan prefijos distintos y hay errores escritos en línea (`"Task.Forbidden"` aparece 4 veces). Paso 6.
- El build Release muestra 218 warnings de analyzers (CA2007, CA1707, RS2008, RS1036, CS8602 y otros). Nadie decidió si cada uno se corrige o se baja a `suggestion`. Sin paso asignado: proponer uno.
- `docs/evolucion/informe-vsa-deep-modules.md` §2.1 propone `AppIdentityUser` en `Auth/Domain`. Choca con `architecture.md` (Domain sin ASP.NET). Desde el paso 0.5, Users ya no lo usa; sigue en `Auth/Contracts` solo porque `AppDbContext` hereda de `IdentityDbContext<AppIdentityUser>`. Paso 22.
- Las dos FKs entre features (`UserProfiles → AspNetUsers`, `TaskItems → UserProfiles`) están en `AppDbContext.ConfigureCrossFeatureRelationships`. Con un `DbContext` por feature hay que elegir: quitarlas y reemplazar la cascada por un evento (Grzybek, Wolverine) o dejarlas solo en SQL (Jovanović). Paso 22.
- `RegisterUser` (Auth) y `UserProfiles` (Users) usan el mismo `AppDbContext` del request: la atomicidad del registro depende de eso. Con contextos separados hace falta compartir conexión y transacción, o un outbox. Pasos 22 y 24.
- `"Task.Forbidden"` y `"User.Forbidden"` siguen escritos en línea en cada slice. Paso 6.
- El rate limiter `auth` es una sola ventana de 20 requests por minuto para toda la API, sin partición por cliente. Un recorrido de navegador seguido del smoke HTTP da 503 (`fase0-06-runtime`). Paso 12.
- `AuthErrors.AccountNotFound` (`Auth.AccountNotFound`) es nuevo y `DeleteUser` lo traduce a `User.NotFound`. Entra en el catálogo del paso 6.
- Hay una carpeta vacía y sin versionar, `backend/Features/Health/Test/`, de antes de este paso. Git la ignora. Borrarla es decisión de Tobi.
- `backend/AGENTS.md` y `backend/docs/agents/` describen un issue tracker en `.scratch/`, que `.gitignore` excluye: los issues nunca se versionan.
- `Profile` vuelve a pedir `/users/me` al abrirse, aunque `Session` ya tiene el usuario. Podría leerlo con `httpResource()`. Es de la feature `users`, fuera del paso 0.14.
- Al restaurar, un error de red (`status 0`) deja la sesión `closed`, igual que un 401. Distinguir "sin conexión" de "sin sesión" queda pendiente.
- Una regla que limite `HttpClient` a los archivos de `api/` haría ejecutable la división del paso 0.13 (`api/` hace el HTTP). Hoy FRONT002 solo lo prohíbe en componentes. Sin paso asignado.

- FRONT006 solo mira el import de `@angular/forms`. Un input con `[value]`, `(input)` y `$any($event.target)`, como el que tenía la edición en línea antes del paso 0.19, sigue pasando el gate. Sin paso asignado.
- Las claves de Data Protection, que cifran la cookie `__Host-session` y el antiforgery, quedan donde las pone ASP.NET Core por defecto: el perfil del usuario o el disco del contenedor. Sobreviven a `docker compose restart api`, pero no a recrear el contenedor ni a una segunda instancia: ahí todas las sesiones se cierran. Guardarlas en PostgreSQL (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, un paquete nuevo) o en un volumen es decisión de Tobi. Sin paso asignado; encaja con el paso 5 o el 16.
- La cookie interna de antiforgery (`.AspNetCore.Antiforgery.*`, HttpOnly) sale sin `Secure` por HTTP: usa el valor por defecto `SameAsRequest`. `__Host-session` y `XSRF-TOKEN` sí son `Secure`. Fijarla en `Always` es un cambio de una línea en `AddAntiforgery`. Paso 13.
- Las filas de `Sessions` vencidas o abandonadas se borran solo cuando su cookie vuelve. Falta una limpieza periódica. Sin paso asignado.
- Abrir sesión con una sesión ya abierta en el mismo navegador deja la fila anterior hasta que vence por inactividad. `OpenSession` podría cerrarla. Sin paso asignado.
- No hay regla de Dune que frene un header `Authorization`, un token en `localStorage` o `sessionStorage`, ni un endpoint que se salte el middleware de antiforgery. Hoy lo prueban los tests del backend, `smoke-api.mjs` y la e2e `session.spec`. Sin paso asignado.
- `E2E_OUTPUT_DIR=../artifacts/...`, como dice el README, se resuelve desde `frontend/e2e/` y deja la evidencia en `frontend/artifacts/`. `check-comments.mjs` revisa esa carpeta y falla con los `.css` de los traces. En el paso 0.26 la evidencia se movió a mano a `artifacts/verify-template/fase0-26-e2e/`. Paso 2 de la Fase 1.
- CI no corre las e2e. El job `compose` necesitaría `npm ci` del frontend, un navegador (`npx playwright install chromium --with-deps`) y 65 segundos de espera después del smoke por el limiter de `/auth`. Paso 3 de la Fase 1.

## Cierres

Una entrada por paso cerrado, con este formato:

```md
### Paso N — AAAA-MM-DD
- Enseñado: qué concepto quedó claro.
- Preguntas: qué preguntó Tobi.
- Evidencia: artifacts/verify-template/<runId>/
```

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
