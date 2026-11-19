# Paso 24.1: modelo de lectura alimentado por eventos (investigación)

- Fecha: 2026-11-19.
- Estado: **Listo** como investigación. No hace falta ninguna herramienta, librería ni tecnología nueva. Hay 8 choques con los docs y 14 decisiones, cada una con mi recomendación.
- Alcance: el paso 24.1 del [plan](../plan.md) (`plan.md:423-427`). Concepto: proyección con consistencia eventual; la lectura vive en su propia tabla y se pone al día después de la escritura, a partir de los eventos del paso 24.
- Base: `main` en `e4738b0` (merge del PR #85, paso 24).
- Límites con los pasos vecinos: el broker, la DLQ (cola de mensajes muertos), los mensajes venenosos y la librería de mensajería son del paso 25, y la librería es una decisión de Tobi pendiente (`plan.md:431`). Los reintentos con circuit breaker son del paso 29. Este paso no adelanta ninguno (regla 8).

**Cómo leer las marcas:**
- `[FUENTE]`: lo leí en un documento primario. Cada uno tiene su link.
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `e4738b0`.
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Respuesta corta

1. **Qué cambia para el usuario.** Hoy, si Tasks está caído, `GET /api/v1/projects` y `GET /api/v1/projects/{id}` responden 503 enteros, porque cada lectura le pide los contadores a Tasks por HTTP (`ProjectResponses.cs:29`, `TaskCountsClient.cs:21`). Después del paso, las dos lecturas responden 200 con los contadores que el monolito ya tiene guardados. El precio: si creás una tarea y enseguida abrís "Mis proyectos", el contador puede mostrar el número de antes durante un segundo o dos.
2. **Dónde vive la lectura.** En una tabla nueva de Projects, `projects."ProjectTaskCounters"` (un contador de tareas y uno de completadas por proyecto), dentro de `ProjectsDbContext`. La lectura une `Projects` con esa tabla en una consulta y sigue pidiendo el nombre del dueño a Users, en el mismo proceso.
3. **Cómo llegan los eventos de un proceso al otro (pregunta 1).** Recomiendo la opción **(a), empuje por HTTP**: un consumidor nuevo en Tasks (`IEventHandler<T>`, llamado por el `OutboxRelay` del paso 24) manda cada evento a un endpoint interno del monolito, `PUT /internal/v1/task-events/{messageId}`. El monolito aplica el evento y deduplica con su propio inbox (`projects."InboxMessages"`). Es la única opción que saca a Tasks de la lectura y además reusa el relay, los reintentos, las métricas y el inbox del paso 24.
4. **Duplicados y desorden.** El efecto de cada evento es sumar o restar (`+1`, `-1`). Sumar no depende del orden, así que dos eventos que llegan al revés terminan en el mismo número. El inbox evita que un duplicado sume dos veces.
5. **Reconstrucción desde cero.** No hay un almacén de eventos (el outbox se borra a los 7 días), así que no se puede "reproducir la historia". La reconstrucción pide a Tasks una **foto** de los contadores y, en la misma foto, la lista de eventos que todavía no se entregaron. Esos eventos se anotan como "ya aplicados" en el inbox del monolito, porque su efecto ya está en la foto. Los que nacen después se aplican normal.
6. **Herramientas.** Ninguna nueva: EF Core, Npgsql, `IHttpClientFactory`, `BackgroundService`, advisory locks de PostgreSQL y Playwright ya están.

## Glosario corto

- **Proyección** (= modelo de lectura, = vista materializada armada por la app): una tabla derivada de otros datos, armada para una consulta. Azure: "a *materialized view* is a read model or projection that persists data derived from one or more source stores" ([FUENTE: Azure, Materialized View](https://learn.microsoft.com/en-us/azure/architecture/patterns/materialized-view)).
- **Consistencia eventual**: la lectura puede mostrar un dato viejo un rato y después se pone al día. "the read data might not show the most recent changes immediately. This delay results in stale data" ([FUENTE: Azure, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)).
- **Descartable**: la proyección se puede borrar y volver a armar desde la fuente. "a materialized view and the data it contains are completely disposable because they can be entirely rebuilt from the source data stores" ([FUENTE: Azure, Materialized View](https://learn.microsoft.com/en-us/azure/architecture/patterns/materialized-view)).
- **Event-carried state transfer** (= el receptor guarda su propia copia): Fowler lo define como avisar a otros sistemas "in such a way that they don't need to contact the source system", y el receptor "can then update it's own copy" ([FUENTE: Fowler, What do you mean by "Event-Driven"?](https://martinfowler.com/articles/201701-event-driven.html)).
- **Inbox**: la tabla del paso 24 donde un consumidor anota cada `MessageId` que ya aplicó, en la misma transacción que el efecto (`Inbox.cs:8-28`).

---

## 1. Transporte entre procesos sin broker (choque 4 del paso 24)

### 1.1 Cómo está hoy

- Los eventos nacen en Tasks: `TaskItem` anota `TaskCreated`, `TaskCompleted`, `TaskReopened` y `TaskDeleted` (`TaskItem.cs:27-59`), y `OutboxInterceptor` los guarda en `tasks."OutboxMessages"` en el mismo `SaveChanges` (`OutboxInterceptor.cs:63-77`).
- El relay corre **dentro del proceso de Tasks** (`TasksFeature.cs:18`, `AddOutbox<TasksDbContext>()`). Llama a cada `IEventHandler<T>` registrado en ese proceso, con el record de C# deserializado (`OutboxRelay.cs:121-130`).
- La lectura que hay que reemplazar vive en el **monolito**: `ListProjects` y `GetProject` llaman a `ProjectResponses.ComposeAsync`, que pide los contadores a Tasks por `GET /internal/v1/task-counts` (`ProjectResponses.cs:24-30`, `TaskCountsClient.cs:15-27`) y el nombre del dueño a `IUsersModule` (`ProjectResponses.cs:37-39`).
- Los dos procesos comparten una base, con un schema por feature (`architecture.md:50`). El monolito no compila el código de Tasks (`architecture.md:7`, regla "límite de despliegue" de `check-structure.mjs`).
- No hay consumidor de producción: el relay marca procesados los eventos sin consumidor (`architecture.md:100`).

Entonces el evento nace en un proceso y la proyección vive en otro. El relay del paso 24 solo sabe entregar dentro de su proceso.

### 1.2 Opciones

| Opción | Cómo | ¿Saca a Tasks de la lectura? | ¿Reusa el paso 24? | ¿Sobrevive al paso 25? |
|---|---|---|---|---|
| **(a) Empuje por HTTP** (recomendada) | Un consumidor en Tasks (`TaskEventForwarder : IEventHandler<...>`) manda cada evento con `PUT /internal/v1/task-events/{messageId}` al monolito. El monolito aplica el efecto y deduplica con `Inbox.TryAcceptAsync` en `projects."InboxMessages"` | Sí: la lectura solo lee tablas de Projects | Todo: relay, `SKIP LOCKED`, reintentos, `FailedAt`, métricas de lag, link de traza, inbox | Sí, la forma: en el 25 el relay publica al broker en vez de llamar por HTTP, y el monolito consume de una cola con el mismo inbox y el mismo efecto. Se tira solo el reenvío por HTTP |
| (b) El monolito tira de un feed | Tasks expone `GET /internal/v1/task-events?after=<posición>`; un `BackgroundService` del monolito lo consulta cada segundo y guarda la posición en la misma transacción que el efecto | Sí | Solo la tabla del outbox. El relay y el inbox quedan sin uso | Poco: el 25 reemplaza el feed por el broker. Se tira el feed y el lector |
| (c) Proyección en Tasks | Tabla `tasks."ProjectTaskCounters"` y consumidor dentro de Tasks; Projects sigue llamando por HTTP a un endpoint que lee esa tabla | **No**: la lista y el detalle siguen en 503 con Tasks caído | Todo, sin transporte nuevo | Sí, pero no enseña nada del cruce |
| (d) El monolito lee `tasks."OutboxMessages"` | Un contexto del monolito mapea o consulta con SQL la tabla del outbox de Tasks | Sí | La tabla | No |

**Gana y pierde de cada una:**

- **(a)** Gana: el evento sale solo después del commit, porque lo entrega el relay, que lee filas confirmadas (DUNE0023 sigue valiendo). Es la topología del paso 25 (el productor empuja, el consumidor es idempotente con inbox), que Azure describe para CQRS con dos bases: "have the write model publish events when it updates the database [...] Use the Transactional Outbox pattern [...] and make the read-model consumer idempotent to tolerate duplicate delivery" ([FUENTE: Azure, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)). Pierde: Tasks pasa a depender del monolito para *entregar* (no para responder requests). Con el monolito caído más de unos 30 s, el relay agota sus 5 intentos (esperas de 2, 4, 8 y 16 s, `OutboxRelay.cs:151-159`) y la fila queda con `FailedAt`: la proyección quedaría mal para siempre. Ver decisión 3. Además, la llamada HTTP corre mientras el relay tiene las filas tomadas con `FOR UPDATE` (`OutboxRelay.cs:86-92`): con el monolito colgado, un ciclo puede tardar hasta 50 × 2 s [INFERENCIA, por `BatchSize` 50 y timeout de 2 s, `OutboxOptions.cs:15`, `RemoteServiceOptions.cs:9`]. Otra instancia de Tasks salta esas filas (`SKIP LOCKED`) y las escrituras de tareas no se bloquean, porque insertan filas nuevas.
- **(b)** Gana: si el monolito se cae una hora, al volver sigue desde su posición, sin agotar intentos. Pierde: la posición no puede ser el `Id` del outbox. El paso 24 midió que el `Id` se asigna al insertar y no al confirmar, y que un "último id leído" pierde la fila de una transacción lenta (EX-D, `paso-24-outbox-transaccional.md:153-157`). Dudycz resuelve eso con una columna `xid8` y el filtro `transaction_id < pg_snapshot_xmin(pg_current_snapshot())` ([FUENTE: Dudycz, ordering in Postgres outbox](https://event-driven.io/en/ordering_in_postgres_outbox/)): es una migración más en el outbox de Infrastructure, un feed y un lector nuevos, y el relay y el inbox del 24 quedan sin uso. Richardson anota el mismo riesgo para leer el outbox por sondeo: "Tricky to publish events in order" ([FUENTE: microservices.io, Polling publisher](https://microservices.io/patterns/data/polling-publisher.html)). Si el monolito está caído más de 7 días, el outbox ya borró filas que no leyó y hay que reconstruir.
- **(c)** Gana: cero transporte nuevo. Pierde: no cumple lo que el paso promete. `progreso.md:237` dice que el 24.1 "reemplaza esta lectura por una proyección que no llama a Tasks", y con (c) Projects sigue llamando a Tasks.
- **(d)** Pierde: rompe la propiedad de los datos. Cada feature mapea solo sus tablas en su contexto (`conventions.md:92`, DUNE0021), y el SQL crudo contra la tabla de otra feature es el "límite conocido" que frena la revisión (`conventions.md:96`). Además acopla el monolito al formato interno del outbox de Tasks. Richardson anota del esquema compartido: "Development time coupling" y "Runtime coupling" ([FUENTE: microservices.io, Shared database](https://microservices.io/patterns/data/shared-database.html)).

### 1.3 Recomendación

**(a), empuje por HTTP**, con dos ajustes:

- **Más intentos en el outbox de Tasks** (decisión 3): `Outbox:MaxAttempts = 12` en `Hosts/Tasks/appsettings.json`. Con la espera de 2^n segundos que ya existe, la fila aguanta unos 68 minutos de monolito caído antes de `FailedAt` (2 + 4 + … + 2048 s = 4094 s) [INFERENCIA, por `OutboxRelay.cs:151-159`]. Al volver el monolito, el atraso es como mucho otro tanto como duró la caída. Es configuración, sin código nuevo: el validador acepta de 1 a 20 (`OutboxOptions.cs:17-18`). Si igual se agotan, la reconstrucción (sección 4) arregla la proyección.
- **Distinguir "destino caído" de "mensaje roto"** (no reintentar sin límite lo primero y mandar lo segundo a una cola de muertos) **no** va en este paso: es el concepto "mensajes venenosos" del paso 25 (`plan.md:429`). Lo anoto como pendiente.

### 1.4 Cómo se ve la opción (a) en el código

| Pieza | Lugar | Qué hace |
|---|---|---|
| `TaskEventForwarder` | `Features/Tasks/ForwardTaskEvents/` | `IEventHandler<TaskCreated>`, `<TaskCompleted>`, `<TaskReopened>` y `<TaskDeleted>`. Arma el sobre (nombre estable y versión con `OutboxEvents.For`, `MessageId`, `OccurredAt` del `EventContext`, payload en JSON) y llama a `TaskEventsClient` |
| `TaskEventsClient` | misma carpeta | Cliente tipado, `AddRemoteClient<TaskEventsClient>(MonolithService)`, como `ProjectsClient` (`TasksFeature.cs:27`). `PUT internal/v1/task-events/{messageId}` con `SendRemoteAsync`, que espera 204 |
| `ApplyTaskEvent` | `Features/Projects/ApplyTaskEvent/` | Slice interna: `ApplyTaskEventEndpoint` (mapeado en `MapProjectsInternalEndpoints`), `TaskEventRequest` (su copia del sobre) y la operación que aplica el efecto (sección 3) |
| `InboxMessages` en `projects` | `ProjectsDbContext.OnModelCreating` llama a `MapInbox()` | Migración nueva de Projects. `MapInbox` ya existe (`MessagingTables.cs:14-19`) |

Por qué `PUT` y con el `MessageId` en la ruta:

- `PUT` es idempotente por definición: "the intended effect on the server of multiple identical requests with that method is the same as the effect for a single such request" ([FUENTE: RFC 9110, 9.2.2](https://www.rfc-editor.org/rfc/rfc9110.html#section-9.2.2)). Es exactamente lo que hace el inbox.
- DUNE0016 exige `WithIdempotency<TRequest>()` en todo `MapPost` de una feature fuera de Auth (`IdempotentPostAnalyzer.cs:12,30-40`). El filtro de idempotencia lee el usuario de la cookie para su clave (`IdempotencyFilter.cs`, `GetCallerId`), y un endpoint interno no tiene cookie (`conventions.md:27`). Con `PUT` la regla no aplica y no hay que inventar una excepción [INFERENCIA].

Quién escribe el contrato: `conventions.md:27` dice que lo escribe quien llama. Acá llama Tasks, así que Tasks escribe su DTO y su cliente, y el monolito escribe su propia copia del request (`TaskEventRequest`) y de cada payload que lee. Ninguno compila el código del otro (`architecture.md:60`).

La traza cruza sola: el relay abre un span `Consumer` por mensaje (`OutboxRelay.cs:111-117`), y "the ASP.NET web server and System.Net.Http natively understand how to decode and encode Activity IDs on HTTP messages" ([FUENTE: Learn, distributed tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)). Entonces el span del servidor en el monolito cuelga del span del relay [INFERENCIA; NO PROBADO en el repo].

## 2. Forma de la tabla y de quién es

### 2.1 Opciones

| Opción | Cómo | Gana | Pierde |
|---|---|---|---|
| **T1. Tabla de contadores en Projects** (recomendada) | `projects."ProjectTaskCounters"`: `ProjectId` (PK y FK a `Projects` con borrado en cascada), `TaskCount int`, `CompletedTaskCount int`, `UpdatedAt timestamptz` | Una fila por proyecto. La lectura une dos tablas de su propia feature en una consulta. Sumar y restar no depende del orden (sección 3) | Un contador puede quedar negativo un rato si los eventos llegan al revés (3.3) |
| T2. Columnas en `projects."Projects"` | `TaskCount` y `CompletedTaskCount` en la entidad `Project` | Sin tabla nueva | `Project.Version` es la versión de fila de PostgreSQL (`ProjectConfiguration.cs:39-40`, `IsRowVersion`), que es `xmin`: "this value automatically gets updated every time the row is changed" ([FUENTE: Npgsql, concurrency tokens](https://www.npgsql.org/efcore/modeling/concurrency.html)). Cada tarea nueva cambiaría la versión, y el `PUT` del proyecto que el usuario tenía abierto respondería 409. Además `Project` es `IAuditable`: cada contador dejaría una fila de auditoría, y DUNE0017 prohíbe el `ExecuteUpdate` que suma en una sentencia |
| T3. Una fila por tarea | `projects."ProjectTasks"` (`TaskId`, `ProjectId`, `IsCompleted`) y la lectura agrupa | Se puede comparar tarea por tarea | Completar y reabrir **sí** dependen del orden, y los payloads no traen un número de versión de la tarea (`TaskCompleted.cs`, `TaskReopened.cs`): dos eventos al revés dejan el estado final mal. Crece con cada tarea |
| T4. Schema o contexto aparte para proyecciones | `projections."..."` con otro `DbContext` | Separa lectura de escritura también en la base | Rompe "a lo sumo un contexto por feature" (`conventions.md:92`, DUNE0021) |

Recomiendo **T1**. Detalles:

- **Entidad sin reglas**: `ProjectTaskCounters` vive en `Features/Projects/Persistence/` con su configuración, no en `Domain/`. No es `IAuditable`: es un dato derivado y descartable [INFERENCIA, por la cita de Azure del glosario].
- **FK con cascada**: las FKs dentro de una feature siguen permitidas (`conventions.md:93`). Si se borra un proyecto, su contador se borra con él, y `DeleteOwnedProjectsAsync` (`ProjectsModule.cs:16-20`) no cambia.
- **Alta**: `CreateProject` agrega el proyecto y su fila `0/0` en el mismo `SaveChanges` (`CreateProject.cs:42-45`). Así, cuando Tasks crea la primera tarea, la fila ya existe: Tasks pregunta antes si el proyecto existe (`FindAssignableProject.cs:16-20`), y esa respuesta solo ve proyectos confirmados.
- **Expand puro**: tabla nueva y nada se borra ni se renombra; la migración se genera con `dotnet ef migrations add AddProjectTaskCounters --context ProjectsDbContext` (`conventions.md`, migraciones). Sin `CONCURRENTLY`: la tabla nace en la misma migración (`conventions.md:136`).

### 2.2 El nombre del dueño

| Opción | Gana | Pierde |
|---|---|---|
| **N1. Sigue como lectura en el proceso** con `IUsersModule.FindDisplayNamesAsync` (recomendada) | Users está en el mismo proceso que Projects (`architecture.md:7`): no hay dependencia remota que sacar. No cambia nada | La lectura sigue costando dos consultas (Projects y Users) |
| N2. Proyectar también el nombre | Una sola consulta | Users no tiene outbox ni eventos. Habría que sumar `UserProfileRenamed`, el outbox de Users y otro consumidor: es otro paso (regla 7) |

Recomiendo **N1**. El plan pide reemplazar la parte que junta *otros procesos*; el nombre del dueño ya es local.

## 3. Idempotencia, orden y casos raros

### 3.1 Qué eventos hay y qué traen

[CÓDIGO `Features/Tasks/Domain/Events/*.cs`, `TaskItem.cs:27-59`]

| Evento | Payload (JSON camelCase, `OutboxInterceptor.cs:87`) | Efecto en `ProjectTaskCounters` |
|---|---|---|
| `tasks.task-created` v1 | `taskId`, `ownerId`, `projectId?`, `createdAt` | `TaskCount + 1` |
| `tasks.task-completed` v1 | `taskId`, `ownerId`, `projectId?`, `completedAt` | `CompletedTaskCount + 1` |
| `tasks.task-reopened` v1 | `taskId`, `ownerId`, `projectId?`, `reopenedAt` | `CompletedTaskCount - 1` |
| `tasks.task-deleted` v1 | `taskId`, `ownerId`, `projectId?`, `wasCompleted` | `TaskCount - 1` y, si `wasCompleted`, `CompletedTaskCount - 1` |

Una tarea no cambia de proyecto: `TaskItem.Update` recibe título y estado, no `ProjectId` (`TaskItem.cs:41`). Por eso no hace falta un evento de "tarea movida".

### 3.2 Cómo se aplica uno

Dentro de `ApplyTaskEvent`, en una transacción de `ProjectsDbContext` (`Database.BeginTransactionAsync`, la forma para un contexto, `conventions.md:96`):

1. `SELECT pg_try_advisory_xact_lock_shared(<clave de la proyección>)`. Si devuelve `false`, hay una reconstrucción en curso (sección 4): responde 503 y el relay reintenta. "a shared lock does not conflict with other shared locks on the same resource, only with exclusive locks" ([FUENTE: PostgreSQL, advisory lock functions](https://www.postgresql.org/docs/current/functions-admin.html)). La clave es otra que la del arranque (`DatabaseInitializer.cs:10`).
2. `Inbox.TryAcceptAsync` con `Consumer = "projects.task-counters"`. Si devuelve `false`, el evento ya se aplicó: confirma y responde 204 sin efecto (`Inbox.cs:8-28`).
3. Si el payload no tiene `projectId`, no hay efecto.
4. `ExecuteUpdateAsync` sobre la fila del proyecto con `SetProperty(c => c.TaskCount, c => c.TaskCount + delta)`. EF lo traduce a una sola sentencia `SET x = x + 1` ([FUENTE: EF Core, ExecuteUpdate](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)), así que dos eventos simultáneos del mismo proyecto no se pisan [INFERENCIA]. DUNE0017 no lo frena porque la entidad no es `IAuditable` (`conventions.md:101`).
5. Si actualizó 0 filas, el proyecto no existe (se borró, o el id no es de un proyecto): log `Information` con el `MessageId` y sin efecto.
6. Confirma y responde 204.

El inbox va en la misma transacción que el efecto, como pide Richardson ([FUENTE: microservices.io, Idempotent consumer](https://microservices.io/patterns/communication-style/idempotent-consumer.html)) y como ya hace el paso 24 (`conventions.md:109`).

### 3.3 Cada caso

| Caso | Qué pasa | Por qué |
|---|---|---|
| **Duplicado** (el relay se cae después del 204 y antes de confirmar `ProcessedAt`) | El segundo `PUT` encuentra la fila del inbox y no suma | Clave `(MessageId, Consumer)` (`InboxMessageConfiguration.cs:10`) |
| **Desorden** (`TaskCompleted` antes que `TaskCreated`; `TaskDeleted` antes que `TaskCreated`) | El número final es el mismo | Sumar es conmutativo: el resultado final es la suma de todos los deltas, en cualquier orden [INFERENCIA]. El desorden existe: con dos relays (`paso-24-outbox-transaccional.md:151`) o cuando un mensaje falla y el siguiente del lote sale bien (`OutboxRelay.cs:88-89`). Azure: "The order in which consumer service instances receive messages isn't guaranteed [...] Design the system so that it processes messages idempotently" ([FUENTE: Azure, Competing Consumers](https://learn.microsoft.com/en-us/azure/architecture/patterns/competing-consumers)) |
| **Valores negativos un rato** | `TaskDeleted` antes que `TaskCreated` deja `-1` hasta que llega el otro | La respuesta HTTP muestra `max(0, …)` y `completed ≤ total`; la tabla guarda el número crudo, para que converja. No va un `CHECK >= 0`: frenaría la convergencia [INFERENCIA] |
| **`WasCompleted`** | `TaskDeleted` resta de completadas solo si la tarea estaba completada al borrarse | Lo anota la entidad en el momento del borrado (`TaskItem.cs:58-59`). Con eventos al revés la suma igual cierra: Created(+1,0), Completed(0,+1), Deleted(−1,−1) da 0/0 en cualquier orden |
| **Proyecto que la proyección no conoce** | 0 filas actualizadas, sin efecto | Toda fila nace con su proyecto (2.1). Entonces "no existe" quiere decir "se borró": `DeleteUser` borra las tareas por HTTP **antes** que los proyectos (`DeleteUser.cs:40,44`), y sus `TaskDeleted` pueden llegar después. No se crea la fila: sería un huérfano |
| **Proyectos de antes de la migración** | No tienen fila hasta la reconstrucción inicial (4.4) | Sus eventos se ignoran hasta entonces, pero la reconstrucción parte de la fuente de verdad y los incluye |
| **Tarea sin proyecto** | Sin efecto | `projectId` null |
| **Tipo desconocido** (un evento futuro, como un `TaskRenamed`) | 204 sin efecto | Lector tolerante: "Design event consumers to ignore unknown fields" ([FUENTE: Azure, Event Sourcing](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing)) |
| **Tipo conocido con versión desconocida** | 400: fuera de contrato; el relay lo cuenta como fallo y termina en `FailedAt`, visible | `SendRemoteAsync` tira `InvalidOperationException` ante un status fuera del contrato (`RemoteServices.cs:45-54`). Un v2 lo tiene que entender el monolito antes de que Tasks lo emita (expand/contract de eventos, `conventions.md:108`) |

## 4. Reconstrucción desde cero sin almacén de eventos

### 4.1 Qué hace la industria

- **Con event sourcing**, la proyección se rearma reproduciendo todos los eventos: "you can easily regenerate materialized views [...] by replaying historical events" ([FUENTE: Azure, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)).
- **Con historia larga**, se parte de una foto y se reproduce lo que vino después: "load the most recent snapshot and replay only the events that occur after it" ([FUENTE: Azure, Event Sourcing](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing)).
- **Una vista materializada** se rearma desde la fuente, y conviene que los lectores sigan viendo la versión completa anterior mientras tanto: "continues to serve and use the last complete view while you build and validate the new view" ([FUENTE: Azure, Materialized View](https://learn.microsoft.com/en-us/azure/architecture/patterns/materialized-view)).

Este repo no tiene almacén de eventos: la fuente de verdad es `tasks."TaskItems"` y el outbox borra lo procesado a los 7 días (`OutboxRelay.cs:173-186`). Entonces corresponde la segunda forma: **foto de la fuente + posición**.

### 4.2 El problema: qué eventos ya están en la foto

Si la foto de Tasks dice "proyecto P tiene 3 tareas", algunos eventos pendientes del outbox ya están contados en ese 3. Si después llegan al monolito, sumarían dos veces. Y no sirve "todo lo que tenga `Id` mayor que X": el `Id` no sigue el orden de commit (EX-D del paso 24).

La posición, entonces, no es un número: es **el conjunto de `MessageId` que todavía no se entregaron, leído en la misma foto que las tareas**. Funciona porque la fila del outbox y el cambio de la tarea se confirman juntos (`OutboxInterceptor`, mismo `SaveChanges`):

- Un evento visible en la foto tiene su efecto en la foto. Si todavía no se entregó, se anota como aplicado en el inbox y, cuando llegue, se salta.
- Un evento ya procesado (`ProcessedAt` confirmado) nunca vuelve a salir.
- Un evento confirmado después de la foto no está en la foto ni en el conjunto: se aplica normal.

La foto tiene que ser una sola: en PostgreSQL, una transacción `REPEATABLE READ` "sees a snapshot as of the start of the first non-transaction-control statement in the transaction [...] successive SELECT commands within a single transaction see the same data" ([FUENTE: PostgreSQL, Transaction Isolation](https://www.postgresql.org/docs/current/transaction-iso.html)). El default es `Read Committed` (misma fuente), así que hay que pedirla.

### 4.3 El algoritmo

**En Tasks**, slice interna nueva `SnapshotTaskCounts`, `GET /internal/v1/task-count-snapshot`:

1. Abre una transacción `RepeatableRead` de `TasksDbContext`.
2. Lee los contadores de **todos** los proyectos (`GROUP BY ProjectId`, como `CountTasksByProject.cs:24-28` pero sin la lista de ids).
3. Lee los `MessageId` del outbox con `ProcessedAt IS NULL` (incluye los `FailedAt`: su efecto está en la foto y alguien podría reintentarlos a mano).
4. Responde `{ counts: [...], undeliveredMessageIds: [...] }`.

**En el monolito**, operación `RebuildTaskCounters` (Projects):

1. Abre una transacción de `ProjectsDbContext` y toma `pg_advisory_xact_lock(<clave>)`, exclusivo. Desde ahí, cada `PUT` de un evento responde 503 (3.2, paso 1) y el relay lo reintenta después. Los lectores siguen leyendo la versión vieja hasta el commit: no toman el lock.
2. Pide la foto a Tasks con su cliente tipado.
3. Borra las filas de `ProjectTaskCounters` y las del inbox con `Consumer = "projects.task-counters"`.
4. Inserta una fila por **cada proyecto de `projects."Projects"`**, con los contadores de la foto o `0/0`.
5. Inserta en el inbox los `undeliveredMessageIds`.
6. Confirma. Los lectores pasan a ver la versión nueva de una vez, que es la "atomic publication" que pide Azure (4.1).

El lock se toma **antes** de pedir la foto. Al revés hay una ventana: un evento confirmado después de la foto y aplicado antes del lock quedaría borrado por el paso 3 y no volvería [INFERENCIA]. El precio es una llamada HTTP con una transacción abierta. DUNE0022 no la ve, porque mira solo `CrossContextTransaction` (`DuneDiagnostics.cs:361-368`), pero va contra su espíritu: la transacción sostiene una conexión y un lock mientras espera. Acá es deliberado y corto (timeout de 2 s, `architecture.md:72`), y es una operación de mantenimiento, no de cada request. Lo anoto en el código y en `architecture.md`.

### 4.4 Cuándo corre

| Opción | Gana | Pierde |
|---|---|---|
| **R1. Al arrancar el monolito, solo si falta** (recomendada) | Un `BackgroundService` de Projects corre la reconstrucción si hay algún proyecto sin fila de contadores. Con FK y alta atómica (2.1), eso pasa solo con proyectos de antes de la migración: es el backfill del despliegue. Si Tasks no responde, reintenta cada 30 s | Hasta que termine, un proyecto viejo se lee con `0/0` (ver 4.5) |
| R2. Siempre al arrancar | Se repara solo en cada despliegue, también si quedaron filas `FailedAt` | Esconde errores de la proyección; bloquea los eventos en cada reinicio |
| R3. Endpoint de administración | El operador lo corre cuando quiere | Endpoint nuevo; el `POST` pide idempotencia (DUNE0016) y no hay rol de operador para endpoints internos |

Recomiendo **R1**, y anotar como pendiente un disparo manual (por ejemplo, borrar una fila de contadores fuerza la reconstrucción en el próximo arranque).

### 4.5 Despliegue: expand/contract de la lectura

`conventions.md:118` pide tres commits para reemplazar una lectura: un test que fija lo de hoy, la lectura nueva con un test que compara la vieja (copiada en el test) contra la nueva, y el borrado de ese test. Recomiendo que el PR los siga, y que el primer release **no** cambie la lectura:

1. **Release A (expand)**: tabla, inbox de Projects, `ApplyTaskEvent`, `TaskEventForwarder`, `SnapshotTaskCounts`, `RebuildTaskCounters` y el arranque de R1. La lectura sigue llamando a Tasks. Al terminar el despliegue, la proyección está llena y al día.
2. **Release B (cambio de lectura)**: `ProjectResponses` lee la tabla; el test comparativo (Verifica 1) prueba que dan lo mismo.
3. **Release C (contract)**: se borra el test comparativo, `TaskCountsClient` y sus DTOs, y después `CountTasksByProject` en Tasks (sección 5).

Así nunca hay un release que lea `0/0` para un proyecto viejo [INFERENCIA, por `conventions.md` "Migraciones: expand/contract"].

## 5. Qué pasa con `/internal/v1/task-counts` y `TaskCountsClient`

| Opción | Gana | Pierde |
|---|---|---|
| **B1. Borrarlos en el release C** (recomendada) | "Migrar a los que llaman y después borrar". Ningún código de producción los usa después del release B. La foto de la reconstrucción es otra cosa: todos los proyectos, más los `MessageId`, en una sola transacción | Durante un despliegue por tandas, un monolito del release A contra un Tasks del release C respondería 503 en la lectura. Orden obligatorio: primero el monolito, después Tasks |
| B2. Dejarlos para la reconstrucción | Menos cambios | `CountTasksByProject` acepta de 1 a 100 ids (`CountTasksByProject.cs:9,20`) y no devuelve los `MessageId` pendientes: no sirve para una foto correcta (4.2). Quedaría código muerto |

Recomiendo **B1**. Además cambia `smoke-api.mjs:329`, que usa `/internal/v1/task-counts` para probar que el gateway no publica rutas internas: cualquier ruta interna sirve (el gateway responde 404 por no tener ruta `/internal`, `architecture.md:69`), por ejemplo `/internal/v1/task-count-snapshot`.

## 6. Contrato HTTP y frontend (regla 3)

### 6.1 La forma no cambia; el comportamiento sí

- `ProjectResponse` sigue con `taskCount` y `completedTaskCount` (`ProjectResponse.cs:3-12`, `frontend/src/app/features/projects/api/project-response.ts:9-10`). El frontend no cambia de tipos.
- Cambian dos comportamientos observables:
  1. **Con Tasks caído**, la lista y el detalle responden 200 con los últimos contadores, en vez de 503 (`architecture.md:75`).
  2. **Después de crear, completar, reabrir o borrar una tarea**, el contador se pone al día cuando el relay entrega el evento: hasta 1 s de espera del relay más la entrega (`OutboxOptions.cs:12`), y más si el monolito no responde.

Azure pide pensar qué hace el usuario con un dato viejo: "detecting and handling scenarios where a user acts on stale data requires careful consideration" ([FUENTE: Azure, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)). Acá el contador solo se muestra (`project-row.html:39-41`); ninguna acción depende de él.

| Opción | Gana | Pierde |
|---|---|---|
| **F1. El frontend no cambia; cambian los tests que leen el contador justo después de escribir** (recomendada) | El contrato JSON queda igual. Es lo que vería cualquier cliente | Un usuario rápido puede ver el número de antes por un segundo |
| F2. Un aviso fijo "los contadores pueden tardar unos segundos" | Explica el atraso | Texto nuevo, accesibilidad y e2e para un caso de un segundo |
| F3. El frontend suma localmente después de crear la tarea | El número parece al día | Lógica duplicada en el cliente; cuando llega el dato real puede saltar |

Recomiendo **F1**.

### 6.2 Tests que leen el contador justo después de una escritura

| Lugar | Hoy | Cómo cambia |
|---|---|---|
| `.agents/skills/verify-template/scripts/smoke-api.mjs:291-294` | Crea una tarea en el proyecto y enseguida afirma `taskCount === 1` | Reintenta el `GET` hasta que dé 1, con un límite (por ejemplo 15 s), y anota en la evidencia cuánto tardó: es la consistencia eventual medida |
| `smoke-api.mjs:339-341` (escenario `--compose` con Tasks parado) | Espera 503 en `GET /projects` | Espera **200** con los contadores de antes de parar Tasks. Es la prueba en Compose de lo que gana el paso |
| `smoke-api.mjs:329` | Usa `/internal/v1/task-counts` | Otra ruta interna (sección 5) |
| `frontend/e2e/projects.spec.ts:114-115` | Vuelve a "Mis proyectos" y espera `1 tarea, 0 completadas` | La página carga los proyectos una vez (`projects.ts:55,58`); `toContainText` reintenta sobre el mismo DOM y no recarga. Recomiendo `expect(async () => { recargar; expect(row).toContainText(...) }).toPass({ timeout: 15_000 })`. Playwright: "You can retry blocks of code until they are passing successfully", y "by default `toPass` has timeout 0" ([FUENTE: Playwright, assertions](https://playwright.dev/docs/test-assertions)): el timeout va explícito |
| `frontend/e2e/projects.spec.ts:80` | `0 tareas, 0 completadas` en un proyecto nuevo | Sin cambio: un proyecto nuevo nace con su fila `0/0` |
| `backend/Tests/Remote/RemoteBoundaryTests.cs:147-163` | `TasksUnreachable_ProjectReadsAnswer503_ButANewProjectIsCreated` | Se invierte: con Tasks caído la lista responde 200. Cambia el nombre |
| `GetProjectTests.cs:37-60`, `ListProjectsTests.cs:60-100`, `GetProjectTests.cs:62-90` | Cuentan contra Tasks; afirman una consulta a `TaskItems` | Corren el relay antes de leer (sección 9); afirman cero consultas a `TaskItems` y una sola a `Projects` (con la unión) |
| `CountTasksByProjectTests.cs` | Prueba el endpoint | Se borra con el endpoint (release C) |

Las e2e corren al cerrar el paso: cambia un test del frontend y el comportamiento de un contrato HTTP (`AGENTS.md`, "Verificación").

## 7. Observabilidad

### 7.1 Lo que ya existe sirve

- `templateapp.outbox.delivery.lag` es `ProcessedAt - OccurredAt` (`OutboxRelay.cs:132-134`). Con la opción (a), `ProcessedAt` se toma después de que el monolito respondió 204, y el 204 sale después de su commit. Entonces el histograma mide, casi exacto, **cuánto tarda la proyección en reflejar un hecho** [INFERENCIA].
- `templateapp.outbox.oldest_pending.age` dice cuánto puede estar atrasada, como mucho, la proyección en este momento (`OutboxMetrics.cs:26-29`).
- `templateapp.outbox.delivery.failures` por tipo cuenta los `PUT` fallidos, y `templateapp.outbox.failed` las filas que nadie va a reintentar: cada una es un contador que puede estar mal hasta la próxima reconstrucción.
- La traza cruza de Tasks al monolito sola (1.4).

### 7.2 Qué sumar, mínimo

| Qué | Recomendación |
|---|---|
| Instrumento nuevo | Ninguno. Los cinco del outbox ya cubren atraso y pérdida |
| Logs | `Information` en el monolito cuando un evento no encuentra su proyecto (3.3) y cuando corre una reconstrucción (cuántos proyectos y cuántos `MessageId` sembró) |
| Alerta | La alerta por antigüedad (`templateapp_outbox_oldest_pending_age > 60`) ya está como pendiente (`progreso.md:249`). Desde este paso hay un consumidor de producción que se puede atrasar, y Azure lo pide: "Monitor refresh recency and alert when the view age exceeds the acceptable staleness window" ([FUENTE: Azure, Materialized View](https://learn.microsoft.com/en-us/azure/architecture/patterns/materialized-view)). No está en el "Cambia" del paso (`plan.md:426`): recomiendo dejarla pendiente (regla 7) y corregir su texto, que dice que no hay consumidores |
| Readiness | Nada. El atraso no saca una instancia del balanceo (`architecture.md:101`) |

## 8. Regla de Dune (regla 4)

### 8.1 Qué regla nueva introduce el paso

El paso cambia `conventions.md:116`. Hoy: "Una lectura que junta datos de otras features [...] pide el resto a cada contrato en un lote por página". Después: con una feature **de otro proceso**, la lectura no la llama: lee su proyección local alimentada por eventos. Esa frase es una regla nueva, así que va ejecutable.

### 8.2 Recomendada: DUNE0024 "Lectura que llama a otro proceso"

- **Qué mira:** una slice de lectura (la misma detección que DUNE0019: su endpoint solo mapea `GET`, `ReadSliceAnalyzer.cs`) cuya operación depende, por su constructor, de un cliente remoto. Cliente remoto se define igual que en DUNE0022: un tipo con un `HttpClient` en su constructor, o `HttpClient`/`HttpMessageInvoker` directos (`DuneDiagnostics.cs:361-368`). La dependencia se sigue por los constructores de tipos de la misma feature (`ListProjects` → `ProjectResponses` → `TaskCountsClient`).
- **Rojo con el código de hoy:** `ListProjects` y `GetProject` fallan, por `ProjectResponses` → `TaskCountsClient` [CÓDIGO `ProjectResponses.cs:9-16`].
- **Fixture válida:** la lectura nueva, que lee `ProjectTaskCounters`. **Violación deliberada:** una `GetProject` que inyecta un cliente tipado de Tasks. Tests en `Dune.Analyzers.Tests`, como `RemoteCallInTransactionAnalyzerTests.cs`.
- **Hoy no pega en otra lectura:** `GetTask` depende de `TasksDbContext` y `TaskAccess`, que no llama a nadie (`GetTask.cs:19`, `TaskAccess.cs`); `ListTasks`, solo del contexto (`ListTasks.cs:12`). La validación de la sesión contra Auth corre en el middleware de la cookie, fuera de la slice (`TasksFeature.cs:23-25`), y la regla no la ve.
- **Límite:** no ve un cliente pedido con `IServiceProvider` ni uno detrás de una interfaz de otro ensamblado. Lo anoto en la descripción, como DUNE0022.

| Opción | Gana | Pierde |
|---|---|---|
| **D1. DUNE0024** (recomendada) | La regla nueva de `conventions.md` se hace cumplir (regla 4). El rojo es el código real de antes del paso | Un analyzer más con su fixture |
| D2. Ninguna regla | Menos trabajo | Rompe la regla 4: la frase nueva de `conventions.md` quedaría sin gate |

### 8.3 Reglas que no cambian

- **DUNE0016**: el endpoint de eventos es `PUT`, fuera de la regla (1.4).
- **DUNE0019**: `ApplyTaskEvent` mapea `PUT`, así que no es lectura; la lectura nueva solo proyecta.
- **DUNE0023**: el relay sigue siendo el único que llama a un `IEventHandler<T>`. `ApplyTaskEvent` no es un consumidor en ese sentido: lo llama un endpoint, y el endpoint lo llama el relay de otro proceso.
- **DUNE0017**: no aplica a `ProjectTaskCounters`, que no es `IAuditable`.

## 9. El "Verifica" como tests

### 9.1 Cómo se arma en este repo

- PostgreSQL real con Testcontainers o `TEST_POSTGRES_CONNECTION` (`PostgresFixture.cs`). Monolito y Tasks en memoria con `ApiPair` (`ApiPair.cs:9-117`). Las llamadas remotas se resuelven en memoria por host (`ApiPair.cs:64-71`), así que el `PUT` de `TaskEventsClient` llega al monolito del mismo par sin cambios.
- El relay automático está apagado en los tests (`ApiPair.cs:81`) y cada test llama a `RunOnceAsync`.
- **Base propia para todo test que corre el relay.** Todos los hosts de la suite comparten la base, y un `RunOnceAsync` entrega cualquier fila pendiente (`conventions.md:110`). Hay un choque concreto: `TaskEventsTests` usa la base compartida y afirma `ProcessedAt` null (`TaskEventsTests.cs:8,34`). Si `GetProjectTests` corriera el relay en esa base, esa afirmación fallaría a veces. Por eso los tests de contadores pasan a una base propia, como `OutboxRelayTests` (`TestDatabases.CreateEmptyAsync`, `OutboxRelayTests.cs:259`). Recomiendo sacar a `Tests/Support` el armado que hoy vive privado en `OutboxRelayTests` (base nueva, `ApiPair`, `FakeTimeProvider`), porque `conventions.md:164` prohíbe copiar helpers entre tests.
- `FailOnceMarkingProcessed` (`Tests/Messaging/FailOnceMarkingProcessed.cs`) ya provoca la doble entrega del paso 24 y sirve para el duplicado.
- `RemoteFault` (`Tests/Support/RemoteFault.cs`) ya simula un host caído: sirve para "monolito caído" y "Tasks caído".

### 9.2 Tests

| # | Test | Rojo antes del paso | Verde después |
|---|---|---|---|
| T1 | **(Verifica 1) Comparativo.** Base propia. Varios proyectos de dos dueños, tareas creadas, completadas, reabiertas y borradas, tareas sin proyecto y un proyecto sin tareas. Se corre el relay hasta vaciar el outbox. Para cada página de `ListProjects` y cada `GetProject`, la lectura vieja (copiada en el test, con `TaskCountsClient`) y la nueva dan las mismas respuestas | No compila: la lectura nueva no existe | Pasa. Se borra en el release C (`conventions.md:118`) |
| T2 | **(Verifica 2) Consumidor atrasado.** Base propia. Crear proyecto y tarea: `GET` del proyecto da `taskCount 0`. `RunOnceAsync`: da 1. Completar: `completedTaskCount` sigue en 0 hasta el próximo `RunOnceAsync` | Falla: hoy la lectura cuenta en el momento, da 1 sin correr el relay | Pasa |
| T3 | **(Verifica 2, con el monolito caído)** `RemoteFault` sobre el host del monolito para el `PUT`: el relay deja la fila pendiente con `Attempts 1`, el `GET` sigue con el dato viejo; se levanta el monolito, se avanza el `FakeTimeProvider` y otro `RunOnceAsync` lo pone al día | Falla | Pasa |
| T4 | **(Verifica 3) Reconstrucción con el mismo resultado.** Base propia, mismo juego de datos que T1, relay vaciado. Se guarda la tabla, se borran todas sus filas y se corre `RebuildTaskCounters`: la tabla queda igual fila por fila | Falla: no existe | Pasa |
| T5 | **Reconstrucción con eventos sin entregar.** Crear y completar tareas **sin** correr el relay; reconstruir; correr el relay: los contadores son los de la fuente, sin doble conteo. **Violación deliberada**: una reconstrucción que no siembra el inbox da el doble; va en un commit de evidencia y se revierte, como T6 del paso 24 | Falla | Pasa |
| T6 | **Duplicado.** `FailOnceMarkingProcessed` hace que el mismo mensaje llegue dos veces al monolito: los contadores suben 1 y hay 1 fila en `projects."InboxMessages"` | Falla | Pasa |
| T7 | **Desorden.** `PUT` directos al endpoint interno con sobres armados en el test: `TaskDeleted(wasCompleted: true)`, después `TaskCompleted`, después `TaskCreated`. Final `0/0`; en el medio, la respuesta HTTP nunca muestra negativos | Falla | Pasa |
| T8 | **Proyecto desconocido o borrado.** Un evento con un `projectId` que no existe responde 204 y no crea fila. Borrar la cuenta: los `TaskDeleted` que llegan después no dejan filas | Falla | Pasa |
| T9 | **Tasks caído.** `RemoteFault` sobre Tasks: lista y detalle responden 200 con los contadores de antes. Reemplaza al test de `RemoteBoundaryTests.cs:147` | Falla: hoy 503 | Pasa |
| T10 | **Reconstrucción en curso.** Con el lock exclusivo tomado por el test, un `PUT` responde 503 y la fila del outbox queda pendiente; al soltarlo, el reintento aplica una vez | Falla | Pasa |
| T11 | **Arranque.** Una base con un proyecto sin fila de contadores (insertado por SQL en el test): el servicio de arranque reconstruye y la fila aparece con los números de la fuente | Falla | Pasa |
| T12 | **DUNE0024**: fixture válida y violación deliberada; el build del backend sin diagnósticos nuevos después del release B | El código de hoy da el diagnóstico | Pasa |
| T13 | Se ajustan `GetProjectTests`, `ListProjectsTests` (una consulta a `Projects` con la unión, cero a `TaskItems`), `CreateProjectTests` (la fila `0/0` nace con el proyecto) y `RemoteBoundaryTests` | — | Pasa |

- T1, T2 y T4 son las tres frases del Verifica (`plan.md:427`).
- Smoke local y de Compose con los cambios de 6.2 [NO PROBADO]. E2e completas al cerrar.

## Choques con los docs

1. **`architecture.md:38` y ADR-0004 punto 9** ("un despachador casero del outbox [...] entrega eventos dentro del proceso hasta el paso 25"). Con la opción (a), el despachador entrega también a otro proceso, por HTTP. No es una librería y el gate no se dispara. Recomiendo corregir la frase: "dentro del proceso, y a otro proceso por HTTP con un consumidor que reenvía (paso 24.1)".
2. **`architecture.md:89`** dice que un evento que cruza de proceso "necesita el broker del paso 25". El 24.1 cruza sin broker. Recomiendo corregir la frase y dejar el evento de cuenta borrada en el 25 de todas formas: sumarlo acá amplía el paso (regla 7).
3. **`docs/decisions.tsv` (fila del paso 24, opción A) y `conventions.md:106`**: la traducción a evento de integración y el sufijo `IntegrationEvent` quedan para el paso 25 "mientras no haya receptor en otro proceso". El 24.1 trae el primer receptor en otro proceso. Recomiendo: el monolito escribe su copia del sobre como DTO HTTP (`TaskEventRequest`, sufijo `*Request` porque es un body, `conventions.md:34`), Tasks no traduce, y la opción B con `IntegrationEvent` sigue en el 25. Se corrige la condición en `conventions.md:106`.
4. **`conventions.md:109`** dice que un consumidor abre una transacción de su contexto y llama a `Inbox.TryAcceptAsync`. `TaskEventForwarder` no tiene efecto local: su efecto es la request, y el que deduplica es el receptor. Recomiendo sumar: "un consumidor que reenvía a otro proceso no tiene inbox propio; el receptor deduplica con el suyo".
5. **`conventions.md:116`** (la lectura pide a cada contrato) cambia para otro proceso (sección 8). Va con DUNE0024.
6. **`architecture.md:40`** dice que `GetProject` y `ListProjects` suman los contadores con `ITasksModule.CountByProjectAsync`. Ese contrato no existe desde el paso 23.2: hoy es `TaskCountsClient` por HTTP (`architecture.md:66`, `ProjectResponses.cs:9`). **Es una contradicción entre dos líneas del mismo doc, ya vigente.** El paso reescribe esa frase de todos modos.
7. **`conventions.md:93`** dice que `DeleteUser` llama a `ITasksModule.DeleteOwnedTasksAsync`. Hoy es `OwnedTasksClient` por HTTP (`DeleteUser.cs:40`, `architecture.md:56`). **Contradicción vigente entre `conventions.md` y `architecture.md`**, fuera del alcance: pendiente.
8. **DUNE0022 y la reconstrucción.** La regla escrita (`conventions.md:28`) prohíbe la llamada remota dentro de `CrossContextTransaction`; la reconstrucción la hace, a propósito, dentro de una transacción de un contexto (4.3). No es una violación de la regla, pero sí de su motivo. Recomiendo nombrarla en `architecture.md` como la única excepción, con el porqué.

## Decisiones (con mi recomendación)

En modo autónomo cada una la toma el agente con esta recomendación y la anota en `docs/decisions.tsv`.

1. **Transporte (pregunta 1):** (a) empuje por HTTP desde un consumidor del relay de Tasks a `PUT /internal/v1/task-events/{messageId}`, con inbox en el monolito. Alternativas: (b) feed con posición `xid8`, (c) proyección en Tasks, (d) leer el outbox de Tasks. Recomiendo: (a).
2. **Forma del endpoint:** `PUT` con el `MessageId` en la ruta, 204 al aplicar o al ignorar, 400 para una versión desconocida de un tipo conocido, 503 durante una reconstrucción. Alternativa: `POST` con `WithIdempotency`, que necesita una cookie que el endpoint interno no tiene. Recomiendo: `PUT`.
3. **Monolito caído:** `Outbox:MaxAttempts = 12` en Tasks (unos 68 minutos de tolerancia), sin cambiar el código del relay; separar "destino caído" de "mensaje roto" queda para el 25. Alternativas: dejar 5 (unos 30 s y la proyección queda mal hasta reconstruir) o cambiar el relay ahora (adelanta el 25). Recomiendo: 12.
4. **Tabla:** T1, `projects."ProjectTaskCounters"` en `ProjectsDbContext`, FK con cascada a `Projects`, fila `0/0` creada con el proyecto, entidad en `Persistence/` sin `IAuditable`. Alternativas: T2 (columnas en `Projects`, rompe la versión de fila), T3 (fila por tarea, depende del orden), T4 (contexto aparte). Recomiendo: T1.
5. **Nombre del dueño:** N1, sigue con `IUsersModule` en el proceso. Recomiendo: N1.
6. **Efecto:** deltas con `ExecuteUpdate` (`x = x + d`), inbox en la misma transacción, lock compartido no bloqueante. Respuesta con `max(0, …)` y `completed ≤ total`; la tabla guarda el número crudo, sin `CHECK`. Recomiendo: sí.
7. **Casos raros:** evento sin proyecto o de proyecto desconocido, sin efecto y con log; tipo desconocido ignorado. Recomiendo: sí.
8. **Reconstrucción:** foto `REPEATABLE READ` de Tasks con contadores y `MessageId` sin entregar; el monolito toma el lock exclusivo antes de pedirla, reemplaza la tabla y siembra el inbox en una transacción. Alternativa: tomar el lock después (deja una ventana donde se pierde un evento). Recomiendo: lock antes.
9. **Cuándo reconstruir:** R1, al arrancar el monolito si algún proyecto no tiene fila. Alternativas: R2 (siempre) y R3 (endpoint de administración). Recomiendo: R1.
10. **Orden de despliegue:** releases A (expand), B (cambio de lectura con test comparativo) y C (contract), como pide `conventions.md:118`. Recomiendo: sí.
11. **`/internal/v1/task-counts` y `TaskCountsClient`:** B1, se borran en el release C; el monolito se despliega antes que Tasks. Recomiendo: B1.
12. **Frontend:** F1, sin cambios de código; cambian el smoke y la e2e de proyectos (`toPass` con recarga y timeout explícito). Recomiendo: F1.
13. **Observabilidad:** sin instrumentos nuevos; logs de evento ignorado y de reconstrucción; la alerta de antigüedad sigue pendiente con su texto corregido. Recomiendo: sí.
14. **Dune:** DUNE0024 (lectura que depende de un cliente remoto), con fixture válida y violación deliberada. Alternativa: ninguna regla (rompe la regla 4). Recomiendo: DUNE0024.

## Herramientas (regla 5)

**Ninguna nueva.**

| Pieza | ¿Nueva? | Nota |
|---|---|---|
| `IEventHandler<T>`, `OutboxRelay`, `Inbox`, `MapInbox` | No | Paso 24 (`Infrastructure/Messaging`) |
| `AddRemoteClient`, `SendRemoteAsync` | No | Paso 23.2 (`Infrastructure/Http/RemoteServices.cs`) |
| `ExecuteUpdateAsync`, `BeginTransactionAsync(IsolationLevel.RepeatableRead)` | No | EF Core y Npgsql ya están |
| `pg_try_advisory_xact_lock_shared`, `pg_advisory_xact_lock` | No | Funciones de PostgreSQL; el repo ya usa advisory locks (`DatabaseInitializer.cs:49`) |
| `BackgroundService` | No | Framework |
| `expect(...).toPass()` de Playwright | No | Playwright Test ya está (`frontend/e2e/playwright.config.ts`) |
| Columna `xid8` (solo para la opción b) | No | Tipo de PostgreSQL; no se usa con la recomendación |
| Librería de bus o broker | Sí | **No** se usa: es la decisión pendiente del paso 25 |

No hay preguntas de herramienta para Tobi.

## Pendientes para `progreso.md`

- Separar "destino caído" de "mensaje roto" en el relay: reintentar sin límite lo primero y mandar lo segundo a una cola de muertos (paso 25).
- Disparo manual de la reconstrucción para un operador (4.4).
- Reconciliación periódica que compare la proyección con la fuente y avise si difieren.
- La foto de `SnapshotTaskCounts` devuelve todos los proyectos en una respuesta: con muchos proyectos habría que paginarla dentro de la misma foto (por ejemplo, con `pg_export_snapshot`).
- Alerta de antigüedad del outbox: ahora hay un consumidor de producción (corregir el texto de `progreso.md:249`).
- `conventions.md:93` nombra `ITasksModule.DeleteOwnedTasksAsync`, que ya no existe (choque 7).

## Lo que no comprobé

- No corrí experimentos en esta investigación. Todo lo de PostgreSQL (locks compartidos frente a exclusivos, foto `REPEATABLE READ` con dos `SELECT`) sale de la documentación; el paso lo prueba con T5, T10 y T11.
- No medí cuánto tarda la entrega de punta a punta en Compose (relay, HTTP y commit). El smoke de 6.2 lo deja en la evidencia.
- No probé que la traza del relay de Tasks llegue como padre del span del servidor en el monolito; lo apoyo en la documentación de .NET.
- No revisé el código de `ReadSliceAnalyzer.cs` y `RemoteCallInTransactionAnalyzer.cs` por dentro: la forma de DUNE0024 sale de sus descripciones en `DuneDiagnostics.cs`.
