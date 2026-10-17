# Progreso del plan de evolución

Registro vivo del [plan](plan.md). El agente lo actualiza al cerrar cada paso.

## Paso actual

**Paso 0.13: carpetas por propósito en cada feature del frontend.** Estado: en curso. Los pasos 0.10 a 0.12 cerraron. Tobi pidió el 2026-10-17 separar la vista de los componentes (0.10), un archivo por tipo (0.11), Tailwind (0.12), ordenar los archivos sueltos de cada feature (0.13) y una investigación sobre manejo de sesión en Angular (0.14). Después sigue la Fase 1, paso 2. Los pasos 0.6 a 0.12 esperan el merge de Tobi.

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
- El diálogo de borrar tarea se abre arriba a la izquierda, no centrado. El preflight de Tailwind pone `margin: 0` a todo y le quita el `margin: auto` del navegador. Pasaba antes del paso 0.12 y el paso lo dejó igual. Centrarlo es una línea (`m-auto`): decisión de Tobi.
- `backend/AGENTS.md` y `backend/docs/agents/` describen un issue tracker en `.scratch/`, que `.gitignore` excluye: los issues nunca se versionan.

## Cierres

Una entrada por paso cerrado, con este formato:

```md
### Paso N — AAAA-MM-DD
- Enseñado: qué concepto quedó claro.
- Preguntas: qué preguntó Tobi.
- Evidencia: artifacts/verify-template/<runId>/
```

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
