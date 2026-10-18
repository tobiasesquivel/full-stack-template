# Informe: Vertical Slice Architecture + módulos profundos — dónde vive lo compartido

**Alcance**: solo investigación. Leí el código del repo y fuentes web primarias. No modifiqué código, no compilé, no corrí gates. Este archivo es el único cambio. Al final está la lista de lo verificado y lo no verificado.

**Distinción de fuentes**: **[PRIMARIA]** = el dueño de la idea o del código (Bogard, Ousterhout, Microsoft, Grzybek, el repo de eShop). **[OPINIÓN]** = practicante que interpreta a otros. **[INFERENCE]** = razonamiento mío sobre lo leído.

---

## Conclusión

1. **La regla de ubicación es una sola: si el código nombra una feature, vive en esa feature.** Lo que no nombra ninguna feature se divide en dos lugares: `Infrastructure` (plumbing del host) y `Shared` (kernel mínimo de tipos que las features comparten). Ninguna fuente primaria pide meter todo dentro de `Features/`. Bogard mismo deja `Infrastructure/`, `Data/` y `Models/` fuera de sus slices ([ContosoUniversity](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages)). [INFERENCE]
2. **Bogard no da una regla de extracción.** Dice "minimizar el acoplamiento entre slices" y "refactorizar hacia los patrones que emergen de los code smells" ([VSA](https://www.jimmybogard.com/vertical-slice-architecture/)). La "regla de tres" y la carpeta `Shared` por feature son de Milan Jovanović, no de Bogard. [OPINIÓN]
3. **Hoy `Shared/Authorization` e `Infrastructure` contienen conocimiento de Auth, Tasks y Users.** Ejemplos: el hook de sesión en `BackendInfrastructure.cs`, los permisos de Tasks y Users en un solo `AppPermissions`, y los contadores de negocio en `TemplateMetrics`. Eso es *information leakage* en términos de Ousterhout: el mismo conocimiento vive en dos módulos.
4. **Propuesta**: Auth es dueña de identidad, JWT, sesión, roles, permisos y seeder. Expone un contrato profundo (`IAccessPolicy`) en `Auth/Contracts`. Cada feature declara sus permisos y sus métricas. `Shared` queda con `IOwnedResource` y `ApiResults`. Cada feature se registra sola (`AddTasksFeature`).
5. **Clase por slice no es classitis por sí misma.** Lo es si los slices se vuelven interfaces que alguien tiene que aprender, o si parten un caso de uso en pasos. Ousterhout acepta código especializado arriba ("push specialization upwards") si no se filtra hacia abajo ([extracto cap. 6.6](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf)). La profundidad se mide en el borde de la feature, no en cada slice.
6. **El riesgo real ya existe en el repo**: la regla del título está copiada en `CreateTask.cs:17-24` y `UpdateTask.cs:17-24`, y el número 200 aparece también en `TaskItemConfiguration.cs:20`. Con una clase por slice ese tipo de copia crece. La solución es bajar la regla al dominio ("pull complexity downwards"), no crear un `Common`.
7. **Hallazgo lateral**: `ITemplateMetrics` está registrado (`ObservabilityExtensions.cs:20`) pero ninguna feature lo llama. Los cuatro contadores de negocio no se emiten nunca.

---

## 1. ¿Dónde vive el código compartido en VSA + deep modules?

### 1.1 Qué dicen las fuentes

| Fuente | Tipo | Qué dice o qué hace | Implicancia |
|---|---|---|---|
| Bogard, *Vertical Slice Architecture* ([blog](https://www.jimmybogard.com/vertical-slice-architecture/)) | PRIMARIA | *"Minimize coupling between slices, and maximize coupling in a slice."* *"we don't need any kind of 'shared' layer abstractions like repositories, services, controllers."* *"we keep our cross-slice logic sharing to a minimum."* *"simply refactor to the patterns that emerges from code smells we see in the business logic."* Requisito: *"it does assume that your team understands code smells and refactoring."* | No hay carpeta prescrita para lo compartido. Lo compartido se gana por refactor, no por diseño previo. |
| Bogard, repo `ContosoUniversityDotNetCore-Pages` ([GitHub](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages)) | PRIMARIA (código) | Slices en `Pages/<Feature>/<UseCase>.cshtml.cs`. Fuera de los slices: `Data/SchoolContext.cs` (un solo contexto), `Models/` (entidades compartidas), `Infrastructure/` (filtros de transacción y validación, model binders, tag helpers). Registro central en `Program.cs` con escaneo de ensamblado. | Bogard mismo tiene `Infrastructure` y un modelo compartido fuera de las features. |
| El mismo repo: duplicación | PRIMARIA (código) | `Students/Create.cshtml.cs` y `Students/Edit.cshtml.cs` repiten `RuleFor(m => m.LastName).NotNull().Length(1, 50)`. En cambio `Instructors/CreateEdit.cshtml.cs` une crear y editar en **un solo slice**. | Bogard tolera validadores duplicados. Cuando dos slices son casi iguales, los une. No los extrae a un helper. |
| Bogard, *Domain-Driven Refactoring* ([intro](https://jimmybogard.com/domain-driven-refactoring-intro/), [pushing down](https://www.jimmybogard.com/domain-driven-refactoring-defactoring-and-pushing-behavior-down/)) | PRIMARIA | *"start with the simplest solution that can possibly work, and once it works, examine the code for code smells."* El objetivo es *"taking behavior at the edges of our application"* y llevarlo al dominio. | Lo que se repite entre slices baja a la entidad, no sube a un servicio compartido. |
| Bogard, charla *VSA: Effective Guardrails for AI Development* ([Sessionize](https://sessionize.com/s/jimmy-bogard/vertical-slice-architecture-effective-guardrails-f/185766)) | PRIMARIA (resumen de la charla) | Propone tests de arquitectura, convenciones guardadas en el repo y un "reference slice" para copiar. | Coincide con Dune: las reglas de límite se hacen ejecutables. |
| Ousterhout, *A Philosophy of Software Design*, 2ª ed., cap. 6 ([extracto oficial](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf)) | PRIMARIA | *"specialized code should be cleanly separated from general-purpose code. This can be done by pushing the specialized code either up or down in the software stack."* (6.6) *"general-purpose APIs result in more information hiding"* (intro cap. 6). | Lo compartido debe ser *general-purpose*: no puede saber de Tasks ni de Users. Si sabe, es código especial y pertenece a la feature. |
| Microsoft, *Develop ASP.NET Core MVC apps* ([Learn](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/develop-asp-net-core-mvc-apps)) | PRIMARIA | Sección "Feature organization": organizar *"by feature instead of by file type"*. Para APIs, *"each folder can contain all of the API Endpoints and their associated DTOs."* Sección "Cross-cutting concerns": autenticación, validación, caché y errores se factorizan con filtros del pipeline. | Lo transversal va en el pipeline del host, fuera de las features. |
| `dotnet/eShop` ([GitHub](https://github.com/dotnet/eShop)) | PRIMARIA (código de referencia de Microsoft) | `src/eShop.ServiceDefaults` contiene la validación JWT ([`AuthenticationExtensions.cs`](https://github.com/dotnet/eShop/blob/main/src/eShop.ServiceDefaults/AuthenticationExtensions.cs)), `ClaimsPrincipalExtensions` y OpenTelemetry. La emisión de tokens vive en `Identity.API`. Cada servicio tiene su `DbContext` y su registro ([`Ordering.API/Extensions/Extensions.cs`](https://github.com/dotnet/eShop/blob/main/src/Ordering.API/Extensions/Extensions.cs)). `src/Shared` tiene solo dos archivos: `ActivityExtensions.cs` y `MigrateDbContextExtensions.cs`. | Separación limpia: *validar* un token es plumbing compartido; *emitir* y *revocar* es de la capacidad de identidad. `Shared` chico y genérico. |
| Kamil Grzybek, `modular-monolith-with-ddd` ([GitHub](https://github.com/kgrzybek/modular-monolith-with-ddd)) | PRIMARIA (código de referencia, no Microsoft) | *"Each Module has its own data in a separate schema - shared data is not allowed"*. *"Modules can only have a dependency on the integration events assembly of other Module"*. *"Each Module has its own Composition Root"*. `src/BuildingBlocks` contiene Outbox, Inbox, EventBus y despacho de eventos de dominio. Permisos por módulo en archivos separados ([`MeetingsPermissions.cs`](https://github.com/kgrzybek/modular-monolith-with-ddd/blob/master/src/API/CompanyName.MyMeetings.API/Modules/Meetings/MeetingsPermissions.cs), `PaymentsPermissions.cs`). El módulo `UserAccess` es dueño de la consulta de permisos. | `BuildingBlocks` aparece cuando hay outbox y bus. Permisos: uno por módulo; consulta: en el módulo de acceso. |
| Milan Jovanović, *VSA: Where Does the Shared Logic Live?* ([blog](https://milanjovanovic.tech/blog/vertical-slice-architecture-where-does-the-shared-logic-live)) | OPINIÓN | *"A `Common` project inevitably becomes a junk drawer"*. Regla de tres: *"Don't abstract until you hit three"*. Infraestructura se comparte por defecto. Lo compartido entre slices de una sola feature va en `Features/<X>/Shared`. Reglas de negocio a entidades y value objects. | Útil como heurística. No es de Bogard. |

### 1.2 Qué nombre usar: `Shared`, `Infrastructure`, `BuildingBlocks`, `Common`, `Kernel`

- **`Common`**: descartado. Ninguna fuente primaria lo usa. Jovanović lo llama "junk drawer". [OPINIÓN]
- **`Infrastructure`**: Bogard ([repo](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages)) y Microsoft ([Learn](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/develop-asp-net-core-mvc-apps)) lo usan para el plumbing del host. **Mantener.**
- **`Shared`**: hoy existe y Dune lo conoce (`DomainDependencyAnalyzer.cs:345` permite `TemplateApp.Shared.Domain` dentro de `Domain`). Funciona como *shared kernel* (término de DDD: el subconjunto chico que varios contextos aceptan compartir). **Mantener, pero vaciar todo lo que nombre una feature.**
- **`BuildingBlocks`**: es el nombre de Grzybek para mecanismos genéricos de mensajería ([árbol](https://github.com/kgrzybek/modular-monolith-with-ddd/tree/master/src/BuildingBlocks)). [INFERENCE] Hoy no aporta nada: renombrar costaría cambios en Dune (`DunePaths.cs:26-27`, `check-structure.mjs:54-55`) sin enseñar un concepto nuevo. Reconsiderarlo en los pasos 24-25 (outbox, bus), si el mecanismo genérico crece.
- **Todo dentro de `Features/`**: descartado para el plumbing. Un `GlobalExceptionHandler` no es una capacidad del negocio. La decisión de Tobi ("todas las capacidades bajo `Features/`") se cumple: Auth es una capacidad y va entera a `Features/Auth`. El plumbing no es una capacidad. [INFERENCE]

### 1.3 Cuándo extraer código compartido

- **Bogard**: cuando aparece un code smell y un refactor lo resuelve. No da un número. ([VSA](https://www.jimmybogard.com/vertical-slice-architecture/), [DDR intro](https://jimmybogard.com/domain-driven-refactoring-intro/))
- **Hacia dónde**: Bogard empuja el comportamiento *hacia el dominio* ([pushing down](https://www.jimmybogard.com/domain-driven-refactoring-defactoring-and-pushing-behavior-down/)). Ousterhout coincide: lo especializado se separa de lo general, empujándolo arriba o abajo ([cap. 6.6](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf)).
- **Criterio de Ousterhout para unir o separar**: *"The decision to split or join modules should be based on complexity. Pick the structure that results in the best information hiding, the fewest dependencies, and the deepest interfaces."* ([cap. 9.9](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf))
- **Regla operativa para este repo** [INFERENCE]: extraé cuando dos slices **deben** cambiar juntos (es la misma regla de negocio), no cuando se parecen. Destino según quién la usa:
  - una feature → dominio de esa feature (`TaskItem`, value object);
  - varias features y es contrato → `Contracts` de la feature dueña;
  - varias features y no nombra ninguna → `Shared`;
  - el host → `Infrastructure`.

---

## 2. Dónde debe vivir cada pieza de este repo

### 2.1 Tabla de ubicación

| Pieza (hoy) | Propuesta | Por qué |
|---|---|---|
| **JWT bearer** + hook de sesión (`Infrastructure/DependencyInjection/BackendInfrastructure.cs:80-156`) | `Features/Auth/Jwt/` — `AddAuthFeature()` registra `AddAuthentication().AddJwtBearer(...)`. El cuerpo de `OnTokenValidated` pasa a una clase `SessionValidator` dentro de Auth. | El hook consulta `AppIdentityUser` y `TokenService.IsFamilyActiveAsync`: es la semántica de revocación de Auth. Hoy Infrastructure la conoce: information leakage. eShop separa *validar* (shared) de *emitir* (Identity.API) porque son procesos distintos ([eShop](https://github.com/dotnet/eShop/blob/main/src/eShop.ServiceDefaults/AuthenticationExtensions.cs)). Acá Auth emite, valida y revoca en el mismo proceso, así que todo es de Auth. [INFERENCE] En el paso 23 (gateway), la parte "validar firma, issuer y audience" puede volver a un `ServiceDefaults` compartido. El hook de sesión no vuelve: sigue en Auth. |
| `Infrastructure/Identity/AppIdentityUser.cs` | `Features/Auth/Domain/AppIdentityUser.cs` | Es la cuenta con credenciales: dominio de Auth. **Consecuencia**: `UsersModule` usa `UserManager<AppIdentityUser>` y `UserProfileConfiguration.cs:27` define una FK a `AppIdentityUser`. Con la regla "solo Contracts", Users tiene que pedir esos datos a un contrato de Auth (ver 2.3). |
| `Infrastructure/Options/JwtOptions.cs` | `Features/Auth/Jwt/JwtOptions.cs` | Solo lo usan el setup JWT y `TokenService`, ambos de Auth. La validación manual (`BackendInfrastructure.cs:88-99`) se puede reemplazar por `ValidateOnStart` ([Options pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options)), pero el plan ya lista "validación de configuración por excepciones manuales" como hueco propio. No adelantar. |
| `Shared/Authorization/AppPermissions.cs` (Tasks y Users juntos + matriz rol→permiso) | Constantes: `Features/Tasks/Contracts/TaskPermissions.cs` y `Features/Users/Contracts/UserPermissions.cs`. `ClaimType` y la matriz: Auth. | Grzybek usa un archivo de permisos por módulo ([MeetingsPermissions.cs](https://github.com/kgrzybek/modular-monolith-with-ddd/blob/master/src/API/CompanyName.MyMeetings.API/Modules/Meetings/MeetingsPermissions.cs)). Van en `Contracts` porque las cadenas viajan al cliente en `UserResponse.permissions`: ya son contrato HTTP. Hoy, agregar un permiso de Payments obliga a editar un archivo que conoce Tasks y Users: change amplification. |
| `Shared/Authorization/AppRoles.cs` | `Features/Auth/Access/AppRoles.cs` (o `Auth/Contracts` si otra feature necesita nombres de rol) | Hoy lo usan `AuthModule.cs:57`, `RoleSeeder` y la matriz de permisos: todo Auth. |
| `Shared/Authorization/PermissionService.cs` (+ `IPermissionService`) | Implementación en `Features/Auth/Access/`. Interface fusionada en un contrato `Features/Auth/Contracts/IAccessPolicy.cs`. | Lee roles y claims de Identity: tablas de Auth. Grzybek pone la consulta de permisos en el módulo `UserAccess` ([árbol](https://github.com/kgrzybek/modular-monolith-with-ddd/tree/master/src/Modules/UserAccess)). La interface deja de ser un "seam hipotético" (un solo adapter): pasa a ser *el* contrato entre features, que es lo que exige "solo Contracts". |
| `Shared/Authorization/ResourceAuthorizer.cs` | Fusionar en `IAccessPolicy` (implementado por Auth). | Hoy son dos tipos que Tasks y Users inyectan juntos (`TasksModule.cs:11-12`, `UsersModule.cs:16-17`). Un contrato con `HasPermissionAsync` y `CanAccessAsync` es **más profundo**: una sola cosa que aprender y la misma potencia. Es la regla de Ousterhout de juntar cuando eso da *"the deepest interfaces"* ([cap. 9.9](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf)). |
| `Shared/Domain/IOwnedResource.cs` | **Queda** en `Shared/Domain`. | Es kernel genuino: no nombra features, lo implementan `TaskItem` y `UserProfile`, y Dune lo permite explícitamente en `Domain` (`DomainDependencyAnalyzer.cs:345`). [INFERENCE] Alternativa más simple: `CanAccessAsync(callerId, ownerId, permission)` con un `string` y sin interface. Elimina un tipo compartido. Decisión menor; anotarla para el paso 21. |
| `Shared/Http/ApiResults.cs` | **Queda.** | General-purpose, 14 call sites, no nombra features (ver [informe-deep-mediators.md](informe-deep-mediators.md) §1.3). |
| `Shared/Http/ClaimsPrincipalExtensions.cs` | `Features/Auth/Contracts/` (o junto al setup JWT). | `GetCallerId` depende de que el setup JWT ponga `DefaultMapInboundClaims = false` (`BackendInfrastructure.cs:101`). Lo dice su propio mensaje de error. Son dos archivos que comparten un invariante: information leakage. eShop los pone **juntos** en `ServiceDefaults` ([árbol](https://github.com/dotnet/eShop/tree/main/src/eShop.ServiceDefaults)). Si el JWT va a Auth, esto va con él. |
| `Infrastructure/Observability/TemplateMetrics.cs` + `ITemplateMetrics` (contadores de Auth y Tasks) | `Features/Auth/AuthMetrics.cs` (Meter `TemplateApp.Auth`), `Features/Tasks/TaskMetrics.cs` (Meter `TemplateApp.Tasks`). El setup de OpenTelemetry queda en Infrastructure con `.AddMeter("TemplateApp.*")`. | Microsoft: *"Each library or library subcomponent can (and often should) create its own Meter."* ([Creating Metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)). OpenTelemetry .NET acepta comodines en `AddMeter` ([docs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md)). Hoy `ITemplateMetrics` no tiene ningún llamador: según el deletion test, se puede borrar sin que reaparezca complejidad. Mover sin cablear no tiene sentido. Cablear las llamadas es un cambio observable aparte (pertenece a la Fase 5). |
| `Infrastructure/Persistence/AppDbContext.cs` + `Migrations/` — **hoy** | **Queda** hasta el paso 22. | Regla 8: no adelantar pasos. |
| Lo mismo — **después del paso 22** | `Features/<X>/Persistence/<X>DbContext.cs` + `Features/<X>/Persistence/Migrations/`. Un schema por feature (`HasDefaultSchema`) y una tabla de historial por schema (`MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")`). `AuthDbContext : IdentityDbContext<AppIdentityUser>` incluye `RefreshTokens`. En Infrastructure queda solo un helper genérico tipo `MigrateDbContext<T>` (como `src/Shared/MigrateDbContextExtensions.cs` de eShop). | Grzybek: *"Each Module has its own data in a separate schema"* ([README](https://github.com/kgrzybek/modular-monolith-with-ddd)). EF: con varios contextos, `--context` *"is required"* ([CLI](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)). Usar `DbContextOptions<TContext>` asegura que cada contexto reciba sus opciones *"even when multiple DbContext subtypes are registered"* ([DbContext config](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)). Historial por schema: [history table](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/history-table). Ver los riesgos en 2.3. |
| `Infrastructure/Seeders/RoleSeeder.cs` | `Features/Auth/Access/RoleSeeder.cs` | Siembra roles y claims de permiso de Identity: tablas de Auth. `DatabaseInitializer` pasa a recorrer los inicializadores que registra cada feature. |
| Registro DI central (`BackendInfrastructure.cs:31-38` registra `AuthModule`, `UsersModule`, `TasksModule`, `TokenService`…) | **Auto-registro por feature**: `Features/<X>/<X>Feature.cs` con `Add<X>Feature(services)` (desde el paso 5.3 la configuración llega por options, sin `IConfiguration`; DUNE0013) y `Map<X>Endpoints(app)`. `Program.cs` lista las features de forma explícita, sin escaneo de ensamblado. | Ya existe el precedente: `HealthFeatureExtensions.AddHealthFeature`. Grzybek: *"Each Module has its own Composition Root"* ([README](https://github.com/kgrzybek/modular-monolith-with-ddd)). eShop: cada servicio tiene su `AddApplicationServices` ([Extensions.cs](https://github.com/dotnet/eShop/blob/main/src/Ordering.API/Extensions/Extensions.cs)). La lista explícita mantiene la composición visible para el compilador y para Dune. **Costo**: el analyzer de límites de Infrastructure solo permite referencias a módulos dentro de `AddBackendInfrastructure` (`InfrastructureBoundaryAnalyzer.cs:61`). Hay que actualizar regla, spec y fixtures en el mismo paso. |
| Rate limiter `"auth"` y antiforgery (`BackendInfrastructure.cs:158-176`) | `AddAuthFeature()` | Según `docs/conventions.md`, antiforgery protege register, login, refresh y logout: solo endpoints de Auth. La política `"auth"` lleva el nombre de la feature. [INFERENCE] Si otra feature necesita antiforgery, la configuración vuelve al host. |
| `Infrastructure/Middlewares/*`, `ActivityTraceEnricher`, setup OTel | **Quedan** en Infrastructure. | Plumbing general-purpose. No nombran features. |

### 2.2 Árbol propuesto (estado después de los pasos 21-22)

```text
backend/
├── Program.cs                         # host: AddPlatform + lista explícita de features
├── Infrastructure/                    # plumbing; no nombra ninguna feature
│   ├── DependencyInjection/           # AddPlatform: ProblemDetails, TimeProvider, handlers
│   ├── Middlewares/                   # GlobalExceptionHandler, ValidationExceptionHandler
│   ├── Observability/                 # OTel, ActivityTraceEnricher, AddMeter("TemplateApp.*")
│   └── Persistence/                   # helper genérico MigrateDbContext<T> (después del paso 22)
│                                      # pasos 24-25: Outbox/ y Messaging/ genéricos aquí (o BuildingBlocks/)
├── Shared/                            # kernel mínimo, general-purpose
│   ├── Domain/IOwnedResource.cs
│   └── Http/ApiResults.cs
└── Features/
    ├── Auth/
    │   ├── AuthFeature.cs             # AddAuthFeature / MapAuthEndpoints
    │   ├── Contracts/                 # AuthRequests, AuthResponses, IAccessPolicy, CallerIdentity (GetCallerId)
    │   ├── Domain/                    # AppIdentityUser, RefreshToken
    │   ├── Access/                    # AppRoles, matriz rol→permiso, AccessPolicy (ex PermissionService+ResourceAuthorizer), RoleSeeder
    │   ├── Jwt/                       # JwtOptions, configuración JwtBearer, SessionValidator
    │   ├── Persistence/               # AuthDbContext (IdentityDbContext), RefreshTokenConfiguration, Migrations/
    │   ├── AuthMetrics.cs
    │   ├── AuthModule.cs, TokenService.cs, SessionHttpPolicy.cs, AuthEndpoints.cs, AuthErrors.cs
    │   └── Tests/
    ├── Users/
    │   ├── UsersFeature.cs
    │   ├── Contracts/                 # UserResponse, UserPermissions
    │   ├── Domain/UserProfile.cs
    │   ├── Persistence/               # UsersDbContext, Migrations/
    │   └── …
    ├── Tasks/
    │   ├── TasksFeature.cs
    │   ├── Contracts/                 # TaskResponse, TaskListResponse, TaskPermissions
    │   ├── Domain/                    # TaskItem (+ regla de título, ver §3.4)
    │   ├── Persistence/               # TasksDbContext, TaskItemConfiguration, Migrations/
    │   ├── TaskMetrics.cs
    │   └── CreateTask.cs, UpdateTask.cs, …
    └── Health/
```

### 2.3 Riesgos y decisiones abiertas que salen de mover cosas

1. **Auth y Users se cruzan hoy en las dos direcciones.** `AuthModule.cs:5` importa `Features.Users.Domain` y crea `UserProfile` en el registro. `TokenService.cs:197` construye `Users.Contracts.UserResponse`. `UsersModule` usa `UserManager<AppIdentityUser>` para leer cuentas, roles y para borrar. `docs/architecture.md:15` hoy permite conocer `Domain` ajeno. La decisión de Tobi (solo `Contracts`) lo prohíbe. [INFERENCE] Para evitar un ciclo, conviene una sola dirección: Users depende de `Auth.Contracts` (cuentas, `IAccessPolicy`) y Auth no conoce el interior de Users. La creación del perfil en el registro pasa a un contrato de Users o a un evento (el outbox llega en el paso 24). **Esta decisión es previa al gate del paso 21.** Si no, el gate falla sobre el código actual.
2. **Transacción del registro.** Hoy `AuthModule.RegisterAsync` abre una transacción y escribe usuario de Identity, perfil y refresh token en el mismo `AppDbContext`. Con contextos separados, EF permite compartir una transacción solo si los contextos comparten `DbConnection` y `DbTransaction` (*"only available when using a relational database provider"*, [EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)). Alternativa: consistencia eventual con outbox (paso 24).
3. **FKs entre features.** `TaskItemConfiguration.cs:29-32` (TaskItems→UserProfiles) y `UserProfileConfiguration.cs:27-31` (UserProfiles→AspNetUsers) cruzan contextos. Grzybek prohíbe datos compartidos entre módulos. [INFERENCE] Opciones: mantener la FK solo en SQL de migración (el modelo EF no la ve), o quitarla y reemplazar el cascade por un evento "usuario borrado". Ojo: hoy hay tests de cascada (`docs/conventions.md`: "Prueba … cascadas").
4. **Health.** `DatabaseReadinessProbe` inyecta `AppDbContext`. Después del paso 22 necesita un chequeo de conexión que no dependa de una feature. [INFERENCE]
5. **Dune tiene rutas fijas.** `DuneCommentPolicy.cs:12` (`backend/Infrastructure/Persistence/Migrations/`), `DunePaths.cs:26-27`, `check-structure.mjs:54-55` y `InfrastructureBoundaryAnalyzer.cs:61`. Mover archivos exige cambiar reglas con fixture válida y violación deliberada (regla 4). No es "tocar un gate para que pase" (regla 9) si el cambio de regla es el objetivo declarado del paso.
6. **Orden en el plan.** [INFERENCE] Mover Authorization, Identity, JWT y permisos a Auth no cambia ningún contrato HTTP. Pero es prerequisito del paso 21. Según la regla 8, conviene discutir con Tobi si va como paso propio antes del 21 o como primera mitad del 21.

---

## 3. ¿"Una clase por slice" es compatible con módulos profundos o es classitis?

### 3.1 Qué dice Ousterhout, con la fuente exacta

**Verificado en texto de Ousterhout** (extracto oficial de la 2ª ed. o su repo de debate con Robert Martin):

- Deep: *"The best methods are those that provide a lot of functionality but have a very simple interface"*. Shallow: *"I call these interfaces 'shallow': they don't help much in terms of reducing what the programmer needs to know."* ([aposd-vs-clean-code](https://github.com/johnousterhout/aposd-vs-clean-code))
- Sobre partir de más: *"The amount of functionality hidden behind each interface drops, while the interfaces often become more complex."* La caracterización deep/shallow *"will tell you when a decomposition is good and also when decomposition makes things worse."* (mismo repo)
- Métodos enredados: *"Entangled methods can usually be improved by combining them so that all the code is in one place."* (mismo repo)
- *"Depth is more important than length: first make functions deep, then try to make them short enough to be easily read. Don't sacrifice depth for length."* y *"If functions are made too small, they lose their independence, resulting in conjoined functions that must be read and understood together."* ([extracto, 9.8](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf))
- *"The decision to split or join modules should be based on complexity. Pick the structure that results in the best information hiding, the fewest dependencies, and the deepest interfaces."* ([extracto, 9.9](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf))
- General-purpose: *"When designing modules such as classes or methods, one of the best ways to produce a deep API is to make it general-purpose (general-purpose APIs result in more information hiding)."* y *"the module's functionality should reflect your current needs, but its interface should not."* ([extracto, cap. 6 y 6.1](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf))
- El ejemplo del editor (6.2): la clase de texto terminó con *"a large number of shallow methods, each of which was only suitable for one user interface operation"*, y eso *"created information leakage between the user interface and the text class."* ([extracto](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf))
- Push specialization upwards (6.6): *"The top-level classes of an application, which provide specific features, will necessarily be specialized for those features. But this specialization need not percolate down into the lower-level classes that are used to implement the features."* ([extracto](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf))
- Pregunta de control (6.5): *"If a method is designed for one particular use, such as the backspace method, that is a red flag that it may be too special-purpose."* ([extracto](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf))
- *"Shallow classes are often the result of treating too many things as important."* ([extracto, 21.4](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf))

**Del libro, capítulos que no están en el extracto.** Comparé solo contra transcripciones secundarias. No pude leer el texto original en línea:

- Classitis (cap. 4): el extremo de "las clases deben ser chicas" es *"a syndrome I call classitis, which stems from the mistaken view that 'classes are good, so more classes are better.'"* El efecto: clases simples una por una que aumentan la complejidad del sistema, porque cada clase chica trae su propia interface. (Corroborado en [notas de lector, Goodreads](https://www.goodreads.com/notes/43701534-a-philosophy-of-software-design/74224520-gustavo-bicalho/58a078d2-7c5b-4f83-93b0-b021cae72c19).)
- Red flag Shallow Module: *"A shallow module is one whose interface is complicated relative to the functionality it provides."* ([transcripción, dev.to](https://dev.to/sportebois/software-design-red-flags-wisdom-nuggets-from-john-ousterhout-43i2))
- Red flag Information Leakage (cap. 5): *"Information leakage occurs when the same knowledge is used in multiple places"*. Temporal Decomposition: *"execution order is reflected in the code structure"*. (misma transcripción)
- Red flag Pass-Through Method (cap. 7): *"A pass-through method is one that does nothing except pass its arguments to another method"*. (misma transcripción)
- Red flag Repetition: *"If the same piece of code (or code that is almost the same) appears over and over again"*, indica que falta la abstracción correcta. (misma transcripción)
- Pull complexity downwards (cap. 8): *"it is more important for a module to have a simple interface than a simple implementation."* (Atribución corroborada en [antoinebuteau.com](https://www.antoinebuteau.com/lessons-from-john-ousterhout/). La redacción exacta no está verificada contra el libro.)

### 3.2 Reconciliación: dónde se mide la profundidad

1. **El módulo que importa es la feature, no el slice.** Desde afuera, Tasks se aprende por su contrato HTTP y por `Tasks/Contracts`. Si ninguna otra feature llama a un slice, los slices son *implementación* de un módulo profundo. Cuántas clases tenga esa implementación no le cambia nada al llamador. [INFERENCE]
2. **Los slices son el código especializado "de arriba".** Cada slice sirve una sola operación de usuario. Eso es exactamente lo que el cap. 6.6 acepta para las clases de nivel superior. El problema del cap. 6.2 (un método por operación de UI) aparece cuando esa especialización **baja**: helpers compartidos con nombre de un caso de uso, o un servicio compartido con `CreateTaskX` y `UpdateTaskX`. [INFERENCE]
3. **La classitis aparece cuando el slice se vuelve una interface que alguien aprende.** Tres señales:
   - otra feature inyecta `CreateTaskHandler`;
   - existe un `ICreateTask` con una sola implementación;
   - un caso de uso se parte en `ValidateX` → `AuthorizeX` → `PersistX`. Eso es descomposición temporal: el orden de ejecución dicta la estructura. Ousterhout lo marca como fuente de information leakage. [INFERENCE apoyada en las citas de 3.1]

### 3.3 Condiciones para que "una clase por slice" siga siendo profunda

1. **Un solo punto de entrada por feature hacia otras features**: `Contracts` (DTOs más, si hace falta, una interface tipo `IAccessPolicy`). Nunca un slice. Lo hace cumplir el gate del paso 21.
2. **Cada slice hace el caso de uso completo**: validar → autorizar → cargar → cambiar → guardar → mapear. No hay clases por paso.
3. **Sin interface por slice.** Un solo adapter es un seam hipotético (ver [informe-deep-mediators.md](informe-deep-mediators.md) §1.2).
4. **Sin pass-through**: el slice usa el `DbContext` de su feature directamente. Bogard: no hacen falta *"repositories, services"* ([VSA](https://www.jimmybogard.com/vertical-slice-architecture/)).
5. **El conocimiento que comparten dos slices baja al dominio de la feature** (entidad o value object), no a un `TasksShared` estático. Es "pull complexity downwards" (Ousterhout) y "push logic to the domain" (Bogard).
6. **Se verifica con el deletion test**: si al borrar una clase la complejidad desaparece, era pass-through; si reaparece en N llamadores, se ganaba su lugar.

### 3.4 Comparación con el diseño actual: un módulo parcial por feature

Hoy `TasksModule` es una clase `partial` repartida en `CreateTask.cs`, `UpdateTask.cs`, etc., con **un** constructor de cuatro dependencias (`TasksModule.cs:15-25`). `docs/conventions.md` lo hace obligatorio: *"No agregues un módulo ni registro por archivo. Mantén un solo constructor con las dependencias compartidas"*.

| Aspecto | Módulo parcial por feature (hoy) | Una clase por slice |
|---|---|---|
| Lo que ve otra feature | `TasksModule` con 5 métodos públicos (hoy otra feature podría llamarlo) | Nada, si se respeta la condición 1 |
| Dependencias | Todas en un constructor. `CreateAsync` recibe `_authorizer` y no lo usa; `GetAsync` recibe `_clock`. El constructor crece con la unión de todo. | Cada slice declara solo lo suyo. Si un slice de Payments necesita un cliente HTTP, solo ese slice lo carga. |
| Registro DI | 1 por feature | N por feature (o escaneo), salvo que `Add<X>Feature` los liste |
| Helpers compartidos | Gratis: `ToResponse` es `private static` y lo ven todas las parciales | Hay que decidir dónde ponerlos: dominio, mapper de la feature o copia. Riesgo de copia. |
| Tamaño real del módulo | Una clase con N archivos. [INFERENCE] Es una clase grande disfrazada: el compilador la ve entera. | N clases chicas; la cohesión la da la carpeta, no el tipo |
| Riesgo Ousterhout | Interface ancha si otras features la llaman. Constructor como "unión de dependencias". | Classitis si se viola alguna condición de 3.3 |

[INFERENCE] **Recomendación.** Las dos formas son compatibles con módulos profundos, porque la profundidad se mide en el borde de la feature. Elegí por divergencia de dependencias:
- Mientras los slices compartan las mismas dependencias, el módulo parcial es más barato.
- Cuando empiecen a divergir (pagos, gateway, cliente HTTP saliente), una clase por slice evita que el constructor sea la suma de todo.

Cambiar la convención actual es una decisión de diseño. Requiere una entrada en `docs/decisions.tsv` y cambios en `docs/conventions.md`. No es un efecto colateral de otro paso.

### 3.5 Duplicación entre slices = information leakage (evidencia del repo)

| Conocimiento | Dónde está repetido | Qué pasa si cambia |
|---|---|---|
| "Título: no nulo, 1..200 caracteres después de `Trim`" | `CreateTask.cs:17-24`, `UpdateTask.cs:17-24` (validadores idénticos); `Trim()` en `CreateTask.cs:46` y `UpdateTask.cs:46`; `HasMaxLength(200)` en `TaskItemConfiguration.cs:20` | Cambiar a 300 exige tocar tres archivos. Si te olvidás uno, la BD rechaza lo que el validador acepta, o al revés. |
| Traducción FluentValidation → `ErrorOr` | `CreateTask.cs:33-37`, `UpdateTask.cs:33-37`, `UsersModule.cs:49-53` | Cambiar el formato de los errores 400 exige tocar tres lugares. |
| `Error.Forbidden(code: "Task.Forbidden", description: "Access denied.")` | `CreateTask.cs:40`, `DeleteTask.cs:19`, `GetTask.cs:20`, `UpdateTask.cs:44` | `TaskErrors` ya existe para `NotFound` pero no tiene `Forbidden`. |

Bogard tolera este tipo de copia: su propio repo repite validadores entre `Students/Create` y `Students/Edit` ([Create](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages/blob/main/ContosoUniversity/Pages/Students/Create.cshtml.cs), [Edit](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages/blob/main/ContosoUniversity/Pages/Students/Edit.cshtml.cs)). Ousterhout la marca como red flag (Repetition, Information Leakage). [INFERENCE] La diferencia práctica es si la regla es *la misma regla de negocio* (debe cambiar junta: hay que unificarla) o *una coincidencia* (puede divergir: la copia está bien). El título de una tarea es la misma regla.

**Destino propuesto, sin crear `Common`** [INFERENCE]:
- **Regla del título** → dominio de Tasks: una constante `TaskItem.TitleMaxLength` (o un value object `TaskTitle`) que leen los dos validadores y `TaskItemConfiguration`. Una sola fuente de verdad que baja la complejidad.
- **Traducción de validación** → `Shared/Http` (o `Shared/Validation`), junto a `ApiResults`: es general-purpose y no nombra features.
- **`Forbidden`** → `TaskErrors.Forbidden`, junto a `NotFound`.

Con una clase por slice, estas tres extracciones pasan de "conveniente" a necesarias. Sin ellas, cada slice nuevo copia el bloque.

---

## Verificación y límites

**Leído en el repo (hechos observados)**: `backend/Program.cs`; `backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs` (completo); `Infrastructure/Identity/AppIdentityUser.cs`; `Infrastructure/Options/JwtOptions.cs`; `Infrastructure/Observability/{TemplateMetrics,ITemplateMetrics,ObservabilityExtensions}.cs`; `Infrastructure/Persistence/{AppDbContext,DatabaseInitializer}.cs` y la lista de `Migrations/`; `Infrastructure/Seeders/RoleSeeder.cs`; `Shared/Authorization/*`; `Shared/Domain/IOwnedResource.cs`; `Shared/Http/*`; `Features/Tasks/{TasksModule,CreateTask,UpdateTask}.cs`; `Features/Tasks/Persistence/TaskItemConfiguration.cs`; `Features/Users/{UsersModule.cs,Persistence/UserProfileConfiguration.cs}`; `Features/Auth/AuthModule.cs`; `Features/Health/HealthFeatureExtensions.cs`; `docs/conventions.md`; `docs/architecture.md` (líneas 13-15); `docs/evolucion/plan.md` (pasos 21-28); greps sobre `dune/` (`DunePaths.cs`, `DuneCommentPolicy.cs`, `DomainDependencyAnalyzer.cs`, `InfrastructureBoundaryAnalyzer.cs`, `check-structure.mjs`).

**Greps con resultado concreto**: `ITemplateMetrics` y sus métodos no tienen llamadores fuera de `Infrastructure/Observability` (cero, tests incluidos). Referencias cruzadas entre features: `AuthModule.cs:5` → `Users.Domain`; `TaskItemConfiguration.cs:4` → `Users.Domain`; `TokenService.cs:197` → `Users.Contracts`. Regla de título duplicada (`CreateTask.cs:22-23` = `UpdateTask.cs:22-23`); `"Task.Forbidden"` ×4; traducción de validación ×3.

**Fuentes web leídas directamente**: el post VSA de Bogard; el árbol completo de `ContosoUniversityDotNetCore-Pages` (rama `main`) y los archivos `Students/Create`, `Students/Edit`, `Instructors/CreateEdit` y `Program.cs`; los posts *Domain-Driven Refactoring* (intro, extracting domain services, pushing behavior down); el abstract de Sessionize; el extracto oficial de Ousterhout (20 páginas leídas como imagen: cap. 6, 9.8-9.9, 12.5-12.6, 21); `johnousterhout/aposd-vs-clean-code` (primeros 100k caracteres de 123k); Microsoft Learn (Develop ASP.NET Core MVC apps, DbContext configuration, Transactions, Migrations history table, EF CLI, Options pattern, Creating Metrics); la doc de OpenTelemetry .NET sobre `AddMeter`; el árbol de `dotnet/eShop` (`src/`, `eShop.ServiceDefaults`, `Ordering.API/Extensions`, `src/Shared`); el README y el árbol de `kgrzybek/modular-monolith-with-ddd`; el post de Jovanović.

**No verificado**:
- Las citas de los capítulos 4, 5, 7 y 8 de Ousterhout (classitis, information leakage, pass-through, pull complexity downwards) no están en el extracto gratuito. Las comparé solo contra transcripciones secundarias (dev.to, Goodreads, antoinebuteau.com). La redacción exacta y las páginas deben confirmarse en el libro.
- No encontré un texto de Bogard que trate explícitamente cuándo extraer código compartido entre slices. Tampoco encontré una guía oficial de Microsoft sobre "modular monolith" o VSA más allá de "Feature organization".
- No leí los 23k caracteres finales de `aposd-vs-clean-code`.
- No compilé ni corrí tests ni gates. No probé ningún movimiento de archivos. Las consecuencias sobre Dune (§2.3.5) salen de leer rutas fijas en el código del analyzer, no de ejecutarlo.
- Todo lo marcado [INFERENCE] es razonamiento sobre lo leído, no un hecho observado. Las recomendaciones de ubicación son propuestas. Los cambios de convención (§3.4) y el orden respecto del paso 21 (§2.3.6) son decisiones de Tobi.
