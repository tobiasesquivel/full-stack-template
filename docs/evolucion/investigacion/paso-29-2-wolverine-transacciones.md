# Paso 29.2: las slices del monolito como handlers, con Wolverine dueño de la transacción (investigación)

- Fecha: 2026-11-24.
- Estado: **Listo** como investigación. Hay cinco choques con el plan o con los docs (sección "Choques", al principio) y tres decisiones de diseño que conviene que Tobi vea antes del código (sección "Decisiones"). Ninguna pide una herramienta nueva: los paquetes ya están instalados.
- Alcance: el paso 29.2 de `docs/evolucion/plan.md`. Las slices HTTP de Auth, Users y Projects pasan a ser handlers del mediator de Wolverine, Wolverine abre y confirma la transacción alrededor de cada handler (`AutoApplyTransactions`), y salen los `SaveChangesAsync` y los `TransactionScope` de las slices.
- Base: `main` en `d498c06` (después del merge del PR #94, paso 29.1). Wolverine 6.64.2 (`backend/Platform/TemplateApp.Platform.csproj:30-35`), que depende de JasperFx 2.102.0 (nuspec de `wolverinefx` 6.64.2). EF Core 10 con Npgsql.

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `d498c06`.
- `[FUENTE]`: lo leí en un documento primario, con su link: la documentación de Wolverine (wolverinefx.net se genera desde `docs/` del repo de Wolverine) y el código de Wolverine en la etiqueta `V6.64.2`; el código de JasperFx en `v2.102.0`; el código de EF Core en `v10.0.0` y la documentación de EF Core y de Npgsql.
- `[INFERENCIA]`: deducción mía a partir de código o docs. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Choques (leer primero)

1. **El plan dice "salen sus `SaveChangesAsync`", pero dos slices lo necesitan para traducir una restricción de la base.** `CreateProject` atrapa la violación del índice único y responde `Project.NameTaken` `[CÓDIGO backend/Features/Projects/CreateProject/CreateProject.cs:44-51]`; `UpdateProject` atrapa la de concurrencia y responde `Project.Conflict` `[CÓDIGO backend/Features/Projects/UpdateProject/UpdateProject.cs:68-79]`. Si el `SaveChangesAsync` lo hace Wolverine después del handler, la excepción sale fuera del handler y el `409` pasa a `500`: rompe "tests HTTP sin cambios". Propuesta: la slice puede llamar `SaveChangesAsync` **solo** dentro de un `try` cuyo `catch` traduce `DbUpdateException` a un error; confirmar sigue siendo de Wolverine (sección 3.5). Hay que escribirlo como excepción en el plan y en `conventions.md`.
2. **El filtro de idempotencia choca con el modo por defecto de Wolverine.** `IdempotencyFilter` abre un `TransactionScope` alrededor del endpoint `[CÓDIGO backend/Infrastructure/Http/IdempotencyFilter.cs:44]`. El modo `Eager` de Wolverine llama `BeginTransactionAsync` antes del handler ([FUENTE: Transactional Middleware, Eager vs Lightweight](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html#eager-vs-lightweight-transactions)), y EF Core tira `InvalidOperationException` ("An ambient transaction has been detected") si la conexión ya está anotada en un `TransactionScope` ([FUENTE: EF Core `RelationalConnection.cs:385-387` y `440-451`](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/RelationalConnection.cs#L385-L451)). `CreateProject` y `WithdrawAccountClosure` llevan `WithIdempotency` `[CÓDIGO backend/Features/Projects/CreateProject/CreateProjectEndpoint.cs:9]` `[CÓDIGO backend/Features/Users/WithdrawAccountClosure/WithdrawAccountClosureEndpoint.cs:11]`: con `Eager`, cada `POST` con `Idempotency-Key` respondería 500. Por eso la recomendación no usa `Eager` para esos comandos (sección 2).
3. **Wolverine no maneja dos `DbContext` en un handler.** "There is no way to utilize more than one `DbContext` type in a single handler while using the Wolverine transactional middleware. You can certainly do that, just with explicit code" ([FUENTE: Modular Monolith, With EF Core](https://wolverinefx.net/tutorials/modular-monolith.html#with-ef-core)). El plan pide sacar el `TransactionScope` de `RegisterUser` y `WithdrawAccountClosure`; la única forma de mantener una sola transacción entre contextos es "explicit code". La propuesta pone ese código explícito en un middleware de Wolverine en `Infrastructure/Messaging`, no en la slice (sección 2). La alternativa que Wolverine documenta para EF Core (conexión compartida con `UseTransaction`) ya la descartó Tobi el 2026-11-18 (`docs/decisions.tsv:247`, "Elección de Tobi frente a una conexión compartida con UseTransaction").
4. **"Revertir ante un error de negocio" no vale para `OpenSession`.** Con contraseña incorrecta, `OpenSession` guarda el intento fallido (`AccessFailedAsync`) y devuelve un error `[CÓDIGO backend/Features/Auth/OpenSession/OpenSession.cs:48-53]`. Si la transacción del comando se revierte ante un `ErrorOr` con error, el contador de intentos nunca sube y el bloqueo por contraseña (5 intentos, `AuthFeature.cs`) deja de funcionar. Por eso `OpenSession` no va como comando atómico (sección 5).
5. **La regla del 29.1 "un handler del mediator devuelve solo su respuesta"** (investigación 29.1, sección 1.5, y el test `EveryMediatorHandler_AnswersWithErrorOr` `[CÓDIGO backend/Tests/Messaging/MediatorContractTests.cs:46-57]`) impide arrancar la saga de `DeleteUser` devolviéndola como mensaje en cascada, que es la forma de Wolverine ([FUENTE: Sagas, Starting a Saga](https://wolverinefx.net/guide/durability/sagas.html#starting-a-saga)). La propuesta respeta la regla del 29.1 y cambia `SagaOutbox` en su lugar (sección 5).

Ningún choque entre dos docs del repo: no hace falta frenar por la regla 10. Los cinco son entre el plan (o la guía de Wolverine) y el código.

## Respuesta corta

1. **`AutoApplyTransactions` con EF Core (pregunta 1).** Wolverine envuelve en transacción a todo handler que dependa de un `DbContext`: como parámetro, como dependencia de un servicio que recibe, o como dependencia del constructor del handler. La detección recorre los constructores **hasta un tipo `internal` o un registro con fábrica**: ahí corta. En el modo `Eager` (el de hoy) genera: anotar el `DbContext` en el outbox, `BeginTransactionAsync`, el handler, `SaveChangesAsync`, `Commit` y enviar los mensajes; ante una excepción, `Rollback`. Con `InvokeAsync<T>` el código generado es el mismo que con un mensaje de cola: la transacción aplica igual, y no se escribe fila en el inbox porque el sobre no tiene destino.
2. **Dos contextos (pregunta 2).** `RegisterUser` escribe en `AuthDbContext` (identidad, rol, sesión) y en `UsersDbContext` (perfil, a través de `IUsersModule`). `WithdrawAccountClosure` escribe solo en `UsersDbContext`; Auth solo se lee, antes de la transacción. Wolverine **no ve** el segundo contexto de `RegisterUser` (está detrás de `UsersModule`, que es `internal`): arrancaría sin error y guardaría el perfil fuera de la transacción. **Recomiendo** un middleware de Wolverine propio que abre `CrossContextTransaction` (el `TransactionScope` de hoy) alrededor de cada comando del monolito, con la integración de EF en modo `Lightweight` (Wolverine guarda, pero no abre una transacción de EF). Resuelve los dos contextos, convive con el filtro de idempotencia y revierte también ante un `ErrorOr` con error.
3. **Interacciones (pregunta 3).** El outbox casero no cambia (guarda en el mismo `SaveChanges`). Las reglas de reintento del paso 25 no tocan al mediator gracias al `rethrow` del 29.1; la transacción se revierte antes de relanzar. El handler corre en su propio scope de DI, así que el `DbContext` que ve Wolverine es el mismo que recibe la slice. El filtro de idempotencia es el choque 2. Hay 11 llamadas a `SaveChangesAsync` o `TransactionScope` en Auth, Users y Projects; 8 están en 6 slices HTTP que migran.
4. **Test de fallo a mitad (pregunta 4).** El mismo estilo que `Create_WithKey_WhenItFailsAfterTheTaskWasSaved_LeavesNeitherKeyNorTask`: un `FailAfterSaving<T>` por `ConfigureDbContext` en un cliente aislado, y una consulta a la base real que cuenta filas. Para `RegisterUser`: falla al guardar la sesión y no queda ni identidad ni perfil.
5. **Inventario (pregunta 5).** 15 slices HTTP migran (6 escrituras, 6 lecturas y 3 de sesión); `IssueCsrfToken` queda como endpoint sin handler. Quedan fuera los consumidores del broker (`DecideAccountClosure`, `ApplyTaskEvent`), las sagas y sus pasos, y `RebuildTaskCounters`: ya tienen su transacción, o la tiene la saga.
6. **Dune (pregunta 6).** DUNE0028 suma Auth, Users y Projects a su lista y exime a `IssueCsrfToken`. DUNE0022 tiene que ver la transacción que abre Wolverine (todo el handler del comando queda adentro). Regla nueva, DUNE0029: "un handler del mediator no confirma: no llama `BeginTransaction`, `Commit`, `Rollback`, `UseTransaction` ni `CrossContextTransaction.Begin`, y solo llama `SaveChangesAsync` dentro de un `try` que traduce `DbUpdateException`".
7. **Propuesta (pregunta 7).** Siete commits: spike en rojo con `UpdateProfile`, la política de transacción en `Infrastructure/Messaging`, Projects, Users, Auth, Dune y docs.

## Glosario corto

- **Middleware de transacciones** (= código que Wolverine genera antes y después de cada handler): abre la transacción, llama al handler, guarda y confirma; si algo tira, revierte. La slice ya no lo escribe.
- **`AutoApplyTransactions`** (= la política de Wolverine que pone ese middleware sola): "Wolverine will wrap transactional middleware around any message handler that has a dependency on any type of `DbContext`" ([FUENTE: Transactional Middleware](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html)). El monolito ya la prende `[CÓDIGO backend/Infrastructure/Messaging/SagaStorage.cs:20]`.
- **Modo `Eager`** (= transacción explícita): Wolverine llama `DbContext.Database.BeginTransactionAsync()` antes del handler. Es el modo por defecto.
- **Modo `Lightweight`** (= sin transacción explícita): Wolverine solo llama `SaveChangesAsync()` al final; "The one thing `Lightweight` genuinely cannot give you is a rollback" ([FUENTE: Lightweight Mode and the Outbox](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html#lightweight-mode-and-the-outbox)).
- **Transacción ambiente** (= `TransactionScope`): una transacción de .NET que viaja con el hilo lógico; cada conexión que se abre adentro se anota sola. Es lo que hoy hace `CrossContextTransaction` `[CÓDIGO backend/Infrastructure/Persistence/CrossContextTransaction.cs:7-11]`.
- **Service location** (= pedir una dependencia al contenedor en tiempo de ejecución): Wolverine la usa para los tipos `internal` (investigación 29.1, sección 1.3).

---

## 1. Cómo funciona `AutoApplyTransactions` con EF Core en 6.64.2

### 1.1 Qué se configura hoy

- El monolito llama `AddMessaging(..., storesSagas: true)` `[CÓDIGO backend/Program.cs:19]`, que dentro de `AddWolverine` llama `SagaStorage.Configure` `[CÓDIGO backend/Infrastructure/Messaging/MessagingRegistration.cs:20-26]`.
- `SagaStorage.Configure` guarda los mensajes de Wolverine en PostgreSQL (schema `wolverine`), prende la integración de EF y `AutoApplyTransactions`, y hace durables las colas locales `[CÓDIGO backend/Infrastructure/Messaging/SagaStorage.cs:18-21]`.
- Los contextos del repo se registran con `AddDbContext` común, no con `AddDbContextWithWolverineIntegration` `[CÓDIGO backend/Infrastructure/Persistence/ContextRegistration.cs:20-24]`. No hace falta: la detección mira el tipo, no el registro (1.2), y el código generado de la saga ya muestra la transacción sobre `UsersDbContext` `[CÓDIGO backend/Internal/Generated/WolverineHandlers/AccountClosureGraceExpiredHandler503767472.cs:40-47]`. Sin las tablas de Wolverine mapeadas en el contexto, los sobres del outbox se escriben con ADO dentro de la misma transacción ([FUENTE: código, `EnlistDbContextInOutbox.cs`, comentario de `CommitTransactionOpenedByTheOutbox`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EnlistDbContextInOutbox.cs)).
- El host de Tasks no tiene nada de esto `[CÓDIGO backend/Hosts/Tasks/Program.cs:17]`: el paso no lo toca.

### 1.2 Cuándo decide que un handler es transaccional

- La política recorre cada cadena que no tenga `[Transactional]` ni `[NonTransactional]`, y si un solo proveedor de persistencia la reclama, le aplica la transacción ([FUENTE: código, `AutoApplyTransactions.cs:11-48`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Persistence/AutoApplyTransactions.cs#L11-L48)).
- El proveedor de EF la reclama si alguna dependencia de la cadena "se puede convertir" a `DbContext` ([FUENTE: código, `EFCorePersistenceFrameProvider.cs:969-992`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs#L969-L992)). La documentación lo dice así: "a method argument, a dependency of any service injected as a method argument, or a dependency of any service injected as a constructor argument of the handler class" ([FUENTE: Transactional Middleware](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html)).
- Las dependencias salen de `Chain.ServiceDependencies`: el tipo del handler, el árbol de su constructor y los parámetros del método ([FUENTE: código, `Chain.cs:354-397`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Configuration/Chain.cs#L354-L397)).
- **El recorrido corta en tipos `internal` y en registros con fábrica.** JasperFx arma un plan por servicio: si el tipo del servicio o su implementación no es público, o si el registro es una fábrica (`sp => ...`), el plan es de service location ([FUENTE: código JasperFx, `ServiceFamily.cs:57-111`](https://github.com/JasperFx/jasperfx/blob/v2.102.0/src/JasperFx/CodeGeneration/Services/ServiceFamily.cs#L57-L111)). Ese plan no devuelve dependencias propias; solo el plan de constructor sigue bajando ([FUENTE: código JasperFx, `ConstructorPlan.cs:183-196`](https://github.com/JasperFx/jasperfx/blob/v2.102.0/src/JasperFx/CodeGeneration/Services/ConstructorPlan.cs#L183-L196) y [`ServicePlan.cs:42`](https://github.com/JasperFx/jasperfx/blob/v2.102.0/src/JasperFx/CodeGeneration/Services/ServicePlan.cs#L42)). El tipo cortado sí aparece en la lista: por eso `UsersDbContext`, que es `internal`, se detecta cuando el handler lo pide directo `[INFERENCIA]`.
- **Qué ve Wolverine en este repo** `[INFERENCIA]`, a partir de lo anterior:
  - Un `DbContext` pedido en el constructor del handler: sí (es `internal`, pero aparece).
  - `IAuthModule`: no ve adentro, porque se registra con fábrica `[CÓDIGO backend/Features/Auth/AuthFeature.cs:39]`.
  - `IUsersModule` e `IProjectsModule`: no ve adentro, porque sus implementaciones son `internal` `[CÓDIGO backend/Features/Users/UsersFeature.cs:34]` `[CÓDIGO backend/Features/Projects/ProjectsFeature.cs:16]`.
  - `UserManager<AppIdentityUser>`: probablemente **sí** llega a `AuthDbContext`, porque `UserManager<>` y `UserStore<...>` son genéricos públicos registrados por tipo (`AddEntityFrameworkStores<AuthDbContext>()` `[CÓDIGO backend/Features/Auth/AuthFeature.cs:68]`). En las slices que lo usan, `AuthDbContext` ya está como dependencia directa, así que da un solo contexto igual. `[NO PROBADO]`.
  - Consecuencia: ninguna slice del monolito dispara el error de arranque "multiple DbContext types detected" ([FUENTE: Selecting the Transactional DbContext](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html#selecting-the-transactional-dbcontext)). El riesgo es el opuesto: un segundo contexto escondido detrás de un módulo **queda fuera de la transacción sin aviso** (`RegisterUser`, sección 2.1).
- `[NonTransactional]` y `[Transactional]` son atributos de `Wolverine.Attributes` ([FUENTE: código, `NonTransactionalAttribute.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Attributes/NonTransactionalAttribute.cs)): ponerlos en la slice haría que la slice nombre Wolverine (DUNE0025). Pero `TransactionalAttribute.Modify(chain, rules, container)` es público y una política de `Infrastructure/Messaging` lo puede aplicar a una cadena sin atributo ([FUENTE: código, `TransactionalAttribute.cs:35-84`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Attributes/TransactionalAttribute.cs#L35-L84)).

### 1.3 Qué código genera

En modo `Eager` y con un solo contexto, el proveedor inserta `EnrollDbContextInTransaction` al principio de la cadena y suma tres pasos al final: `SaveChangesAsync`, el commit y el envío de mensajes ([FUENTE: código, `EFCorePersistenceFrameProvider.cs:456-578`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs#L456-L578)). El código que sale (el de la saga del repo es un ejemplo real):

1. Crea un `EfCoreEnvelopeTransaction` y anota el contexto de mensajes en el outbox `[CÓDIGO AccountClosureGraceExpiredHandler503767472.cs:41-42]`.
2. "Start the actual database transaction if one does not already exist": `if (db.Database.CurrentTransaction == null) await db.Database.BeginTransactionAsync(...)` `[CÓDIGO AccountClosureGraceExpiredHandler503767472.cs:43-47]` ([FUENTE: código, `EnrollDbContextInTransaction.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EnrollDbContextInTransaction.cs)).
3. `try { handler }` y, si tira, `catch { await RollbackAsync(); throw; }` (misma fuente).
4. Después del handler: `SaveChangesAsync` ("Added by EF Core Transaction Middleware"), después `CommitAsync` y después el envío de los mensajes guardados en el outbox ([FUENTE: código, `EFCorePersistenceFrameProvider.cs:609-622`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs#L609-L622)).

En modo `Lightweight`, para un handler de mensajes con persistencia en base: anota el contexto en el outbox **sin** `BeginTransactionAsync` (`EnlistDbContextInOutbox`), y al final `SaveChangesAsync` y un commit solo si el outbox abrió una transacción para escribir sobres ([FUENTE: código, `EFCorePersistenceFrameProvider.cs:536-557`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs#L536-L557); [`EnlistDbContextInOutbox.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EnlistDbContextInOutbox.cs)). Un handler del mediator no publica mensajes, así que esa transacción no se abre `[INFERENCIA]`.

El modo se puede fijar por cadena: la etiqueta de la cadena gana al modo global ([FUENTE: código, `EFCorePersistenceFrameProvider.cs:728-767`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs#L728-L767)). Un `ExecuteUpdate`/`ExecuteDelete` o SQL crudo necesita `Eager` para quedar dentro de la transacción ([FUENTE: Set-Based Operations and EfCoreOps](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html#set-based-operations-and-efcoreops)).

### 1.4 `InvokeAsync<T>` en línea: ¿aplica igual?

- **Sí.** La cadena se genera una vez por tipo de mensaje; `InvokeAsync` y una cola ejecutan la misma clase generada. El camino en línea arma un `MessageContext`, lee el sobre y llama al handler generado, y después envía lo que haya quedado en el outbox ([FUENTE: código, `Executor.cs:142-175`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L142-L175)). La transacción, el `SaveChanges` y el commit están adentro de esa clase `[INFERENCIA]`.
- **El inbox no escribe fila.** Al confirmar, `EfCoreEnvelopeTransaction` marca el mensaje como atendido solo si el sobre tiene `Destination` ([FUENTE: código, `EfCoreEnvelopeTransaction.cs:282-323`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Internals/EfCoreEnvelopeTransaction.cs#L282-L323)). El sobre de `InvokeAsync<T>` no tiene destino: lleva respuesta, `ResponseType` y `DoNotCascadeResponse` ([FUENTE: código, `Executor.cs:178-197`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L178-L197)). Entonces un `POST` no suma filas a `wolverine_incoming_envelopes` `[INFERENCIA]`.
- **El commit va antes de que vuelva la respuesta.** `InvokeAsync<T>` devuelve `envelope.Response` cuando termina `InvokeInlineAsync`, que incluye el commit (mismas fuentes). El endpoint arma la respuesta HTTP con datos ya confirmados `[INFERENCIA]`.

## 2. Dos `DbContext` en un handler

### 2.1 Qué hacen hoy las dos slices

| Slice | Contextos | Cómo guarda hoy | Qué haría `Eager` |
|---|---|---|---|
| `RegisterUser` | `AuthDbContext` (identidad y rol por `UserManager`, sesión por `Sessions`) y `UsersDbContext` (perfil, por `IUsersModule.CreateProfileAsync`, que llama su propio `SaveChangesAsync`) `[CÓDIGO RegisterUser.cs:61-79]` `[CÓDIGO backend/Features/Users/UsersModule.cs:18-33]` | `CrossContextTransaction.Begin()` alrededor de todo; las conexiones se abren de a una y Npgsql reusa la misma conexión física `[CÓDIGO docs/conventions.md:97]` | Wolverine ve solo `AuthDbContext`. Abre su transacción en esa conexión y la deja abierta; el perfil se guarda en **otra** conexión con su propia transacción. Si falla la sesión, el perfil queda guardado `[INFERENCIA]` |
| `WithdrawAccountClosure` | `UsersDbContext` para escribir (`FOR UPDATE`, `ExecuteDelete`, historial con SQL crudo, `SaveChanges`); Auth solo se lee, **antes** de la transacción `[CÓDIGO WithdrawAccountClosure.cs:36-62]` | `CrossContextTransaction.Begin()`, para que funcione igual con o sin el filtro de idempotencia `[CÓDIGO docs/conventions.md:97]` | Un solo contexto: `Eager` alcanza. Pero el endpoint tiene `WithIdempotency`, y con el filtro el `BeginTransactionAsync` tira (choque 2) |

Npgsql: "if you open and close connections to the same database inside an ambient transaction, without ever having two connections open *at the same time*, Npgsql internally reuses the same connection, avoiding the need for a distributed transaction" ([FUENTE: Npgsql, System.Transactions](https://www.npgsql.org/doc/basic-usage.html#systemtransactions-and-distributed-transactions)). Eso es lo que hace funcionar `CrossContextTransaction` hoy.

### 2.2 Opciones

| Opción | Cómo | Gana | Pierde |
|---|---|---|---|
| (C1) **Transacción ambiente abierta por Wolverine** (recomendada) | Una política de `Infrastructure/Messaging` envuelve cada comando del monolito en `CrossContextTransaction.Begin()` y fija la integración de EF en `Lightweight` para esa cadena. Wolverine guarda (`SaveChangesAsync`) y la política confirma (`Complete()`) | Los dos contextos de `RegisterUser` quedan en una transacción sin tocar `Users`; convive con el filtro de idempotencia (el `TransactionScope` de la política se suma al del filtro, `TransactionScopeOption.Required`); `ExecuteSql`/`ExecuteDelete` quedan adentro; cero cambios de contrato | Código generado propio (frames de JasperFx) en vez de la configuración estándar; depende del orden de las políticas `[NO PROBADO]`; `TransactionScope` confirma de forma síncrona al hacer `Dispose` ([FUENTE: EF Core, Limitations of System.Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions#limitations-of-systemtransactions)) |
| (C2) `Eager` estándar + conexión compartida | Los contextos del monolito comparten una `DbConnection` por scope y el segundo llama `Database.UseTransaction(...)` ([FUENTE: EF Core, Cross-context transaction](https://learn.microsoft.com/en-us/ef/core/saving/transactions#cross-context-transaction)) | Es lo que documenta Microsoft; sin `TransactionScope` | Plomería por scope en `ContextRegistration` y en cada módulo; Tobi la descartó (`docs/decisions.tsv:247`); no resuelve el choque 2 |
| (C3) Separar en dos handlers con un mensaje en cascada | `RegisterUser` guarda la cuenta y publica "crear perfil" por el outbox durable; Users lo atiende después | Un contexto por handler, el modelo de Wolverine | Consistencia eventual: `OpenSession` responde `Auth.ProfileMissing` si el login llega antes que el perfil `[CÓDIGO OpenSession.cs:57-59]`; la respuesta del registro no tendría el perfil; cambia el contrato. Wolverine: "We (the Wolverine team) are loathe to recommend using eventual consistency between modules if you don't have to" ([FUENTE: Modular Monolith](https://wolverinefx.net/tutorials/modular-monolith.html#eventual-consistency-between-modules)) |
| (C4) `[NonTransactional]` y la slice sigue con su `TransactionScope` | La política excluye esas dos slices | Cero riesgo | Contradice el plan ("salen sus `TransactionScope`") y deja dos dueños de la transacción en el monolito (`docs/decisions.tsv:507`) |

**Recomiendo C1** para todos los comandos del monolito, no solo para los dos de dos contextos:
- Es la única opción que cumple a la vez el plan (la slice no abre ni confirma), Wolverine ("just with explicit code", choque 3), el filtro de idempotencia (choque 2) y "tests HTTP sin cambios".
- El concepto del paso se ve igual: el código que abre, guarda y confirma está en la cadena generada alrededor del handler, no en la slice.
- La pieza que abre la transacción ya existe y está probada (`CrossContextTransaction`, paso 22.1); cambia de dueño.
- `DeleteUser` es la excepción: usa la saga con el outbox de Wolverine y necesita una transacción de EF (sección 5).

### 2.3 Cómo queda C1 en el código generado

Para un comando atómico, la cadena generada queda así `[INFERENCIA]`, `[NO PROBADO]`:

```
using var scope = CrossContextTransaction.Begin();     // frame propio, el primero de la cadena
var tx = new EfCoreEnvelopeTransaction(db, context, ...); // Lightweight: anota el outbox, sin BeginTransaction
var result = await handler.Handle(command, ct);
await context.EnqueueCascadingAsync(result);            // guarda la respuesta de InvokeAsync<T>
if (result.IsError) return;                             // frame propio: un error de negocio no guarda nada
await db.SaveChangesAsync(ct);                          // de Wolverine (Lightweight)
scope.Complete();                                       // frame propio, el último
```

- **Por qué el `if (result.IsError) return;`**: hoy, si una slice devuelve un error, no llama `SaveChangesAsync` y no se guarda nada. Con Wolverine guardando después del handler, una slice que cambió una entidad y después devolvió un error la guardaría igual. Pasa en `UpdateProject`: renombra el proyecto y después puede devolver `NameTaken` `[CÓDIGO UpdateProject.cs:51-65]`. El frame corta antes del `SaveChangesAsync` de Wolverine y, sin `Complete()`, el `Dispose` revierte también lo que se escribió con SQL crudo `[INFERENCIA]`.
- **Por qué `Complete()` al final**: "After calling this method, you can no longer access the ambient transaction via the `Transaction.Current` property, and trying to do so results in an exception being thrown" ([FUENTE: .NET API, `TransactionScope.Complete`](https://learn.microsoft.com/en-us/dotnet/api/system.transactions.transactionscope.complete)). Tiene que ir después del `SaveChangesAsync`.
- **Por qué `Lightweight` y no `Eager`**: con `Eager`, EF tira al ver la transacción ambiente (choque 2). `Lightweight` no llama `BeginTransactionAsync` y el `SaveChangesAsync` se anota en el `TransactionScope` ([FUENTE: EF Core, Using System.Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions#using-systemtransactions)).
- **Por qué un `TransactionScope` síncrono funciona**: el constructor de `TransactionScope` con `TransactionScopeAsyncFlowOption.Enabled` guarda la transacción en un `AsyncLocal`; si se crea en el mismo método `async` generado (no adentro de otro método `async`), sigue viva en todo lo que viene después `[INFERENCIA]`. Por eso tiene que ser un frame que escribe la línea en el método generado, no un middleware `async Before`.
- **Cómo se escribe**: una `IHandlerPolicy` en `Infrastructure/Messaging`, como `MessagingChainPolicy` `[CÓDIGO backend/Infrastructure/Messaging/MessagingChainPolicy.cs:10-24]`, que para cada comando atómico (1) aplica `new TransactionalAttribute { Mode = TransactionMiddlewareMode.Lightweight }.Modify(chain, rules, container)`, (2) inserta un frame al principio de `chain.Middleware` y (3) inserta el corte y el `Complete()` en `chain.Postprocessors`. Es la misma técnica que usa el proveedor de EF (`chain.Middleware.Insert(0, ...)`, `chain.Postprocessors.Add(...)`) ([FUENTE: código, `EFCorePersistenceFrameProvider.cs:485-576`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs#L485-L576)).
- **Orden de las políticas** `[NO PROBADO]`: si `AutoApplyTransactions` corre antes, aplica `Eager` y la etiqueta `uses_efcore_transaction` impide volver a aplicarla ([FUENTE: código, `EFCorePersistenceFrameProvider.cs:458-459`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs#L458-L459)). Hoy `AutoApplyTransactions` se agrega en el callback de `AddWolverine` `[CÓDIGO SagaStorage.cs:20]` y `MessagingChainPolicy` en la extensión `[CÓDIGO MessagingExtension.cs:56]`. El paso tiene que mover `AutoApplyTransactions()` a `MessagingExtension`, después de la política nueva, y el spike lo comprueba leyendo el código generado.

## 3. Cómo interactúa con lo que ya hay

### 3.1 Outbox casero (paso 24)

- `AddOutbox<TContext>()` agrega `OutboxInterceptor` al contexto; las entidades anotan eventos y el interceptor escribe las filas en el mismo `SaveChanges` (`conventions.md`, "Eventos y outbox"). Si el `SaveChanges` lo llama Wolverine, el interceptor corre igual: es del contexto, no de quien lo llama `[INFERENCIA]`.
- Hoy solo Users y Auth tienen outbox casero (`AuthFeature.cs:22`, `UsersFeature.cs:20`). `UserDeleted` lo emite `CompleteAccountClosure`, un paso de la saga: queda fuera del paso.
- El relay (`OutboxRelay`) lee filas confirmadas `[CÓDIGO backend/Infrastructure/Messaging/OutboxRelay.cs:96]`. Con C1 la fila se confirma en el `Complete()` del comando: el relay no la ve antes `[INFERENCIA]`.
- `AfterCommit` (paso 28) es un interceptor de transacciones de EF `[CÓDIGO backend/Infrastructure/Persistence/AfterCommitInterceptor.cs:6]`. Con una transacción ambiente no hay transacción de EF y no dispara. Hoy lo usan solo pasos de la saga (`AccountClosureSteps`, `RemindAccountClosure`), que siguen en `Eager` `[INFERENCIA]`. Si un comando atómico lo necesitara, no funcionaría: lo anoto como límite en `conventions.md`.

### 3.2 Reintentos (paso 25) y `rethrow` en línea (29.1)

- `QueueFailureRules` reintenta cualquier excepción (200 ms y 1 s) y las de base con esperas largas `[CÓDIGO backend/Infrastructure/Messaging/QueueFailureRules.cs:16-26]`. `MessagingChainPolicy` le pone a cada cadena del mediator una regla que relanza sin reintentar `[CÓDIGO MessagingChainPolicy.cs:18-22]`; las reglas de la cadena van antes que las globales (investigación 29.1, sección 1.4).
- Hoy esa política recibe solo los handlers de Tasks porque es la única feature que registra `AddMediatorHandler`. En el monolito la misma lista crece sola con los handlers nuevos `[CÓDIGO MessagingExtension.cs:56]`: el `rethrow` aplica sin cambios `[INFERENCIA]`.
- Orden dentro de una excepción: el `catch` de la cadena generada revierte (en `Eager`) o el `using` del scope descarta (en C1) **antes** de que la excepción llegue al `Executor`, que busca la regla y relanza ([FUENTE: código, `Executor.cs:292-330`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L292-L330)). Si alguna vez se reintentara, el reintento arrancaría con una transacción nueva `[INFERENCIA]`.
- `UseSyncRetryBlock` y las reglas de cola no cambian `[CÓDIGO MessagingExtension.cs:53-55]`.

### 3.3 Scope de DI y service location de tipos `internal`

- El código generado abre un scope propio y pide al contenedor cada tipo `internal` (el `DbContext`, `TaskMetrics`, `ProjectsClient`) `[CÓDIGO backend/Hosts/Tasks/Internal/Generated/WolverineHandlers/CreateTaskCommandHandler776868131.cs:23-36]`. `IncludeMediatorHandler` ya marca esos tipos para service location `[CÓDIGO MessagingExtension.cs:116-128]`.
- La transacción y el handler usan **la misma** variable `DbContext` del método generado: `EnrollDbContextInTransaction` y `EnlistDbContextInOutbox` la buscan con `chain.FindVariable(dbContextType)` ([FUENTE: código, `EnrollDbContextInTransaction.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EnrollDbContextInTransaction.cs)). El ejemplo real: `usersDbContext` en la transacción y en la carga de la saga `[CÓDIGO AccountClosureGraceExpiredHandler503767472.cs:34-60]`.
- Los módulos de otra feature (`IUsersModule`, `IAuthModule`) se resuelven en el mismo scope del handler, así que `UsersModule` recibe el `UsersDbContext` de ese scope. Con C1 su conexión se anota en la transacción ambiente; con `Eager` quedaría afuera (2.1).
- El filtro de idempotencia usa el scope del **request** (`http.RequestServices`) `[CÓDIGO IdempotencyFilter.cs:39]`, otro scope. Lo único que cruza de un scope al otro es la transacción ambiente, que viaja en el `AsyncLocal` y no en el contenedor `[INFERENCIA]`. Esa es la razón técnica de fondo para C1.
- `IMessageBus` pedido dentro del scope del handler es el contexto de la cadena: Wolverine lo registra como `ScopedMessageContextHolder.Context ?? MessageContext`, y el código generado llena el holder al empezar ([FUENTE: código, `HostBuilderExtensions.cs:299-303`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/HostBuilderExtensions.cs#L299-L303)) `[CÓDIGO CreateTaskCommandHandler776868131.cs:24]`. Sirve para `DeleteUser` (sección 5).

### 3.4 Filtro de idempotencia (`WithIdempotency`)

- Hoy: el filtro inserta la clave, corre el endpoint, guarda la respuesta y llama `Complete()` solo si el estado es 2xx `[CÓDIGO IdempotencyFilter.cs:44-75]`. Los endpoints con filtro: `CreateTask`, `CreateProject` y `WithdrawAccountClosure`.
- Con `Eager`: choque 2, el `BeginTransactionAsync` tira.
- Con C1: el `TransactionScope` del comando es `Required` y se suma al del filtro; el `Complete()` del comando no confirma, confirma el del filtro al salir `[INFERENCIA]`. Es el mismo comportamiento que hoy, con un `TransactionScope` anidado en vez de uno en la slice (`conventions.md:154` ya lo describe para métricas).
- Wolverine tiene deduplicación propia (`[Deduplicated]`), pero con `InvokeAsync<T>` no devuelve la respuesta original: "Replaying the *original response* of a deduplicated `InvokeAsync<T>` is Stripe-style idempotency-key machinery ... and is not part of this feature"; la versión con respuesta es de Wolverine.HTTP ([FUENTE: Idempotent Message Delivery, Scope and limitations](https://wolverinefx.net/guide/durability/idempotency.html#scope-and-limitations)). Tobi descartó Wolverine.HTTP en el 29.1. El filtro se queda.

### 3.5 Slices que hoy llaman `SaveChangesAsync` o usan `TransactionScope`

`grep -rn "SaveChangesAsync\|TransactionScope\|BeginTransactionAsync" backend/Features/{Auth,Users,Projects}`:

| Archivo | Qué hace | Qué cambia |
|---|---|---|
| `Auth/RegisterUser/RegisterUser.cs:61,78` | `CrossContextTransaction` + `SaveChangesAsync` | Salen los dos; comando atómico (C1) |
| `Auth/OpenSession/OpenSession.cs:62` | `SaveChangesAsync` de la sesión | Sale; lo hace Wolverine en `Lightweight` (sección 5) |
| `Auth/DecideAccountClosure/DecideAccountClosure.cs:33,55,58` | Consumidor del broker con transacción de EF | No cambia (queda fuera) |
| `Users/UpdateProfile/UpdateProfile.cs:41` | `SaveChangesAsync` | Sale; comando atómico |
| `Users/WithdrawAccountClosure/WithdrawAccountClosure.cs:51,61` | `CrossContextTransaction` + `SaveChangesAsync` | Salen los dos; comando atómico |
| `Users/DeleteUser/DeleteUser.cs:46,58,70` | `BeginTransactionAsync`, `CommitAsync`, `SagaOutbox` (que confirma) | Salen; `Eager` estándar (sección 5) |
| `Users/UsersModule.cs:30` | `SaveChangesAsync` del perfil (puerta de otra feature) | **Se queda**: lo llama `RegisterUser` desde Auth, y Wolverine no ve ese contexto. Con C1 se anota en la transacción ambiente |
| `Projects/CreateProject/CreateProject.cs:46` | `SaveChangesAsync` en `try`/`catch` del índice único | Se queda como "flush para traducir" (choque 1) |
| `Projects/UpdateProject/UpdateProject.cs:70` | `SaveChangesAsync` en `try`/`catch` de concurrencia y del índice | Se queda como "flush para traducir" (choque 1) |
| `Projects/ProjectsModule.cs:19` | `SaveChangesAsync` (puerta, la usa la saga) | No cambia |
| `Projects/ApplyTaskEvent`, `RebuildTaskCounters` | Consumidor del broker y servicio de arranque, con transacción propia | No cambian (quedan fuera) |

Sobre el "flush para traducir" (choque 1):
- Con C1, si el `INSERT` choca con el índice único, PostgreSQL aborta la transacción; la slice devuelve `NameTaken`, el frame corta antes del `SaveChangesAsync` de Wolverine y el scope se descarta sin `Complete()`: no se reintenta el `INSERT` y no queda nada `[INFERENCIA]`.
- Con `Eager` haría falta además limpiar el `ChangeTracker`: EF crea un savepoint antes de `SaveChanges` dentro de una transacción y vuelve a él si falla ([FUENTE: EF Core, Savepoints](https://learn.microsoft.com/en-us/ef/core/saving/transactions#savepoints)), pero la entidad sigue rastreada y el `SaveChangesAsync` de Wolverine la volvería a insertar `[INFERENCIA]`. Es otra razón para C1.

## 4. Test de "un fallo a mitad del handler no deja nada guardado"

- **Estilo del repo:** tests de integración con PostgreSQL real (Testcontainers) y el par de hosts de `ApiFixture` `[CÓDIGO backend/Tests/Support/ApiFixture.cs:7-24]`. Para meter un fallo se usa un cliente aislado con un interceptor de EF agregado por `ConfigureDbContext`, y después se cuentan filas en la base `[CÓDIGO backend/Features/Tasks/CreateTask/CreateTaskTests.cs:221-237]`. `FailAfterSaving<T>` tira **después** de que `SaveChanges` escribió una entidad del tipo pedido `[CÓDIGO backend/Tests/Support/FailAfterSaving.cs:7-50]`.
- **Test principal propuesto (`RegisterUserTests`)**: `CreateIsolatedClient(services => services.ConfigureDbContext<AuthDbContext>((_, o) => o.AddInterceptors(new FailAfterSaving<Session>())), out var sp)` `[CÓDIGO ApiFixture.cs:40]`; registrar un usuario; esperar 500; y en la base: ninguna fila en `auth."AspNetUsers"` ni en `users."UserProfiles"` con ese nombre. El `SaveChanges` de la sesión es el último de la slice y el perfil ya se guardó en otro contexto: si el perfil queda, la transacción no cruzó contextos. Hoy ese test pasa con el `TransactionScope` de la slice; tiene que seguir pasando sin él `[NO PROBADO]`.
- **Test de corte por error de negocio (`UpdateProjectTests`)**: renombrar a un nombre que ya existe con el mismo `Version` y verificar 409 `Project.NameTaken` y que el nombre en la base no cambió. Hoy pasa porque la slice no guarda; con C1 tiene que pasar por el frame `if (result.IsError) return;` `[NO PROBADO]`.
- **Test de la política** (`backend/Tests/Messaging/`, junto a `MediatorContractTests`): cada handler del monolito registrado es atómico, de `Eager` o sin transacción según su registro, leyendo `HandlerGraph` (`graph.HandlerFor(command)`, como hace `MediatorContractTests` `[CÓDIGO MediatorContractTests.cs:29-44]`) y el código generado versionado.
- **"El monolito arranca con todas las slices registradas"**: el mismo test de `MediatorContractTests` para el monolito: los tipos `*Command` de Auth, Users y Projects son exactamente los que tienen handler en `_fixture.Services`. `check-codegen.mjs` asegura que el código generado del monolito está al día `[CÓDIGO dune/check-codegen.mjs:6]`.
- Los tests HTTP existentes de cada slice no cambian: el contrato es el mismo (regla 3 no aplica).

## 5. Inventario: qué pasa con cada slice

Clases de comando que propongo (el registro lo dice, no un atributo en la slice):
- **Atómico** (C1): todo o nada, también ante un `ErrorOr` con error.
- **`Eager` estándar**: la configuración de Wolverine tal cual.
- **Sin transacción**: Wolverine en `Lightweight`, sin scope; guarda lo pendiente al final y no revierte nada.

| Feature/slice | HTTP | Hoy | Propuesta | Por qué |
|---|---|---|---|---|
| Auth/RegisterUser | `POST /auth/register` | clase + `TransactionScope` | handler **atómico** | dos contextos (2.1) |
| Auth/OpenSession | `POST /auth/login` | clase + `SaveChanges` | handler **sin transacción** | el intento fallido se guarda aunque la respuesta sea un error (choque 4); los cambios de Identity son hechos sueltos |
| Auth/CloseSession | `POST /auth/logout` | clase, un `ExecuteDelete` `[CÓDIGO backend/Features/Auth/Sessions.cs:54-55]` | handler **sin transacción** | una sola sentencia |
| Auth/CheckSession | `POST /internal/.../session-checks` | clase, lee y a veces toca o cierra la sesión `[CÓDIGO Sessions.cs:28-52]` | handler **sin transacción** | sentencias sueltas; lo llama el gateway en cada request y no conviene abrir un `TransactionScope` |
| Auth/IssueCsrfToken | `GET /auth/csrf` | solo endpoint, escribe una cookie `[CÓDIGO backend/Features/Auth/IssueCsrfToken/IssueCsrfTokenEndpoint.cs:5-11]` | **queda como está** | no hay caso de uso: es puro HTTP (cookie de antiforgery); DUNE0028 lo exime |
| Auth/DecideAccountClosure | (broker) | `IIntegrationEventHandler<T>` con transacción propia | **fuera** | consumidor del broker, con inbox propio; otro paso |
| Users/UpdateProfile | `PUT /users/me` | clase + `SaveChanges` | handler **atómico** | un contexto; spike del paso |
| Users/WithdrawAccountClosure | `POST /users/closures/{id}/cancel` | clase + `TransactionScope` | handler **atómico** | `FOR UPDATE` + `ExecuteDelete` + SQL crudo + filtro de idempotencia |
| Users/DeleteUser | `DELETE /users/me` | clase + transacción de EF + `SagaOutbox` | handler **`Eager` estándar** | arranca la saga de gracia: necesita la transacción de EF que usa el outbox de Wolverine (`SagaOutbox` tira si no hay `CurrentTransaction` `[CÓDIGO backend/Infrastructure/Messaging/SagaOutbox.cs:21-22]`); no tiene filtro, así que `Eager` no choca. `SagaOutbox` deja de confirmar: agrega la saga y publica por el `IMessageBus` de la cadena (3.3), y Wolverine guarda, confirma y envía `[NO PROBADO]` |
| Users/GetUser, GetAccountClosure, GetAccountClosureHistory | `GET` | clases | handlers **sin transacción** | lecturas (CQRS liviano, como Tasks en el 29.1) |
| Users/Cancel, Complete, Start, RemindAccountClosure, OrchestrateAccountClosure, ScheduleAccountClosure | (saga) | pasos de la saga | **fuera** | ya corren dentro de la transacción `Eager` de la saga |
| Users/PublishUserEvents | (outbox casero → broker) | `IEventHandler<T>` | **fuera** | lo llama `OutboxRelay` |
| Projects/CreateProject | `POST /projects` | clase + `SaveChanges` en `try` | handler **atómico** + flush para traducir | índice único (choque 1); filtro de idempotencia |
| Projects/UpdateProject | `PUT /projects/{id}` | clase + `SaveChanges` en `try` | handler **atómico** + flush para traducir | concurrencia e índice (choque 1); cambia la entidad antes de un error posible |
| Projects/GetProject, ListProjects | `GET` | clases | handlers **sin transacción** | lecturas |
| Projects/FindAssignableProject | `GET /internal/...` | clase | handler **sin transacción** | lectura que llama Tasks |
| Projects/ApplyTaskEvent, RebuildTaskCounters | (broker, arranque) | consumidor y servicio | **fuera** | no son HTTP; tienen su transacción |

Total: 15 handlers nuevos (5 atómicos, 1 `Eager`, 9 sin transacción). Cada uno con la forma del 29.1: `<Slice>Command.cs`, `<Slice>CommandValidator.cs` si valida, `<Slice>.cs` estática con un `Handler` público anidado, y el endpoint que despacha con `InvokeAsync<ErrorOr<T>>` (ADR-0004 punto 2). El comando lleva al que llama (`Caller` o el id), porque el handler corre en otro scope (ADR-0004 punto 9).

Lo que **no** migra y por qué, en una línea: los consumidores del broker y los pasos de la saga ya son de Wolverine o los llama Wolverine dentro de una transacción `Eager` que funciona; pasarlos a handlers del mediator es otro concepto (va a pendientes).

## 6. Dune

### 6.1 Reglas que cambian

- **DUNE0028 ("Endpoint de una slice que no despacha su comando por el mediator")**: su lista de features es `{ "Tasks" }` `[CÓDIGO dune/Dune.Analyzers/MediatorEndpointAnalyzer.cs:15]`, y su descripción ya dice que crece en el 29.2 `[CÓDIGO dune/Dune.Analyzers/DuneDiagnostics.cs:425-431]`. Pasa a `{ "Tasks", "Auth", "Users", "Projects" }`, con una excepción nombrada para `Auth/IssueCsrfToken` (endpoint sin caso de uso). Fixture válida: `CreateProjectEndpoint.cs` con `bus.InvokeAsync<ErrorOr<ProjectResponse>>(...)`. Violación deliberada: un endpoint de Users que recibe la clase de la slice y llama `Handle`.
- **DUNE0022 ("Llamada remota dentro de una transacción entre contextos")**: hoy mira solo el método que llama `CrossContextTransaction.Begin()` `[CÓDIGO dune/Dune.Analyzers/RemoteCallInTransactionAnalyzer.cs:14,107-109]`. Con C1 la transacción la abre Wolverine y cubre **todo** el handler atómico: una llamada remota en cualquier parte de ese `Handle` queda adentro. Extensión: en un handler registrado como atómico o `Eager`, es error cualquier llamada remota (las mismas que ya cuenta: `HttpClient`, cliente tipado, `SendRemoteAsync`). Hoy ninguna slice del monolito hace una, así que no rompe nada `[INFERENCIA]`. Fixture válida: `UpdateProfile`. Violación: un handler atómico de Projects que recibe `TaskCountsSnapshotClient`.
- **DUNE0025**: no cambia. El endpoint de una slice ya puede nombrar `IMessageBus` en cualquier feature (`DuneDiagnostics.cs`, descripción de DUNE0025). La política nueva vive en `Infrastructure/Messaging`, donde Wolverine está permitido.
- **DUNE0021 (`TransactionScope` solo en `CrossContextTransaction.cs`)**: no cambia; la política llama `CrossContextTransaction.Begin()`, no `new TransactionScope` `[CÓDIGO dune/Dune.Analyzers/PersistenceOwnershipAnalyzer.cs:15,119-128]`.

### 6.2 Regla nueva: DUNE0029 "La slice no maneja la transacción"

En `backend/Features/{Auth,Users,Projects}`, dentro de un tipo con método `Handle` registrado como handler del mediator (el `Handler` anidado de la slice):
1. es error llamar `CrossContextTransaction.Begin`, `DatabaseFacade.BeginTransaction(Async)`, `CommitTransaction(Async)`, `RollbackTransaction(Async)`, `UseTransaction(Async)`, `IDbContextTransaction.Commit(Async)`/`Rollback(Async)`, y crear un `TransactionScope`;
2. es error llamar `SaveChanges`/`SaveChangesAsync` de un `DbContext`, **salvo** cuando la llamada es la expresión de un `await` dentro de un `try` que tiene un `catch` de `DbUpdateException` (o una subclase) que devuelve un error (choque 1).

- Fuera de la regla: las puertas `<Feature>Module` (`UsersModule.CreateProfileAsync` guarda porque Wolverine no ve su contexto), los pasos de la saga, los consumidores del broker y los tests.
- Fixture válida: `UpdateProfile` sin `SaveChanges`; `CreateProject` con el `try`/`catch` de `ProjectNameUniqueness`.
- Violaciones deliberadas: (a) un handler que llama `SaveChangesAsync` suelto; (b) uno que abre `CrossContextTransaction.Begin()`; (c) uno que llama `Database.BeginTransactionAsync()`.
- Se puede sumar como un diagnóstico más de DUNE0028 (mismo analyzer, misma lista de features) o como analyzer propio. Recomiendo propio: es otro concepto (dueño de la transacción) y otra fila en `decisions.tsv`.
- Límite: no ve `SaveChanges` llamado a través de un servicio de la feature (por ejemplo `Sessions`); `Sessions` hoy no guarda con `SaveChanges` (usa `ExecuteUpdate`/`ExecuteDelete`) `[CÓDIGO Sessions.cs:46-55]`.

## 7. Propuesta en commits ordenados

Cada commit deja el build y los tests en verde. No hay contrato HTTP nuevo, así que no hace falta el orden "release" entre procesos del paso 27; el orden reduce riesgo.

1. **Spike en rojo con `UpdateProfile`.** La política `MediatorTransactionPolicy` mínima (frame del scope, `Lightweight`, corte por error y `Complete()`), `AutoApplyTransactions()` movido después de ella, `UpdateProfile` como handler atómico y un test que hace fallar el guardado y verifica que no cambió nada. Se lee el código generado en `backend/Internal/Generated/WolverineHandlers/`. Si el frame síncrono del `TransactionScope` no funciona, plan B: el mismo comando en `Eager` estándar (es un solo contexto) y una fila en `decisions.tsv`.
2. **Infraestructura completa.** Registro con la clase de transacción (`AddMediatorHandler<T>(MediatorTransaction.Atomic | Eager | None)` o tres métodos; el nombre lo decide el paso), el test de política (sección 4) y el test "el monolito arranca con todas las slices".
3. **Projects:** `CreateProject` y `UpdateProject` (atómicos con flush para traducir), `GetProject`, `ListProjects`, `FindAssignableProject`; DUNE0028 suma Projects.
4. **Users:** `WithdrawAccountClosure` (atómico; sale su `TransactionScope`), `GetUser`, `GetAccountClosure`, `GetAccountClosureHistory`, y `DeleteUser` en `Eager` con `SagaOutbox` sin commit; DUNE0028 suma Users.
5. **Auth:** `RegisterUser` (atómico; sale su `TransactionScope`) con el test de fallo a mitad entre contextos, `OpenSession`, `CloseSession`, `CheckSession`; DUNE0028 suma Auth con la excepción de `IssueCsrfToken`.
6. **Dune:** DUNE0029 nueva y DUNE0022 extendida, cada una con fixture válida y violación deliberada (regla 4).
7. **Docs:** ADR-0004 punto 9 ("el monolito también"; Wolverine dueño de la transacción) y su entrada en "Cambios"; `architecture.md:38` ("El monolito todavía llama a la clase de la slice directo"); `conventions.md:97` (quién abre la transacción, el flush para traducir, las tres clases de comando, el límite de `AfterCommit`); `decisions.tsv`; `progreso.md`; `plan.md` (choque 1 en el texto del paso).

E2E de Playwright: `NOT_RUN` salvo que cambie el frontend o un contrato; este paso no cambia ninguno (`AGENTS.md`, "Verificación").

## Decisiones (con mi recomendación)

| # | Decisión | Opciones | Recomiendo | Por qué |
|---|---|---|---|---|
| D1 | Transacción de los comandos del monolito | C1 ambiente abierta por Wolverine; C2 `Eager` + conexión compartida; C3 separar con mensajes; C4 excluir las de dos contextos | **C1** | la única que cumple plan, filtro de idempotencia y contrato (2.2) |
| D2 | Error de negocio (`ErrorOr` con error) | revierte todo; guarda lo pendiente (semántica de Wolverine: solo una excepción revierte) | **revierte** en atómicos | hoy una slice que devuelve error no guarda nada; `UpdateProject` cambia la entidad antes de un error |
| D3 | `SaveChangesAsync` para traducir restricciones | permitido solo en `try`/`catch` de `DbUpdateException`; traducir en el endpoint; aceptar 500 | **permitido en `try`/`catch`** | mantiene `409 NameTaken`/`Conflict` y la slice completa (ADR-0004) |
| D4 | `OpenSession`, `CheckSession`, `CloseSession` | sin transacción; atómicos | **sin transacción** | el contador de intentos fallidos tiene que sobrevivir a la respuesta de error (choque 4) |
| D5 | `DeleteUser` | `Eager` estándar con `SagaOutbox` sin commit; devolver la saga en cascada; atómico | **`Eager` + `SagaOutbox` sin commit** | respeta "el handler devuelve solo su respuesta" (choque 5) y muestra el modo estándar de Wolverine |
| D6 | Lecturas | handlers sin transacción; dejarlas como clases | **handlers** | el plan dice "las slices de Auth, Users y Projects"; Tasks ya migró sus lecturas en el 29.1 |
| D7 | DUNE0029 | analyzer propio; diagnóstico dentro de DUNE0028 | **propio** | otro concepto, otra fila en `decisions.tsv` |

Qué hace la industria: Wolverine empuja un `DbContext` por módulo y por handler, con `Eager` y `AutoApplyTransactions`, y pide código explícito para más de un contexto ([FUENTE: Modular Monolith](https://wolverinefx.net/tutorials/modular-monolith.html#with-ef-core)). Microsoft documenta dos formas de cruzar contextos: conexión compartida con `UseTransaction` y `TransactionScope`, y avisa que esta última depende del proveedor y no tiene commit asíncrono ([FUENTE: EF Core, Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)). C1 usa la segunda, que este repo ya eligió en el paso 22.1.

## Pendientes para `progreso.md`

- Los consumidores del broker (`DecideAccountClosure`, `ApplyTaskEvent`) y los pasos de la saga como handlers de Wolverine, sin `IIntegrationEventHandler<T>`.
- Separar `RegisterUser` en dos módulos con un mensaje (C3), si algún día Users sale del monolito.
- Revisar `OpenSession`: si el contador de intentos y la sesión deberían ser dos comandos.
- `AfterCommit` dentro de una transacción ambiente (3.1).

## Verificado / No verificado

**Verificado leyendo** (sin correr nada):
- Detección de `DbContext` de `AutoApplyTransactions`, el corte en tipos `internal` y registros con fábrica, el código de `Eager` y `Lightweight`, la etiqueta de modo por cadena y `TransactionalAttribute.Modify` público: código de Wolverine `V6.64.2` y JasperFx `v2.102.0`.
- `InvokeAsync<T>` ejecuta la misma clase generada, y el inbox no escribe fila sin `Destination`: `Executor.cs`, `EfCoreEnvelopeTransaction.cs`.
- EF Core tira con `BeginTransaction` dentro de un `TransactionScope`: `RelationalConnection.cs` `v10.0.0`; savepoints y `TransactionScope` en la documentación de EF Core; `Complete()` en la documentación de .NET; reuso de conexión de Npgsql en su documentación.
- Repo: contextos de cada slice, registros de los módulos, filtro de idempotencia, reglas de reintento, código generado de la saga y de Tasks, DUNE0021/0022/0025/0028.

**No verificado** (queda para el paso):
- Que el frame síncrono del `TransactionScope` deje la transacción ambiente viva en el método generado, y el orden entre la política nueva y `AutoApplyTransactions` (2.3). Es el commit 1.
- Que `UserManager<AppIdentityUser>` haga que Wolverine vea `AuthDbContext` (1.2); no cambia la recomendación.
- Que `SagaOutbox` sin commit, publicando por el `IMessageBus` de la cadena, guarde y envíe los plazos de la saga en la transacción `Eager` de `DeleteUser` (5).
- Que Npgsql confirme sin error al descartar un scope cuya transacción PostgreSQL quedó abortada por una violación de índice (3.5).
- El costo de `Lightweight` en lecturas (un `SaveChangesAsync` sin cambios por request): creo que EF no va a la base, no lo medí.
