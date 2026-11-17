# Arquitectura de TemplateApp

La decisión vigente es [ADR-0004](adr/0004-vertical-slices-deep-modules.md): vertical slices dentro de features que funcionan como módulos profundos. Las reglas para escribir código están en [conventions.md](conventions.md).

## Forma del backend

El backend es un solo proyecto ASP.NET Core 10, con namespace raíz `TemplateApp`.

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
        ├── Persistence/     mappings de EF
        └── CreateTask/
            ├── CreateTaskRequest.cs
            ├── CreateTaskRequestValidator.cs
            ├── CreateTask.cs            operación: clase con sus dependencias
            ├── CreateTaskEndpoint.cs    adapter HTTP: Map y handler
            └── CreateTaskTests.cs
```

- **Feature:** un módulo profundo. Su interfaz pública es `Contracts/` más sus rutas HTTP.
- **Slice:** un caso de uso, en su carpeta. La operación es una clase propia, con sus dependencias. El endpoint solo adapta HTTP y llama a la operación.
- **Domain:** sin EF, ASP.NET ni infraestructura. Concentra las reglas que comparten las slices.
- **Infrastructure:** observabilidad, middlewares, arranque de la base, el grupo versionado de la API, el filtro de idempotencia de los `POST` (`Http/IdempotencyFilter`, con su tabla `IdempotencyKeys`), la auditoría de cambios (`Persistence/AuditInterceptor`, con su tabla `AuditEntries`), el limiter global de requests (`Http/ApiRateLimiting`), las cabeceras de seguridad de toda respuesta (`Http/ApiSecurityHeaders`) y composición del host.
- **Shared:** tipos generales que usan varias features, como `ApiResults`.

No hay mediator ni bus. El paso 25 del plan elige uno.

## Estado actual frente al objetivo

El código todavía no tiene la forma de arriba. La Fase 0 del [plan](evolucion/plan.md) lo lleva hasta ahí.

| Diferencia | Paso que la resuelve |
|---|---|
| Un solo `AppDbContext` para todas las features: `Infrastructure` todavía nombra los `Domain` y `Contracts` de las features. Por eso `AppIdentityUser`, una entidad de Identity, sigue en `Auth/Contracts`: `AppDbContext` hereda de `IdentityDbContext<AppIdentityUser>` y `Domain` no admite ASP.NET | Paso 22 |

## Perfil y propiedad

Identity guarda el nombre de login, el email y las credenciales. `UserProfile` guarda `AuthId`, `DisplayName` y `Timezone`. `TaskItem.OwnerId` referencia `UserProfile.AuthId`. La cascada va de Identity al perfil y del perfil a las tareas y a las claves de idempotencia. Las sesiones referencian Identity.

Entre features, una entidad guarda solo el Id de la otra: `TaskItem.OwnerId` es un `string`, sin navegación. Las FKs que cruzan features (`UserProfiles → AspNetUsers`, `TaskItems → UserProfiles` e `IdempotencyKeys → UserProfiles`) se declaran en `AppDbContext.ConfigureCrossFeatureRelationships`, no en la `Persistence` de cada feature. La base mantiene la integridad y la cascada mientras sea compartida. El paso 22 decide si esas FKs se quitan o quedan solo en SQL.

Auth y Users se usan solo por contrato. Auth crea y lee el perfil con `Users.Contracts.IUsersModule`. Users y Tasks leen la cuenta, la borran y deciden permisos con `Auth.Contracts.IAuthModule`. Ninguna de las dos conoce el interior de la otra.

## Auditoría de cambios

`AuditInterceptor` es un `SaveChangesInterceptor` de EF Core registrado en el `AppDbContext`. Antes de cada `SaveChanges`, agrega una fila en `AuditEntries` por cada entidad `IAuditable` (`Shared/Domain`) agregada, modificada o borrada. Hoy son `TaskItem` y `UserProfile`. La fila viaja en el mismo `SaveChanges`, entonces queda en la misma transacción que el cambio: si el cambio hace rollback (un 409 de versión, un 400 dentro del filtro de idempotencia), la fila tampoco queda.

Cada fila dice quién, qué y cuándo, sin datos personales:

- **Quién:** `ActorKind` (`user`, `anonymous` en el registro, `system` fuera de un pedido HTTP) y `ActorId`, el id opaco de Identity leído del claim `NameIdentifier` con `IHttpContextAccessor`, como hace el filtro de idempotencia. Infrastructure no nombra a Auth.
- **Qué:** `EntityType`, `EntityId`, `Operation` (`Added`, `Modified`, `Deleted`) y `ChangedProperties`, solo los nombres de las propiedades modificadas. Nunca valores: el título de una tarea es texto libre y el perfil guarda nombre visible y zona horaria.
- **Cuándo:** `OccurredAt` del `TimeProvider` y el `TraceId` de la actividad del pedido, que une la fila con el log y la traza.

Lo que no pasa por el ChangeTracker no deja fila: `ExecuteUpdate`, `ExecuteDelete`, SQL crudo y las cascadas que ejecuta la base. DUNE0017 prohíbe las dos primeras sobre una entidad `IAuditable`. Al borrar la cuenta, queda la fila `Deleted` del perfil (EF lo tiene cargado), pero las tareas las borra la base en cascada, sin fila propia. `AuditEntries` no tiene FK a la cuenta: la historia sobrevive al borrado, con el id opaco. La tabla todavía no es append-only ni se limpia (pendientes en `progreso.md`).

El registro crea identidad, perfil, rol y sesión en una transacción explícita. Las restricciones únicas protegen las carreras. Un resultado de error no confirma la transacción. Las migraciones son forward: renombran y hacen backfill, sin reset de base ni `EnsureCreated`.

## Autorización y sesión

- Una operación carga el recurso por Id. Si no existe, devuelve 404. Después decide propietario o permiso, y si falla devuelve 403.
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
- Toda respuesta de la API lleva seis cabeceras de seguridad: `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cross-Origin-Resource-Policy: same-origin` y `Cache-Control: no-store`. Las escribe el middleware de la librería NetEscapades.AspNetCore.SecurityHeaders con la política de `Infrastructure/Http/ApiSecurityHeaders`. `UseApiSecurityHeaders()` va primero en `Program.cs` y escribe en `OnStarting`, justo antes de mandar la respuesta: así cubre también el 500 de `UseExceptionHandler` (que borra las cabeceras escritas antes), el 404 de `UseStatusCodePages`, el 429 del limiter y el 400 del antiforgery, y sus valores pisan el `SAMEORIGIN` y el `no-cache, no-store` que pone el antiforgery. Kestrel no manda `Server`. No hay HSTS: la app solo recibe HTTP (detrás de nginx en Compose) y el navegador ignora HSTS por HTTP; va donde se termine TLS. La cookie interna del antiforgery sale `Secure` cuando la request llega por HTTPS (`SameAsRequest`). El documento de la SPA lo sirve nginx y no lleva estas cabeceras todavía.
- Un cliente sin cookies (una app móvil) entraría por otro esquema de autenticación (Bearer) sobre los mismos endpoints. No existe todavía. Ese esquema no necesitaría antiforgery, porque el navegador no manda su credencial solo.

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

- Compose levanta PostgreSQL 18 con volumen, un collector OpenTelemetry, la API (sin root) y el frontend.
- Serilog exporta logs por OTLP. OpenTelemetry exporta métricas y trazas a `/v1/metrics` y `/v1/traces`.
- Los logs de request llevan trace y span, sin credenciales.
- Readiness comprueba PostgreSQL y las migraciones. Liveness no depende de la base.

## Dune

Dune hace cumplir esta arquitectura con analyzers Roslyn, símbolos prohibidos y gates de Node. Vive en `dune/`. La raíz solo lo conecta: `Directory.Build.props` importa `dune/Dune.props`, y `TemplateApp.slnx` y CI llaman a `dune/`.

Cada regla tiene una fixture válida y una violación deliberada. Las pruebas observan diagnósticos o códigos de salida, no búsquedas de texto.

Para comparar con el framework Dune de Lauren, leé [research-dune.md](research-dune.md).
