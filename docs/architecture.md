# Arquitectura de TemplateApp

La decisión vigente es [ADR-0004](adr/0004-vertical-slices-deep-modules.md): vertical slices dentro de features que funcionan como módulos profundos. Las reglas para escribir código están en [conventions.md](conventions.md).

## Forma del backend

El backend es un solo proyecto ASP.NET Core 10, con namespace raíz `TemplateApp`. Desde el paso 23, un gateway YARP (`gateway/`, otro proyecto y otro proceso) lo tiene adelante: toda request a la API entra por él ([ADR-0005](adr/0005-gateway-y-procesos-propios.md)).

```text
backend/
├── Program.cs               host: plataforma + lista explícita de features
├── Infrastructure/          fontanería técnica; no nombra ninguna feature
├── Shared/                  núcleo mínimo; no nombra ninguna feature
└── Features/
    └── Tasks/
        ├── TasksFeature.cs  registra DI y rutas de la feature
        ├── TaskErrors.cs    piezas internas que comparten varias slices
        ├── Contracts/       lo que usa otra feature: la puerta I<Feature>Module y sus datos
        ├── Domain/          modelos puros y reglas del dominio
        ├── Persistence/     su DbContext, sus mappings de EF y sus migraciones
        └── CreateTask/
            ├── CreateTaskRequest.cs
            ├── CreateTaskRequestValidator.cs
            ├── CreateTask.cs            operación: clase con sus dependencias
            ├── CreateTaskEndpoint.cs    adapter HTTP: Map y handler
            └── CreateTaskTests.cs
```

- **Feature:** un módulo profundo. Su interfaz pública es `Contracts/` más sus rutas HTTP. En C#, `public` marca exactamente `Contracts/` y el resto es `internal` (DUNE0020). Todas las features viven en `TemplateApp.dll`, así que `internal` no las separa entre sí: lo hace DUNE0004.
- **Slice:** un caso de uso, en su carpeta. La operación es una clase propia, con sus dependencias. El endpoint solo adapta HTTP y llama a la operación.
- **Domain:** sin EF, ASP.NET ni infraestructura. Concentra las reglas que comparten las slices.
- **Infrastructure:** observabilidad, middlewares, arranque de la base (`Persistence/DatabaseInitializer`: migra cada contexto bajo un advisory lock), los health checks de la base (`Persistence/DatabaseHealthCheck` y `Persistence/PendingMigrationsHealthCheck`), el grupo versionado de la API, el filtro de idempotencia de los `POST` (`Http/IdempotencyFilter`, con su tabla `IdempotencyKeys`), la auditoría de cambios (`Persistence/AuditInterceptor`, con su tabla `AuditEntries`), el contexto de esas dos tablas (`Persistence/PlatformDbContext`), la transacción entre contextos (`Persistence/CrossContextTransaction`), el limiter global de requests (`Http/ApiRateLimiting`), las cabeceras de seguridad de toda respuesta (`Http/ApiSecurityHeaders`) y composición del host.
- **Shared:** tipos generales que usan varias features, como `ApiResults`.

No hay mediator ni bus. El paso 25 del plan elige uno.

Lectura y escritura están separadas por slice (CQRS liviano): misma base y mismo contexto de la feature, dos modelos. Las slices de escritura cargan la entidad rastreada, la cambian y guardan. Las de lectura (`ListTasks`, `GetTask`) proyectan a la respuesta en una consulta y no rastrean la entidad. `Users/GetUser` todavía carga `UserProfile` sin rastreo: es la excepción de la regla. Los permisos sí pueden dejar roles de Identity rastreados (pendiente en `progreso.md`). DUNE0019 lo hace cumplir (`conventions.md`, "Lecturas").

## Estado actual frente al objetivo

Hoy no queda una diferencia abierta entre el código y la forma de arriba. La última, las tablas de todas las features en el schema `public`, la cerró el paso 22.1. Los pasos siguientes del [plan](evolucion/plan.md) agregan piezas nuevas.

## Perfil y propiedad

Identity guarda el nombre de login, el email y las credenciales. `UserProfile` guarda `AuthId`, `DisplayName` y `Timezone`. `TaskItem.OwnerId` guarda el `AuthId` del dueño. Las sesiones referencian Identity con una FK y su cascada, porque las dos tablas son de Auth.

Cada feature es dueña de sus tablas y las mapea en su propio contexto, `internal`, en su `Persistence/`: `AuthDbContext` (Identity y `Sessions`), `UsersDbContext` (`UserProfiles`) y `TasksDbContext` (`TaskItems`). `PlatformDbContext`, en `Infrastructure/Persistence`, es dueño de `IdempotencyKeys` y `AuditEntries`, que son de todo el host. Cada contexto tiene su carpeta de migraciones y su schema (`auth`, `users`, `tasks`, `platform`), declarado con `HasDefaultSchema`: ahí están sus tablas y su historial, y la base muestra de quién es cada tabla. `public` guarda solo el historial del contexto viejo en una base heredada. Toda conexión lleva `Search Path=public,auth,users,tasks,platform` (`DatabaseSearchPath`). EF nombra las tablas con su schema; el path existe para el release que todavía las nombra sin schema. Por eso las tablas se movieron en dos releases (paso 22.1): primero el path, después las migraciones `Move*`, que hacen `ALTER TABLE ... SET SCHEMA`. El `AppDbContext` viejo no mapea nada: solo es dueño de las migraciones anteriores al paso 22. DUNE0021 hace cumplir dónde vive cada contexto y que haya uno por feature.

Al arrancar, `DatabaseInitializer` toma el advisory lock y mira si existe `public."__EFMigrationsHistory"`. Si existe, la base es de antes del paso 22: corre lo que falte del contexto viejo y anota la `InitialCreate` de cada contexto nuevo como aplicada, sin correrla, porque sus tablas ya están (línea base, con `IHistoryRepository`). Después migra cada contexto (su `Move*` lleva las tablas a su schema) y siembra. Una base vacía no crea el historial viejo: cada `InitialCreate` crea sus tablas en `public` y su `Move*` las mueve. Los dos caminos dejan el mismo esquema, y `PerFeatureContextsTransitionTests` lo compara y afirma el schema de cada tabla.

Entre features, una entidad guarda solo el Id de la otra: `TaskItem.OwnerId` es un `string`, sin navegación ni FK. No hay FKs entre tablas de features distintas: una FK no puede cruzar dos contextos sin que uno mapee la tabla del otro. La base ya no impide una tarea huérfana; la cascada vive en código. `DeleteUser` (Users) borra, en una sola transacción, las tareas con `Tasks.Contracts.ITasksModule.DeleteOwnedTasksAsync`, las claves de idempotencia con `Infrastructure/Persistence/IdempotencyKeys`, el perfil, y la cuenta con `IAuthModule.DeleteAccountAsync` (las sesiones caen por su FK dentro de Auth). El borrado en código salió un release antes que el borrado de las FKs, para que el release anterior no deje huérfanos durante el despliegue.

Auth, Users y Tasks se usan solo por contrato. Auth crea y lee el perfil con `Users.Contracts.IUsersModule`. Users y Tasks leen la cuenta, la borran y deciden permisos con `Auth.Contracts.IAuthModule`. Users borra las tareas de una cuenta con `Tasks.Contracts.ITasksModule`. Ninguna conoce el interior de otra.

## Auditoría de cambios

`AuditInterceptor` es un `SaveChangesInterceptor` de EF Core, un singleton registrado en cada contexto. Antes de cada `SaveChanges`, agrega una fila en `AuditEntries` por cada entidad `IAuditable` (`Shared/Domain`) agregada, modificada o borrada. Hoy son `TaskItem` y `UserProfile`. La fila viaja en el mismo `SaveChanges`, entonces queda en la misma transacción que el cambio. Para eso, `UsersDbContext` y `TasksDbContext` mapean `AuditEntries` con `ExcludeFromMigrations` y su schema escrito, `platform` (`MapSharedAuditEntries`): la escriben, pero solo `PlatformDbContext` la crea y la migra. si el cambio hace rollback (un 409 de versión, un 400 dentro del filtro de idempotencia), la fila tampoco queda.

Cada fila dice quién, qué y cuándo, sin datos personales:

- **Quién:** `ActorKind` (`user`, `anonymous` en el registro, `system` fuera de un pedido HTTP) y `ActorId`, el id opaco de Identity leído del claim `NameIdentifier` con `IHttpContextAccessor`, como hace el filtro de idempotencia. Infrastructure no nombra a Auth.
- **Qué:** `EntityType`, `EntityId`, `Operation` (`Added`, `Modified`, `Deleted`) y `ChangedProperties`, solo los nombres de las propiedades modificadas. Nunca valores: el título de una tarea es texto libre y el perfil guarda nombre visible y zona horaria.
- **Cuándo:** `OccurredAt` del `TimeProvider` y el `TraceId` de la actividad del pedido, que une la fila con el log y la traza.

Lo que no pasa por el ChangeTracker no deja fila: `ExecuteUpdate`, `ExecuteDelete`, SQL crudo y las cascadas que ejecuta la base. DUNE0017 prohíbe las dos primeras sobre una entidad `IAuditable`. Al borrar la cuenta, cada tarea y el perfil dejan su fila `Deleted`: `DeleteUser` los carga y los borra con `SaveChanges`. `AuditEntries` no tiene FK a la cuenta: la historia sobrevive al borrado, con el id opaco. La tabla todavía no es append-only ni se limpia (pendientes en `progreso.md`).

El registro crea identidad, perfil, rol y sesión en una transacción explícita. Las escrituras que cruzan contextos (`RegisterUser`, `DeleteUser` y `IdempotencyFilter`) abren un `TransactionScope` con `CrossContextTransaction.Begin()`, que fija `ReadCommitted` y el flujo async: cada contexto abre su conexión adentro y Npgsql la anota sola. Acopla los módulos en la transacción; es un acople que el plan acepta hasta el outbox del paso 24. Las restricciones únicas protegen las carreras. Un resultado de error no confirma la transacción. Las migraciones son forward y siguen expand/contract (`conventions.md`, "Migraciones: expand/contract"): ningún release borra ni renombra lo que usa el release anterior, y el backfill va en su propia migración. Sin reset de base ni `EnsureCreated`.

## Autorización y sesión

- Una operación lee el recurso por Id: una escritura carga la entidad y una lectura, su modelo de lectura. Si no existe, devuelve 404. Después decide propietario o permiso, y si falla devuelve 403.
- Solo la lista personal filtra por propietario. Un permiso administrativo no amplía la lista personal.
- `User` recibe `Tasks.Create`. La propiedad habilita leer y modificar lo propio. `Admin` y `SuperAdmin` reciben permisos globales de Tasks. `SuperAdmin` recibe `Users.Manage`.
- Los permisos se consultan en cada request. La sesión lleva solo identidad (el id del usuario y el de la sesión), nunca roles ni permisos.
- `AccessPolicy` (`Features/Auth/Access`) es scoped: hay una por request. La primera comprobación de un usuario lee sus roles y permisos de Identity y los guarda en un `AccessGrant` dentro de esa instancia; las demás comprobaciones del mismo request lo reusan. El request siguiente arranca sin memo, entonces un cambio de rol o de permiso vale desde ahí. Si un request cambia roles y después vuelve a comprobar, ve el valor del principio del request; hoy ninguna slice lo hace.
- La sesión vive en el servidor (BFF): una fila de la tabla `Sessions` (`Auth/Domain/Session`) con usuario, creación, vencimiento absoluto y última actividad. El navegador solo guarda la cookie `__Host-session`: `HttpOnly`, `Secure`, `SameSite=Strict` y `Path=/`. Su valor es el ticket de la autenticación por cookie de ASP.NET Core, cifrado con Data Protection; adentro van los dos ids.
- Las claves de Data Protection cifran la cookie de sesión y el antiforgery. `BackendInfrastructure` fija el nombre de aplicación `TemplateApp` y, si existe `DataProtection:KeysDirectory`, las guarda en esa carpeta. La imagen Docker apunta esa carpeta a `/var/lib/templateapp/data-protection-keys` y Compose monta ahí el volumen `dataprotection`: recrear el contenedor de la API no cierra las sesiones. Varias instancias necesitan compartir esas claves; un volumen local no alcanza, y guardarlas en PostgreSQL queda como opción.
- La autenticación por cookie es el único esquema. En cada request, `OnValidatePrincipal` busca la fila: si no está, venció (vencimiento absoluto) o pasó el tiempo de inactividad, rechaza la sesión, borra la cookie y la request sigue como anónima (401 si la ruta pide sesión). La última actividad se escribe como mucho una vez por minuto por sesión.
- Abrir sesión (`OpenSession`, `RegisterUser`) crea la fila, emite la cookie y devuelve el usuario. Ninguna respuesta lleva un token. Cerrar sesión (`CloseSession`) borra la fila y la cookie. Revocar una sesión es borrar su fila: corta el acceso en la request siguiente. Las otras sesiones del mismo usuario siguen.
- "Quién soy" es `GET /api/v1/users/me`: con la cookie sola devuelve el usuario. Angular lo usa para restaurar la sesión al recargar.
- La cookie viaja sola, así que toda escritura de la API (`POST`, `PUT`, `PATCH`, `DELETE` bajo `/api`) valida antiforgery, también para anónimos. Sin token válido responde 400 con el código `Auth.AntiforgeryFailed`. Abrir o cerrar sesión cambia la identidad y emite un `XSRF-TOKEN` nuevo en la misma respuesta. DUNE0011 mantiene toda ruta de escritura bajo `/api` y `BannedSymbols.txt` prohíbe las formas de saltear el antiforgery.
- `SessionHttpPolicy` es el adapter HTTP de la sesión: configura la cookie, firma la entrada y la salida, emite `XSRF-TOKEN`, valida la sesión en cada request y exige antiforgery en las escrituras. `Sessions` no conoce HTTP: abre, valida y cierra filas.
- El limiter global (`Infrastructure/Http/ApiRateLimiting`) cuenta toda request de la API en un balde de ventana fija. El balde depende de quién llama y del tipo de request: el id de usuario de la sesión, separado en lecturas (`GET`, `HEAD`, `OPTIONS`) y escrituras; sin sesión, la IP del cliente. Auth suma su política `auth` por IP en login, registro y logout, contra el abuso de credenciales; la request necesita lugar en los dos baldes. Health queda exento. `UseRateLimiter()` va después de `UseAuthentication()`, porque la partición lee al usuario. El rechazo es 429 con `Retry-After` y `Http.TooManyRequests`. Los límites salen de las options `RateLimiting` y `AuthRateLimiting`. Los contadores viven en memoria de cada instancia: con varias instancias, el límite efectivo se multiplica. Detrás de un proxy, la API ve la IP del proxy hasta que se configuren los forwarded headers.
- Toda respuesta de la API lleva seis cabeceras de seguridad: `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cross-Origin-Resource-Policy: same-origin` y `Cache-Control: no-store`. Las escribe el middleware de la librería NetEscapades.AspNetCore.SecurityHeaders con la política de `Infrastructure/Http/ApiSecurityHeaders`. `UseApiSecurityHeaders()` va primero en `Program.cs` y escribe en `OnStarting`, justo antes de mandar la respuesta: así cubre también el 500 de `UseExceptionHandler` (que borra las cabeceras escritas antes), el 404 de `UseStatusCodePages`, el 429 del limiter y el 400 del antiforgery, y sus valores pisan el `SAMEORIGIN` y el `no-cache, no-store` que pone el antiforgery. Kestrel no manda `Server`. No hay HSTS: la app solo recibe HTTP (en Compose, nginx → gateway → API) y el navegador ignora HSTS por HTTP; va donde se termine TLS. La cookie interna del antiforgery sale `Secure` cuando la request llega por HTTPS (`SameAsRequest`). El documento de la SPA lo sirve nginx y no lleva estas cabeceras todavía.
- Un cliente sin cookies (una app móvil) entraría por otro esquema de autenticación (Bearer) sobre los mismos endpoints. No existe todavía. Ese esquema no necesitaría antiforgery, porque el navegador no manda su credencial solo.

## Gateway

El gateway (`gateway/`, proyecto `TemplateApp.Gateway`) es un proceso propio con YARP. Solo reparte requests: no referencia otro proyecto ni conoce features ([ADR-0005](adr/0005-gateway-y-procesos-propios.md)).

- **Rutas.** Las rutas y los destinos viven en `gateway/appsettings.json`, sección `ReverseProxy`. Hoy hay una ruta, `/api/{**rest}`, hacia el cluster `monolith`. Compose cambia la dirección del destino con `ReverseProxy__Clusters__monolith__Destinations__api__Address`. Una URL fuera de `/api/` no es de la API: el gateway responde 404.
- **Cadena en Compose.** El navegador → nginx (`web`) → gateway → API. El gateway publica `API_PORT`; la API no publica puertos. `check-structure.mjs` lo hace cumplir.
- **Health.** `/health/live` y `/health/ready` del gateway miran solo al gateway: el ready pide que YARP haya cargado al menos una ruta y que cada ruta llegue a un cluster con destinos (una ruta con un `ClusterId` que no existe da 503 en el ready). A la API la vigila el chequeo activo de YARP cada 5 s sobre `/api/v1/health/live`; con 2 fallas seguidas la marca caída y la ruta responde 503 en el acto (`HealthyAndUnknown`). Hasta esa marca, una request a una API caída espera. El chequeo usa el live y no el ready, porque con la base caída la API tiene que seguir respondiendo su 503 con el detalle de cada dependencia.
- **Errores.** Un error propio del gateway sale como ProblemDetails con `code`, igual que en la API: `UseStatusCodePages` le pone cuerpo a toda respuesta 400-599 vacía (404 fuera de `/api/`, 502 y 503 de YARP) y el `code` sale de la frase del status (`Http.ServiceUnavailable`, `Http.BadGateway`). Una respuesta de error reenviada desde la API ya trae cuerpo y pasa sin cambios.
- **Cabeceras.** El gateway escribe las mismas seis cabeceras de seguridad que la API, con la misma política, y Kestrel no manda `Server`. Así también las llevan sus propias respuestas (503, 404). En una respuesta reenviada, el valor del gateway pisa al de la API, que es el mismo.
- **Telemetría.** El gateway exporta como `templateapp-gateway`: un span SERVER y un log de request por request, y un span CLIENT por cada reenvío. La API recibe el `traceparent` y su span SERVER cuelga del CLIENT del gateway, en la misma traza. No escucha el `ActivitySource` de YARP: su span `proxy.forwarder` es SERVER y no tiene log de request, y `check-otel-export.mjs` lo rechaza.
- **Forwarded headers.** YARP reemplaza el `X-Forwarded-For` que manda nginx por la IP de nginx. La API no lee esos headers todavía: ve la IP del gateway.
- **Desarrollo local.** `ng serve` y `verify-template` siguen hablando directo con la API (`proxy.conf.json`). El gateway corre en Compose y en CI.

## Frontend

Angular consume `/api/v1` del mismo origen, mediante un proxy. No maneja tokens: la sesión es la cookie HttpOnly, que el navegador manda solo. FRONT015 y FRONT003 rechazan `localStorage` y `sessionStorage`; FRONT016 rechaza un header `Authorization`.

```text
frontend/src/app/
├── app.config.ts, app.routes.ts   raíz: compone las features
├── core/                          transversal: errores HTTP (ProblemDetails)
└── features/
    ├── auth/
    │   ├── index.ts               la puerta: lo único que ve el resto de la app
    │   ├── auth.routes.ts         rutas de la feature, sin guards
    │   ├── api/                   AuthApi y sus modelos: OpenSessionRequest, RegisterRequest…
    │   ├── session/               Session, guards e interceptor
    │   └── login/, register/      un componente por carpeta
    └── tasks/
        ├── index.ts, tasks.routes.ts
        ├── api/                   TasksApi y sus modelos: TaskResponse, UpdateTaskRequest…
        ├── tasks/                 Tasks: componente de ruta; orquesta la lista y llama a TasksApi
        ├── task-pager/            TaskPager: presentación; recibe la página con input() y la pide con output()
        ├── task-row/              TaskRow: presentación; una fila con su borrador y su edición en línea
        └── task-delete-dialog/    TaskDeleteDialog: presentación; el <dialog> de confirmar el borrado
```

- **Componente de ruta y de presentación.** `Tasks` es el destino de `/tasks`: guarda el estado de la lista, con la pila de cursores visitados, y es el único que llama a `TasksApi`. `TaskPager` no inyecta nada de la app: recibe `page`, `hasPrevious`, `hasNext` y `disabled`, y avisa `previous` y `next`. `TaskRow` es el `<li>` de cada tarea (`li[app-task-row]`): recibe `task`, `editing` y `pending`, guarda el borrador del título y avisa `completedChange`, `editStarted`, `renamed`, `editCancelled` y `removeRequested`. Un título inválido muestra su mensaje justo debajo del input, enlazado con `aria-describedby`, y no avisa nada. `Tasks` decide qué fila edita, llama a la API y muestra bajo la lista los errores de la API. `TaskDeleteDialog` recibe la tarea a borrar: con una tarea abre su `<dialog>`, centrado con `m-auto`, con `showModal()` desde un `afterRenderEffect`, y con `null` lo cierra. Cierra el diálogo antes de avisar `confirmed` o `cancelled`. El foco lo decide `Tasks`: vuelve al `Eliminar` de la fila al cancelar y va al encabezado "Mis tareas" después de borrar. Los datos bajan por `input()` y los hechos suben por `output()`. FRONT013 frena un componente que no es de ruta y que inyecta un servicio de la app, `Router` o `ActivatedRoute`.

- En la raíz de una feature solo viven `index.ts` y `<feature>.routes.ts`. El resto va en una carpeta con nombre de propósito: `session/`, `api/` o la carpeta del componente. FRONT011 lo hace cumplir.

- Cada feature tiene su cliente HTTP, sus modelos y sus rutas. Su `index.ts` es el equivalente de `Contracts/` en el backend.
- `Session` guarda un solo estado en un `signal` privado: `restoring` al arrancar, `open` con el usuario o `closed`. Expone `status`, `currentUser` e `isOpen` como `computed`, y las acciones `restore`, `register`, `open`, `close`, `end` y `replaceUser`. `AuthApi` hace el HTTP; `Session` decide el estado. `restore()` pide `XSRF-TOKEN` y después "quién soy" (`UsersApi.me()`); `open` y `register` toman el usuario de la respuesta. `provideAppInitializer()` llama a `restore()` sin bloquear el arranque, y los guards esperan a que `status` deje de ser `restoring`.
- `authInterceptor` no agrega nada a las requests. Ante un 401 de `/api/`, si la sesión estaba abierta, llama a `Session.end()`: cierra la sesión local y lleva a `/login?returnUrl=<página actual>`. Deja pasar sin mirar lo marcado con `SKIP_SESSION` (todo `AuthApi`): un 401 del login es "credenciales incorrectas", no una sesión vencida. El `authGuard` hace lo mismo con la página pedida. El login vuelve ahí si es una ruta interna.
- Varias pestañas comparten la cookie, y con ella la sesión: no hay nada que renovar ni coordinar. Abrir o cerrar la sesión en una pestaña se avisa a las demás por `BroadcastChannel('session')`: con `closed` las otras cierran la sesión local y van a `/login`; con `opened` la restauran, así muestran al usuario nuevo.
- La raíz de la app compone las rutas y decide qué grupo protege cada guard (`canActivateChild`). Las features no declaran guards: así `auth` y `users` no se importan entre sí al cargar la app.
- Las dependencias siguen la dirección del backend: Auth usa `UsersApi` y `UserResponse` desde `features/users`; el perfil usa `Session` desde `features/auth`. Los componentes se cargan lazy.

## Operación

- Compose levanta PostgreSQL 18 con volumen, un collector OpenTelemetry, Prometheus, la API (sin root) y el frontend.
- Serilog exporta logs por OTLP. OpenTelemetry exporta métricas y trazas a `/v1/metrics` y `/v1/traces`.
- Además de las métricas de ASP.NET Core, HttpClient, runtime e Identity, hay métricas de negocio: el Meter `TemplateApp.Tasks` (`templateapp.tasks.created` y el histograma `templateapp.tasks.completion.duration`) y el Meter `TemplateApp.Auth` (`templateapp.auth.login.attempts` por `outcome`). El smoke de Compose falla si no llegan al collector.
- Los logs de request llevan trace y span, sin credenciales. El job `compose` de CI lo comprueba sobre el export completo, después de apagar el stack: cada log de request apunta a un span que el collector recibió, cada span SERVER tiene su log de request y ninguna métrica llega vacía (`check-otel-export.mjs`).
- Readiness (`GET /api/v1/health/ready`) corre todos los health checks registrados con el framework de ASP.NET Core: `database` (un `SELECT 1` con `PlatformDbContext`) y `migrations` (ninguna migración pendiente en ningún contexto de feature ni en `PlatformDbContext`; el `AppDbContext` viejo no cuenta). Los registra `Infrastructure`, cada uno con un límite de 2 s que también acota la conexión de Npgsql del check. Esa conexión cambia la connection string, así que va en un pool de Npgsql aparte, de 2 conexiones como máximo: una ráfaga de sondas no le quita conexiones a la app. La feature `Health` no sabe qué dependencias hay. La slice `GetReadiness` no tiene clase de operación propia: la operación es `HealthCheckService` del framework y el endpoint traduce su `HealthReport` a HTTP (las piezas que el caso de uso no necesita no existen). Responde 503 si alguno sale `Unhealthy`, y el body nombra cada check con su estado (`{"status":"not-ready","timestamp":…,"checks":{"database":"unhealthy","migrations":"unhealthy"}}`), sin descripción ni excepción. El motivo de cada falla queda en el log, con su excepción. Liveness no corre checks y no depende de la base. El collector de OpenTelemetry no entra en readiness: si se cae, la API sigue atendiendo.
- Prometheus 3 (LTS) recibe las métricas del collector por OTLP (`otlp_http/prometheus`, al lado del export a archivo) y evalúa las alertas. No hay Alertmanager ni tableros: las alertas se ven en Prometheus. La configuración y las reglas viven en `deploy/prometheus/`; la traducción de nombres OTLP está fijada en `UnderscoreEscapingWithSuffixes`, porque las reglas dependen de `http_server_request_duration_seconds_*`.
- Dos SLOs sobre `http.server.request.duration`, sin health: disponibilidad (99,5 % sin 5xx) y latencia (99 % en 500 ms o menos), en 30 días. Reglas de grabación por ventana y dos alertas multiventana de burn rate (`ApiAvailabilityBudgetBurn`, `ApiLatencyBudgetBurn`), cada una con `page` y `ticket` y un runbook en `docs/operacion/runbooks/`. El documento es [operacion/slos.md](operacion/slos.md). `promtool` prueba las reglas con series sintéticas, `dune/check-runbooks.mjs` exige un runbook por alerta y `check-slo-alerts.mjs` dispara cada alerta en el job `compose`.

## Dune

Dune hace cumplir esta arquitectura con analyzers Roslyn, símbolos prohibidos y gates de Node. Vive en `dune/`. La raíz solo lo conecta: `Directory.Build.props` importa `dune/Dune.props`, y `TemplateApp.slnx` y CI llaman a `dune/`.

Cada regla tiene una fixture válida y una violación deliberada. Las pruebas observan diagnósticos o códigos de salida, no búsquedas de texto.

Para comparar con el framework Dune de Lauren, leé [research-dune.md](research-dune.md).
