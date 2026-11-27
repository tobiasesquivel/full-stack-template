# Paso T2: las listas que se pueden calcular se calculan (investigación)

- Fecha: 2026-11-27.
- Estado: **Listo** como investigación. Hay cuatro choques entre el plan y el código (sección "Choques"). Ninguno es entre dos docs del repo, así que la regla 10 no obliga a frenar. No pide herramientas nuevas.
- Alcance: el paso T2 de `docs/evolucion/plan.md` (Fase T). Cómo sale el `Search Path` de los contextos registrados, cómo se recorren todos los relays del outbox y cómo declara cada entidad su columna de dueño.
- Base: `main` en `1b8a9f0`. EF Core 10.0.12, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, .NET SDK 10.0.401.

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `1b8a9f0`.
- `[FUENTE]`: lo leí en un documento primario, con su link (Microsoft Learn, el repo `dotnet/efcore`, el repo `npgsql/npgsql`, la documentación de PostgreSQL).
- `[MEDIDO]`: lo corrí en un proyecto aparte, fuera del repo (sección "Mediciones").
- `[INFERENCIA]`: deducción mía. No la medí.

## Choques (leer primero)

1. **La columna de dueño no es solo `OwnerId` o `AuthId`, y `IOwnedResource` no alcanza como declaración.** Las siete tablas que cuenta hoy `OwnedRowsAsync` usan cuatro columnas: `Id` (`AspNetUsers`), `UserId` (`AspNetUserRoles`, `Sessions`), `AuthId` (`UserProfiles`) y `OwnerId` (`TaskItems`, `Projects`, `IdempotencyKeys`) `[CÓDIGO backend/Tests/Support/Rows/DatabaseRows.cs:25-31]`. `IOwnedResource` ya existe en `Shared/Domain` y significa "recurso que se autoriza por dueño": lo implementan entidades y también modelos de lectura (`ProjectRow`, `OwnedTask`, `ClosureOwner`) `[CÓDIGO backend/Features/Projects/ProjectRow.cs:12]`, `[CÓDIGO backend/Features/Tasks/GetTask/GetTaskQueryHandler.cs:44]`. En `UserProfile`, `OwnerId => AuthId` es una propiedad calculada que EF no mapea `[CÓDIGO backend/Features/Users/Domain/UserProfile.cs:12, 21]` `[MEDIDO E3]`, así que la interfaz no dice qué columna contar. Las tablas de Identity (`AspNetUsers`, `AspNetUserRoles`) no son tipos nuestros. Propuesta: la declaración va en la configuración EF de cada entidad (sección 4). Sigue siendo una declaración explícita por entidad, como pide el plan, pero no es el ejemplo que da el plan.
2. **Con el path calculado, cada host tiene un path distinto.** El monolito no registra `TasksDbContext` y el host de Tasks no registra Auth, Users ni Projects `[CÓDIGO backend/Hosts/Monolith/Program.cs:19-25]`, `[CÓDIGO backend/Hosts/Tasks/Program.cs:17-21]`. Hoy `DatabaseSearchPathTests` espera los mismos seis schemas en los dos hosts `[CÓDIGO backend/Tests/Persistence/DatabaseSearchPathTests.cs:12, 26-27]`. Tres ayudas de test dependen de eso: `OwnedRowsAsync` busca `"TaskItems"` sin schema con el `PlatformDbContext` del monolito `[CÓDIGO backend/Tests/Support/Rows/DatabaseRows.cs:12, 29]`, y `TestDatabases.OpenAsync` es estático y pone el path completo `[CÓDIGO backend/Tests/Support/Hosts/TestDatabases.cs:28-33]`. Esos tests cambian en el paso. No es tapar una falla (regla 9): cambiar de dónde sale el path es el objetivo del paso.
3. **"Mover una tabla de schema" describe una lista a mano.** El paso 1 de esa receta dice que el schema nuevo entra a `DatabaseSearchPath` "sin cambio de modelo ni migración" `[CÓDIGO docs/conventions.md:157]`. Con el path calculado no hay lista que editar. Además, la receta existe porque EF nombraba las tablas sin schema `[CÓDIGO docs/conventions.md:156]`. Desde el paso 22.1 todo contexto tiene `HasDefaultSchema` `[CÓDIGO docs/conventions.md:107]`, y EF nombra cada tabla con su schema, como ya hace `MappedTable` `[CÓDIGO backend/Infrastructure/Messaging/MappedTable.cs:14]`. Entonces mover una tabla entre dos schemas que no son `public` rompe al release anterior aunque el schema nuevo esté en el path `[INFERENCIA]`. El paso tiene que reescribir esa sección de `conventions.md` (sección 1.4).
4. **El ensayo con binarios ya no llega a ready.** Publica solo el monolito y no levanta RabbitMQ: "desde el paso 25 la API que levanta no llega a ready" `[CÓDIGO docs/evolucion/progreso.md:305]`. El "sigue verde" del Verifica se puede probar con `PerFeatureContextsTransitionTests` y `TaskCompletionMigrationTests`, que son el ensayo en tests. El ensayo con binarios queda `NOT_RUN` por ese pendiente, y el cierre lo tiene que decir.

## Respuesta corta

1. **Search Path (preguntas 1 y 2).** `AddMigratedDbContext` ya recibe el schema `[CÓDIGO backend/Infrastructure/Persistence/ContextRegistration.cs:15-17]`. Basta con guardarlo en `MigratedContext` y armar el path con `public` primero y después los schemas registrados, sin repetir. No hace falta construir un `DbContext` para leer `GetDefaultSchema()`, y hacerlo dentro de sus propias opciones sería circular `[INFERENCIA]`. `wolverine` no tiene que entrar: hoy no está y la suite pasa, porque Wolverine nombra su schema `[CÓDIGO backend/Infrastructure/Messaging/Sagas/SagaStorage.cs:13, 19]`. `public` tiene que ir primero (sección 1.3).
2. **Relays (pregunta 3).** `GetKeyedServices<OutboxRelay>(KeyedService.AnyKey)` devuelve todos los relays registrados con una clave concreta, y son las mismas instancias que los hosted services `[FUENTE]` `[MEDIDO E5]`. No pide registros nuevos.
3. **Columna de dueño (pregunta 4).** Un método de extensión sobre `EntityTypeBuilder<T>` guarda la propiedad en una anotación del modelo. Las tablas se leen con `GetTableName()`, `GetSchema()` y `GetColumnName(StoreObjectIdentifier)` `[FUENTE]` `[MEDIDO E3]`. La anotación no cuenta como cambio pendiente del modelo `[MEDIDO E1]`, pero aparece en el snapshot de la próxima migración `[MEDIDO E2]`.
4. **Dónde vive (pregunta 5).** En `Infrastructure/Persistence`. `Domain` no puede usarla (DUNE0001 solo admite BCL y `Shared.Domain`), pero `Persistence` de cada feature ya usa `Infrastructure` (`MapOutbox`, `MapSharedAuditEntries`).

## 1. El `Search Path`

### 1.1 Cómo se arma hoy

- `DatabaseSearchPath.Schemas` es una constante: `"public,auth,users,tasks,platform,projects"` `[CÓDIGO backend/Infrastructure/Persistence/DatabaseSearchPath.cs:7]`. Es el único lugar de `Infrastructure` que nombra features (busqué `auth|users|tasks|projects` en `backend/Infrastructure`).
- `Apply` la escribe con `NpgsqlConnectionStringBuilder.SearchPath` `[CÓDIGO DatabaseSearchPath.cs:9-10]`. La usan tres lugares: las opciones de cada contexto `[CÓDIGO ContextRegistration.cs:46-47]`, la fuente de datos de Wolverine `[CÓDIGO SagaStorage.cs:18]` y `TestDatabases.OpenAsync` `[CÓDIGO TestDatabases.cs:30]`.
- Cuándo corre: dentro del lambda de `AddDbContext((provider, options) => ...)` `[CÓDIGO ContextRegistration.cs:22-26]`. Ese overload "has an `optionsAction` that provides the applications IServiceProvider" ([FUENTE: AddDbContext](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontext)). Las opciones se registran con `optionsLifetime` Scoped por defecto ([misma fuente](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontext)), así que el lambda corre una vez por scope y por contexto, no una vez por proceso `[INFERENCIA de la firma; no lo medí]`.
- Npgsql manda el path como parámetro de arranque de la conexión, no como un `SET` aparte: `startupParams["search_path"] = Settings.SearchPath;` en `WriteStartupMessage` ([FUENTE: NpgsqlConnector.cs](https://github.com/npgsql/npgsql/blob/main/src/Npgsql/Internal/NpgsqlConnector.cs)). Su documentación dice solo "Sets the schema search path." ([FUENTE: Npgsql](https://www.npgsql.org/doc/connection-string-parameters.html)).

### 1.2 Qué dice PostgreSQL del path

- Formato: "must be a comma-separated list of schema names" ([FUENTE](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-SEARCH-PATH)).
- Un schema que no existe "is silently ignored" ([misma fuente](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-SEARCH-PATH)). Entonces el host de Tasks puede nombrar un schema que todavía no se creó sin fallar.
- Gana el primero: "the one found first in the search path is used" ([misma fuente](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-SEARCH-PATH)). Sin coincidencia, "an error is reported, even if matching table names exist in other schemas" ([FUENTE](https://www.postgresql.org/docs/current/ddl-schemas.html#DDL-SCHEMAS-PATH)).
- Crear sin schema: "they will be placed in the first valid schema named in `search_path`" ([FUENTE](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-SEARCH-PATH)).
- `pg_catalog` siempre se busca, aunque no esté en la lista ([misma fuente](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-SEARCH-PATH)).
- Repetidos y comillas: la página no los trata. Medí que `NpgsqlConnectionStringBuilder` acepta `public,auth,public` tal cual `[MEDIDO E6]`; lo que hace el servidor con el repetido no lo probé. Conviene no repetir (`Distinct`). Los schemas del repo son minúsculas sin comillas, así que no hace falta citar nombres.

### 1.3 Qué schemas tienen que estar

- **Los de cada contexto migrado del host.** Hoy cinco llamadas: Platform en los dos hosts `[CÓDIGO backend/Infrastructure/BackendInfrastructure.cs:35]`, Auth `[CÓDIGO backend/Features/Auth/AuthFeature.cs:29]`, Users `[CÓDIGO backend/Features/Users/UsersFeature.cs:22]`, Projects `[CÓDIGO backend/Features/Projects/ProjectsFeature.cs:18]` y Tasks `[CÓDIGO backend/Features/Tasks/TasksFeature.cs:25]`. Todas pasan `TContext.Schema`.
- **`public`, y primero.** El `AppDbContext` viejo no tiene schema y solo es dueño de migraciones anteriores al paso 22 `[CÓDIGO backend/Infrastructure/Persistence/AppDbContext.cs:5-10]`. El arranque las aplica sobre una base heredada `[CÓDIGO backend/Infrastructure/Persistence/Initialization/DatabaseInitializer.cs:27-29]`. Esas migraciones crean tablas sin schema, y PostgreSQL las pone en el primer schema válido del path ([FUENTE](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-SEARCH-PATH)). Después las `Move*` las llevan desde `public` a su schema `[CÓDIGO docs/conventions.md:158]`. Si `public` no va primero, una base heredada recibiría tablas en `platform` o `auth` `[INFERENCIA]`. La decisión de dejar `public` en el path ya está tomada `[CÓDIGO docs/decisions.tsv:260]`. Sale de `ContextRegistration.LegacySchema`, que es de Infrastructure y no nombra features `[CÓDIGO ContextRegistration.cs:12]`.
- **`wolverine` no.** Wolverine recibe su schema al configurar la persistencia `[CÓDIGO SagaStorage.cs:19]`, y hoy no está en el path y todo anda `[CÓDIGO DatabaseSearchPath.cs:7]`. Los tests ya lo nombran: `wolverine.wolverine_outgoing_envelopes` `[CÓDIGO backend/Tests/Support/Hosts/IsolatedApi.cs:141]`.

### 1.4 Cómo calcularlo

Tres formas:

| Forma | Gana | Pierde |
|---|---|---|
| **A. Guardar el schema en `MigratedContext`** y leer `GetServices<MigratedContext>()` | El dato ya llega como parámetro `[CÓDIGO ContextRegistration.cs:17]`. Es el patrón que ya usa el repo para recorrer contextos `[CÓDIGO ContextRegistration.cs:49-64]`. No construye nada | El parámetro y `HasDefaultSchema` pueden separarse. Hoy lo vigila un test `[CÓDIGO DatabaseSearchPathTests.cs:43-52]`, que queda |
| **B. Leer `context.Model.GetDefaultSchema()`** de cada contexto | Sale del modelo mismo. No necesita conexión `[MEDIDO E4]`; el modelo se arma una vez por tipo de contexto ("runs `OnModelCreating` only once, caching the result", [FUENTE](https://learn.microsoft.com/en-us/ef/core/modeling/dynamic-model)) | Para leer el modelo hay que pedir el contexto, y el contexto pide sus opciones. Si las opciones piden los contextos, la resolución vuelve sobre sí misma `[INFERENCIA; no lo medí]` |
| **C. Una lista estática** | Es lo de hoy | Es justo lo que el paso saca |

Recomiendo A. Detalles:
- El path se calcula una vez: un singleton (por ejemplo `DatabaseConnection`) con `public` y después los schemas de `MigratedContext`, sin repetir. Las opciones de cada contexto y `SagaStorage` lo piden al proveedor.
- Orden de los schemas de features: alfabético, después de `public`. El orden de registro cambia con T3, cuando los hosts descubren las features. En el código de producción ningún SQL usa un nombre sin schema (sección 2.2), así que el orden solo importa para tests. Uno fijo los hace repetibles.
- `DatabaseSearchPath.Apply(string)` estático desaparece o pasa a recibir el path. `TestDatabases.OpenAsync` también (sección 2.2).
- La receta "Mover una tabla de schema" de `conventions.md` cambia. Un contexto nuevo ya entra en el mismo release que su schema `[CÓDIGO docs/conventions.md:107]`, y eso pasa solo al registrarlo. La etapa "Search Path" de la receta sirve solo para tablas que salen de `public`, porque ahí EF las nombraba sin schema. Mover entre dos schemas con nombre necesita otra estrategia, y ningún paso del plan la pide hoy. Propuesta: la sección dice eso y deja `public` como único origen soportado.

## 2. ¿El host de Tasks queda con otro path?

### 2.1 Sí

Con la forma A, el monolito queda con `public,auth,platform,projects,users` y el host de Tasks con `public,platform,tasks` `[INFERENCIA de los registros citados en 1.3]`. Hoy los dos tienen los seis `[CÓDIGO DatabaseSearchPathTests.cs:26-27]`.

### 2.2 Qué SQL depende del path

- **Producción: nada.** Todo SQL escrito a mano nombra su schema: `tasks."DeletedOwners"` `[CÓDIGO backend/Features/Tasks/OwnerTombstones.cs:30]`, `projects."ProjectTaskCounters"` `[CÓDIGO backend/Features/Projects/RebuildTaskCounters/RebuildTaskCounters.cs:39]`, `users."AccountClosureHistory"` `[CÓDIGO backend/Features/Users/Persistence/AccountClosureHistory.cs:22]`, `platform."IdempotencyKeys"` `[CÓDIGO backend/Infrastructure/Http/Idempotency/IdempotencyFilter.cs:50]`. El outbox y el inbox arman el nombre con el modelo `[CÓDIGO backend/Infrastructure/Messaging/Inbox/InboxAcceptance.cs:15]`. Es la regla de `conventions.md`: "El SQL escrito a mano en el código de la app nombra el schema" `[CÓDIGO docs/conventions.md:107]`.
- **Tests: varios, y uno se rompe.**
  - `OwnedRowsAsync` cuenta `"TaskItems"` desde el monolito `[CÓDIGO DatabaseRows.cs:29]`. Lo llaman muchos tests con `api.Pair.Monolith.Services`, por ejemplo `[CÓDIGO backend/Features/Users/DeleteUser/DeleteUserBrokerTests.cs:54]`. Sin `tasks` en el path del monolito, falla con "relation does not exist". El conteo nuevo (sección 4) lo resuelve: cada host cuenta sus propias tablas, nombradas con su schema.
  - `TestDatabases.OpenAsync` pone el path completo y lo usan tests que nombran tablas sin schema: `AspNetRoleClaims` `[CÓDIGO backend/Tests/Startup/ConcurrentStartupTests.cs:117]` y las tablas de la transición, primero en `public` y después en su schema `[CÓDIGO backend/Tests/Persistence/PerFeatureContextsTransitionTests.cs:242]`. Este segundo test necesita la unión de los dos hosts. Propuesta: `OpenAsync` recibe el path; `ApiPair` y `ApiFixture` lo arman como la unión de los dos hosts.
  - `Sessions` sin schema desde el monolito `[CÓDIGO backend/Features/Auth/CloseSession/CloseSessionTests.cs:55]`, `[CÓDIGO backend/Tests/Support/Clients/TestAccounts.cs:115]`: sigue andando, porque `auth` es del monolito.

## 3. Todos los relays del outbox

Hoy: `AddOutbox<TContext>` registra un `OutboxRelay` con clave `typeof(TContext)` y lo reexpone como `IHostedService` `[CÓDIGO backend/Infrastructure/Messaging/Outbox/OutboxRegistration.cs:18-19]`. `IsolatedApi` tiene tres propiedades con nombre `[CÓDIGO IsolatedApi.cs:32-36]`, y `DrainAsync` las suma a mano `[CÓDIGO IsolatedApi.cs:127]`. `UndeliveredAsync` une a mano tres tablas `OutboxMessages` y cuatro `InboxMessages` `[CÓDIGO IsolatedApi.cs:154-162]`.

Opciones:
- **`GetKeyedServices<OutboxRelay>(KeyedService.AnyKey)`.** Microsoft: "You can query for all services that were registered using a specific key (that is, not with `KeyedService.AnyKey`)" ([FUENTE: DI, keyed services](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#query-for-services)). En .NET 10 cambió: `GetKeyedServices()` con `AnyKey` "no longer returns registrations for `AnyKey`", y `GetKeyedService()` en singular lanza `InvalidOperationException` ([FUENTE: breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/getkeyedservice-anykey)). Medido con el runtime del repo: devuelve los dos relays registrados con clave, no el registrado sin clave, y son las mismas instancias que se piden por clave `[MEDIDO E5]`.
- **Un registro marcador por `AddOutbox`** (`OutboxContext(Type)`), como `MigratedContext`. Es explícito y no depende de `AnyKey`. Pide una línea más en `AddOutbox`.
- **`GetServices<IHostedService>().OfType<OutboxRelay>()`.** Ya funciona, pero ata el test a cómo se hospeda el relay.

Recomiendo `AnyKey`: es la consulta documentada para este caso y no agrega registros. `IsolatedApi.Relays` une los de los dos hosts, y `DrainAsync` los recorre todos. Un test que quiere un relay concreto lo pide por clave con su contexto, desde la carpeta de su feature (`api.RelayOf<TasksDbContext>()`), así `Tests/Support` no nombra features.

`UndeliveredAsync` sale del modelo igual que el conteo por dueño. Recorre los contextos de los dos hosts, se queda con los que mapean `OutboxMessage` o `InboxMessage` (`FindEntityType`, como `MappedTable` `[CÓDIGO MappedTable.cs:12-14]`) y une sus tablas sin repetir. La lista `expected` de pares evento → consumidor `[CÓDIGO IsolatedApi.cs:148-153]` es el dato que el test espera, no una lista derivable: queda fuera del paso y va como pendiente en `progreso.md` (T7 la toca al sacar Tasks y Projects).

## 4. La columna de dueño

### 4.1 Qué tablas guardan filas por dueño

| Tabla | Columna | Contexto | ¿La cuenta `OwnedRows` hoy? |
|---|---|---|---|
| `AspNetUsers` | `Id` | Auth (Identity) | sí |
| `AspNetUserRoles` | `UserId` | Auth (Identity) | sí |
| `Sessions` | `UserId` `[CÓDIGO backend/Features/Auth/Domain/Session.cs:8]` | Auth | sí |
| `UserProfiles` | `AuthId` `[CÓDIGO UserProfile.cs:12]` | Users | sí |
| `TaskItems` | `OwnerId` `[CÓDIGO backend/Features/Tasks/Domain/TaskItem.cs:17]` | Tasks | sí |
| `Projects` | `OwnerId` `[CÓDIGO backend/Features/Projects/Domain/Project.cs:14]` | Projects | sí |
| `IdempotencyKeys` | `OwnerId` `[CÓDIGO backend/Infrastructure/Persistence/Idempotency/IdempotencyKey.cs:5]` | Platform | sí |
| `AccountClosures`, `AccountClosureSagas`, `AccountClosureGracePeriods`, `AccountClosureHistory` | `UserId` | Auth, Users | no |
| `DeletedOwners`, `OwnerHolds` | `OwnerId` | Tasks | no |

Las de abajo también tienen dueño, pero son la bitácora del cierre o la lápida que queda después: los tests esperan `OwnedRows.None` al terminar el cierre `[CÓDIGO backend/Features/Users/CompleteAccountClosure/CompleteAccountClosureTests.cs:48]`, y esas filas quedan `[INFERENCIA: el historial y la lápida existen para durar después del cierre]`. Entonces la declaración no significa "tiene columna de dueño", sino **"el cierre de cuenta borra estas filas"**. El nombre tiene que decirlo, para que un agente no la ponga en `DeletedOwners`. Propuesta: `ErasedWithOwner(e => e.OwnerId)`. T5 ("cada feature con datos por dueño se anota como participante") puede leer la misma marca.

### 4.2 Los mecanismos comparados

- **`IOwnedResource` (o otra interfaz en `Shared/Domain`).** Es el patrón de `IAuditable`, que lee el `AuditInterceptor` `[CÓDIGO docs/architecture.md:96]`. Pero la interfaz no dice la columna: EF mapea "all public properties with a getter and a setter" ([FUENTE](https://learn.microsoft.com/en-us/ef/core/modeling/entity-properties)), y `UserProfile.OwnerId` es calculada `[MEDIDO E3]`. `Session` usa `UserId`. `IdentityUserRole<string>` no es nuestra: la interfaz pediría un tipo derivado y cambiar los genéricos de `IdentityDbContext`. Además `IOwnedResource` ya significa "se autoriza por dueño" (choque 1).
- **Anotación del modelo, puesta por la configuración de la entidad.** `HasAnnotation` "Adds or updates an annotation on the entity type" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.metadata.builders.entitytypebuilder.hasannotation)). Un método de extensión tipado (`ErasedWithOwner(Expression<Func<T, string>>)`) guarda el nombre de la propiedad y falla si no es una propiedad mapeada. Sirve igual para Identity: `builder.Entity<IdentityUserRole<string>>().ErasedWithOwner(r => r.UserId)` en `AuthDbContext`, después de `base.OnModelCreating` `[CÓDIGO backend/Features/Auth/Persistence/AuthDbContext.cs:22]` `[MEDIDO E3]`.
- **Un registro por tabla en `<F>Feature.cs`** (`AddOwnedTable<TContext, TEntity>(...)`). Funciona, pero es una lista por feature y separa el dato de la entidad. El plan pide lo contrario.

Recomiendo la anotación. Se lee así, sin conexión `[MEDIDO E3]`:
- `model.GetEntityTypes()` y, de cada una, la anotación.
- `GetTableName()`: "Returns the name of the table to which the entity type is mapped" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.relationalentitytypeextensions.gettablename)). `GetSchema()`: "Returns the database schema that contains the mapped table" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.relationalentitytypeextensions.getschema)).
- `property.GetColumnName(StoreObjectIdentifier.Table(tabla, schema))`: "the name of the column to which the property is mapped for a particular table" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.relationalpropertyextensions.getcolumnname)). Respeta `HasColumnName`: medí `owner_id` `[MEDIDO E3]`.
- El nombre SQL se arma con `ISqlGenerationHelper.DelimitIdentifier`, como `MappedTable` `[CÓDIGO MappedTable.cs:14]`.

### 4.3 La anotación y las migraciones

- **No es un cambio pendiente.** Desde EF 9, `Migrate` falla si el modelo tiene cambios sin migración ([FUENTE: EF 9 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes)). EF lo decide con `HasDifferences` entre los modelos relacionales del snapshot y el actual ([FUENTE: Migrator.cs](https://github.com/dotnet/efcore/blob/main/src/EFCore.Relational/Migrations/Internal/Migrator.cs)). Con y sin la anotación, `HasDifferences` da `False` `[MEDIDO E1]`. Entonces el arranque no falla.
- **Sí entra al snapshot.** `dotnet ef migrations add` escribió `b.HasAnnotation("TemplateApp:OwnerProperty", "AuthId")` en el snapshot y en el `.Designer.cs` `[MEDIDO E2]`. El paso no regenera snapshots; la próxima migración de cada contexto traerá esa línea. Ningún gate compara snapshots (busqué `has-pending-model-changes` y `ModelSnapshot` en `.github`, `dune/gates` y los scripts de `verify-template`).

## 5. Dónde vive cada pieza

- `Domain` de una feature solo puede usar BCL, `Shared.Domain` y su propio `Domain`: DUNE0001 "Domain solo admite BCL y Shared.Domain/Domain propio" `[CÓDIGO dune/Dune.Analyzers/DuneDiagnostics.cs:7-10]`, `[CÓDIGO dune/Dune.Analyzers/DomainDependencyAnalyzer.cs:345]`. Una anotación de EF no puede ir ahí. Va en `Persistence`, en la `<Entity>Configuration`.
- `Persistence` de cada feature ya usa `Infrastructure`: `AuthDbContext` llama `MapOutbox` y `MapInbox` `[CÓDIGO AuthDbContext.cs:4, 26-27]`. Entonces `ErasedWithOwner` y su lector viven en `Infrastructure/Persistence` (por ejemplo `Infrastructure/Persistence/Ownership/`), con la convención de una carpeta por mecanismo `[CÓDIGO docs/architecture.md:52]`.
- DUNE0003 deja a `Infrastructure` conocer `Domain` y `Contracts` de las features `[CÓDIGO DuneDiagnostics.cs:25-28]`. El Verifica pide más: que no nombre ninguna. Hoy solo la constante del path las nombra (sección 1.1). Si el paso quiere hacerlo ejecutable (regla 4), es una regla nueva con fixture válida y violación deliberada. Si no, el Verifica se prueba con una búsqueda en el cierre.
- Un `DbContext` de prueba en `backend/Tests` está permitido: DUNE0021 no revisa archivos de tests `[CÓDIGO dune/Dune.Analyzers/PersistenceOwnershipAnalyzer.cs:151-152]`.

## Recomendación para este repo

1. `MigratedContext` suma `Schema`. `AddMigratedDbContext` lo guarda; la firma no cambia.
2. Un singleton en `Infrastructure/Persistence` arma la connection string una vez: `public` (`LegacySchema`) primero, después los schemas de `MigratedContext` ordenados y sin repetir. Las opciones de cada contexto y `SagaStorage` lo piden al proveedor. Se borra la constante.
3. `DatabaseSearchPathTests` espera un path por host (el del monolito y el de Tasks) y sigue afirmando que el schema registrado es el de `HasDefaultSchema`. `TestDatabases.OpenAsync` recibe el path; `ApiPair` y `ApiFixture` dan la unión de los dos hosts.
4. `ErasedWithOwner(e => e.Columna)` en `Infrastructure/Persistence`, sobre `EntityTypeBuilder<T>`, guarda una anotación. Lo declaran las siete tablas de hoy, en su configuración (Identity, en `AuthDbContext`). Las de cierre y la lápida no lo declaran.
5. `OwnedRows` pasa a un mapa `"schema.Tabla" → filas`, armado desde los modelos de los contextos de los dos hosts, sin repetir `platform`. `OwnedRows.None` es "todo en cero". Las aserciones con tuplas (`new OwnedRows(1, 2, 1, 1, 2, 1, 0)`) pasan a nombrar la tabla desde el test de cada feature. El método cuelga de `ApiPair`, no de un `IServiceProvider`, porque un host solo ve sus tablas (choque 2).
6. `IsolatedApi.Relays` usa `GetKeyedServices<OutboxRelay>(KeyedService.AnyKey)` en los dos hosts. `DrainAsync` y `UndeliveredAsync` recorren lo calculado. Se van `UsersRelay` y `AuthRelay`.
7. La prueba del Verifica: un `ProbeDbContext` en `backend/Tests` con schema `probe`, outbox y una entidad con `ErasedWithOwner`. Se registra con `appliesMigrations: false` y sus tablas se crean en el test: `Migrate` sin migraciones fallaría por el cambio pendiente ([FUENTE](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes)). El `configure` de `ApiPair` corre en los dos hosts `[CÓDIGO backend/Tests/Support/Hosts/ApiPair.cs:32, 37]`; el paso decide si lo registra en uno solo.
8. Docs: `conventions.md` (el path y "Mover una tabla de schema", choque 3), `architecture.md` (la línea del path `[CÓDIGO docs/architecture.md:67]`) y una fila en `decisions.tsv`. En `progreso.md`, el pendiente de la lista `expected` y el ensayo con binarios `NOT_RUN` (choque 4).

## Mediciones

Proyecto de consola aparte (`net10.0`, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, EF Core Design 10.0.12, Identity EF 10.0.12), con un `IdentityDbContext<IdentityUser>` de schema `exp`, sin base de datos (connection string a un puerto cerrado).

- **E1**: `IMigrationsModelDiffer.HasDifferences` entre el modelo sin anotaciones y el modelo con `HasAnnotation("TemplateApp:OwnerProperty", ...)` en cuatro entidades: `False`.
- **E2**: `dotnet ef migrations add Init` (herramienta 10.0.2): la anotación aparece en `CtxModelSnapshot.cs` y en el `.Designer.cs`, una línea por entidad.
- **E3**: con la anotación, `GetSchema()`, `GetTableName()` y `GetColumnName(StoreObjectIdentifier.Table(...))` dieron `exp.Item → owner_id` (con `HasColumnName`), `exp.AspNetUsers → Id`, `exp.AspNetUserRoles → UserId`, `exp.Profile → AuthId`. `FindProperty("OwnerId")` en `Profile` (con `OwnerId => AuthId`) dio `null`.
- **E4**: `Model.GetDefaultSchema()` devolvió `exp` sin abrir conexión.
- **E5**: con dos `AddKeyedSingleton<Relay>` (claves `typeof(A)` y `typeof(B)`) y uno sin clave, `GetKeyedServices<Relay>(KeyedService.AnyKey)` devolvió 2 (`A`, `B`), y uno de ellos era la misma instancia que `GetRequiredKeyedService<Relay>(typeof(A))`. `GetKeyedService<Relay>(KeyedService.AnyKey)` lanzó `InvalidOperationException`.
- **E6**: `new NpgsqlConnectionStringBuilder(...) { SearchPath = "public,auth,public" }` escribe `Search Path=public,auth,public` sin quejarse.

## Sin probar

- Qué hace PostgreSQL con un schema repetido en el path.
- Que resolver los contextos dentro de sus propias opciones sea circular (forma B): lo deduje, no lo corrí.
- La prueba del Verifica completa (contexto de prueba, outbox, conteo): queda para el paso.
