# Plan progresivo: llevar este backend hacia el nivel empresarial del informe

**Propósito**: usar este repo como laboratorio de práctica. El objetivo no es llegar a microservicios, sino **dominar una escalera**: cada paso agrega una pieza real, la verifica con los gates que ya existen, y deja evidencia. Al final tenés criterio para decidir en cualquier volumen.

**Cómo leerlo**: una Fase 0 de bases y 10 fases con 36 pasos en orden creciente de dificultad. Cada paso tiene tres líneas fijas: **Concepto** (qué se aprende), **Cambia** (áreas y archivos), **Verifica** (gate o test del repo que lo prueba). Nada de código en este plan: los pasos describen *qué* cambia y *cómo* se prueba.

---

## 1. Estado real de partida (verificado en el código)

### Lo que existe hoy

Es el punto de partida, no un límite. Cualquier pieza puede cambiar si mejora el rigor técnico o el aprendizaje.

| Área | Estado real |
|---|---|
| Estilo | Features `Auth`, `Users`, `Tasks`, `Health`, sin mediator ni bus. Desde el paso 0.4, cada caso de uso es una slice en su carpeta (request, validador, operación, endpoint y test). La Fase 0 lleva el resto a la forma de [ADR-0004](../adr/0004-vertical-slices-deep-modules.md) |
| Contratos | `Features/*/Contracts`, `Features/*/Domain`, `Features/*/Persistence`; `<Slice>Tests.cs` junto a cada slice, excluidos de producción por `Compile Remove`. El contrato HTTP lo fijan los tests de integración |
| Errores | `Shared/Http/ApiResults` traduce `ErrorOr` a ProblemDetails (400/401/403/404/409/500) |
| Autorización | `IPermissionService` + `ResourceAuthorizer`; permisos consultados por request; orden 404 antes de 403 |
| Seguridad | Desde el paso 0.26, sesión en el servidor (BFF): una fila en `Sessions` y la cookie `__Host-session` (HttpOnly, Secure, SameSite=Strict) de la autenticación por cookie de ASP.NET Core, con vencimiento absoluto y por inactividad; sin JWT ni refresh token. Desde el paso 0.27, las claves de Data Protection viven en el volumen `dataprotection`. Antiforgery en toda escritura de la API, lockout de Identity, rate limiting de ventana fija **solo** en los 3 endpoints de escritura de `/auth` |
| Observabilidad | OpenTelemetry (trazas + métricas) con exporter OTLP y meters declarados: `Npgsql`, `TemplateApp.Api`, `TemplateApp.Business`; Serilog a consola y OTLP; collector en `deploy/otel-collector-config.yaml`; señales en `otel-export/*.json` |
| Salud | `/api/v1/health/live` y `/ready` (base + migraciones) |
| Datos | `AppDbContext` único, las migraciones `InitialCreate` y `ServerSideSessions` (paso 0.26), `DatabaseInitializer`, EF Core + Npgsql |
| Reglas ejecutables | Dune: analizadores DUNE0001-0011 con severidad error, `BannedSymbols.txt`, `Dune.CommentVerifier`, y los gates Node `check-structure.mjs`, `check-comments.mjs`, `check-frontend.mjs` (FRONT001-016). Cada regla tiene fixture válida y violación deliberada. El paso 0.2 corrigió `.editorconfig` y cambió el gate de Wolverine por un gate neutral de bus |
| Pruebas backend | MTP + xunit.v3 + Testcontainers PostgreSQL real; `ApiFixture` y `TestSession` en `backend/Tests/Support` |
| CI | `.github/workflows/ci.yml` con 4 jobs (backend, frontend, comentarios, compose + smoke con evidencia OTLP). Desde el paso 3, probado en verde y en rojo. `main` no tiene protección de rama (repo privado del plan gratis): CI avisa, pero no frena un merge. Las e2e no corren en CI |
| Verificación runtime | Skill del proyecto `verify-template`: `launch` / `doctor` / `api` / `cleanup` con artefactos en `artifacts/verify-template/<runId>/` |
| Frontend | Angular 22. Desde el paso 0.22, una suite de Vitest con jsdom para funciones puras y componentes de presentación; el recorrido completo se prueba en el navegador |

### Lo que falta (los huecos que este plan llena, en este orden)

Nada de bus, outbox, eventos de dominio ni sagas. Nada de caché. Ningún trabajo programado. Sin idempotencia de escritura. Sin control de concurrencia propio en `TaskItem`. Sin auditoría. Validación de configuración por excepciones manuales. Sin cliente HTTP saliente ni resiliencia. Sin SLOs ni tableros. Sin SBOM, escaneo de dependencias ni de secretos. Un solo `DbContext` para todo el sistema. Sin despliegue progresivo.

---

## 2. Reglas de avance

Las reglas viven en [AGENTS.md](../../AGENTS.md#reglas-de-avance). Este plan no las repite.

---

## 3. Protocolo de verificación por paso

Los comandos viven en [README.md](../../README.md#ejecutar-gates). Cada paso indica abajo cuáles aplican.

Regla de oro: **un paso no termina con tests verdes.** Termina cuando el caso que agrega se ejercita de verdad: por HTTP con `smoke-api.mjs`, o en el navegador si hay superficie visible.

---

## 4. El plan

### Fase 0 — Bases (pasos 0.0-0.28)

Llevan el repo a la forma de [ADR-0004](../adr/0004-vertical-slices-deep-modules.md). Ningún paso cambia un contrato HTTP, salvo el 0.26 (decisión de Tobi): el smoke de la API prueba que nada se rompió.

**0.0. Inicialización concurrente de la base.** (adelantado del paso 3)
**Concepto**: inicialización concurrente; serializar con un advisory lock de PostgreSQL.
**Cambia**: `DatabaseInitializer` toma un advisory lock antes de migrar y sembrar; `ApiFixture` deja de sembrar; test nuevo que corre 8 inicializaciones a la vez.
**Verifica**: el test nuevo falla sin el lock y pasa con él; la suite completa pasa con una sola base compartida, como en CI.

**0.1. Docs y reglas de trabajo.**
**Concepto**: una sola fuente de verdad por regla; decisiones registradas en ADR.
**Cambia**: ADR-0004, `architecture.md`, `conventions.md`, `AGENTS.md`, `README.md`, este plan, `decisions.tsv` desde cero, notas en los informes, limpieza de referencias viejas. Nada de código.
**Verifica**: ningún link relativo roto en los `.md`; ninguna referencia a OpenSpec, `artifact://` ni a ADR borrados; gates de Node en verde.

**0.2. Arreglos de Dune.**
**Concepto**: un gate que no se aplica, o que elige por vos, es peor que no tenerlo.
**Cambia**: `.editorconfig` (las severidades quedaron bajo `[*.md]`); gate de bus neutral en lugar del gate de Wolverine; excepciones de comentarios para migraciones de cualquier feature y para directivas de herramientas. Cada regla con fixture válida y violación deliberada.
**Verifica**: build y tests de Dune; la violación deliberada de cada regla nueva falla con el diagnóstico esperado.

**0.3. Reparto del código.**
**Concepto**: lo que nombra una feature vive en esa feature; cada feature es su propio composition root.
**Cambia**: JWT, `AppIdentityUser`, `JwtOptions`, roles, `RoleSeeder` y `PermissionService` + `ResourceAuthorizer` (fusionados en `IAccessPolicy`) a `Features/Auth`; permisos al `Contracts` de cada feature; `<Feature>Feature.cs` en cada feature; DUNE0003; se borra el código muerto (`ITemplateMetrics`, `ValidationExceptionHandler`, `UserErrors.InvalidTimezone`). Ver [informe-vsa-deep-modules.md](informe-vsa-deep-modules.md) §2.
**Verifica**: build, tests y smoke HTTP sin cambios de comportamiento.

**0.4. Slices.**
**Concepto**: vertical slice como clase propia; feature como módulo profundo.
**Cambia**: una carpeta y una clase por caso de uso en Tasks, Users, Auth y Health, con su test; las reglas comunes bajan al `Domain`; DUNE0008 y `check-structure.mjs`.
**Verifica**: build, tests y smoke HTTP sin cambios de comportamiento; fixtures de DUNE0008 con la forma nueva.

**0.5. Límite entre Auth y Users.**
**Concepto**: dependencia en una sola dirección entre módulos; contrato público.
**Cambia**: Users usa solo `Auth.Contracts`; Auth crea el perfil mediante un contrato de Users; DUNE0004 permite solo `Contracts`. Absorbe la mayor parte del paso 21.
**Verifica**: build, tests y smoke HTTP; fixture negativa que usa el `Domain` de otra feature y falla.

**0.6. Frontend por feature.**
**Concepto**: el mismo límite de módulo, en el cliente.
**Cambia**: servicios, modelos y rutas de Auth y Users a su feature; `core/` solo transversal; `*Payload` pasa a `*Request`; `check-frontend.mjs` si hace falta.
**Verifica**: build de Angular, `check-frontend` y navegador: registro, login, recarga, perfil, tareas y logout.

Los pasos 0.7 a 0.9 llevan el frontend a la forma de Angular v22. Se agregaron el 2026-10-17, a pedido de Tobi. La investigación está en [informe-angular-moderno.md](informe-angular-moderno.md).

**0.7. Servicios y nombres de Angular v22.**
**Concepto**: *Intent over Role*: el nombre dice el propósito, no el tipo técnico; `@Service()` para los servicios de una sola instancia.
**Cambia**: los servicios, guards e interceptores del frontend pierden el sufijo de tipo y pasan a `@Service()`; una regla nueva en `check-frontend.mjs` con su fixture válida y su violación deliberada.
**Verifica**: build de Angular, `check-frontend` y navegador con el mismo recorrido del paso 0.6.

**0.8. Signal Forms.**
**Concepto**: el estado del formulario como signals (`@angular/forms/signals`), estable desde v22.
**Cambia**: los formularios de login, registro, tareas y perfil pasan de Reactive Forms a Signal Forms, con los mismos ids, mensajes y validaciones.
**Verifica**: build de Angular, `check-frontend` y navegador: cada formulario acepta lo válido y muestra el mismo error con lo inválido.

**0.9. Arranque con `provideAppInitializer()`.**
**Concepto**: providers funcionales en lugar de tokens multi.
**Cambia**: `app.config.ts` reemplaza `APP_INITIALIZER` por `provideAppInitializer()`.
**Verifica**: build de Angular y navegador: la recarga restaura la sesión y una ruta protegida sin sesión lleva a `/login`.

Los pasos 0.10 y 0.11 ordenan los archivos. Se agregaron el 2026-10-17, a pedido de Tobi.

**0.10. Componentes en archivos separados.**
**Concepto**: separar la vista de la lógica; cada archivo cambia por un solo motivo.
**Cambia**: login, registro, tareas y perfil pasan su `template` a `<componente>.html` y sus `styles` a `<componente>.css`, como `app.ts`; una regla nueva en `check-frontend.mjs` con su fixture válida y su violación deliberada.
**Verifica**: build de Angular, `check-frontend` y navegador con el mismo recorrido del paso 0.8.

**0.11. Un archivo, un tipo.**
**Concepto**: un archivo por tipo en todo el repo; un tipo auxiliar que usa solo otro tipo queda en su archivo, pero el compilador le impide salir de ahí.
**Cambia**: los modelos del frontend y `Tests/Support/TestSession.cs` se separan; un analyzer nuevo de Dune y una regla nueva en `check-frontend.mjs`, cada uno con su fixture válida y su violación deliberada.
**Verifica**: build y tests .NET, tests de Dune, build de Angular, `check-frontend`, smoke HTTP y navegador.

El paso 0.12 se agregó el 2026-10-17, a pedido de Tobi: "migrá de CSS a Tailwind".

**0.12. Estilos con Tailwind.**
**Concepto**: CSS utility-first: el estilo vive en clases del template y no en hojas por componente; el CSS global queda solo para lo que no es una clase.
**Cambia**: los templates de app, login, registro, tareas y perfil usan clases de Tailwind v4 con los mismos colores (la paleta más cercana); se borran `app.css`, `tasks.css`, los `styleUrl` y los `style=""` del diálogo; `styles.css` queda con `@import 'tailwindcss'`, un breakpoint y el foco visible; una regla nueva en `check-frontend.mjs` (FRONT009) con su fixture válida y su violación deliberada.
**Verifica**: build de Angular, `check-frontend`, capturas antes y después de cada vista, y navegador con el recorrido del paso 0.10 adaptado a las clases nuevas.

El paso 0.13 se agregó el 2026-10-17, a pedido de Tobi: "hay muchos archivos sueltos en auth/".

**0.13. Carpetas por propósito en cada feature del frontend.**
**Concepto**: agrupar por propósito, no por tipo técnico; la raíz de la feature muestra solo su puerta y sus rutas.
**Cambia**: `auth/session/` (Session, guards, interceptor y sus modelos), `tasks/api/` y `users/api/` (cliente HTTP y modelos); los imports se ajustan y las puertas `index.ts` exportan lo mismo; una regla nueva en `check-frontend.mjs` (FRONT011) con su fixture válida y su violación deliberada.
**Verifica**: build de Angular, `check-frontend` y navegador con el recorrido del paso 0.12.

Los pasos 0.14 a 0.16 se agregaron el 2026-10-17, a pedido de Tobi: "investigá la manera más profesional de manejo de sesión en Angular y aplicalo". La investigación está en [informe-sesion-angular.md](informe-sesion-angular.md). Ninguno cambia el contrato HTTP: lo que lo cambiaría (BFF, prefijo de cookie, inactividad, margen de rotación) espera la decisión de Tobi.

**0.14. Forma de la sesión.**
**Concepto**: un solo estado con signals: el dato en un `signal` privado, lo público con `computed`; una unión discriminada (`restoring`, `open`, `closed`) impide combinaciones imposibles.
**Cambia**: `auth/api/` (`AuthApi` y los modelos de Auth, con `OpenSessionRequest` nuevo); `Session` con `status`, `currentUser`, `isOpen` y las acciones `restore`, `open`, `close`, `refresh`, `end` y `replaceUser`; los guards esperan a `status`; login y registro navegan ellos; una regla nueva en `check-frontend.mjs` (FRONT012) con su fixture válida y su violación deliberada.
**Verifica**: build de Angular, `check-frontend` y navegador con el recorrido del paso 0.13, sin cambios de comportamiento.

**0.15. Interceptor y vuelta a la página pedida.**
**Concepto**: un interceptor funcional que renueva una vez y reintenta una vez; `HttpContextToken` para marcar peticiones; `returnUrl` sin redirección abierta.
**Cambia**: `SKIP_SESSION` en `auth/api/`; el interceptor agrega el token solo a `/api/` y nunca a `/auth`; el error del reintento sale tal cual; `Session.end()` lleva a `/login?returnUrl=…`; el `authGuard` agrega `returnUrl` y el login vuelve ahí solo si es una ruta interna.
**Verifica**: build de Angular, `check-frontend` y navegador: rojo en el código del 0.14 y verde después para el header `Authorization` en `/auth`, un 404 en el reintento y el `returnUrl`; varias 401 dan una sola renovación; la renovación fallida lleva al login.

**0.16. Varias pestañas.**
**Concepto**: coordinar pestañas del mismo origen: Web Locks para que dos renovaciones no se pisen y `BroadcastChannel` para avisar que la sesión se abrió o se cerró.
**Cambia**: `Session.refresh()` corre dentro de `navigator.locks.request`; `Session` publica `opened` y `closed` en un `BroadcastChannel` y reacciona a los de otras pestañas.
**Verifica**: build de Angular, `check-frontend` y navegador con dos páginas del mismo contexto: rojo en el código del 0.15 y verde después para dos recargas a la vez, el logout en otra pestaña y el login de otro usuario en otra pestaña.

Los pasos 0.17 a 0.20 se agregaron el 2026-10-17, a pedido de Tobi: "aplicá lo que tengas que aplicar". Salen de [informe-componentes-angular.md](informe-componentes-angular.md) y [informe-frontend-para-agentes.md](informe-frontend-para-agentes.md). Ninguno cambia el contrato HTTP ni lo que ve el usuario. Lo que pide una herramienta o una regla nueva (suite de Vitest, e2e con Playwright, `prettier --check`, el MCP del CLI de Angular, un Stop hook) espera la decisión de Tobi.

**0.17. El CLI genera código que pasa el gate.**
**Concepto**: la herramienta oficial y el gate dicen lo mismo. Si el generador produce algo que el gate rechaza, cada componente nuevo cuesta una vuelta de corrección.
**Cambia**: `angular.json` fija `"style": "none"` para `@schematics/angular:component`; `frontend/README.md` y `README.md` dicen la verdad sobre el runner de tests (Vitest conectado, sin specs); una regla nueva en `check-frontend.mjs` (FRONT014) con su fixture válida y su violación deliberada.
**Verifica**: `ng generate component --dry-run` antes y después: antes lista un `.css`, después no; un componente generado de verdad pasa `check-frontend` y se borra; build de Angular y navegador con el recorrido del paso 0.16, como regresión.

**0.18. Componente de ruta y componente de presentación: `TaskPager`.**
**Concepto**: un componente de ruta orquesta: inyecta servicios y llama a la API. Uno de presentación recibe datos con `input()` y avisa con `output()`. El gate los distingue por las rutas.
**Cambia**: `features/tasks/task-pager/` nuevo; `Tasks` usa `<app-task-pager />`; una regla nueva en `check-frontend.mjs` (FRONT013) que mira el tipo de cada `inject()`, con su fixture válida y sus violaciones deliberadas; `conventions.md` y `architecture.md`.
**Verifica**: build de Angular, tests del gate en rojo antes y en verde después, `check-frontend` y navegador: con 11 tareas, `Siguiente` habilitado, "Página 2 de 2 (11)" y `Anterior` vuelve.

**0.19. Estado local en el hijo: `TaskRow`.**
**Concepto**: el borrador del título es estado de la fila (`linkedSignal`); quién edita es estado de la página (`editingId`).
**Cambia**: `features/tasks/task-row/` nuevo; la edición en línea usa Signal Forms (`form`, `[formField]`, `required`, `maxLength(200)`); `Tasks` pierde `editTitle`, el `$any` y la validación manual. El mensaje de error queda en el mismo lugar y con el mismo texto.
**Verifica**: build de Angular, gates y navegador: renombrar da `PUT` 200; un título vacío muestra el mismo mensaje y no manda `PUT`; `Cancelar` restaura el título; abrir otra fila cierra la primera; el checkbox manda `PUT` con `isCompleted=true`; el dato sigue después de recargar.

**0.20. Diálogo de presentación: `TaskDeleteDialog`.**
**Concepto**: un hijo maneja su propio DOM con `viewChild` y `afterRenderEffect`; el padre decide el foco.
**Cambia**: `features/tasks/task-delete-dialog/` nuevo; `Tasks` pierde `confirmDialog` y el `showModal()`.
**Verifica**: build de Angular, gates y navegador: `Escape` cierra y el foco vuelve al `Eliminar` de esa fila; `Confirmar` da `DELETE` 204 y el foco va a "Mis tareas"; borrar la única tarea de la página 2 vuelve a la página 1.

Los pasos 0.21 a 0.25 se agregaron el 2026-10-17, a pedido de Tobi, con sus decisiones sobre los dos informes. Eligió la suite de Vitest, `prettier --check` como gate, el MCP del CLI de Angular en solo lectura y e2e con Playwright Test (`@playwright/test`; tester-army/e2e quedó descartado: es pre-1.0, pide un modelo de IA y manda telemetría por defecto). Para lo visual: centrar el diálogo y llevar el error de la edición en línea bajo su campo. Storybook queda descartado. El Stop hook espera hasta después del paso 2 de la Fase 1.

**0.21. Diálogo centrado y error bajo su campo.** (cambia comportamiento)
**Concepto**: un error de campo vive junto al campo y se enlaza con `aria-describedby`; un error de la acción vive en la página. El preflight de Tailwind quita el `margin: auto` del `<dialog>`.
**Cambia**: `TaskDeleteDialog` suma `m-auto`; `TaskRow` muestra "El título es obligatorio (máximo 200 caracteres)." justo después del input y lo enlaza con `aria-describedby`; se van `renameRejected` y el mensaje bajo la lista para ese caso. Los errores de la API siguen bajo la lista.
**Verifica**: navegador en rojo con el código del 0.20 y en verde después: el centro del diálogo coincide con el del viewport; el mensaje es el elemento que sigue al input y `aria-describedby` apunta a él; mismo texto y sin `PUT`.

**0.22. Primera suite de Vitest.**
**Concepto**: pruebas unitarias y de componente con Vitest y jsdom, sin zone.js: "Act, Wait, Assert" con `await fixture.whenStable()`.
**Cambia**: `return-url.spec.ts`, `http-error.spec.ts` y `task-pager.spec.ts`; `ng test --watch=false` en el README, en CI y en `AGENTS.md`.
**Verifica**: cada spec falla contra una implementación rota a propósito y pasa con la real; build, gates y CI local.

**0.23. Formato verificado con Prettier.**
**Concepto**: un formato que decide una herramienta, no una persona; el gate falla ante un archivo sin formatear.
**Cambia**: un commit que solo formatea con la configuración de `frontend/.prettierrc`, y otro con `npm run format:check` en el README y en CI.
**Verifica**: un archivo mal formateado a propósito hace fallar el gate; después pasa. El build y el navegador no cambian.

**0.24. E2E con Playwright Test.**
**Concepto**: el recorrido del navegador como specs que corren igual cada vez, con selectores por rol y label.
**Cambia**: `@playwright/test` en `frontend/`; `frontend/e2e/` con `playwright.config.ts` y specs por feature; la URL sale de una variable de entorno, así corre contra una instancia de `verify-template`; el README y la skill dicen cómo correrlo.
**Verifica**: una prueba falla contra un label roto a propósito y todo pasa contra una instancia real. CI queda para el paso 3 si el job de compose no lo aloja sin cambios.

**0.25. MCP del CLI de Angular, solo lectura.**
**Concepto**: el agente consulta la documentación y la guía de la versión instalada desde el propio CLI.
**Cambia**: `.mcp.json` con `ng mcp --read-only`; `AGENTS.md` lo nombra.
**Verifica**: el servidor arranca, lista sus herramientas y `list_projects` devuelve `frontend`.

El paso 0.26 se agregó el 2026-10-17. Tobi eligió BFF porque es lo que usaría en un SaaS ([informe-sesion-angular.md](informe-sesion-angular.md), sección 4, y `docs/decisions.tsv`). Cambia el contrato HTTP de `/auth`: va completo, con backend, frontend y tests juntos.

**0.26. Sesión en el servidor (BFF).** (cambia el contrato HTTP)
**Concepto**: la sesión vive en el servidor y el navegador solo guarda una cookie HttpOnly con su id; cerrar o revocar es borrar una fila; el envío automático de la cookie obliga a defenderse de CSRF; un cliente futuro sin cookies entra por otro esquema de autenticación sobre los mismos endpoints.
**Cambia**: backend: tabla de sesiones con migración, autenticación por cookie de ASP.NET Core (`__Host-`, `HttpOnly`, `Secure`, `SameSite`, vencimiento absoluto y por inactividad), `OpenSession` y `RegisterUser` abren una sesión, `CloseSession` la borra, una consulta "quién soy" para restaurar y antiforgery en toda escritura; se van JWT, `RefreshSession`, `RefreshToken` y `TokenService`. Frontend: `Session` restaura con "quién soy"; se van el interceptor de `Authorization`, la renovación y `navigator.locks`; el `BroadcastChannel` queda solo para avisar a las otras pestañas que la sesión se abrió o se cerró. Tests, `smoke-api.mjs`, e2e, `architecture.md`, `conventions.md` y las reglas de Dune que nombren tokens, con su fixture válida y su violación deliberada.
**Verifica**: tests nuevos en rojo con el código del 0.25 y en verde después: después de cerrar sesión, la misma cookie da 401; borrar la fila de la sesión en la base (lo que hará un admin cuando exista esa función) corta el acceso en la request siguiente; una escritura sin token antiforgery da 400; ninguna respuesta lleva un token legible por JavaScript. Suite del backend, gates, Vitest, `smoke-api.mjs` y e2e en el navegador; la sesión sigue después de recargar y entre pestañas.

El paso 0.27 se agregó el 2026-10-17, a pedido de Tobi, con un pendiente del 0.26. Tobi eligió guardar las claves en un volumen de Docker, sin paquete nuevo.

**0.27. Claves de Data Protection en un volumen.**
**Concepto**: Data Protection cifra la cookie de sesión y el antiforgery con un anillo de claves; si las claves viven en el disco del contenedor, recrearlo cierra todas las sesiones; las claves son estado y necesitan un lugar que sobreviva.
**Cambia**: `BackendInfrastructure` fija el nombre de aplicación y guarda las claves en `DataProtection:KeysDirectory` cuando existe; el `Dockerfile` crea esa carpeta para el usuario `app` y apunta la variable ahí; `compose.yaml` monta el volumen `dataprotection`; el modo `--compose` de `smoke-api.mjs` recrea el contenedor de la API en lugar de reiniciarlo; un test de backend nuevo; `README.md` y `architecture.md`.
**Verifica**: en rojo con el código del 0.26 y en verde después: `smoke-api.mjs --compose` recrea la API y la misma cookie da 401 antes y 200 después; el test nuevo prueba que otra instancia con la misma carpeta acepta la cookie y una con otra carpeta la rechaza. Recrear la API y `docker compose down` sin `-v` conservan la sesión. Build, tests, gates y launch de `verify-template` con su smoke.

El paso 0.28 se agregó el 2026-10-17, a pedido de Tobi, con otro pendiente del 0.26: las reglas de la sesión solo las probaban los tests.

**0.28. Reglas de la sesión en Dune.**
**Concepto**: un test prueba los endpoints que existen; una regla estática frena el cambio que todavía no tiene test, como un endpoint nuevo fuera de `/api` o un token guardado en el navegador.
**Cambia**: `check-frontend.mjs` suma FRONT015 (`localStorage` y `sessionStorage` fuera de los componentes, que ya cubre FRONT003) y FRONT016 (header `Authorization`); un analyzer nuevo, DUNE0011, que mantiene toda ruta de escritura bajo `/api`; `BannedSymbols.txt` prohíbe `DisableAntiforgery()`, `[RequireAntiforgeryToken(false)]` y `UseAntiforgery()`. Cada regla con su fixture válida y su violación deliberada. `conventions.md`, `architecture.md` y la fila de reglas ejecutables de este plan.
**Verifica**: los tests nuevos de Dune y de `check-frontend` en rojo sin la regla y en verde con ella; una violación deliberada en el producto frena el build o el gate con el diagnóstico esperado; todos los gates en verde sobre el repo real, build del backend y `ng build`.

### Fase 1 — Base verificable (pasos 1-3)

**1. Línea base: qué funciona hoy, con evidencia.**
**Concepto**: verificación reproducible; separar "compila" de "funciona"; PASS/FAIL/NOT_RUN.
**Cambia**: nada de producto. Solo `artifacts/verify-template/<runId>/` y una nota de estado en `docs/`.
**Verifica**: los gates del README + `launch`/`api`/`cleanup` de la skill, con estado por etapa.

**2. Un solo comando de verificación local.**
**Concepto**: pipeline local reproducible; una entrada, no veinte comandos recordados.
**Cambia**: `.agents/skills/verify-template/scripts/` (wrapper que orquesta los gates existentes; **no** duplicar `smoke-api.mjs`).
**Verifica**: el comando nuevo produce el mismo resultado que los comandos sueltos, y `node dune/check-structure.mjs` sigue en verde (sin herramientas duplicadas).

**3. CI verde de verdad.**
**Concepto**: integración continua como gate efectivo, no como archivo versionado.
**Nota**: el paso 0.0 ya arregló la falla de siembra concurrente que tumbaba el job de backend.
**Cambia**: `ci.yml` solo si algún job falla; `docs/` con el estado real; `artifacts/`.
**Verifica**: un PR de prueba dispara los 4 jobs; el job `compose` deja el artifact `otel-export` descargable.

### Fase 2 — Contratos de la API (pasos 4-7)

**4. Versionado de API declarado.**
**Concepto**: evolución de contratos públicos; `/api/v1` deja de ser un string literal y pasa a ser una versión gestionada.
**Cambia**: agrupaciones de endpoints de cada feature, tests de contrato.
**Verifica**: `dotnet test TemplateApp.slnx`; `smoke-api.mjs` responde igual en v1; `check-structure.mjs` en verde.

**5. Validación de configuración al arranque.**
**Concepto**: fail-fast; la configuración es un contrato, no un supuesto.
**Cambia**: options de cada feature (por ejemplo `SessionOptions` en Auth) y del host, `appsettings.example.json`, tests de arranque.
**Verifica**: nuevo test que arranca el host con configuración inválida y espera fallo con mensaje claro; `dotnet test` completo.

**5.1. Inicialización de la base como hosted service.**
**Concepto**: ciclo de vida del host; el host ordena el arranque (validar → hosted services → Kestrel) y el código entre `Build()` y `RunAsync()` queda fuera de ese orden.
**Cambia**: `DatabaseInitializer` pasa a un `IHostedService` en `Infrastructure/Persistence`; `Program.cs` pierde la validación a mano; los registros de options usan `AddOptionsWithValidateOnStart`.
**Verifica**: `ConfigurationValidationTests` sin cambios; rojo con la migración otra vez antes de `RunAsync`; `dotnet test` completo.

**5.2. Las herramientas de diseño no registran un arranque fallido.**
**Concepto**: `HostAbortedException`; `dotnet ef` construye la app y la corta después de `Build()`, y un `catch` amplio lo toma por un fallo.
**Cambia**: el `catch` de `Program.cs`.
**Verifica**: `dotnet ef migrations list` antes (log Fatal falso) y después (sin Fatal); `dotnet test` completo.

**5.3. La configuración se lee solo con options validadas.**
**Concepto**: una sola puerta para la configuración; el bootstrap de Serilog en dos etapas.
**Cambia**: `DataProtectionKeysOptions` (ruta absoluta), `TelemetryOptions` con `[ConfigurationKeyName]` para `OTEL_EXPORTER_OTLP_ENDPOINT`, logger final de Serilog desde los servicios, `Program.cs` y las features sin `IConfiguration`.
**Verifica**: casos nuevos en `ConfigurationValidationTests`; logs, trazas y métricas llegan a un collector; `dotnet test` completo.

**5.4. Regla de Dune para la lectura de configuración (DUNE0013).**
**Concepto**: regla ejecutable; ningún archivo del backend lee `IConfiguration` ni variables de entorno.
**Cambia**: `dune/Dune.Analyzers`, sus tests, `Dune.props`, `conventions.md`.
**Verifica**: fixture válida y violación deliberada; rojo sobre el código de antes del paso 5.3; build en verde.

**6. Catálogo de errores estable.**
**Concepto**: el contrato de error también es API; hoy los códigos se arman en cada caso de uso.
**Cambia**: `Shared/Http` (`ApiResults` con un solo mapeo, `HttpErrors`), catálogos por feature, el 500 de `GlobalExceptionHandler`, el 401/403 de la cookie en `SessionHttpPolicy`, tests de estados y payloads.
**Verifica**: test de cada tipo de error (400/401/403/404/409/500) con `code` estable; `smoke-api.mjs`.

**6.1. Todo error sin cuerpo sale como ProblemDetails.**
**Concepto**: ningún error queda mudo; `UseStatusCodePages` le da cuerpo a las respuestas de error vacías y un `CustomizeProblemDetails` completa el `code`.
**Cambia**: `Program.cs`, `BackendInfrastructure`, `HttpErrors` (`Http.NotFound`, `Http.MethodNotAllowed`…), `ApiVersioningTests` a propósito.
**Verifica**: `/api/v9/...` y una ruta inexistente responden 404 con `code`; `dotnet test`; `smoke-api.mjs`.

**6.2. Regla de Dune para el catálogo de errores (DUNE0014).**
**Concepto**: regla ejecutable; un `Error.<Factory>(...)` de ErrorOr solo se escribe en un `*Errors.cs`, y en una feature el prefijo del código es el de la clase (`TaskErrors` → `Task.`), salvo los de validación, cuyo código es el campo.
**Cambia**: `dune/Dune.Analyzers`, sus tests, `Dune.props`, `conventions.md`.
**Verifica**: fixture válida y violación deliberada; rojo sobre el código de antes del paso 6; build en verde.

**6.3. Convención de nombres para los contratos entre features (DUNE0015).**
**Concepto**: la interfaz de un módulo profundo se reconoce por su nombre; cada feature que expone algo tiene una sola puerta, `I<Feature>Module`, con métodos por caso de uso, y los datos que cruzan el límite dicen su propósito (`<Cosa><Propósito>`, sufijo de lista cerrada). `Response` queda solo para HTTP. Un contrato nace solo cuando otra feature lo pide.
**Cambia**: `Features/*/Contracts` (las interfaces de Auth y Users pasan a `IAuthModule` e `IUsersModule`; `UserAccount` y `UserProfileSnapshot` toman nombre de propósito), sus implementaciones y llamadores, revisión de `UserProfiles.FindAsync` con el deletion test, `conventions.md`, `dune/Dune.Analyzers` y sus tests. No cambia el JSON de la API.
**Verifica**: fixture válida y violación deliberada de DUNE0015; rojo sobre el código de antes del paso; `dotnet test`; `smoke-api.mjs`.

**7. Paginación por keyset.**
**Concepto**: lectura estable a escala; el offset se degrada con volumen y duplica filas al insertar.
**Cambia**: la slice `ListTasks` (con `TaskListResponse`, que desde el paso 6.3 vive en la slice), cliente y modelos de tareas en el frontend, tests.
**Verifica**: test que inserta filas entre dos páginas y no repite ni omite; navegador (`verify-template` drive) sobre la lista paginada.

### Fase 3 — Escritura correcta (pasos 8-10)

**8. Concurrencia optimista en `TaskItem`.**
**Concepto**: lost update; el estado final explícito no alcanza sin control de versión.
**Cambia**: `Domain/TaskItem.cs`, `Persistence/TaskItemConfiguration.cs`, la slice `UpdateTask`, `TaskResponse` y `TaskErrors`, migración forward, tests. Como el `PUT` exige la versión, cambia el contrato: entran también el frontend de tareas, `smoke-api.mjs` y los e2e (regla 3).
**Verifica**: test con dos actualizaciones concurrentes: una gana y la otra recibe 409; migración aplicada por CLI sin editar a mano.

**9. Idempotencia de escritura.**
**Concepto**: reintentos seguros; a todo POST de negocio se le puede repetir la intención.
**Cambia**: mecanismo genérico en `Infrastructure/`, la slice `CreateTask`, migración de la tabla de claves, tests. El `POST` suma el header `Idempotency-Key` y respuestas nuevas, así que entran también `ApiResults` y `HttpErrors` (422), el frontend de tareas, `smoke-api.mjs` y los e2e (regla 3), y la regla DUNE0016 (regla 4).
**Verifica**: test que envía la misma clave dos veces y obtiene un solo recurso y la misma respuesta; `smoke-api.mjs` sin regresión.

**10. Auditoría de cambios.**
**Concepto**: trazabilidad; quién cambió qué y cuándo, sin guardar datos personales.
**Cambia**: interceptor EF en `Infrastructure/Persistence`, migración, tests.
**Verifica**: test que verifica el registro de una actualización y comprueba que no hay PII en el log de auditoría.

### Fase 4 — Seguridad y autorización (pasos 11-13)

**11. Permisos memorizados por request.**
**Concepto**: caché con alcance correcto; hoy cada comprobación golpea Identity, y el alcance permitido es *solo* dentro del request.
**Cambia**: `AccessPolicy` en `Features/Auth/Access` (detrás de `IAuthModule` desde el paso 6.3), tests.
**Verifica**: test que cuenta consultas de permisos en una request con varias comprobaciones; cambiar un permiso sigue aplicándose en el request siguiente.

**12. Rate limiting por usuario y por tipo de endpoint.**
**Concepto**: protección de recursos y fairness; hoy solo hay una política fija para `/auth`.
**Cambia**: configuración del limiter, endpoints, tests.
**Verifica**: test que observa 429 con `Retry-After`; `smoke-api.mjs` sigue pasando.

**13. Cabeceras de seguridad y escaneo continuo.**
**Concepto**: superficie HTTP y cadena de dependencias como parte del diseño.
**Cambia**: middlewares, `Program.cs`, `ci.yml` (escaneo de dependencias y de secretos), `docs/`.
**Verifica**: cabeceras observables en una respuesta real; el job nuevo falla ante un secreto plantado en un fixture de prueba y luego se retira.

### Fase 5 — Observabilidad y operación (pasos 14-17)

**14. Métricas de negocio reales.**
**Concepto**: métricas contra logs; cardinalidad y etiquetas acotadas.
**Cambia**: un `Meter` por feature (`TemplateApp.Tasks`, `TemplateApp.Auth`), instrumentación en las slices, tests.
**Verifica**: test con `MeterListener` que observa el contador/histograma emitido; `smoke-api.mjs --compose` con export OTLP no vacío.

**15. Correlación log↔traza automatizada.**
**Concepto**: verificación de señales en CI, no a ojo.
**Cambia**: scripts de `verify-template`, job `compose` de `ci.yml`, `artifacts/`.
**Verifica**: el job falla si un log de request no lleva trace/span o si el collector recibe métricas vacías.

**16. Health por dependencia.**
**Concepto**: disponibilidad accionable; readiness que refleje cada dependencia real.
**Cambia**: feature `Health`, tests.
**Verifica**: readiness 503 con base caída y con migraciones pendientes; live sigue 200 sin base.

**17. SLOs y runbooks.**
**Concepto**: operación por objetivos y presupuesto de error.
**Cambia**: documentos de operación y runbooks en `docs/`, alertas sobre las métricas ya existentes.
**Verifica**: cada SLO con su métrica consultada realmente en el backend de métricas; cada alerta con su runbook y una ejecución de prueba.

### Fase 6 — Datos y rendimiento (pasos 18-20)

**18. Índices medidos, no supuestos.**
**Concepto**: plan de consulta verificado.
**Cambia**: `Persistence/TaskItemConfiguration.cs`, migración, test de integración con `EXPLAIN`.
**Verifica**: test que afirma que la consulta de listado usa el índice esperado.

**19. Migraciones expand/contract ensayadas.**
**Concepto**: despliegue sin corte; nunca destructivo en el mismo release.
**Cambia**: migraciones, `docs/conventions.md`, evidencia de la secuencia completa.
**Verifica**: ciclo probado sobre una base con datos: agregar, backfill, cambiar código, borrar; readiness en verde en cada etapa.

**20. Lectura dedicada por módulo (CQRS liviano).**
**Concepto**: separar lectura de escritura sin tocar invariantes.
**Cambia**: contratos de lectura y operación en `Tasks`, tests.
**Verifica**: la lectura nueva devuelve exactamente el mismo resultado observable que la anterior; test comparativo antes de borrar la vieja.

### Fase 7 — Modularidad y límites (pasos 21-23)

**21. Contrato público por feature, y gate que lo haga cumplir.**
**Concepto**: módulos con interface chica.
**Nota**: el paso 0.5 ya hace cumplir "solo `Contracts`" entre features. Este paso queda para lo que falte, por ejemplo impedir que una slice sea pública.
**Cambia**: `dune/Dune.Analyzers`, `check-structure.mjs` + su test, `docs/conventions.md`.
**Verifica**: fixture negativa que referencia el interior de otra feature y falla con el diagnóstico esperado; fixture válida que compila.

**21.1. Cerrar los huecos de DUNE0004.**
**Concepto**: un gate vale lo que su caso más débil; cada forma de cruzar el límite que no ve es una puerta abierta.
**Nota**: Tobi lo pidió el 2026-11-17, durante el paso 21. La investigación del paso 21 ([paso-21-contrato-publico.md](investigacion/paso-21-contrato-publico.md), sección 3) probó 16 formas de usar el interior de otra feature y DUNE0004 no ve tres: un método o una propiedad de extensión de otra feature (`s.Shout()` con su `using`), el resultado de una invocación que no se guarda en una variable (`Bridge.Get().Name`), y un tipo en una subcarpeta de `Contracts/` (DUNE0004 acepta `Contracts.*`). La revisión del paso 21 sumó un cuarto: DUNE0005 no visita `record struct` ni `delegate`, así que uno de esos tipos en una slice puede declarar el namespace `...Contracts` y DUNE0004 lo acepta (DUNE0020 ya lo frena si es `public`, pero no si es `internal`).
**Cambia**: `dune/Dune.Analyzers/CrossFeatureAnalyzer.cs`, `NamingAnalyzer.cs` y sus tests, `docs/conventions.md`.
**Verifica**: un test rojo por hueco con el analyzer de antes del paso, y los tres en verde después; la suite de Dune y el build del backend sin DUNE0004 nuevos.

**22. Un `DbContext` por feature.**
**Concepto**: propiedad de datos; el contexto dios impide extraer módulos.
**Cambia**: `Infrastructure/Persistence` hacia `Features/*/Persistence`, migraciones por contexto, arranque, tests.
**Verifica**: suite completa con dos contextos; smoke con base limpia y migrada; readiness en verde. **Paso de alto riesgo**: la migración de esquema es irreversible en producción, ensayala primero en el paso 19.
**Nota**: Tobi eligió el 2026-11-18, con la investigación ([paso-22-dbcontext-por-feature.md](investigacion/paso-22-dbcontext-por-feature.md)): un contexto por feature más `PlatformDbContext` para `IdempotencyKeys` y `AuditEntries`; tablas en `public` en este paso; borrar las FKs entre features y que `DeleteUser` borre en código por contrato, en dos tandas (primero el borrado en código, después las FKs); `TransactionScope` con un helper que fija `ReadCommitted`; el `AppDbContext` viejo queda sin tablas como dueño de sus migraciones; regla nueva DUNE0021. "Dos contextos" quiere decir más de uno: quedan cuatro.

**22.1. Un schema por feature.**
**Concepto**: aislamiento lógico de los datos en la base; cada módulo tiene su schema (`auth`, `users`, `tasks`, `platform`).
**Nota**: Tobi lo pidió el 2026-11-18, durante el paso 22 ("prefiero hacer el schema por feature porque lo propone milan"). Va en un paso propio porque mover una tabla de schema rompe al release anterior: primero la connection string suma `Search Path`, después se mueven las tablas. El paso 22 ya deja cada tabla de historial en su lugar final.
**Cambia**: `HasDefaultSchema` en cada contexto, migraciones con `EnsureSchema` y `RenameTable` de solo schema, connection string, DUNE0018 (regla 9: entrada en `decisions.tsv`, fixture válida y violación), `informe-vsa-deep-modules.md`.
**Verifica**: ensayo con binarios (`rehearse-migrations.mjs`) donde el release anterior sigue andando después de mover las tablas; base limpia y base migrada con el mismo esquema; readiness en verde.

**23. Gateway delante del monolito.**
**Concepto**: límite de despliegue; el primer proceso propio. Una fachada que recibe toda request de la API y la reparte (patrón Strangler Fig), antes de extraer nada.
**Nota**: Tobi eligió el 2026-11-18, con la investigación ([paso-23-servicio-gateway.md](investigacion/paso-23-servicio-gateway.md)): YARP como gateway (regla 5), un ADR nuevo que permite un proyecto .NET por proceso ([ADR-0005](../adr/0005-gateway-y-procesos-propios.md)), y partir el paso en dos: este pone el gateway y el 23.2 extrae `Tasks`. Las demás decisiones son las recomendaciones de la investigación.
**Cambia**: proyecto `gateway/` (YARP), `compose.yaml` (gateway, healthchecks), `frontend/nginx.conf`, Dockerfile del gateway, `ci.yml`, `smoke-api.mjs`, `check-structure.mjs` (gateway como única entrada).
**Verifica**: `docker compose up --build --wait` + `smoke-api.mjs --compose` contra el nuevo despliegue; navegador completo; health de cada proceso; la traza cruza el gateway.

**23.1. Segunda entidad con reglas propias y una lectura con otra forma.**
**Concepto**: CQRS completo del lado de escritura y de lectura. Con `Task` sola, el comando casi no tiene reglas que proteger y la respuesta es una copia de la tabla, así que separar lectura y escritura no muestra su beneficio. Una entidad con reglas que siempre tienen que cumplirse (invariantes) muestra por qué el comando carga y protege la entidad, y una lectura que junta datos de varias features muestra por qué la consulta no la carga.
**Nota**: Tobi lo pidió el 2026-11-17, durante el paso 20 ("dale si la tercera"). La entidad la elige la investigación del paso (regla 12); un candidato es `Projects`, un proyecto con estados (activo, archivado) y reglas que solo él protege, con una lectura "proyecto con sus contadores de tareas y el nombre del dueño". Va después del paso 22: con un `DbContext` por feature, esa lectura no puede unir tablas de otras features y tiene que pasar por sus `Contracts`.
**Cambia**: feature nueva con Domain/Contracts/Persistence/Tests, migración, endpoints, frontend (regla 3: el contrato HTTP es nuevo), `smoke-api.mjs`. También `Tasks` (`ProjectId` en la tarea, `projectId` en `POST /tasks` y en la respuesta, `CountByProjectAsync` en su contrato), `Users` (`FindDisplayNamesAsync` y `DeleteUser` borra los proyectos) y el arranque de la base (la línea base de una base heredada tomaba también los contextos nuevos).
**Nota**: la investigación es [paso-23-1-segunda-entidad.md](investigacion/paso-23-1-segunda-entidad.md). Se hizo en modo autónomo: la entidad es `Project` y las demás elecciones son las recomendaciones de la investigación, en `docs/decisions.tsv`. "CQRS completo" quiere decir los dos lados con reglas de verdad sobre la misma base (CQRS liviano, `architecture.md`); la lectura con base propia es el paso 24.1.
**Verifica**: un test por invariante, donde el comando que la rompe falla sin cambiar la base; la lectura no carga la entidad (test que lo afirma); smoke y navegador con la feature nueva.

**23.2. Extraer `Tasks` a su propio proceso detrás del gateway.**
**Concepto**: contrato remoto; una llamada entre features que cruza el límite pasa a ser HTTP.
**Nota**: Tobi lo pidió el 2026-11-18, al partir el paso 23. Va después del 23.1, para que la lectura del 23.1 se escriba con las features en un proceso. Decisiones tomadas en el paso 23 con la investigación: `Tasks` sigue leyendo la cookie de sesión (claves de Data Protection compartidas) y le pregunta a Auth por HTTP si la sesión sigue activa y qué permisos hay; el contrato remoto lo escribe quien llama, con sus DTOs y un cliente HTTP tipado; `DeleteUser` borra primero las tareas por HTTP (operación idempotente) y después el perfil y la cuenta en una transacción local; los clientes llevan solo timeout (los reintentos son del paso 29); las tablas de `platform` quedan compartidas y las migra el monolito; los endpoints internos se protegen solo por red en este paso; proyectos separados con una librería compartida para `Infrastructure/` y `Shared/`.
**Cambia**: backend (proyecto nuevo de `Tasks`, librería compartida, clientes HTTP, endpoints internos), Dune y `check-structure.mjs` (varios proyectos), `ApiFixture`, `compose.yaml`, rutas del gateway, `verify.mjs`, `ci.yml`, reglas de SLO, docs.
**Verifica**: smoke de Compose y navegador completo contra los dos procesos; revocar una sesión corta el acceso a `Tasks` en la request siguiente; borrar la cuenta con `Tasks` caído no deja tareas huérfanas; health de cada proceso.
**Nota**: la investigación es [paso-23-2-extraer-tasks.md](investigacion/paso-23-2-extraer-tasks.md). Se hizo en modo autónomo y sin herramientas nuevas: `IHttpClientFactory` viene con ASP.NET Core. Las cuatro llamadas que cruzan el límite (sesión y permisos, proyecto asignable, contadores de tareas y borrado de las tareas de una cuenta) son endpoints bajo `/internal/v1`, y la falla del otro proceso responde 503. Las elecciones están en `docs/decisions.tsv`. Reglas nuevas: DUNE0022 (ninguna llamada remota dentro de `CrossContextTransaction`) y "límite de despliegue" en `check-structure.mjs`.

### Fase 8 — Asincronía, mensajería y consistencia (pasos 24-28)

**24. Eventos de dominio + outbox transaccional, sin broker.**
**Concepto**: atomicidad entre estado y evento; el error clásico es publicar antes de confirmar.
**Cambia**: `Features/Tasks` (eventos), tabla de outbox, `Infrastructure/Persistence`, migración, tests.
**Verifica**: test donde el relay falla y el evento queda pendiente sin perder el cambio de estado; duplicado entregado dos veces y consumido una sola vez.
**Nota**: la investigación es [paso-24-outbox-transaccional.md](investigacion/paso-24-outbox-transaccional.md). Se hizo en modo autónomo y sin herramientas nuevas: EF Core, Npgsql, `BackgroundService`, `System.Text.Json` y OpenTelemetry ya estaban. `TaskItem` anota sus eventos, un interceptor de EF los guarda en `tasks."OutboxMessages"` en el mismo `SaveChanges`, y un relay casero los entrega dentro del proceso con `FOR UPDATE SKIP LOCKED`, reintentos y un inbox por consumidor. No hay consumidor de producción hasta el 24.1, y el evento de cuenta borrada, que cruza de proceso, queda para el paso 25. Las elecciones están en `docs/decisions.tsv`. Regla nueva: DUNE0023 (solo `OutboxRelay` llama a un consumidor de eventos).

**24.1. Modelo de lectura alimentado por eventos.**
**Concepto**: proyección con consistencia eventual: la lectura vive en su propia tabla y se pone al día después de la escritura, a partir de los eventos del paso 24.
**Nota**: Tobi lo pidió el 2026-11-17 junto con el paso 23.1. La proyección reemplaza la lectura del paso 23.1 que junta varias features.
**Cambia**: tabla de proyección, consumidor de eventos que la actualiza, la consulta del paso 23.1, migración, tests.
**Verifica**: test comparativo, donde la proyección devuelve lo mismo que la lectura de antes; test donde el consumidor se atrasa y la lectura muestra el dato viejo hasta procesar el evento; reconstrucción de la proyección desde cero con el mismo resultado.
**Nota**: la investigación es [paso-24-1-proyeccion-eventos.md](investigacion/paso-24-1-proyeccion-eventos.md). Se hizo en modo autónomo y sin herramientas nuevas. El relay de Tasks reenvía cada evento de tarea por HTTP (`PUT /internal/v1/task-events/{messageId}`) al monolito, que suma o resta en `projects."ProjectTaskCounters"` con su propio inbox. La reconstrucción parte de una foto de Tasks más los eventos sin entregar. El PR va en tres commits ordenados como releases (expand, cambio de lectura, contract). Las elecciones están en `docs/decisions.tsv`. Regla nueva: DUNE0024 (una lectura no llama a otro proceso).

**25. Broker real con consumidor idempotente y DLQ.**
**Concepto**: at-least-once, deduplicación, mensajes venenosos, orden por partición.
**Cambia**: `compose.yaml` (broker), adaptador de mensajería, relay del outbox, health de la dependencia, tests de integración con el broker contenedorizado. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: librería — Wolverine (MIT) o MassTransit v8 (Apache-2.0) si no hay presupuesto; v9 y NServiceBus son comerciales. MediatR NO es candidato aquí (es in-process, no bus).
**Verifica**: test de reintento y de DLQ; test que mata al consumidor a mitad y el mensaje se reprocesa sin efecto duplicado.

**26. Saga coreografiada (3 pasos con compensación).**
**Concepto**: transacción distribuida sin locks; compensación explícita.
**Cambia**: los dos o tres módulos participantes, mensajería, tests.
**Verifica**: test donde el último paso falla y la compensación deja el sistema consistente y observable.

**27. Saga orquestada con máquina de estados persistida.**
**Concepto**: orquestación, visibilidad del proceso, reanudación tras caída.
**Cambia**: módulo orquestador o infraestructura de sagas, tests. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: tecnología — saga orquestada a mano sobre el bus elegido en el paso 25, o state machine del propio bus (MassTransit state machine vs Wolverine saga).
**Verifica**: test que reinicia el proceso a mitad de saga y esta continúa desde su estado persistido.

**28. Durable execution para un proceso largo con auditoría.**
**Concepto**: cuándo conviene una plataforma de workflow en lugar de una saga casera.
**Cambia**: worker nuevo, `compose.yaml`, tests de reanudación. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: plataforma — workflow durable (p. ej. Temporal, Elsa) vs saga persistida propia del paso 27.
**Verifica**: proceso de varios días simulado con timers acelerados; historial consultable y auditable.

### Fase 9 — Integraciones externas (pasos 29-32)

**29. Resiliencia en los clientes HTTP salientes.**
**Concepto**: timeouts en cascada, retry con backoff, circuit breaker, bulkhead; contra un servidor falso. Los primeros clientes HTTP llegan en el paso 23.2, solo con timeout.
**Cambia**: el cliente en la feature que lo usa, la política de resiliencia en `Infrastructure/`, `Microsoft.Extensions.Http.Resilience`, tests con WireMock.
**Verifica**: test con fallo transitorio que termina en éxito, y con fallo persistente que abre el circuito; prueba de que el timeout interno es menor que el externo.

**30. Pasarela de pago detrás de un puerto propio.**
**Concepto**: ports & adapters, anti-corruption layer, webhook como fuente de verdad, PCI por checkout hospedado.
**Cambia**: feature nueva con Domain/Contracts/Persistence/Tests, tabla de eventos de webhook, `smoke-api.mjs` con caso de webhook firmado. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: proveedor (p. ej. Stripe vs MercadoPago) y modalidad (checkout hospedado vs propio, con implicancia PCI).
**Verifica**: test de firma inválida rechazada, de replay de webhook sin efecto, y de orden de eventos alterado; el modelo propio no contiene tipos del proveedor.

**31. Máquina de estados de pago + conciliación programada.**
**Concepto**: dinero con estados auditados, idempotencia, conciliación diaria, jobs con visibilidad.
**Cambia**: feature de pagos, scheduler (⏸ **DECISIÓN DE TOBI pendiente**: Quartz.NET si no hay presupuesto, Hangfire si querés dashboard), tests.
**Verifica**: test de transición inválida rechazada; job de conciliación que detecta una diferencia plantada en un fixture.

**32. Integración por archivo (SFTP o CSV) con cuarentena.**
**Concepto**: integraciones no HTTP; idempotencia por checksum y control de rechazados.
**Cambia**: worker o feature de intercambio, tests con archivos de fixture.
**Verifica**: archivo con una fila inválida entra en cuarentena y el resto se procesa; reprocesar el mismo archivo no duplica.

### Fase 10 — Escala, cadena de suministro y gobernanza (pasos 33-36)

**33. Caché distribuida con invalidación medida.**
**Concepto**: caching con alcance y evidencia; hit ratio como métrica de ingeniería.
**Cambia**: `Microsoft.Extensions.Caching.Hybrid` en una lectura caliente, caché en `compose.yaml`, health de la caché, tests. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: servidor — Redis (AGPL) o Valkey (BSD-3).
**Verifica**: test de invalidación tras escritura; test de aislamiento entre usuarios; métrica de hit ratio observable en el export OTLP. Revisar licencia del servidor elegido.

**34. Cadena de suministro.**
**Concepto**: qué se entrega y cómo se prueba que no fue alterado.
**Cambia**: `Dockerfile`, `ci.yml` (escaneo de imagen y dependencias, SBOM CycloneDX por release, procedencia), versionado centralizado de paquetes y lockfile, `docs/`.
**Verifica**: SBOM generado y descargable como artifact; el job falla ante una dependencia vulnerable plantada a propósito en un fixture.

**35. Despliegue progresivo con rollback probado.**
**Concepto**: liberar sin miedo; feature flags para desacoplar release de deploy.
**Cambia**: pipeline de entrega, configuración de canary o blue-green, feature flags, `docs/`.
**Verifica**: despliegue parcial con métricas comparadas y rollback ejecutado de verdad, con evidencia de ambas direcciones.

**36. Gobernanza final.**
**Concepto**: sostenibilidad organizacional, no técnica.
**Cambia**: `docs/adr/` con las decisiones de las fases nuevas, `docs/decisions.tsv`, matriz de licencias aprobada (Polly, MediatR, FluentAssertions, MassTransit si entraron), política de deprecación de API, simulacro de recuperación con RTO/RPO medidos.
**Verifica**: simulacro de restauración ejecutado con tiempos medidos y registrados en `artifacts/`; cada ADR con su alternativa descartada y su consecuencia.

---

## 5. Qué se aprende en cada tramo

| Tramo | Se aprende a manejar | Complejidad |
|---|---|---|
| Fases 1-2 | Verificación reproducible, contratos, configuración, errores, paginación | Baja |
| Fases 3-4 | Correctitud de escritura bajo concurrencia y reintentos, seguridad de superficie | Baja-media |
| Fases 5-6 | Operar con evidencia: métricas, SLOs, índices, migraciones sin corte | Media |
| Fases 7-8 | Límites reales entre módulos y procesos; consistencia eventual con outbox y sagas | Alta |
| Fases 9-10 | Integrar terceros con resiliencia y dinero de por medio; entregar y gobernar a escala | Alta |

**Cuándo se puede parar**: un producto de un equipo se sostiene bien al terminar la fase 6. La fase 7 paga cuando hay varios equipos. Las fases 8-10 pagan cuando hay procesos largos, dinero, terceros o auditoría.

**Dónde está el riesgo real**: el paso 22 (migración de esquema) y el 23 (extracción de proceso) son los únicos difíciles de revertir. Los pasos 25, 27 y 33 obligan a decidir por licencia antes de escribir una línea: Wolverine y Rebus son MIT, MassTransit v8 es Apache-2.0, MassTransit v9 y NServiceBus son comerciales, Redis es AGPL y Valkey BSD-3.

---

## 6. Verificación de este plan

Leí el estado real antes de escribirlo: `backend/Program.cs`, `Infrastructure/DependencyInjection/BackendInfrastructure.cs` (composición, ProblemDetails, Identity, JWT, rate limiter), `Infrastructure/Observability/ObservabilityExtensions.cs`, `Infrastructure/Persistence` (contexto, inicializador, migración única), `Features/Tasks/*` (`TasksModule`, `TasksEndpoints`, casos de uso), `Shared/Authorization/*` (`ResourceAuthorizer`, `PermissionService`), `Shared/Http/ApiResults.cs`, `backend/Tests/TemplateApp.Tests.csproj` y `Support/`, `dune/` (analizadores, gates y sus tests), `.github/workflows/ci.yml`, y la skill del proyecto `verify-template`. Los huecos listados en §1 salen de búsquedas sobre el backend: sin `BackgroundService`, sin caché (`IDistributedCache`/`HybridCache`), sin `DomainEvent`, sin tokens de concurrencia propios, sin `AddHttpClient`, sin idempotencia.

No modifiqué archivos, no compilé y no ejecuté gates para este plan. Los comandos de §3 son los declarados por el repo; su estado real de verde o rojo es justamente el producto del paso 1.
