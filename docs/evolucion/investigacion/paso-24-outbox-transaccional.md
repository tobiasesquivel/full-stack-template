# Paso 24: eventos de dominio + outbox transaccional, sin broker (investigación)

- Fecha: 2026-11-19.
- Estado: **Listo** como investigación. No hace falta ninguna herramienta, librería ni tecnología nueva. Hay 7 choques con los docs y 14 decisiones, cada una con mi recomendación.
- Alcance: el paso 24 del [plan](../plan.md) (`plan.md:418-421`). Concepto: atomicidad entre estado y evento; el error clásico es publicar antes de confirmar.
- Base: rama `fase8/24-outbox-transaccional`, igual a `main` (`1ad952f`, con el paso 23.2).
- Límites con los pasos vecinos: la proyección (tabla de lectura alimentada por eventos) es el paso 24.1. El broker, la DLQ (cola de mensajes muertos) y la librería de mensajería son el paso 25, y la librería es una decisión de Tobi pendiente (`plan.md:431`). Este paso no adelanta ninguno de los dos (regla 8).

**Cómo leer las marcas:**
- `[FUENTE]`: lo leí en un documento primario. Cada uno tiene su link.
- `[CÓDIGO archivo:línea]`: lo leí en el repo.
- `[EXPERIMENTO EX-x]`: lo corrí en esta sesión, en el scratchpad, contra PostgreSQL 18.6 en Docker. Detalle en "Experimentos".
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo medí. Queda pendiente para el paso.

## Respuesta corta

1. **Qué cambia.** Cuando `Tasks` crea, completa, reabre o borra una tarea, la entidad `TaskItem` anota un evento (`TaskCreated`, `TaskCompleted`, `TaskReopened`, `TaskDeleted`). Un interceptor de EF (`SaveChangesInterceptor`, el mismo mecanismo que ya usa la auditoría) convierte esos eventos en filas de una tabla nueva, `tasks."OutboxMessages"`, **en el mismo `SaveChanges`**. Entonces la fila del evento y el cambio de la tarea entran o se pierden juntos.
2. **Quién los entrega.** Un `BackgroundService` (un servicio que corre en segundo plano dentro del proceso de Tasks) lee las filas pendientes cada segundo, las reserva con `FOR UPDATE SKIP LOCKED` (dos instancias nunca toman la misma fila), llama a los consumidores registrados en el mismo proceso y marca la fila como procesada. Si un consumidor falla, la fila queda pendiente con su error y su número de intentos.
3. **Cómo un duplicado se consume una vez.** Cada consumidor escribe el id del mensaje en una tabla "inbox" (`InboxMessages`, clave `(MessageId, Consumer)`) **en la misma transacción** que su efecto. Si el mensaje llega otra vez, el insert no inserta nada y el consumidor no repite el efecto.
4. **El error que el paso enseña existe hoy en este repo, con otra forma.** Dentro del filtro de idempotencia, `SaveChanges` **no** confirma: confirma `transaction.Complete()` del `TransactionScope`, después (`IdempotencyFilter.cs:44,57,77`). Lo medí: un "publicar" puesto justo después de `SaveChanges` sale aunque la transacción se revierta, y la tarea no existe (EX-B, evento fantasma). La métrica `templateapp.tasks.created` ya tiene ese defecto hoy (`CreateTask.cs:57-58`, choque 2).
5. **Consumidor de este paso.** Ninguno de producción: el primer consumidor real es la proyección del paso 24.1. El paso deja el despachador, el inbox y un consumidor que registran solo los tests, para hacer ejecutable el "Verifica" (decisión 9).
6. **Herramientas.** Ninguna nueva. EF Core 10.0.12, Npgsql 10.0.3, `System.Text.Json`, `System.Diagnostics` y `BackgroundService` ya están (`TemplateApp.Platform.csproj`). No es un bus ni un mediator de librería, así que el gate "bus o mediator antes del paso 25" no se dispara (`check-structure.mjs:206-220,272-283`).

## Glosario corto

- **Evento de dominio**: un hecho del pasado ("la tarea se completó"), escrito como un record inmutable. "An event is something that has happened in the past" ([FUENTE: Learn, domain events](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)).
- **Outbox** (= bandeja de salida): una tabla de la misma base donde el servicio guarda el mensaje en la misma transacción que el cambio. Richardson: "the service that sends the message to first store the message in the database" ([FUENTE: microservices.io, Transactional outbox](https://microservices.io/patterns/data/transactional-outbox.html)).
- **Relay** (= repartidor): el proceso que lee el outbox y entrega cada mensaje. "Message relay - sends the messages stored in the outbox to the message broker" ([FUENTE: misma página](https://microservices.io/patterns/data/transactional-outbox.html)). Acá no hay broker: entrega a consumidores del mismo proceso.
- **At-least-once** (= al menos una vez): cada mensaje confirmado se entrega una o más veces. "The Message relay might publish a message more than once" ([FUENTE](https://microservices.io/patterns/data/transactional-outbox.html)).
- **Consumidor idempotente / inbox**: el consumidor guarda los ids que ya procesó. "the message handler inserts the message's ID into the `PROCESSED_MESSAGE` table" después de abrir la transacción ([FUENTE: microservices.io, Idempotent consumer](https://microservices.io/patterns/communication-style/idempotent-consumer.html)).
- **Evento fantasma**: un evento que salió aunque el cambio que lo causó se revirtió. El informe .NET lo llama "Publish-then-commit" (`informe-dotnet-empresarial.md:452`).

---

## 1. Qué eventos emite Tasks y dónde nacen

### 1.1 Cómo está hoy

- `TaskItem` es anémico: propiedades con `set` públicos y sin métodos de comportamiento (`TaskItem.cs:9-16`). `progreso.md:277` ya lo anota como pendiente.
- Las slices cambian la entidad a mano: `CreateTask` la arma con un inicializador (`CreateTask.cs:47-54`); `UpdateTask` escribe `Title`, `CompletedAt` y `UpdatedAt` (`UpdateTask.cs:46-50`); `DeleteTask` y `DeleteOwnedTasks` usan `Remove`/`RemoveRange` sobre entidades cargadas (`DeleteTask.cs:28`, `DeleteOwnedTasks.cs:17`). El brief decía "bulk ExecuteDelete": no es así, `DeleteOwnedTasks` carga las tareas y las borra con el ChangeTracker [CÓDIGO `DeleteOwnedTasks.cs:17-18`]. DUNE0017 prohíbe `ExecuteDelete` sobre `TaskItem` porque es `IAuditable` (`DuneDiagnostics.cs`, `AuditBypass`).
- `Project` (paso 23.1) ya muestra el otro estilo: setters privados, `Project.Create` y transiciones `Rename`, `Archive`, `Restore` [CÓDIGO `Project.cs:13-52`].

### 1.2 Qué eventos

| Evento | Cuándo | Payload (v1) |
|---|---|---|
| `TaskCreated` | `CreateTask` guarda una tarea nueva | `TaskId`, `OwnerId`, `ProjectId?`, `CreatedAt` |
| `TaskCompleted` | `UpdateTask` pasa `CompletedAt` de `null` a una fecha | `TaskId`, `OwnerId`, `ProjectId?`, `CompletedAt` |
| `TaskReopened` | `UpdateTask` pasa `CompletedAt` de una fecha a `null` | `TaskId`, `OwnerId`, `ProjectId?`, `ReopenedAt` |
| `TaskDeleted` | `DeleteTask` y `DeleteOwnedTasks`, una por tarea | `TaskId`, `OwnerId`, `ProjectId?`, `WasCompleted` |

Por qué esta forma:

- **Sin título.** El título es texto libre del usuario. La auditoría ya decidió no guardar valores por esa razón (`conventions.md:100`, `architecture.md` "Auditoría de cambios"). Ningún consumidor previsto lo necesita: la lectura que reemplaza el 24.1 son contadores por proyecto y el nombre del dueño (`plan.md:425-426`, `architecture.md:40`) [INFERENCIA].
- **Sin `TaskRenamed`.** Sin título en el payload, el evento solo diría "cambió algo". Ningún consumidor lo pide. Se agrega cuando uno lo pida (expand: un tipo nuevo no rompe a nadie) [INFERENCIA].
- **`WasCompleted` en `TaskDeleted`.** Un contador de "completadas por proyecto" (24.1) necesita saber si resta de las completadas al borrar [INFERENCIA].
- **Uno por tarea en `DeleteOwnedTasks`**, no un `OwnerTasksDeleted`. Así un consumidor de contadores trata igual el borrado de una tarea y el de una cuenta [INFERENCIA]. Si el endpoint interno se repite, la segunda vez no encuentra tareas y no emite nada (`DeleteOwnedTasks.cs:17`).
- **Nombres en pasado**, como pide Learn: "the class name of the event should be represented as a past-tense verb" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)).

### 1.3 Versión

- La fila guarda un **nombre estable** (`"tasks.task-created"`) y una **versión** (`1`), no el nombre del tipo CLR. eShop guarda el nombre completo del tipo (`IntegrationEventLogEntry`, `EventTypeName`, [FUENTE: eShop](https://github.com/dotnet/eShop/blob/main/src/IntegrationEventLogEF/IntegrationEventLogEntry.cs)); entonces renombrar o mover la clase rompe la lectura de las filas viejas [INFERENCIA].
- Cambio compatible (un campo nuevo opcional): misma versión. Cambio incompatible: versión 2 como tipo nuevo, emitido junto al 1 mientras haya consumidores del 1. Es el expand/contract del paso 19 llevado a los mensajes [INFERENCIA, por `conventions.md` "Migraciones: expand/contract"].
- Cada fila lleva además un `MessageId` (`Guid`, generado con `Guid.CreateVersion7()`, ordenado por tiempo). El inbox usa ese id y no el `Id` numérico de la fila: en el paso 25 el mensaje sale de la base y el número de la fila deja de viajar [INFERENCIA].

### 1.4 ¿Desde la entidad o desde la slice?

| Opción | Cómo | Gana | Pierde |
|---|---|---|---|
| **E1. La entidad anota el evento** (recomendada) | `TaskItem.Create(...)`, `ApplyUpdate(title, isCompleted, now)` y `MarkDeleted()` agregan el evento a una lista privada. Interfaz `IHasDomainEvents` en `Shared/Domain`, al lado de `IAuditable` | Es el patrón de Learn y eShop: "add the events happening in your entities into a collection or list of events per entity" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)). La regla "completar = pasar `CompletedAt` de null a fecha" vive en un solo lugar. Sigue el estilo de `Project` | Toca `TaskItem` y tres slices. Hay que decidir si los setters pasan a privados (recomiendo que sí, como `Project`, porque si no una slice puede cambiar `CompletedAt` sin evento) |
| E2. La slice escribe el evento | `_db.Add(OutboxMessage.From(new TaskCompleted(...)))` antes de `SaveChanges`, como el ejemplo de Milan ("calls `AddToOutbox` directly in the command handler", [FUENTE: Milan, outbox](https://www.milanjovanovic.tech/blog/implementing-the-outbox-pattern)) | Cero cambios en el dominio. Se ve en la slice | La regla de qué evento corresponde queda copiada en cada slice. Olvidarse en una slice nueva no lo nota nadie |
| E3. El interceptor deduce el evento del ChangeTracker | Como la auditoría: mira qué propiedades cambiaron | Ninguna slice cambia | Pierde la intención: "`CompletedAt` cambió" no dice si completó o reabrió sin repetir la regla. Learn llama a esto acoplar la regla "implicitly, to the code" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)) |

Recomiendo **E1**. `Domain/` sigue sin EF ni ASP.NET: la lista de eventos es una `List<object>` o de una interfaz marcadora de `Shared/Domain` [INFERENCIA, por `architecture.md:34`]. La propiedad de eventos se ignora en el mapeo (`builder.Ignore(...)`) [EXPERIMENTO EX-A].

## 2. Del evento a la fila del outbox, en la misma transacción

### 2.1 Interceptor o escritura explícita

Recomiendo **un `OutboxInterceptor` en `Infrastructure/Messaging/`**, hermano de `AuditInterceptor`:

- En `SavingChangesAsync`: recorre las entidades `IHasDomainEvents` del ChangeTracker y agrega un `OutboxMessage` por evento al mismo contexto. EF guarda todo en un `SaveChanges`, y "all changes in a single call to `SaveChanges` are applied in a transaction" ([FUENTE: EF Core, transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)). Lo medí: la fila de la tarea y la del outbox tienen el mismo `xmin` (id de la transacción que las escribió) [EXPERIMENTO EX-A].
- En `SavedChangesAsync`: vacía la lista de eventos de cada entidad. No antes: si `SaveChanges` falla (por ejemplo el 409 de versión, `UpdateTask.cs:53-60`) y alguien reintenta, los eventos tienen que seguir ahí [INFERENCIA]. Lo medí: después de guardar, la entidad queda sin eventos [EXPERIMENTO EX-A].
- Al empezar, desprende (`Detached`) los `OutboxMessage` agregados por un intento anterior que falló, igual que hace la auditoría con sus filas (`AuditInterceptor.cs:47-48`).
- **Materializar antes de agregar.** Agregar filas mientras se recorre `ChangeTracker.Entries()` tira "Collection was modified" [EXPERIMENTO EX-A, primera corrida]. La auditoría ya lo evita con `.ToList()` (`AuditInterceptor.cs:50-52`).
- **Sin estado en el interceptor.** Se registra como singleton, igual que `AuditInterceptor` (`BackendInfrastructure.cs:27`, `ContextRegistration.cs:23`). Learn: "Interceptors are often stateless, which means that a single interceptor instance can be used for all DbContext instances" ([FUENTE: EF Core, interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)). Todo lo que necesita lo saca de `eventData.Context`.
- **Solo en contextos que mapean el outbox.** `AddMigratedDbContext` le pone `AuditInterceptor` a todos (`ContextRegistration.cs:19-23`). El outbox, no: un parámetro `withOutbox: true` lo suma solo a `TasksDbContext`. Si una entidad con eventos se guarda en un contexto sin outbox, el interceptor tira una excepción en vez de perder el evento callado [INFERENCIA].

La alternativa explícita (E2 de 1.4) no necesita interceptor. La descarto por la misma razón que E2.

### 2.2 La tabla

`tasks."OutboxMessages"`, en el schema de la feature, migrada por `TasksDbContext`:

| Columna | Tipo | Para qué |
|---|---|---|
| `Id` | `bigint` identity, PK | Orden de lectura del relay |
| `MessageId` | `uuid`, único | Id del mensaje para el inbox |
| `Type` | `text` | Nombre estable (`tasks.task-created`) |
| `Version` | `int` | Versión del payload |
| `Payload` | `jsonb` | El evento en JSON |
| `OccurredAt` | `timestamptz` | Cuándo pasó (de `TimeProvider`) |
| `TraceParent` | `varchar(55)` null | Contexto de traza W3C (sección 5) |
| `Attempts` | `int`, default 0 | Intentos de entrega |
| `NextAttemptAt` | `timestamptz` null | Cuándo reintentar |
| `LastError` | `text` null | Último error, recortado |
| `ProcessedAt` | `timestamptz` null | Entregado |
| `FailedAt` | `timestamptz` null | Se agotaron los intentos |

- **Índice parcial** sobre `("Id") WHERE "ProcessedAt" IS NULL AND "FailedAt" IS NULL`. Milan propone el mismo tipo de índice: parcial sobre las filas sin procesar ([FUENTE: Milan, outbox](https://www.milanjovanovic.tech/blog/implementing-the-outbox-pattern)). El relay pregunta solo por esas filas, y el índice no crece con las procesadas [INFERENCIA].
- **Sin `CONCURRENTLY`.** DUNE0018 exige índice concurrente solo "sobre una tabla existente"; la tabla nace en la misma migración (`MigrationSafetyAnalyzer.cs:77,185-189`).
- **Expand puro.** Una tabla nueva, sin rename ni drop: la migración se genera con `dotnet ef migrations add AddOutboxMessages --context TasksDbContext` (`conventions.md`, migraciones) y un release viejo no la ve [INFERENCIA].
- **Schema de la feature y no `platform`.** Cada feature es dueña de su outbox, como cada servicio de eShop tiene su tabla de log de eventos ([FUENTE: Learn, subscribe events](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/subscribe-events): "you have an IntegrationEvent table in the same database where you have your domain entities"). Si fuera `platform`, la migraría el monolito y no Tasks (`architecture.md:50`), y Tasks escribiría una tabla que otro proceso migra.
- **Cómo se mapea.** Un helper `MapOutbox()` en `Infrastructure/Messaging`, como `MapSharedAuditEntries` (`SharedAuditEntries.cs:7-13`), pero **sin** `ExcludeFromMigrations`: la tabla queda en el schema por defecto del contexto que la llama y la migra ese contexto. Ver choque 5.

### 2.3 Con el filtro de idempotencia y `CrossContextTransaction`

- Con `Idempotency-Key`, `POST /tasks` corre dentro del `TransactionScope` del filtro (`IdempotencyFilter.cs:44,57`). La fila del outbox entra en el `SaveChanges` de `TasksDbContext`, que se anota en esa misma transacción ambiente ("cada contexto se anota solo al abrir su conexión", `conventions.md:96`). Si el filtro no llega a `Complete()` (`IdempotencyFilter.cs:77`), se revierten la tarea, la clave y el evento juntos [EXPERIMENTO EX-B: con `TransactionScope` y una excepción antes de `Complete`, 0 filas de tarea y 0 del outbox].
- **El punto que enseña el paso:** en este repo, "después de `SaveChanges`" no siempre es "después del commit". EX-B lo muestra con un interceptor que "publica" en `SavedChangesAsync`: publicó 1 evento y la tarea no existe. Por eso el relay es el único que entrega, y solo lee filas confirmadas [EXPERIMENTO EX-B].
- `TransactionScope` confirma de forma síncrona ("disposing it synchronously blocks the executing thread", [FUENTE: EF Core, transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)). El outbox no lo cambia: este paso no quita ninguna `CrossContextTransaction` (choque 3).
- **Replay sin evento.** El replay de idempotencia no ejecuta la slice (`IdempotencyFilter.cs:54-55`), y el reintento tolerante de `UpdateTask` devuelve sin guardar (`UpdateTask.cs:41-44`): ninguno emite evento. Hay que fijarlo con un test [INFERENCIA].

## 3. El relay sin broker

### 3.1 Forma

`OutboxRelay : BackgroundService` en `Infrastructure/Messaging/`, registrado por la feature que tiene outbox (`AddOutbox<TasksDbContext>()` desde `TasksFeature`). Por ciclo:

1. Crea un scope (DI). "No scope is created for a hosted service by default" ([FUENTE: Learn, hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0#consuming-a-scoped-service-in-a-background-task)).
2. Abre una transacción del contexto (`Database.BeginTransactionAsync`, la forma para un solo contexto, `conventions.md:96`) y reserva un lote:

   ```sql
   SELECT ... FROM tasks."OutboxMessages"
   WHERE "ProcessedAt" IS NULL AND "FailedAt" IS NULL
     AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= @now)
   ORDER BY "Id" LIMIT @batch
   FOR UPDATE SKIP LOCKED
   ```

3. Por cada fila, en orden: llama a cada consumidor registrado para ese `Type` (sección 4). Si todos terminan bien, `ProcessedAt = now`. Si alguno falla: `Attempts + 1`, `LastError`, `NextAttemptAt = now + espera`; con `Attempts = MaxAttempts`, `FailedAt = now`.
4. Confirma y espera el próximo tick de un `PeriodicTimer(TimeSpan, TimeProvider)` ([FUENTE: API PeriodicTimer](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer.-ctor?view=net-10.0)). Con `TimeProvider`, los tests usan `FakeTimeProvider`, que el repo ya tiene (`TemplateApp.Tests.csproj:15`).

El relay atrapa y loguea cualquier excepción del ciclo (base caída, por ejemplo) y sigue: un `BackgroundService` que termina con excepción no vuelve solo [INFERENCIA].

### 3.2 Varias instancias: `FOR UPDATE SKIP LOCKED`

- PostgreSQL: "With `SKIP LOCKED`, any selected rows that cannot be immediately locked are skipped. [...] can be used to avoid lock contention with multiple consumers accessing a queue-like table" ([FUENTE: PostgreSQL, SELECT, The Locking Clause](https://www.postgresql.org/docs/current/sql-select.html)).
- Lo medí con dos conexiones y seis filas, lote de 4: el relay 1 tomó `[1,2,3,4]` y el relay 2 tomó `[5,6]` en 2 ms, sin esperar. Con `NOWAIT` en lugar de `SKIP LOCKED`, el segundo falla con `55P03 could not obtain lock` [EXPERIMENTO EX-C].
- Milan sugiere `SKIP LOCKED` solo como forma de escalar y su código no lo usa ([FUENTE](https://www.milanjovanovic.tech/blog/implementing-the-outbox-pattern)). Acá sí hace falta desde el principio: los tests ya levantan varios hosts de Tasks contra la misma base (`ApiFixture.cs:60-71`, cada `ApiPair` aislado usa la misma connection string) [CÓDIGO].
- **Costo:** el orden global se pierde con dos relays. Una tarea creada y completada enseguida puede entregarse en dos relays distintos y llegar al revés [INFERENCIA]. El orden por partición es del paso 25 (`plan.md:430`). Para el 24, cada payload lleva su fecha y el consumidor del 24.1 la usa si le importa el orden.

### 3.3 Qué quiere decir "pendiente" y por qué no un "último id visto"

- Pendiente = `ProcessedAt IS NULL AND FailedAt IS NULL`. No se usa un "último `Id` leído" como marca de agua.
- Por qué: el número de identity se asigna al insertar, no al confirmar. Lo medí: una transacción lenta tomó el `Id` 1, una rápida el 2 y confirmó primero. Un relay que leyó `[2]` y guardó "último = 2" nunca ve el 1. El filtro por `ProcessedAt IS NULL` sí lo ve [EXPERIMENTO EX-D].
- Dudycz describe el mismo problema: el orden de los registros "may differ from the order of positions", y "If we optimistically assume that the gaps were caused by rollbacks, we may lose messages" ([FUENTE: Oskar Dudycz, ordering in Postgres outbox](https://event-driven.io/en/ordering_in_postgres_outbox/)).

### 3.4 Lote, reintentos y errores

| Parámetro | Valor recomendado | Por qué |
|---|---|---|
| `Outbox:PollInterval` | 1 s | El 24.1 mide "se pone al día después de la escritura"; 1 s es corto para leer y no carga la base con un índice parcial [INFERENCIA] |
| `Outbox:BatchSize` | 50 | Milan usa 100 y dice "You can make the limit a parameter" ([FUENTE](https://www.milanjovanovic.tech/blog/implementing-the-outbox-pattern)). 50 mantiene corta la transacción que tiene las filas tomadas [INFERENCIA] |
| `Outbox:MaxAttempts` | 5 | Después, `FailedAt` y un log de error. No es una DLQ: la fila queda en la tabla, visible y contable (sección 5) [INFERENCIA] |
| Espera entre intentos | 2^intento segundos (2, 4, 8, 16) | Backoff exponencial simple; los reintentos con Polly son del paso 29 [INFERENCIA] |
| `Outbox:Retention` | 7 días | Limpieza de procesadas (3.5) |
| `Outbox:RelayEnabled` | `true` (los tests lo apagan) | Sección 6 |

Se leen con options validadas al arrancar, como pide el paso 5 (`AddOptionsWithValidateOnStart`, `BackendInfrastructure.cs:24-25`).

- **Milan no reintenta:** si publicar falla, "the catch block still sets `processed_on_utc` and stores the exception text in the `error` column" ([FUENTE](https://www.milanjovanovic.tech/blog/implementing-the-outbox-pattern)). Eso cumple "el evento no se pierde del registro", pero no "queda pendiente", que es lo que pide el Verifica (`plan.md:421`). Por eso recomiendo `ProcessedAt` solo en el éxito.
- **eShop** marca estados `NotPublished`, `InProgress`, `Published`, `PublishedFailed` y cuenta `TimesSent` ([FUENTE: eShop, IntegrationEventLogService](https://github.com/dotnet/eShop/blob/main/src/IntegrationEventLogEF/Services/IntegrationEventLogService.cs)). Las columnas de 2.2 dicen lo mismo con fechas en vez de un enum.
- `LastError` guarda tipo y mensaje de la excepción, recortados (por ejemplo a 2000 caracteres). El stack completo va al log con el `MessageId` [INFERENCIA].

### 3.5 Limpieza

Cada N ciclos (por ejemplo una vez por minuto) el relay borra en lotes las filas con `ProcessedAt < now - Retention`. Es la misma tabla y el mismo dueño, y sin limpieza la tabla crece con cada escritura de tareas [INFERENCIA]. Las `FailedAt` no se borran solas: alguien tiene que mirarlas. `IdempotencyKeys` tiene el mismo hueco sin resolver (`progreso.md:132`); este paso no lo toca (regla 7).

### 3.6 Arranque y migraciones

- `AddBackendInfrastructure` registra `DatabaseInitialization` (`BackendInfrastructure.cs:32`) antes de `AddTasksFeature` (`Hosts/Tasks/Program.cs:14,16`). Los hosted services arrancan en orden de registro: "Hosted service instances start in the order that they're registered [...] unless the app opts into concurrent startup" ([FUENTE: Learn, hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)), y `ServicesStartConcurrently` vale `false` por defecto ([FUENTE: API HostOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.hostoptions.servicesstartconcurrently?view=net-10.0-pp)). Entonces el relay arranca con las migraciones aplicadas.
- En .NET 10, todo `ExecuteAsync` corre en segundo plano y "no part of it blocks other services from starting" ([FUENTE: breaking change .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task)). Bien para el relay: no frena el arranque de Kestrel.
- Los tests que quitan `DatabaseInitialization` (`TestDatabases.cs:36-41`) dejan el relay contra una base sin migrar: por eso el relay atrapa errores (3.1) y los tests lo apagan (sección 6).

## 4. Consumidores sin broker

### 4.1 Despacho en el mismo proceso

- Contrato en `Infrastructure/Messaging`: `IEventHandler<TEvent>` con `HandleAsync(TEvent, EventContext, CancellationToken)`. `EventContext` lleva `MessageId`, `OccurredAt` y la traza. La feature registra el tipo y su nombre estable (`services.AddOutboxEvent<TaskCreated>("tasks.task-created", version: 1)`) y sus consumidores (`services.AddEventHandler<TaskCreated, X>()`). Infrastructure no nombra ninguna feature (`architecture.md:35`, ADR-0004 punto 6).
- El relay deserializa el payload al tipo registrado y llama a cada consumidor **en su propio scope**. Así cada uno abre su contexto y su transacción [INFERENCIA, por [FUENTE: Learn, hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0#consuming-a-scoped-service-in-a-background-task)].
- Es un despachador casero, no una librería: ADR-0004 punto 9 rechaza librerías de bus o mediator hasta el paso 25, y el gate mira paquetes y `using` (`check-structure.mjs:206-220,272-283`). Ver choque 1.

### 4.2 Consumidor idempotente con inbox

- Tabla `InboxMessages` en el schema **del consumidor** (`MessageId uuid`, `Consumer text`, `ProcessedAt timestamptz`, PK `(MessageId, Consumer)`). La clave por consumidor sale de Richardson: "a `(subscriberId, messageID)` primary key" ([FUENTE](https://microservices.io/patterns/communication-style/idempotent-consumer.html)).
- Por qué por consumidor: si un evento tiene dos consumidores y uno falla, el relay reintenta la fila completa. El que ya terminó ve su fila en el inbox y no repite; el que falló vuelve a correr [INFERENCIA].
- Forma, dentro de una transacción del contexto del consumidor:
  1. `INSERT INTO "InboxMessages" ... ON CONFLICT DO NOTHING`.
  2. Si insertó 0 filas: ya se procesó, rollback y salir.
  3. Si insertó 1: aplicar el efecto con el mismo contexto y confirmar.
- Lo medí: el mismo mensaje dos veces da `first=True second=False` y 1 efecto. Ocho entregas **simultáneas** del mismo mensaje dan 1 consumido y 1 efecto: la segunda transacción espera a la primera en la clave primaria y después no inserta [EXPERIMENTO EX-E, EX-E2].
- Es el `ON CONFLICT DO NOTHING` que ya usa el filtro de idempotencia (`IdempotencyFilter.cs:46-52`): mismo SQL, otra tabla.
- Un helper `MapInbox()` en `Infrastructure/Messaging` la mapea en el contexto del consumidor, como `MapOutbox()` (choque 5).

### 4.3 ¿Qué consumidor existe en el paso 24?

| Opción | Gana | Pierde |
|---|---|---|
| **C1. Ninguno de producción; uno de test** (recomendada) | No adelanta el 24.1 (regla 8). El despachador, el inbox y el relay quedan probados con un consumidor que registra solo el test (`ConfigureTestServices`) y escribe su efecto en una tabla que crea el test | En producción el relay marca eventos sin consumidores: hoy no le sirven a nadie. El valor llega en el 24.1 |
| C2. Mover las métricas de `TaskMetrics` a un consumidor | Real y chico. Arregla la métrica fantasma (choque 2): solo cuenta hechos confirmados | El efecto no es de la base, así que el inbox no puede ir en la misma transacción que el efecto: entre confirmar el inbox y sumar la métrica, una caída la pierde. Cambia los tests del paso 14 (la métrica aparece hasta 1 s después) |
| C3. Adelantar la proyección | Consumidor de verdad con efecto en la base | Es el paso 24.1. Rompe las reglas 1 y 8 |

Recomiendo **C1**, y anotar la métrica fantasma como pendiente con C2 como salida posible.

### 4.4 Dónde vive cada pieza (ADR-0004)

| Pieza | Lugar | Motivo |
|---|---|---|
| `OutboxMessage`, `OutboxInterceptor`, `OutboxRelay`, `IEventHandler<T>`, `InboxMessage`, `MapOutbox`, `MapInbox`, `OutboxOptions`, `OutboxMetrics` | `backend/Infrastructure/Messaging/` (librería `TemplateApp.Platform`) | Fontanería técnica que no nombra ninguna feature (`architecture.md:35`) |
| `IHasDomainEvents` | `backend/Shared/Domain/` | Al lado de `IAuditable` (`Shared/Domain/IAuditable.cs`) |
| `TaskCreated`, `TaskCompleted`, `TaskReopened`, `TaskDeleted` | `backend/Features/Tasks/Domain/Events/` (`internal`) | Son hechos del dominio de Tasks. No van a `Contracts/`: DUNE0015 exige que otra feature **del mismo ensamblado** los use, y en `TemplateApp.Tasks.dll` no hay otra (`conventions.md:27`, decisión del paso 23.2) |
| Registro de nombres estables | `TasksFeature.cs` | Cada feature se registra sola (ADR-0004 punto 6) |
| Consumidor de prueba | `backend/Tests/Messaging/` | Solo existe en los tests |

Cuando un evento cruce de proceso (paso 25), el receptor escribe su propia copia del record, como hoy escribe sus DTOs del contrato HTTP (`conventions.md:27`). eShop hace lo mismo con los eventos de integración (paso 23 §4.1).

### 4.5 Evento de dominio o de integración

Learn separa los dos: los de dominio corren en el proceso, pueden ir en la misma transacción; los de integración "should occur only if the entity is successfully persisted" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)). Dudycz pide no publicar afuera los eventos internos y traducirlos a uno externo (`comunicacion-entre-features.md`, §1.2, [Dudycz](https://event-driven.io/en/internal_external_events/)).

| Opción | Gana | Pierde |
|---|---|---|
| **A. El evento de dominio va tal cual al outbox** (recomendada para este paso) | Un solo tipo y un solo concepto. Es lo que hace Milan ("convert domain events into outbox messages") | El mismo record sirve adentro y afuera. Cuando haya un receptor en otro proceso, cambiar el record lo rompe |
| B. Un handler traduce el evento de dominio a uno de integración, que va al outbox | Separa lo interno de lo externo (Dudycz, eShop) | Dos tipos por hecho y un handler más, sin ningún receptor externo todavía |

Recomiendo **A** en el 24 y **B** en el paso 25, cuando aparezca el primer receptor en otro proceso. El nombre estable más la versión (1.3) ya separan la fila del tipo CLR.

## 5. Observabilidad

### 5.1 Traza

- **Guardar:** el interceptor guarda `Activity.Current?.Id` en `TraceParent`. Con el formato W3C por defecto, es el `traceparent` de 55 caracteres: "version-format = trace-id "-" parent-id "-" trace-flags" ([FUENTE: W3C Trace Context](https://www.w3.org/TR/trace-context/)). Lo medí: el valor guardado es igual al `Id` de la actividad de la request [EXPERIMENTO EX-A].
- **Continuar:** el relay parsea el valor con `ActivityContext.TryParse` y abre una actividad `outbox process {type}` de tipo `Consumer`. Lo medí: misma `TraceId` y el `ParentSpanId` apunta al span de la request [EXPERIMENTO EX-F].
- **¿Padre o link?** La convención de mensajería de OpenTelemetry dice: "These conventions use spans links as the default mechanism to correlate producers and consumer(s)", y "Exclusively for single messages scenarios, the "Process" span MAY use the message's creation context as its parent". Está en estado "Development" ([FUENTE: OTel, messaging spans](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/)). Recomiendo **link** (el default), con un span `Consumer` por mensaje. Learn muestra cómo pasar links al crear la actividad ([FUENTE: Learn, distributed tracing instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)).
- **Registrar la fuente:** OpenTelemetry solo recoge las fuentes listadas. Hoy la lista es `"Npgsql", "TemplateApp.Api"` (`ObservabilityExtensions.cs:51`) y en el código no hay ningún `ActivitySource` llamado `TemplateApp.Api` [CÓDIGO, grep sin resultados]. El paso suma `"TemplateApp.Outbox"`. Learn: crear la fuente una vez y guardarla en un campo estático ([FUENTE](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)).

### 5.2 Métricas y logs

Meter `TemplateApp.Outbox`, creado con `IMeterFactory` (regla de `BannedSymbols.txt`, "Do not create a Meter directly"). `AddMeter("TemplateApp.*")` ya lo toma (`ObservabilityExtensions.cs:57`).

| Instrumento | Tipo | Qué responde |
|---|---|---|
| `templateapp.outbox.pending` | gauge | Cuántos pendientes vio el último ciclo |
| `templateapp.outbox.oldest_pending.age` | gauge, `s` | Cuánto lleva esperando el más viejo |
| `templateapp.outbox.delivery.lag` | histograma, `s` | `ProcessedAt - OccurredAt` de cada mensaje entregado: la consistencia eventual medida |
| `templateapp.outbox.delivery.failures` | counter, tag `type` | Intentos fallidos |
| `templateapp.outbox.failed` | gauge | Filas con `FailedAt`, que nadie va a reintentar |

- Los gauges los actualiza el relay en cada ciclo; el callback del gauge no consulta la base [INFERENCIA: una consulta por cada exportación cada 10 s, `ObservabilityExtensions.cs:60`, cargaría la base sin necesidad].
- Lo que describe un mensaje concreto (`MessageId`, tipo, error) va al log, no a la métrica: "Lo que describe un evento concreto (quién, qué id) va al log" (`conventions.md:133`).
- Log `Warning` en cada fallo con `MessageId` y `Attempts`; `Error` al marcar `FailedAt`.

### 5.3 Health y SLO

- **No en readiness.** Si "hay atraso" sacara la instancia del balanceo, se iría justo la que vacía el outbox [INFERENCIA]. Readiness sigue mirando base y migraciones (`BackendInfrastructure.cs:33-35`).
- **Alerta:** `templateapp_outbox_oldest_pending_age > 60` en Prometheus encaja con las reglas del paso 17 (`deploy/prometheus/rules/slo-api.rules.yml`). La dejo como pendiente: el paso no tiene consumidores de producción que puedan atrasarse, y agregarla amplía el alcance (regla 7).

## 6. El "Verifica" como tests

### 6.1 Cómo se arma en este repo

- PostgreSQL real con Testcontainers (`postgres:18`) o `TEST_POSTGRES_CONNECTION` (`PostgresFixture.cs:13-28`). Los dos hosts en memoria con `ApiFixture`/`ApiPair` (`ApiFixture.cs`).
- **El relay automático se apaga en los tests** (`Outbox:RelayEnabled=false` en la configuración del fixture) y cada test llama a `OutboxRelay.RunOnceAsync(ct)` resuelto del `TasksServices`. Si quedara prendido, el relay de cada host de la suite procesaría filas de otros tests en cualquier momento, y "el evento queda pendiente" sería una carrera [INFERENCIA, por `ApiFixture.cs:60-71`: todos los hosts comparten la base].
- `FailAfterSaving<TEntity>` ya existe: deja que `SaveChanges` guarde y después tira (`FailAfterSaving.cs:12-35`). Sirve para el evento fantasma.

### 6.2 Tests

| # | Test | Rojo antes del paso | Verde después |
|---|---|---|---|
| T1 | `POST /tasks` deja una fila `tasks.task-created` v1 con el `TaskId` de la respuesta, y su `TraceParent` tiene la `TraceId` de la request | Falla: la tabla no existe | Pasa |
| T2 | Completar, reabrir y borrar dejan su evento; `DELETE /internal/v1/owners/{id}/tasks` deja un `TaskDeleted` por tarea | Falla | Pasa |
| T3 | **(Verifica a) El relay falla y el evento queda pendiente sin perder el cambio.** Consumidor de test que tira. Crear tarea, `RunOnceAsync`. La tarea existe por `GET`; la fila tiene `ProcessedAt` null, `Attempts = 1` y `LastError` con el mensaje. Se arregla el consumidor, se avanza el `FakeTimeProvider` pasado `NextAttemptAt`, otro `RunOnceAsync`: `ProcessedAt` con fecha | Falla | Pasa |
| T4 | **(Verifica b) El mismo mensaje entregado dos veces se consume una vez.** Se entrega la misma fila dos veces (dos `RunOnceAsync` con un interceptor de test que hace fallar la marca de `ProcessedAt` la primera vez, que es lo que pasa si el relay se cae después del consumidor y antes de confirmar). Efectos del consumidor: 1. Filas de inbox: 1 | Falla | Pasa |
| T5 | Dos relays a la vez (dos `ApiPair` contra la misma base, `RunOnceAsync` en paralelo) sobre 20 filas: el consumidor se llama exactamente 20 veces | Falla | Pasa (EX-C) |
| T6 | **Rojo del concepto: evento fantasma.** `POST /tasks` con `Idempotency-Key` y `FailAfterSaving<TaskItem>`: responde 500, no hay tarea, no hay fila en el outbox y, después de `RunOnceAsync`, el consumidor no se llamó | Antes del paso no hay eventos, así que pasa vacío. El rojo se muestra con una **violación deliberada**: una versión que "publica" en `SavedChangesAsync` llama al consumidor aunque la tarea no exista (EX-B). Esa versión va en un commit de evidencia y se revierte | Pasa |
| T7 | Un `PUT` con versión vieja (409) y un replay de idempotencia no dejan eventos | Falla por la tabla | Pasa |
| T8 | Con `MaxAttempts` intentos fallidos, la fila queda con `FailedAt` y el relay no la vuelve a tomar | Falla | Pasa |

- T3 y T4 son las dos frases del Verifica (`plan.md:421`).
- El rojo de T1 a T5, T7 y T8 es "la tabla no existe". Para que el rojo pruebe algo más, T6 tiene su violación deliberada y T4 se corre también con el inbox apagado (el consumidor sin el `INSERT ... ON CONFLICT`): con eso da 2 efectos [NO PROBADO en el repo; EX-E lo muestra en el scratchpad].
- Las e2e del navegador quedan `NOT_RUN`: el paso no cambia un contrato HTTP ni el frontend (`AGENTS.md`, "Verificación").

## 7. Regla nueva de Dune (regla 4)

### 7.1 Recomendada: DUNE0023 "Consumidor de eventos llamado directo"

- **Qué mira:** una invocación de un método de un tipo que implementa `IEventHandler<T>` (o la construcción con `new` de ese tipo) fuera de `backend/Infrastructure/Messaging/OutboxRelay.cs`. Los tests quedan fuera, como en las demás reglas (`DunePaths.IsTestsFile`).
- **Por qué:** es la forma ejecutable de "publicar antes de confirmar". El relay solo lee filas confirmadas (3.3). Si nadie más llama a un consumidor, ningún efecto de un evento puede correr antes del commit [INFERENCIA].
- **Fixture válida:** el relay llamando `HandleAsync`. **Violación deliberada:** una slice que, después de `SaveChangesAsync`, resuelve el consumidor y llama `HandleAsync` (la versión de T6). Test en `Dune.Analyzers.Tests`, como `RemoteCallInTransactionAnalyzerTests.cs`.
- **Límite:** no ve una llamada por reflexión ni un delegado guardado. Lo anoto en la descripción, como hace DUNE0022 con el filtro.

### 7.2 Otras candidatas (no recomendadas ahora)

| Regla | Por qué no |
|---|---|
| "Toda entidad `IHasDomainEvents` se guarda en un contexto con outbox" | El interceptor ya tira en tiempo de ejecución (2.1) y T1/T2 lo cubren. Roslyn no sabe en qué contexto se agrega una entidad sin seguir el flujo |
| "Un evento solo se anota desde `Domain/`" | Con E1, el método que anota es privado de la entidad: el compilador ya lo impide |
| "Ningún `SavedChanges` publica" | Demasiado amplia: `FailAfterSaving` y la auditoría legítima usan esos ganchos |

### 7.3 Reglas que cambian de texto (regla 9)

- **DUNE0017** (`AuditBypass`): hoy dice que `ExecuteUpdate`/`ExecuteDelete` no dejan fila de auditoría. Desde este paso tampoco dejan evento. Cambia la descripción, no el código: `TaskItem` ya es `IAuditable`. Fixture: el test existente sigue.
- **DUNE0021**: su descripción dice que la única tabla compartida la mapea `MapSharedAuditEntries`. `MapOutbox`/`MapInbox` aplican una configuración de Infrastructure a un contexto de feature. El analyzer lo permite hoy (mira el archivo donde está `ApplyConfiguration`, `PersistenceOwnershipAnalyzer.cs:132-146`), pero la regla escrita no (choque 5). Se agrega una fixture válida con `MapOutbox()` en un contexto de feature.

## 8. Herramientas (regla 5)

**Ninguna nueva.**

| Pieza | ¿Nueva? | Nota |
|---|---|---|
| `SaveChangesInterceptor`, `Database.BeginTransactionAsync`, SQL con `FOR UPDATE SKIP LOCKED` vía `FromSql`/`SqlQuery` interpolado | No | EF Core 10.0.12 y Npgsql 10.0.3 (`TemplateApp.Platform.csproj`). El SQL va interpolado, como pide `BannedSymbols.txt` |
| `BackgroundService`, `PeriodicTimer`, `TimeProvider` | No | Framework compartido |
| `System.Text.Json` | No | Framework |
| `ActivitySource`, `IMeterFactory`, OpenTelemetry | No | Ya están (`ObservabilityExtensions.cs`) |
| `FakeTimeProvider` | No | `TemplateApp.Tests.csproj:15` |
| Librería de outbox o bus (Wolverine, MassTransit, CAP) | Sí | **No** se usa: es la decisión pendiente del paso 25 y el gate la rechaza |

No hay preguntas de herramienta para Tobi.

## Choques con los docs

1. **ADR-0004 punto 9 y `architecture.md:38`** ("No hay mediator ni bus. El paso 25 del plan elige uno"). El despachador del relay con `IEventHandler<T>` es un bus mínimo casero, en el proceso. No es una librería, así que el gate no se dispara. Recomiendo agregar a `architecture.md` una línea: "un despachador casero del outbox, sin librería, entrega eventos dentro del proceso hasta el paso 25".
2. **`conventions.md:137`** ("Se mide después del `SaveChangesAsync` que confirma el hecho"). Dentro del `TransactionScope` del filtro de idempotencia, `SaveChangesAsync` no confirma (`IdempotencyFilter.cs:44,77`). Si el filtro revierte después de la slice, `templateapp.tasks.created` ya contó una tarea que no existe (`CreateTask.cs:57-58`) [EXPERIMENTO EX-B, mismo mecanismo; NO PROBADO con la métrica]. Recomiendo corregir la frase ("después de confirmar; dentro de un scope, `SaveChanges` no confirma") y anotar la métrica fantasma como pendiente, con la opción C2 (4.3) como salida.
3. **`architecture.md:89`** ("un acople que el plan acepta hasta el outbox del paso 24") y **`progreso.md:197,239`** (la carrera de `DeleteUser` y el hueco del 23.2 "Lo cierra el evento de cuenta borrada (pasos 24 y 25)"). El evento de cuenta borrada nace en Users (monolito) y lo consume Tasks (otro proceso). Sin broker, este paso no tiene cómo llevarlo de un proceso a otro, y el "Cambia" del paso nombra solo `Features/Tasks` (`plan.md:420`). Recomiendo: el paso 24 no toca `DeleteUser` ni `CrossContextTransaction`; el evento de cuenta borrada queda para el paso 25, y se corrigen las dos frases.
4. **`plan.md:423-427` (paso 24.1)**. La proyección reemplaza una lectura de Projects (monolito) con eventos de Tasks (otro proceso), antes del broker. El 24.1 va a necesitar un transporte entre procesos sin broker (por ejemplo, Projects lee un endpoint interno de Tasks que expone el outbox) o poner la proyección en Tasks. No lo decido acá (regla 8). Lo que este paso deja para que cualquiera de las dos funcione: `MessageId` estable, nombre y versión en la fila, e inbox por consumidor. Recomiendo que la investigación del 24.1 lo trate como su primera pregunta.
5. **`conventions.md:94-95` y DUNE0021**: "`AuditEntries` es la única tabla que mapean varios contextos" y "cada contexto aplica [...] solo configuraciones de su feature". `MapOutbox()`/`MapInbox()` aplican una configuración de Infrastructure en un contexto de feature, aunque cada tabla queda en el schema de esa feature y la migra ella. Recomiendo agregar la excepción a `conventions.md` y a la descripción de DUNE0021, con su fixture (7.3).
6. **`comunicacion-entre-features.md` §4, propuesta 1** sugiere para eventos "pasado + `IntegrationEvent`". La decisión de Tobi del paso 6.3 (`decisions.tsv:131`) no incluye eventos, y DUNE0015 no los nombra. Recomiendo nombres en pasado sin sufijo (`TaskCreated`) para los eventos de dominio de este paso, y el sufijo `IntegrationEvent` para los tipos de la opción B en el paso 25 (4.5).
7. **`ObservabilityExtensions.cs:79`** escribe `service.name = "templateapp-api"` en los logs de los dos procesos, aunque el host de Tasks se llama `templateapp-tasks` (`Hosts/Tasks/Program.cs:15`). No choca con un doc, pero los logs del relay van a salir con el servicio equivocado. Fuera de alcance (regla 7): pendiente.

## Decisiones (con mi recomendación)

En modo autónomo cada una la toma el agente con esta recomendación y la anota en `docs/decisions.tsv`.

1. **Eventos y payload:** `TaskCreated`, `TaskCompleted`, `TaskReopened`, `TaskDeleted`, versión 1, sin título, sin `TaskRenamed`. Gana: payload chico y sin datos personales. Pierde: un consumidor futuro que quiera el título tiene que pedirlo (expand). Recomiendo: sí.
2. **Dónde nacen:** E1, la entidad anota (con setters privados en `TaskItem`, como `Project`). Gana: la regla en un lugar. Pierde: toca la entidad y tres slices. Alternativas: E2 (slice) y E3 (deducir del ChangeTracker). Recomiendo: E1.
3. **Cómo llegan al outbox:** `OutboxInterceptor` en `SavingChanges`, solo en contextos con outbox, vacía la lista en `SavedChanges`. Gana: mismo patrón que la auditoría y atomicidad medida (EX-A). Pierde: un mecanismo implícito más. Recomiendo: sí.
4. **Tabla:** `tasks."OutboxMessages"` con las columnas de 2.2, índice parcial, nombre estable más versión, `MessageId` v7. Alternativa: tabla en `platform` (la migraría el monolito). Recomiendo: schema de la feature.
5. **Dominio o integración:** opción A (el evento de dominio va tal cual) en este paso; B en el 25. Recomiendo: A.
6. **Reserva del lote:** `FOR UPDATE SKIP LOCKED` dentro de una transacción corta. Alternativa: un "lease" (columna `LockedUntil` que se marca y se libera), que no mantiene la transacción abierta mientras se entrega; conviene cuando entregar es lento (broker, paso 25). Recomiendo: `SKIP LOCKED` (EX-C).
7. **Pendiente y reintentos:** pendiente = sin `ProcessedAt` ni `FailedAt`; nunca un "último id" (EX-D). 5 intentos con espera exponencial; después `FailedAt`, sin DLQ. Alternativa: el estilo de Milan (marcar procesado con error, sin reintentar), que no cumple el Verifica. Recomiendo: reintentos.
8. **Limpieza:** el relay borra procesadas con más de 7 días; las fallidas quedan. Alternativa: dejarla como pendiente, como `IdempotencyKeys`. Recomiendo: hacerla en el paso.
9. **Consumidor del paso:** C1, ninguno de producción; uno de test. Alternativas: C2 (métricas) y C3 (proyección, prohibida por la regla 8). Recomiendo: C1.
10. **Inbox:** tabla en el schema del consumidor, PK `(MessageId, Consumer)`, `INSERT ... ON CONFLICT DO NOTHING` en la misma transacción que el efecto (EX-E, EX-E2). Recomiendo: sí.
11. **Traza:** `traceparent` en la fila; span `Consumer` por mensaje con link al contexto de creación (default de OTel); fuente `TemplateApp.Outbox` sumada a `AddSource`. Alternativa: usar el contexto como padre (permitido para un solo mensaje). Recomiendo: link.
12. **Métricas y health:** los cinco instrumentos de 5.2; nada en readiness; alerta de Prometheus como pendiente. Recomiendo: sí.
13. **Dune:** DUNE0023 (consumidor llamado solo por el relay) y los cambios de texto de DUNE0017 y DUNE0021. Recomiendo: sí.
14. **Tests:** relay apagado por configuración y `RunOnceAsync` explícito; T1 a T8, con la violación deliberada de T6 en un commit de evidencia. Recomiendo: sí.

## Pendientes para `progreso.md`

- Métrica fantasma: `templateapp.tasks.created` cuenta dentro del scope de idempotencia antes del commit (choque 2).
- Evento de cuenta borrada, carrera de `DeleteUser` con `POST /tasks` y hueco del 23.2: pasan al paso 25 (choque 3).
- Transporte entre procesos para la proyección del 24.1 (choque 4).
- Alerta de Prometheus por antigüedad del pendiente más viejo (5.3).
- Logs de Tasks con `service.name = templateapp-api` (choque 7).
- `TemplateApp.Api` está en `AddSource` y ningún código crea esa fuente.

## Experimentos

Todos corrieron en el scratchpad (`paso24-research/OutboxLab/Program.cs`, salida en `paso24-research/ex-run.log`) con el SDK 10.0.401, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 y PostgreSQL 18.6 en un contenedor `postgres:18` descartable. Un `DbContext` mínimo con una entidad `Item` que anota `ItemCreated`, una tabla `outbox`, una `inbox` y una `effects`.

- **EX-A.** `OutboxInterceptor` en `SavingChangesAsync` agrega una fila por evento; `SavedChangesAsync` vacía la lista. Resultado: `item xmin=756 outbox xmin=756 same_tx=True`; `TraceParent` igual al `Id` de la actividad de la request; 0 eventos en la entidad después de guardar. La primera corrida, sin `.ToList()` sobre `ChangeTracker.Entries()`, falló con `InvalidOperationException: Collection was modified`.
- **EX-B.** `TransactionScope` (`ReadCommitted`, flujo async), `SaveChangesAsync` y una excepción antes de `Complete()`. Un interceptor "ingenuo" que publica en `SavedChangesAsync` publicó 1 evento. En la base: 0 filas de la tarea y 0 del outbox. Es el evento fantasma.
- **EX-C.** Seis filas pendientes, dos conexiones, cada una en su transacción con `LIMIT 4 FOR UPDATE SKIP LOCKED`: relay 1 `[1,2,3,4]`, relay 2 `[5,6]`, espera 2 ms. Con `FOR UPDATE NOWAIT`: `55P03 could not obtain lock on row in relation "outbox"`.
- **EX-D.** Transacción lenta inserta (`Id` 1), rápida inserta (`Id` 2) y confirma. Primera lectura: `[2]`. Después confirma la lenta. Lectura por "último id > 2": `[]` (se perdió el 1). Lectura por `ProcessedAt IS NULL`: `[1]`.
- **EX-E.** Consumidor con `INSERT INTO inbox ... ON CONFLICT DO NOTHING` y efecto en la misma transacción. Mismo mensaje dos veces: `first=True second=False effects=1`.
- **EX-E2.** Ocho entregas simultáneas del mismo mensaje (`Task.WhenAll`): `consumed=1 effects=1`.
- **EX-F.** `ActivityContext.TryParse` del `traceparent` guardado y una actividad `Consumer` con ese padre: `same_trace=True parent_span_matches=True`.

No probé: el relay dentro del host real de Tasks, el orden de arranque con `DatabaseInitialization` (lo apoyo en la documentación), el link de traza exportado al collector, la métrica fantasma con la métrica real y nada con dos procesos reales.
