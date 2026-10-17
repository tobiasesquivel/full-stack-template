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
        ├── Contracts/       lo que ven otras features y las responses HTTP compartidas
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
- **Infrastructure:** observabilidad, middlewares, arranque de la base y composición del host.
- **Shared:** tipos generales que usan varias features, como `ApiResults`.

No hay mediator ni bus. El paso 25 del plan elige uno.

## Estado actual frente al objetivo

El código todavía no tiene la forma de arriba. La Fase 0 del [plan](evolucion/plan.md) lo lleva hasta ahí.

| Diferencia | Paso que la resuelve |
|---|---|
| Un solo `AppDbContext` para todas las features: `Infrastructure` todavía nombra los `Domain` y `Contracts` de las features. Por eso `AppIdentityUser`, una entidad de Identity, sigue en `Auth/Contracts`: `AppDbContext` hereda de `IdentityDbContext<AppIdentityUser>` y `Domain` no admite ASP.NET | Paso 22 |

## Perfil y propiedad

Identity guarda el nombre de login, el email y las credenciales. `UserProfile` guarda `AuthId`, `DisplayName` y `Timezone`. `TaskItem.OwnerId` referencia `UserProfile.AuthId`. La cascada va de Identity al perfil y del perfil a las tareas. Las sesiones referencian Identity.

Entre features, una entidad guarda solo el Id de la otra: `TaskItem.OwnerId` es un `string`, sin navegación. Las dos FKs que cruzan features (`UserProfiles → AspNetUsers` y `TaskItems → UserProfiles`) se declaran en `AppDbContext.ConfigureCrossFeatureRelationships`, no en la `Persistence` de cada feature. La base mantiene la integridad y la cascada mientras sea compartida. El paso 22 decide si esas FKs se quitan o quedan solo en SQL.

Auth y Users se usan solo por contrato. Auth crea y lee el perfil con `Users.Contracts.IUserProfiles`. Users lee y borra la cuenta con `Auth.Contracts.IUserAccounts`. Ninguna de las dos conoce el interior de la otra.

El registro crea identidad, perfil, rol y sesión en una transacción explícita. Las restricciones únicas protegen las carreras. Un resultado de error no confirma la transacción. Las migraciones son forward: renombran y hacen backfill, sin reset de base ni `EnsureCreated`.

## Autorización y sesión

- Una operación carga el recurso por Id. Si no existe, devuelve 404. Después decide propietario o permiso, y si falla devuelve 403.
- Solo la lista personal filtra por propietario. Un permiso administrativo no amplía la lista personal.
- `User` recibe `Tasks.Create`. La propiedad habilita leer y modificar lo propio. `Admin` y `SuperAdmin` reciben permisos globales de Tasks. `SuperAdmin` recibe `Users.Manage`.
- Los permisos se consultan en cada request. El JWT lleva identidad y vigencia, nunca roles ni permisos.
- El refresh token es opaco. Se guarda como hash, con familia y vencimiento. La rotación es atómica. Reusar un token rotado revoca esa familia, no todas las sesiones del usuario.
- Register, login, refresh y logout validan antiforgery. El refresh vive en una cookie HttpOnly. Angular guarda el access token solo en memoria.
- `SessionHttpPolicy` es el adapter HTTP de la sesión: emite, lee, adjunta y borra cookies. `TokenService` no conoce HTTP; maneja el JWT y la rotación por familia.

## Frontend

Angular consume `/api/v1` del mismo origen, mediante un proxy. No guarda tokens en el storage del navegador.

```text
frontend/src/app/
├── app.config.ts, app.routes.ts   raíz: compone las features
├── core/                          transversal: errores HTTP (ProblemDetails)
└── features/
    ├── auth/
    │   ├── index.ts               la puerta: lo único que ve el resto de la app
    │   ├── auth.routes.ts         rutas de la feature, sin guards
    │   ├── api/                   AuthApi y sus modelos: AuthResponse, OpenSessionRequest…
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

- **Componente de ruta y de presentación.** `Tasks` es el destino de `/tasks`: guarda el estado de la lista y es el único que llama a `TasksApi`. `TaskPager` no inyecta nada de la app: recibe `page`, `totalPages`, `totalCount` y `disabled`, y avisa `pageChange`. `TaskRow` es el `<li>` de cada tarea (`li[app-task-row]`): recibe `task`, `editing` y `pending`, guarda el borrador del título y avisa `completedChange`, `editStarted`, `renamed`, `renameRejected`, `editCancelled` y `removeRequested`. `Tasks` decide qué fila edita, llama a la API y muestra el error bajo la lista. `TaskDeleteDialog` recibe la tarea a borrar: con una tarea abre su `<dialog>` con `showModal()` desde un `afterRenderEffect`, y con `null` lo cierra. Cierra el diálogo antes de avisar `confirmed` o `cancelled`. El foco lo decide `Tasks`: vuelve al `Eliminar` de la fila al cancelar y va al encabezado "Mis tareas" después de borrar. Los datos bajan por `input()` y los hechos suben por `output()`. FRONT013 frena un componente que no es de ruta y que inyecta un servicio de la app, `Router` o `ActivatedRoute`.

- En la raíz de una feature solo viven `index.ts` y `<feature>.routes.ts`. El resto va en una carpeta con nombre de propósito: `session/`, `api/` o la carpeta del componente. FRONT011 lo hace cumplir.

- Cada feature tiene su cliente HTTP, sus modelos y sus rutas. Su `index.ts` es el equivalente de `Contracts/` en el backend.
- `Session` guarda un solo estado en un `signal` privado: `restoring` al arrancar, `open` con el usuario o `closed`. Expone `status`, `currentUser` e `isOpen` como `computed`, y las acciones `restore`, `register`, `open`, `refresh`, `close`, `end` y `replaceUser`. `AuthApi` hace el HTTP; `Session` decide el estado. `provideAppInitializer()` llama a `restore()` sin bloquear el arranque, y los guards esperan a que `status` deje de ser `restoring`.
- `authInterceptor` agrega el token a `/api/` y deja pasar sin token lo marcado con `SKIP_SESSION` (todo `AuthApi`). Un 401 dispara una sola renovación compartida (`Session.refresh()`) y un reintento. Solo una renovación fallida termina la sesión: `Session.end()` lleva a `/login?returnUrl=<página actual>`, y el `authGuard` hace lo mismo con la página pedida. El login vuelve ahí si es una ruta interna.
- Varias pestañas: el backend revoca la familia si recibe un refresh token ya rotado (`RefreshSessionTests`). Por eso `Session.refresh()` toma un Web Lock: la segunda pestaña renueva después, con la cookie nueva que dejó la primera. Abrir o cerrar la sesión en una pestaña se avisa a las demás por `BroadcastChannel`; ninguna pestaña comparte el access token, cada una lo pide con su renovación.
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
