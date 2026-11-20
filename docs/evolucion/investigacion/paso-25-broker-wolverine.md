# Paso 25: broker real con Wolverine y RabbitMQ (investigación)

- Fecha: 2026-11-19.
- Estado: **Listo** como investigación. Las herramientas ya las eligió Tobi (Wolverine y RabbitMQ, `docs/decisions.tsv`, últimas dos filas). Quedan tres piezas menores (paquetes de la librería y del broker elegidos) que anoto para Tobi en la sección "Herramientas". Hay 7 choques con los docs y 16 decisiones, cada una con mi recomendación.
- Alcance: el paso 25 del [plan](../plan.md) (`plan.md:431-434`). Concepto: entrega al menos una vez, deduplicación, mensajes venenosos y orden por partición. Cambia: `compose.yaml`, adaptador de mensajería, relay del outbox, health de la dependencia y tests con el broker en un contenedor. Verifica: test de reintento, test de DLQ y un test que mata al consumidor a mitad y el mensaje se reprocesa sin efecto duplicado.
- Base: `main` en `f4fa3ff` (merge del PR #86, paso 24.1).
- Límites con los pasos vecinos: las sagas son de los pasos 26 y 27, y la resiliencia de los clientes HTTP del paso 29. Este paso no adelanta ninguno (regla 8).

**Cómo leer las marcas:**
- `[FUENTE]`: lo leí en un documento primario (docs de Wolverine, código de Wolverine en la etiqueta `V6.64.2`, docs de RabbitMQ, NuGet, Testcontainers, microservices.io). Cada uno tiene su link.
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `f4fa3ff`.
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Respuesta corta

1. **Qué cambia para Tobi.** Hoy, si el monolito se cae más de unos 68 minutos, los eventos de tareas de Tasks se marcan `FailedAt` y la proyección de contadores queda mal hasta reconstruirla (`architecture.md:76`, `progreso.md:258`). Después del paso, Tasks deja cada evento en una cola de RabbitMQ. Si el monolito se cae una hora o un día, los eventos esperan en la cola y llegan cuando vuelve. Un mensaje roto (una versión que nadie entiende) va a una cola de mensajes muertos en el primer intento, en vez de gastar 12 reintentos.
2. **Outbox (pregunta 1).** Recomiendo **conservar el outbox casero del paso 24** (tablas, interceptor, relay con `FOR UPDATE SKIP LOCKED`, métricas) y usar Wolverine **solo como transporte**: el consumidor que hoy reenvía por HTTP (`TaskEventForwarder`) pasa a publicar en RabbitMQ. No se usa el outbox ni el inbox durable de Wolverine. La razón principal: el outbox de Wolverine manda el mensaje justo después de `SaveChangesAsync`, y en este repo `CreateTask` corre dentro del `TransactionScope` del filtro de idempotencia, donde `SaveChanges` todavía no confirma. Es el error exacto que el paso 24 vino a evitar (`architecture.md:94`).
3. **Qué pasa al broker (pregunta 2).** En el paso 25, solo los cuatro eventos de tarea (Tasks → monolito). El evento de cuenta borrada (Users → Tasks) va en un paso 25.1, porque necesita un outbox nuevo en Users y cambia `DeleteUser`. Los mensajes del broker son **eventos de integración**, con sufijo `IntegrationEvent`, y cada lado escribe su propia copia `internal` (como hoy con los `*Request` del paso 24.1). No van en `Contracts/`.
4. **Consumidor idempotente (pregunta 3).** El listener de RabbitMQ corre en modo `Inline` (Wolverine confirma el mensaje al broker recién después de que el handler termina) y el handler usa el inbox del repo (`Inbox.TryAcceptAsync`) en la misma transacción que su efecto. Con eso hay "al menos una vez" de punta a punta: confirmación del broker al publicar (*publisher confirms*, apagadas por defecto en Wolverine, hay que prenderlas) y confirmación del consumidor después del efecto.
5. **Reintento vs DLQ (pregunta 4).** Dos reglas separadas. Destino caído: del lado de Tasks, si RabbitMQ no responde, el relay reintenta sin límite y no marca `FailedAt`. Del lado del monolito, un error transitorio (la base caída, la proyección en reconstrucción) se reintenta con espera y después pausa el listener. Mensaje venenoso: un error que no se arregla reintentando va directo a la DLQ nativa de RabbitMQ.
6. **Orden (pregunta 5).** Lo mínimo que enseña el concepto: una cola por consumidor con *single active consumer* (un solo consumidor activo por cola, aunque haya dos monolitos). La cola es la partición. Repartir en varias particiones por `TaskId` con el exchange *modulus hash* queda como pendiente.
7. **Sin mediator (pregunta 6).** Los handlers de Wolverine no viven en las features. `Infrastructure/Messaging` registra un handler propio por evento de integración, sin generación de código, que llama a la operación de la slice. Ninguna slice llama a `IMessageBus.InvokeAsync`. Una regla nueva de Dune lo hace cumplir.
8. **Paquetes.** `WolverineFx.RabbitMQ` 6.64.2 (trae `WolverineFx` 6.64.2 y `RabbitMQ.Client` 7.2.2) en `TemplateApp.Platform`, y `Testcontainers.RabbitMq` 4.17.0 en los tests. Imagen `rabbitmq:4.4.1-management`. No hacen falta `WolverineFx.EntityFrameworkCore` ni `WolverineFx.Postgresql`.

## Glosario corto

- **Broker** (= intermediario de mensajes): un servidor que guarda mensajes en colas hasta que un consumidor los procesa. Acá, RabbitMQ.
- **Entrega al menos una vez** (*at-least-once*): un mensaje puede llegar dos veces, pero no se pierde. RabbitMQ: "consumers must be prepared to handle redeliveries and otherwise be implemented with idempotence in mind" ([FUENTE: RabbitMQ, Confirms](https://www.rabbitmq.com/docs/confirms)).
- **Publisher confirm** (= acuse del broker al que publica): RabbitMQ avisa al productor que guardó el mensaje. "In order to guarantee persistence, a client should use confirms" ([FUENTE: RabbitMQ, Confirms](https://www.rabbitmq.com/docs/confirms)).
- **Ack del consumidor** (= acuse del consumidor al broker): el consumidor avisa que terminó. Si el canal se cierra antes, "any delivery (message) that was not acked is automatically requeued" ([FUENTE: RabbitMQ, Confirms](https://www.rabbitmq.com/docs/confirms)).
- **Consumidor idempotente**: "Make a consumer idempotent by having it record the IDs of processed messages in the database" ([FUENTE: microservices.io, Idempotent Consumer](https://microservices.io/patterns/communication-style/idempotent-consumer.html)). Es el inbox del paso 24 (`Inbox.cs:8-28`).
- **Mensaje venenoso** (*poison message*): un mensaje que falla siempre. RabbitMQ lo define para colas quorum como "messages that cause a consumer to repeatedly requeue a delivery" ([FUENTE: RabbitMQ, Quorum Queues](https://www.rabbitmq.com/docs/quorum-queues)).
- **DLQ** (*dead letter queue*, = cola de mensajes muertos): la cola donde termina un mensaje que no se va a reintentar. En RabbitMQ la alimenta un *dead letter exchange* (DLX) ([FUENTE: RabbitMQ, Dead Letter Exchanges](https://www.rabbitmq.com/docs/dlx)).
- **Cola quorum**: el tipo de cola replicada de RabbitMQ 4.x. Cuenta las reentregas y desde 4.0 tiene un límite de 20 por defecto ([FUENTE: RabbitMQ, Quorum Queues](https://www.rabbitmq.com/docs/quorum-queues)).
- **Single active consumer** (SAC): "makes it possible to only have one consumer at a time consuming from a queue" ([FUENTE: RabbitMQ, Consumers](https://www.rabbitmq.com/docs/consumers)).
- **Evento de integración**: el mensaje que cruza de proceso. Es un contrato entre dos despliegues, distinto del evento de dominio, que es interno de la feature (`conventions.md:106`).

---

## 1. Outbox: el de Wolverine o el casero con un transporte nuevo

### 1.1 Cómo está hoy

- `OutboxInterceptor` convierte los eventos de `TaskItem` en filas de `tasks."OutboxMessages"` en el mismo `SaveChanges` que el cambio (`OutboxInterceptor.cs:63-77`).
- `OutboxRelay` es un `BackgroundService` por contexto. Toma hasta 50 filas con `FOR UPDATE SKIP LOCKED` (`OutboxRelay.cs:96-107`), llama a cada `IEventHandler<T>` registrado (`OutboxRelay.cs:121-130`) y marca `ProcessedAt`. Si un consumidor falla, suma `Attempts` y espera 2^n segundos; al agotar `MaxAttempts` marca `FailedAt` (`OutboxRelay.cs:143-161`). En Tasks, `MaxAttempts` es 12 (`Hosts/Tasks/appsettings.json`).
- El único consumidor de producción es `TaskEventForwarder`, que manda cada evento al monolito con `PUT /internal/v1/task-events/{messageId}` (`TaskEventForwarder.cs:19-25`, `TaskEventsClient.cs:20-33`). El monolito lo aplica con `ApplyTaskEvent`, que deduplica con `projects."InboxMessages"` (`ApplyTaskEvent.cs:33-53`).
- `CreateTask` corre dentro del `TransactionScope` del filtro de idempotencia: ahí `SaveChanges` no confirma (`architecture.md:94`).
- Dos procesos comparten una base, con un schema por feature (`architecture.md:50`). Cada feature es dueña de su outbox y su inbox (`conventions.md:95`).

### 1.2 Opciones

| Opción | Cómo | Qué se borra | Tablas nuevas |
|---|---|---|---|
| **(A) Outbox e inbox durables de Wolverine** | `PersistMessagesWithPostgresql`, `UseEntityFrameworkCoreTransactions`, `IDbContextOutbox<TasksDbContext>` en cada slice que escribe, y el inbox durable en los listeners | `OutboxInterceptor`, `OutboxRelay`, `OutboxMessages`, `InboxMessages`, `OutboxMetrics`, DUNE0023 | Tablas de Wolverine en un schema por proceso |
| **(B) Outbox casero + Wolverine como transporte** (recomendada) | El relay sigue igual. `TaskEventForwarder` pasa a `TaskEventPublisher`: publica el evento de integración en RabbitMQ en vez de llamar por HTTP. El consumidor del monolito es un listener de Wolverine en modo `Inline` que usa el inbox del repo | El reenvío por HTTP (`TaskEventsClient`, `ForwardedTaskEventRequest`, `ApplyTaskEventEndpoint`) | Ninguna |
| (C) (B) más el inbox durable de Wolverine en el consumidor | Como (B), pero el listener usa `UseDurableInbox()` | Lo mismo que (B) | Tablas de Wolverine en el monolito |

**Gana y pierde de cada una:**

- **(A)** Gana: es la forma idiomática de Wolverine. Wolverine guarda los mensajes salientes "as part of Wolverine's outbox support" en el mismo `SaveChanges` ([FUENTE: Wolverine, EF Core transactional middleware](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html)) y da DLQ en base con reenvío por columna `replayable` ([FUENTE: Wolverine, Dead Letter Storage](https://wolverinefx.net/guide/durability/dead-letter-storage.html)). Pierde, en este repo:
  - **Publica antes de confirmar dentro del `TransactionScope`.** El middleware "will both call `DbContext.SaveChangesAsync()` and flush any persisted messages for you" ([FUENTE: Wolverine, EF Core](https://wolverinefx.net/guide/durability/efcore/)). Fuera de un handler, el camino es `IDbContextOutbox<T>.SaveChangesAndFlushMessagesAsync()` ([FUENTE: Wolverine, Outbox and Inbox](https://wolverinefx.net/guide/durability/efcore/outbox-and-inbox.html)). Dentro del `TransactionScope` del filtro de idempotencia, ese `SaveChanges` no confirma, y el *flush* manda el mensaje al broker antes del commit real. Si el filtro revierte, el mensaje ya salió [INFERENCIA, por `architecture.md:94` y las dos citas]. Es el error que el paso 24 vino a evitar, y que DUNE0023 hace ejecutable (`conventions.md:109`).
  - **Los eventos de dominio dependen de handlers de Wolverine.** El raspado de eventos de las entidades "is completely reliant on the EF Core transactional middleware" ([FUENTE: Wolverine, Domain Events](https://wolverinefx.net/guide/durability/efcore/domain-events.html)). Las slices de este repo son endpoints de Minimal API con su clase de operación, no handlers de Wolverine (ADR-0004 punto 9). Cada slice que escribe tendría que pedir `IDbContextOutbox<T>` en vez de su `DbContext`.
  - **Tablas fuera del schema de la feature.** Wolverine guarda sus mensajes en sus propias tablas (`wolverine_outgoing_envelopes`, `wolverine_dead_letters`, [FUENTE: Wolverine, Durability](https://wolverinefx.net/guide/durability/)) y pide un schema por aplicación lógica: "If separate logical services are targeting the same physical database, you should use a separate schema name for each logical application" ([FUENTE: Wolverine, PostgreSQL](https://wolverinefx.net/guide/durability/postgresql.html)). Choca con `conventions.md:95` ("`OutboxMessages` e `InboxMessages` no son tablas compartidas", cada feature las migra en su schema). Ver choque 1.
  - **El inbox de Wolverine olvida rápido.** Guarda los mensajes procesados "for a default of 5 minutes" ([FUENTE: Wolverine, Idempotency](https://wolverinefx.net/guide/durability/idempotency.html)). El inbox del repo no borra nunca (`progreso.md`, pendiente "Ningún inbox se vacía"), y la reconstrucción del 24.1 depende de eso (decisión de la revisión independiente del 24.1, `docs/decisions.tsv`).
  - Borra el código y las métricas del paso 24 y del 24.1, con sus tests (`OutboxRelayTests`, `RebuildTaskCountersTests`).
- **(B)** Gana: el relay sigue leyendo solo filas confirmadas, así que publicar después del commit sigue garantizado por construcción (DUNE0023 vale igual). No hay tablas nuevas ni migraciones. Reusa `SKIP LOCKED`, `Attempts`, `FailedAt`, métricas y el inbox de cada feature. Es la forma que ya describía la investigación del 24.1: "en el 25 el relay publica al broker en vez de llamar por HTTP, y el monolito consume de una cola con el mismo inbox y el mismo efecto" (`paso-24-1-proyeccion-eventos.md`, sección 1.2). Pierde: no usa la DLQ en base ni el reenvío por columna de Wolverine (la DLQ es la nativa de RabbitMQ, sección 4). Hay que prender a mano las *publisher confirms* (sección 3).
- **(C)** Gana: Wolverine guarda el mensaje en la base antes de procesarlo. Pierde: suma las tablas de (A) en el monolito para algo que el inbox del repo ya hace, y con una ventana de 5 minutos en vez de permanente.

### 1.3 Recomendación

**(B).** Si el repo se diseñara hoy desde cero con Wolverine, con handlers de Wolverine en vez de slices de Minimal API, (A) sería lo natural. Pero el repo tiene dos reglas que (A) rompe: publicar solo lo confirmado aunque haya un `TransactionScope` alrededor (paso 24) y cada feature dueña de sus tablas (paso 22). (B) es además lo que el plan pide: "Cambia: [...] relay del outbox" (`plan.md:433`), no "reemplaza el outbox".

### 1.4 Qué se borra, qué queda y cómo conviven los dos procesos

- **Queda tal cual:** `OutboxInterceptor`, `OutboxMessage`, `InboxMessage`, `Inbox`, `MappedTable`, `MessagingTables`, `OutboxEvents`, `OutboxMetrics`, `OutboxTracing` y las migraciones `AddOutboxMessages` y `AddInboxMessages` de Tasks y el inbox de Projects. Ninguna migración nueva.
- **Cambia:** `OutboxRelay` separa "destino caído" de "mensaje roto" (sección 4.3). `TaskEventForwarder` pasa a `TaskEventPublisher`.
- **Se borra en el último commit** (expand/contract, como el 24.1): `TaskEventsClient`, `ForwardedTaskEventRequest`, `ApplyTaskEventEndpoint` y su ruta `PUT /internal/v1/task-events/{messageId}` (`ApplyTaskEventEndpoint.cs:8`), y la fila de esa ruta en `architecture.md:66`.
- **Dos procesos, una base:** sin las tablas de Wolverine no hay nada nuevo que compartir. Cada proceso abre su conexión a RabbitMQ. Wolverine abre por defecto "two connections, one for listening and another for sending messages" ([FUENTE: Wolverine, RabbitMQ](https://wolverinefx.net/guide/messaging/transports/rabbitmq/)). Tasks solo publica (puede usar `UseSenderConnectionOnly()`, misma fuente); el monolito solo escucha en el paso 25.

### 1.5 Cómo se ve (B) en el código

| Pieza | Lugar | Qué hace |
|---|---|---|
| `AddMessaging(...)` | `Infrastructure/Messaging/` | Registra Wolverine con `services.AddWolverine(...)` ([FUENTE: código, `HostBuilderExtensions.cs:78`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/HostBuilderExtensions.cs)), la conexión a RabbitMQ desde la configuración `Messaging:RabbitMq`, las confirms, las políticas de error y el modo de código (sección 6) |
| `AddIntegrationEvent<T>(name, version)` | `Infrastructure/Messaging/` | Le da al tipo un nombre estable en el cable con `RegisterMessageType(Type, string)` ([FUENTE: código, `WolverineOptions.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/WolverineOptions.cs)), igual que `AddOutboxEvent` lo hace en la tabla |
| `TaskEventPublisher` | `Features/Tasks/PublishTaskEvents/` | `IEventHandler<TaskCreated>` y los otros tres. Traduce el evento de dominio a su `*IntegrationEvent` y lo publica con un publicador de `Infrastructure` |
| `TaskCreatedIntegrationEvent` (y tres más) | `Features/Tasks/PublishTaskEvents/` | Copia del emisor, `internal`. Ids y fechas, como el evento de dominio |
| `TaskCreatedIntegrationEvent` (copia del receptor) | `Features/Projects/ApplyTaskEvent/` | Solo los campos que lee (`ProjectId`, `WasCompleted`) |
| Handler de Projects | `Features/Projects/ApplyTaskEvent/` | Recibe cada evento y llama a `ApplyTaskEvent.ExecuteAsync` con el `MessageId` del sobre de Wolverine |

---

## 2. Qué flujos pasan al broker y dónde viven los contratos

### 2.1 Los dos flujos

| Flujo | Hoy | En el paso | Por qué |
|---|---|---|---|
| Eventos de tarea, Tasks → monolito | `PUT /internal/v1/task-events/{messageId}` desde el relay de Tasks (`TaskEventsClient.cs:28-32`) | **25**: cola `projects.task-counters` | Es el flujo que ya existe. Cambiar su transporte es el cambio observable del paso: el monolito caído un día ya no rompe la proyección |
| Cuenta borrada, Users (monolito) → Tasks | `DeleteUser` llama a Tasks por HTTP antes de su transacción (`DeleteUser.cs:40-47`, `OwnedTasksClient.cs:15-19`) | **25.1** (propuesto) | Pide un outbox nuevo en `users`, un evento de dominio en `UserProfile`, un consumidor con inbox en Tasks y borrar `DELETE /internal/v1/owners/{id}/tasks`. Es otro cambio observable: borrar la cuenta con Tasks caído responde 204 en vez de 503 |

Los pendientes que van con la cuenta borrada (`progreso.md:196`, `:197`, `:201`, `:237`, `:246`) se mueven al 25.1. Uno no se cierra solo con el evento: la carrera de `DeleteUser` con `POST /tasks` (`progreso.md:197`). Si el `POST` ya pasó la validación de sesión y guarda después de que el consumidor borró las tareas, la tarea queda huérfana igual [INFERENCIA]. La salida habitual es una lápida (*tombstone*): el consumidor anota el dueño borrado en `tasks` y `CreateTask` la consulta en su transacción. Lo dejo propuesto para el 25.1.

### 2.2 Evento de dominio frente a evento de integración

- `conventions.md:106` ya lo decidió: "El sufijo `IntegrationEvent` queda para el contrato de un bus (paso 25)". El evento de dominio (`TaskCreated`) sigue en `Domain/Events/`, `internal`, sin sufijo.
- La traducción vive en el consumidor que publica (`TaskEventPublisher`), no en el relay. Así el evento de dominio puede cambiar sin romper al otro proceso, y el contrato cambia solo con versión [INFERENCIA].
- Nombre y versión en el cable: Wolverine identifica cada tipo por un alias, por defecto "the .NET full name", y se puede fijar ([FUENTE: Wolverine, Messages](https://wolverinefx.net/guide/messages.html)). Recomiendo el mismo nombre estable del outbox (`tasks.task-created`) y versión 1. Con el alias fijo, el emisor y el receptor pueden tener tipos de C# distintos, cada uno en su feature.

### 2.3 Dónde viven los contratos

- **No van en `Contracts/`.** `Contracts/` es lo que usa otra feature del mismo ensamblado (ADR-0004 punto 5, DUNE0020). El monolito no compila el código de Tasks (`Hosts/Tasks/TemplateApp.Tasks.csproj` compila solo `Features/Tasks` y `Features/Health`; regla "límite de despliegue" en `check-structure.mjs:114`). Un tipo en `Features/Tasks/Contracts/` no llegaría al monolito.
- **No van en `Shared/` ni en `Infrastructure/`**: serían código de feature fuera de su feature (ADR-0004, contexto).
- **Cada lado escribe su copia `internal`**, con su sufijo `IntegrationEvent`, como hoy hace el 24.1 con los `*Request` (`conventions.md:106`). El alias y la versión son el contrato.

---

## 3. Consumidor idempotente y entrega al menos una vez

### 3.1 La cadena completa

| Tramo | Quién garantiza | Cómo |
|---|---|---|
| Request → fila del outbox | PostgreSQL | La fila viaja en el mismo `SaveChanges` (`OutboxInterceptor.cs:63-77`) |
| Fila → RabbitMQ | El relay + publisher confirms | El relay marca `ProcessedAt` solo si la publicación volvió bien (`OutboxRelay.cs:126-133`). Con un endpoint `Inline`, "calling `IMessageBus.SendAsync()` immediately sends the message to the external message broker" ([FUENTE: Wolverine, Runtime](https://wolverinefx.net/guide/runtime.html)) |
| RabbitMQ → efecto | El ack después del handler + el inbox | En `Inline`, los mensajes "are processed inline before Wolverine acknowledges to the message broker" (misma fuente). El handler inserta en el inbox y aplica el efecto en una transacción (`ApplyTaskEvent.cs:33-53`) |

### 3.2 Publisher confirms: apagadas por defecto

- En Wolverine 6.64.2, `PublisherConfirmationsEnabled` y `PublisherConfirmationTrackingEnabled` valen `false` ([FUENTE: código, `WolverineRabbitMqChannelOptions.cs:14,21`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Transports/RabbitMQ/Wolverine.RabbitMQ/WolverineRabbitMqChannelOptions.cs)). El plan de rendimiento del propio repo de Wolverine lo describe: "Confirms off = fastest but silently lossy on broker failure" ([FUENTE: JasperFx/wolverine, `RABBITMQ-PERF-DEEP-DIVE-PLAN.md`](https://github.com/JasperFx/wolverine/blob/main/RABBITMQ-PERF-DEEP-DIVE-PLAN.md)).
- Se prenden con `ConfigureChannelCreation(o => { o.PublisherConfirmationsEnabled = true; o.PublisherConfirmationTrackingEnabled = true; })` ([FUENTE: Wolverine, RabbitMQ](https://wolverinefx.net/guide/messaging/transports/rabbitmq/)).
- Sin confirms, el relay podría marcar `ProcessedAt` de un mensaje que RabbitMQ perdió, y el paso no cumpliría "al menos una vez" [INFERENCIA].
- [NO PROBADO] que `SendAsync` en un endpoint `Inline` con confirms tire una excepción cuando RabbitMQ rechaza o no confirma. El paso lo prueba (test T3, sección 8).

### 3.3 Inbox: el del repo, no el de Wolverine

- El inbox del repo hace exactamente el patrón de microservices.io: "the message handler inserts the message's ID into the `PROCESSED_MESSAGE` table" en la misma transacción ([FUENTE: microservices.io](https://microservices.io/patterns/communication-style/idempotent-consumer.html), `Inbox.cs:16-27`).
- El `MessageId` sigue siendo el del outbox de Tasks (uuid v7, `OutboxInterceptor.cs:84`). El publicador lo pone como id del sobre de Wolverine y el handler lo lee del sobre [NO PROBADO: cómo fijar el id del sobre al publicar; si no se puede, va como campo del evento de integración].
- Con el modo `Inline` no hay inbox de Wolverine. Wolverine lo avisa para un caso parecido: "Native ack mode has no inbox, so handlers must be idempotent" ([FUENTE: Wolverine, Partitioning](https://wolverinefx.net/guide/messaging/partitioning.html)). Acá el handler ya es idempotente.

### 3.4 Un riesgo que el broker abre en la reconstrucción del 24.1

- La foto de Tasks devuelve como "sin entregar" los `MessageId` con `ProcessedAt` en null (`SnapshotTaskCounts.cs:26-29`). El monolito los siembra en su inbox porque su efecto ya está en la foto (`architecture.md:111`).
- Con el broker, `ProcessedAt` pasa a querer decir "publicado en RabbitMQ", no "aplicado por el monolito". Un mensaje publicado que todavía espera en la cola no está en la lista, su efecto sí está en la foto, y al llegar se cuenta dos veces [INFERENCIA, por el código citado].
- Arreglo mínimo: la foto devuelve todos los `MessageId` del outbox, procesados o no. Las filas procesadas se borran a los 7 días (`OutboxOptions.cs:21`), así que el arreglo supone que ningún mensaje espera en la cola más de 7 días. Lo anoto como decisión 9 y como test T7.

---

## 4. Reintento frente a DLQ

### 4.1 Qué ofrece cada uno

- **Wolverine.** Sin política propia, un mensaje se reencola hasta 3 intentos: `MaximumAttempts ?? parent.MaximumAttempts ?? 3` ([FUENTE: código, `FailureRuleCollection.cs:30`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/ErrorHandling/FailureRuleCollection.cs)). Después va a la cola de error: "By default, a message will be moved to an error queue when it exhausts all its configured retry/requeue slots" ([FUENTE: Wolverine, Error Handling](https://wolverinefx.net/guide/handlers/error-handling.html)). Las reglas se evalúan en orden por tipo de excepción (`OnException<T>()`), con `RetryWithCooldown`, `Requeue`, `MoveToErrorQueue`, `Discard` y la pausa del listener (`AndPauseProcessing`) o un *circuit breaker* por endpoint (misma fuente).
- **RabbitMQ.** Un mensaje va al DLX con un `nack` sin reencolar, por TTL, por largo de cola o por pasar el `delivery-limit` de una cola quorum ([FUENTE: RabbitMQ, DLX](https://www.rabbitmq.com/docs/dlx)). La cola quorum tiene `delivery-limit` 20 por defecto desde 4.0; al pasarlo, sin DLX el mensaje "will be dropped" ([FUENTE: RabbitMQ, Quorum Queues](https://www.rabbitmq.com/docs/quorum-queues)).
- **Wolverine + RabbitMQ.** "By default, Wolverine uses RabbitMQ's native NACK mechanism to move failed messages to the dead letter exchange", con una cola `wolverine-dead-letter-queue` para toda la aplicación ([FUENTE: Wolverine, RabbitMQ Dead Letter Queues](https://wolverinefx.net/guide/messaging/transports/rabbitmq/deadletterqueues.html)). La DLQ en base (`wolverine_dead_letters`, con reenvío por columna `replayable` o `dotnet run -- storage replay`) pide las tablas de Wolverine ([FUENTE: Wolverine, Dead Letter Storage](https://wolverinefx.net/guide/durability/dead-letter-storage.html)), que la opción (B) no tiene.

### 4.2 Recomendación del lado del consumidor (monolito)

| Error | Ejemplo en el repo | Regla de Wolverine | Por qué |
|---|---|---|---|
| Mensaje roto | Versión desconocida o payload vacío (`ApplyTaskEvent.cs:30-31`, hoy un 400 `Project.UnreadableTaskEvent`); JSON que no se deserializa | `MoveToErrorQueue()` en el primer intento | Reintentar no lo arregla. Es el "mensaje venenoso" del concepto |
| Destino caído | La base del monolito no responde (`NpgsqlException`, `TimeoutException`); la proyección en reconstrucción (hoy un 503 `Project.TaskCountersRebuilding`, `ApplyTaskEvent.cs:34-35`) | `RetryWithCooldown(1 s, 5 s, 30 s)` y después `Requeue().AndPauseProcessing(1 min)` | El mensaje está bien; el que falta es el destino. Pausar el listener deja los mensajes en la cola en vez de gastarlos |
| Cualquier otra excepción | Un bug | Las 3 vueltas por defecto y después la DLQ | Un bug se arregla con un release y el mensaje se reenvía desde la DLQ |

- La operación devuelve hoy un `ErrorOr`, no una excepción (`ApplyTaskEvent.cs:24-55`). El handler la traduce: `UnreadableTaskEvent` → una excepción propia de mensaje roto; `TaskCountersRebuilding` → una excepción propia transitoria. Las dos viven en `Infrastructure/Messaging` [INFERENCIA, diseño].
- **DLQ por cola, no una para toda la app.** Recomiendo una DLQ por consumidor (`projects.task-counters.dlq`), así un operador sabe de qué consumidor es el mensaje [INFERENCIA]. Wolverine permite sobreescribir la DLQ por cola (misma fuente de Dead Letter Queues).
- **Reenviar desde la DLQ.** RabbitMQ mueve mensajes entre colas con un *shovel* dinámico (plugin `rabbitmq_shovel`; "When in doubt, prefer dynamic shovels", [FUENTE: RabbitMQ, Shovel](https://www.rabbitmq.com/docs/shovel)). El paso no automatiza el reenvío: queda como runbook y pendiente. [NO VERIFICADO] que la imagen `management` traiga el plugin de shovel prendido.

### 4.3 Recomendación del lado del productor (relay de Tasks)

- Hoy un fallo de entrega cuenta un intento siempre (`OutboxRelay.cs:143-161`). Con el broker, el destino del relay es RabbitMQ, no el monolito. El monolito caído ya no hace fallar al relay, porque la cola guarda los mensajes.
- **RabbitMQ caído** (no conecta, no confirma): la fila queda pendiente con `NextAttemptAt` y `LastError`, pero **no suma `Attempts` hacia `FailedAt`**. La espera crece hasta un tope (por ejemplo 60 s) y sigue sin límite. Es "reintentar sin límite lo primero" (`progreso.md:258`).
- **Mensaje roto en origen** (sin traductor registrado, sin nombre estable): cuenta intentos y termina en `FailedAt`, como hoy.
- La clasificación la hace una excepción propia de `Infrastructure/Messaging` (`BrokerUnavailableException`) que el publicador tira cuando falla la conexión o la confirm [INFERENCIA, diseño]. [NO VERIFICADO] qué tipo de excepción tira `RabbitMQ.Client` 7.2.2 en cada caso: el paso lo mide con el contenedor pausado (test T5).
- `Outbox:MaxAttempts` vuelve a 5 en Tasks: los 12 intentos existían para aguantar un monolito caído (decisión del 24.1 en `docs/decisions.tsv`), y eso ya no aplica [INFERENCIA].

---

## 5. Orden por partición

### 5.1 Qué garantiza RabbitMQ

- "When publishing on a single channel, messages are enqueued in publishing order", y la entrega sigue ese orden salvo prioridades, varios consumidores activos o reentregas: "any redelivery can change order" ([FUENTE: RabbitMQ, Queues](https://www.rabbitmq.com/docs/queues)).
- Para conservar el orden con varios consumidores, RabbitMQ recomienda SAC, y para orden por entidad con paralelismo, "use the `x-modulus-hash` exchange to partition messages across several single-active-consumer queues" (misma fuente).
- El exchange modulus hash es "a built-in exchange type in RabbitMQ" que "applies a `Hash mod N` operation to pick the destination queue"; si cambia N, "the routing for almost all keys will be reshuffled" ([FUENTE: RabbitMQ, Modulus Hash Exchange](https://www.rabbitmq.com/docs/modulus-hash-exchange)).

### 5.2 Opciones

| Opción | Cómo | Enseña | Costo |
|---|---|---|---|
| **(O1) Una cola por consumidor con SAC** (recomendada) | Cola quorum `projects.task-counters` con `x-single-active-consumer: true`, listener `Inline` | El orden de la cola se conserva aunque haya dos monolitos; si el activo muere, otro toma la cola ("the queue fails over automatically", [FUENTE: RabbitMQ, Consumers](https://www.rabbitmq.com/docs/consumers)) | Un argumento de cola. Wolverine no tiene un helper: "`x-single-active-consumer` unsupported — settable only via raw `Queue.Arguments`" ([FUENTE: JasperFx/wolverine, `RABBITMQ-PERF-DEEP-DIVE-PLAN.md`](https://github.com/JasperFx/wolverine/blob/main/RABBITMQ-PERF-DEEP-DIVE-PLAN.md)) |
| (O2) N colas con SAC detrás de un exchange modulus hash | Clave de ruteo = `TaskId` | Orden por entidad con paralelismo: la partición de verdad | Exchange y N colas; [NO VERIFICADO] cómo fija Wolverine la clave de ruteo por mensaje y el tipo de exchange |
| (O3) Particionado de Wolverine | `PartitionProcessingByGroupId` con `GroupId = TaskId` | Orden por grupo dentro del proceso | "`ProcessInline()` endpoints cannot use partitioning" ([FUENTE: Wolverine, Partitioning](https://wolverinefx.net/guide/messaging/partitioning.html)); el particionado global con `UseShardedRabbitQueues` fuerza colas durables (misma fuente del plan de rendimiento), o sea, las tablas de Wolverine |
| (O4) `ListenWithStrictOrdering()` | Wolverine escucha en un solo nodo | Lo mismo que O1 | Usa la elección de líder de Wolverine, que vive en sus tablas: el ejemplo oficial lo arma con `PersistMessagesWithPostgresql` ([FUENTE: código, `exclusive_listeners.cs`](https://github.com/JasperFx/wolverine/blob/main/src/Transports/RabbitMQ/Wolverine.RabbitMQ.Tests/exclusive_listeners.cs)) |

### 5.3 Recomendación

**(O1)** en el paso 25. Es lo mínimo que muestra el concepto con un test (sección 8, T6): dos monolitos escuchan, uno solo consume, y `TaskCreated` → `TaskCompleted` de la misma tarea llegan en orden. La proyección no lo necesita (sumar no depende del orden, `architecture.md:109`), pero el paso 26 sí lo va a necesitar. (O2) queda como pendiente con nombre: es el siguiente escalón cuando una sola cola no alcance.

Un detalle: el orden también depende del productor. Con dos relays de Tasks, dos instancias publican en paralelo y el orden global se pierde antes de llegar al broker (`progreso.md:252`). O1 conserva el orden en que llegan a la cola, no el orden en que nacieron. Para el orden por tarea de punta a punta hace falta, además, que un solo relay publique los eventos de una misma tarea [INFERENCIA]. Lo dejo escrito en el pendiente de O2.

---

## 6. Wolverine como bus, no como mediator

### 6.1 El riesgo

- ADR-0004 punto 9 descarta un mediator. Wolverine es las dos cosas: "Using Wolverine as a 'Mediator' in MVC controllers, Minimal API functions [...] is an exception case that we fully support" ([FUENTE: Wolverine, Modular Monolith tutorial](https://github.com/JasperFx/wolverine/blob/main/docs/tutorials/modular-monolith.md)).
- El descubrimiento por convención toma cualquier clase pública cuyo nombre termine en `Handler` o `Consumer`, con métodos `Handle` o `Consume` ([FUENTE: Wolverine, Discovery](https://wolverinefx.net/guide/handlers/discovery.html)). En este repo las slices tienen clases como `ApplyTaskEvent` con `ExecuteAsync`, que no matchean, pero una clase futura `FooHandler` sí [INFERENCIA].
- El ensamblado de la aplicación se adivina mirando la pila de llamadas, y queda guardado para todo el proceso: con varios hosts en una suite de tests, los handlers pueden "silently disappear" (misma fuente). Acá `AddWolverine` se llamaría desde `Infrastructure`, que compila en `TemplateApp.Platform` (`Platform/TemplateApp.Platform.csproj:14`), y los tests levantan dos hosts en un proceso (`ApiPair`).

### 6.2 Opciones para los handlers

| Opción | Cómo | Genera código | Wolverine en las features |
|---|---|---|---|
| **(D1) Handler propio sin generación** (recomendada) | `Infrastructure/Messaging` registra por cada evento de integración un `MessageHandler<T>` propio con `opts.AddMessageHandler(...)`. Ese handler abre un scope y llama a una interfaz del repo (`IIntegrationEventHandler<T>`) que la feature implementa | No: es "pre-canned message handling that does not require any code generation" ([FUENTE: código, `WolverineOptions.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/WolverineOptions.cs)) | No |
| (D2) Handlers por convención | Una clase `TaskEventsHandler` en `Features/Projects/ApplyTaskEvent/` con `Handle(TaskCreatedIntegrationEvent)`, registrada con `opts.Discovery.DisableConventionalDiscovery()` + `IncludeType<T>()` (misma fuente de Discovery) | Sí | Sí, si el handler pide `Envelope` o `IMessageContext` |

**Gana y pierde:**

- **(D1)** Gana: no hay descubrimiento ni ensamblado de aplicación que adivinar. No hay generación de código en tiempo de ejecución. Las features no conocen Wolverine, así que la regla de Dune puede ser simple (sección 10). Es la misma forma que `AddEventHandler` del paso 24 (`OutboxRegistration.cs:27-36`). Las políticas de error siguen valiendo: para un `MessageHandler`, Wolverine le arma la cadena "For error handling and other policies" (misma fuente, `AddMessageHandler`). Pierde: es la vía "For advanced usages"; no es la que muestran los tutoriales.
- **(D2)** Gana: es la vía de los tutoriales. Pierde: desde Wolverine 6.0, "Applications running `TypeLoadMode.Dynamic` (the default) or `Auto` now must reference `WolverineFx.RuntimeCompilation`", y sin ese paquete la app "fails fast at startup" ([FUENTE: Wolverine, Code Generation](https://wolverinefx.net/guide/codegen.html)). La alternativa es `TypeLoadMode.Static` con el código generado antes con `dotnet run -- codegen write` (misma fuente), que suma un archivo generado por handler y un paso más al Dockerfile.

### 6.3 Recomendación

**(D1)** con `opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static`, `opts.ApplicationAssembly` fijado por host y el descubrimiento por convención apagado. [NO PROBADO] que Wolverine 6.64.2 arranque sin `WolverineFx.RuntimeCompilation` cuando no hay ningún handler generado. Es la primera prueba del paso. Si falla, el plan B es (D2) con `WolverineFx.RuntimeCompilation`, que es un paquete más de la misma librería elegida.

Y en los dos casos: ninguna slice llama a `IMessageBus.InvokeAsync` (eso sería usar Wolverine como mediator). Lo controla la regla de Dune.

---

## 7. Health, trazas y métricas

### 7.1 Health de RabbitMQ

- Hoy readiness corre todos los checks registrados y responde 503 solo si alguno sale `Unhealthy`; `Degraded` sigue en 200 (`GetReadinessEndpoint.cs:14-22`). El collector de OpenTelemetry no entra en readiness porque sacar la API del balanceo por él sería un fallo en cascada (`docs/decisions.tsv`, paso 16).
- Wolverine ofrece checks en un paquete aparte, `WolverineFx.HealthChecks`: `AddWolverine()` mira el runtime y `AddWolverineListeners()` mira los listeners (`Accepting` → `Healthy`, `TooBusy` → `Degraded`, todos `Stopped` → `Unhealthy`) ([FUENTE: Wolverine, Health Checks](https://wolverinefx.net/guide/health-checks.html)).
- **Recomendación:** un check propio, `BrokerHealthCheck` en `Infrastructure/Messaging`, registrado en `AddMessaging`, con el límite de 2 s de los demás (`BackendInfrastructure.cs:13`). Responde `Degraded` (no `Unhealthy`) si RabbitMQ no responde, porque con RabbitMQ caído la API sigue atendiendo: Tasks guarda los eventos en su outbox y el monolito sirve la proyección que tiene [INFERENCIA, mismo argumento del paso 16]. Es la misma elección del paso 16: checks propios con el framework, sin paquetes (`docs/decisions.tsv`, paso 16). [NO VERIFICADO] qué API pública de Wolverine expone el estado de la conexión; la alternativa es abrir una conexión AMQP corta con `RabbitMQ.Client`, que ya viene con `WolverineFx.RabbitMQ`.
- **Compose:** RabbitMQ recomienda para contenedores "a TCP port check on the AMQP port as the `readinessProbe`" y avisa que los comandos de CLI pesan más ([FUENTE: RabbitMQ, Monitoring](https://www.rabbitmq.com/docs/monitoring)). Con un nodo, `rabbitmq-diagnostics -q check_port_connectivity` (etapa 4 de la misma fuente) también sirve. `api` y `tasks` esperan a `rabbitmq` con `condition: service_healthy`, como esperan a `db` (`compose.yaml:58-62`).

### 7.2 Trazas

- La fuente de Wolverine se llama `Wolverine` ([FUENTE: Wolverine, Logging](https://wolverinefx.net/guide/logging.html)). Hay que sumarla a `AddSource` (`ObservabilityExtensions.cs:52`).
- Wolverine propaga el contexto en el sobre: el span de recepción arranca como hijo del `ParentId` del sobre, salvo que el sobre pida una traza nueva con link ([FUENTE: código, `WolverineTracing.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/WolverineTracing.cs), `StartEnvelopeActivity` y `StartNewLinkedTrace`).
- Queda así: la request guarda el evento → el relay abre `outbox process` con **link** a la request (`OutboxRelay.cs:111-114`, decisión del paso 24) → el publicador publica dentro de ese span → Wolverine abre `send` y, en el monolito, `receive` como hijos. La traza del relay llega hasta el efecto en el monolito; el link a la request no cambia [INFERENCIA].

### 7.3 Métricas

- El Meter de Wolverine se llama `Wolverine:{ApplicationName}`: "a bare `AddMeter("Wolverine")` matches nothing", hay que usar `AddMeter("Wolverine*")` ([FUENTE: Wolverine, Logging](https://wolverinefx.net/guide/logging.html)). Hoy el repo exporta solo `TemplateApp.*` (`ObservabilityExtensions.cs:58`).
- Instrumentos útiles para el concepto: `wolverine-execution-failure`, `wolverine-dead-letter-queue` (contador de mensajes a la DLQ) y `wolverine-effective-time` (misma fuente).
- `templateapp.outbox.delivery.lag` pasa a medir "hasta que RabbitMQ confirmó", no "hasta que el monolito aplicó" (`architecture.md:108`). El atraso de la proyección lo mide `wolverine-effective-time` en el monolito [INFERENCIA; NO VERIFICADO qué mide exactamente ese instrumento].

---

## 8. Tests

### 8.1 Cómo se arma en este repo

- Los tests ya usan Testcontainers para PostgreSQL: `PostgresFixture` es un fixture del ensamblado, con `postgres:18` y una variable `TEST_POSTGRES_CONNECTION` para usar una base externa (`PostgresFixture.cs:5-31`, `TemplateApp.Tests.csproj:18`).
- **Recomendación:** un `RabbitMqFixture` igual, con `Testcontainers.RabbitMq` ("`new RabbitMqBuilder(...)`", `GetConnectionString()`, usuario y clave por defecto `rabbitmq`, [FUENTE: Testcontainers, RabbitMQ](https://dotnet.testcontainers.org/modules/rabbitmq/)) y `TEST_RABBITMQ_CONNECTION` para CI.
- **Aislamiento:** hoy todos los hosts comparten la base y un test que corre el relay usa una base propia (`conventions.md:110`). Con el broker pasa lo mismo: un test que publica usa nombres de cola propios. Recomiendo una opción `Messaging:QueuePrefix` que el test fija con un id único [INFERENCIA, diseño; NO VERIFICADO que Wolverine tenga un prefijo de nombres para RabbitMQ, la doc lo muestra para otros transportes].
- Los tests que no tocan mensajería apagan los transportes con `DisableAllExternalWolverineTransports()`, que "not making any connection to external brokers" ([FUENTE: Wolverine, Testing](https://wolverinefx.net/guide/testing.html), [código `HostBuilderExtensions.cs:515`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/HostBuilderExtensions.cs)).
- Arranque más rápido en tests: la doc dice `RunWolverineInSoloMode()`, pero en 6.64.2 el método se llama `UseWolverineSoloMode()` ([FUENTE: código, `HostBuilderExtensions.cs:566`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/HostBuilderExtensions.cs)). Con la opción (B) no hay agentes durables, así que puede no hacer falta [NO PROBADO].
- Las sesiones rastreadas de Wolverine (`TrackActivity()`, `IncludeExternalTransports()`, `DoNotAssertOnExceptionsDetected()`, timeout de 5 s por defecto, [FUENTE: Wolverine, Testing](https://wolverinefx.net/guide/testing.html)) sirven para esperar un mensaje sin `Task.Delay`. Para T4 no alcanzan, porque el test corta el host a mitad. Recomiendo esperar con una señal del consumidor de prueba (un `TaskCompletionSource`), como `ConsumerProbe` (`Tests/Messaging/ConsumerProbe.cs`).
- Para simular RabbitMQ caído, el contenedor de Testcontainers tiene `PauseAsync`, `UnpauseAsync` y `StopAsync` ([FUENTE: código, `IContainer.cs`](https://github.com/testcontainers/testcontainers-dotnet/blob/develop/src/Testcontainers/Containers/IContainer.cs)). Ojo: pausar el contenedor compartido rompe a los demás tests; T5 necesita su propio contenedor.

### 8.2 Los tests del Verifica

| Test | Qué hace | Qué afirma |
|---|---|---|
| **T1 Reintento** | Un consumidor de prueba en la cola tira una excepción transitoria las 2 primeras veces | El efecto aparece una vez; hubo 3 entregas; nada en la DLQ |
| **T2 DLQ** | Se publica un evento con versión 99 (o JSON roto) | Termina en `projects.task-counters.dlq` en el primer intento (se lee con `RabbitMQ.Client` `BasicGet`); el contador no cambia; el header `x-death` dice `rejected` ([FUENTE: RabbitMQ, DLX](https://www.rabbitmq.com/docs/dlx)) |
| **T3 Confirm** | El relay publica con RabbitMQ arriba | `ProcessedAt` se marca recién después de la confirm; el mensaje está en la cola |
| **T4 Matar al consumidor a mitad** | Host A recibe el mensaje, aplica el efecto y confirma su transacción, y queda esperando antes de devolver. El test descarta el host A (se cierra el canal sin ack). Arranca host B | RabbitMQ reentrega (el mensaje no tenía ack, [FUENTE: RabbitMQ, Confirms](https://www.rabbitmq.com/docs/confirms)); el inbox de B lo rechaza; el contador vale 1, no 2. Variante: matar antes de confirmar la transacción → el efecto se aplica una vez en B |
| **T5 Broker caído** | Contenedor propio, `PauseAsync`, el relay corre 20 ciclos | La fila sigue pendiente, sin `FailedAt`, con `LastError`; `UnpauseAsync` y el siguiente ciclo la publica |
| **T6 Orden con SAC** | Dos hosts del monolito escuchan la misma cola; se publican 50 pares creado/completado | Uno solo consume (el otro no recibe nada); el orden de llegada es el de publicación |
| **T7 Reconstrucción con mensajes en la cola** | Mensaje publicado y sin consumir; se reconstruye la proyección; después se consume | El contador no cuenta dos veces (sección 3.4) |

- El rojo de cada uno: T4 con el inbox apagado da 2 (como T4 del paso 24); T2 sin la regla `MoveToErrorQueue` gasta 3 intentos; T5 con el relay de hoy termina en `FailedAt`; T7 con la foto de hoy cuenta dos veces [NO PROBADO, todo].

---

## 9. Paquetes, versiones e imagen

| Pieza | Versión | Dónde | Fuente |
|---|---|---|---|
| `WolverineFx.RabbitMQ` | 6.64.2 (16/11/2026), net9.0 y net10.0 | `Platform/TemplateApp.Platform.csproj` | [NuGet](https://www.nuget.org/packages/WolverineFx.RabbitMQ). Depende de `WolverineFx` 6.64.2, `WolverineFx.Newtonsoft` 6.64.2 y `RabbitMQ.Client` ≥ 7.2.2 |
| `WolverineFx` | 6.64.2, viene con el anterior | — | [NuGet](https://www.nuget.org/packages/WolverineFx). Hay un 7.0.0-alpha.3 del 19/11/2026: no usar una alfa |
| `Testcontainers.RabbitMq` | 4.17.0 (16/11/2026) | `Tests/TemplateApp.Tests.csproj` | [NuGet](https://www.nuget.org/packages/Testcontainers.RabbitMq). Depende de `Testcontainers` ≥ 4.17.0: **sube también `Testcontainers.PostgreSql` de 4.15.0 a 4.17.0** (`TemplateApp.Tests.csproj:18`) para que el núcleo sea uno |
| Imagen RabbitMQ | `rabbitmq:4.4.1-management` | `compose.yaml` | [Docker Hub](https://hub.docker.com/_/rabbitmq): 4.4.1 es `latest`. Fijo la versión exacta, como `prom/prometheus:v3.13.5` (`compose.yaml:35`) |
| No hacen falta | `WolverineFx.EntityFrameworkCore`, `WolverineFx.Postgresql` (los dos 6.64.2) | — | Son para la opción (A). `WolverineFx.Postgresql` trae `Weasel.Postgresql`, que pide `Npgsql` ≥ 9.0.4 ([NuGet](https://www.nuget.org/packages/Weasel.Postgresql)); el repo ya usa Npgsql 10 por `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`Platform/TemplateApp.Platform.csproj:42`) |
| Solo si D1 falla | `WolverineFx.RuntimeCompilation` 6.64.2 | `Platform` | [NuGet](https://www.nuget.org/packages/WolverineFx.RuntimeCompilation) |

- Licencias: Wolverine MIT ([LICENSE](https://github.com/JasperFx/wolverine/blob/main/LICENSE)), RabbitMQ MPL 2.0 (`informe-dotnet-empresarial.md:160`). Ya las aceptó Tobi.
- [NO VERIFICADO] que `WolverineFx.Newtonsoft` no cambie el serializador por defecto a Newtonsoft. Si lo cambia, conviene fijar System.Text.Json con `JsonSerializerOptions.Web`, como usa el outbox (`OutboxInterceptor.cs:87`).

### 9.1 Compose

```yaml
  rabbitmq:
    image: rabbitmq:4.4.1-management
    restart: unless-stopped
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER:-templateapp}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:?Define RABBITMQ_PASSWORD en .env}
    ports:
      - "${RABBITMQ_MANAGEMENT_PORT:-15672}:15672"
    volumes:
      - rabbitmq-data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "check_port_connectivity"]
      interval: 5s
      timeout: 5s
      retries: 20
      start_period: 15s
```

- La clave sale de `.env` con `:?`, como `POSTGRES_PASSWORD` (`compose.yaml:8`). `api` y `tasks` reciben `Messaging__RabbitMq__Uri: amqp://...@rabbitmq:5672/`.
- El puerto AMQP 5672 no se publica al host: solo lo usan los contenedores. Los tests usan Testcontainers.

---

## 10. Dune (reglas 4 y 9)

### 10.1 El gate neutral cambia

- Hoy `checkNoBus` rechaza `Wolverine`, `WolverineFx` y `JasperFx` en cualquier `PackageReference`, `<Using>` o `using` del producto (`check-structure.mjs:206-220`, `:271-283`), con el mensaje "bus o mediator antes del paso 25".
- **Cambio propuesto:** `WolverineFx.RabbitMQ` (y `WolverineFx.RuntimeCompilation` si hace falta) se permite **solo** en `backend/Platform/TemplateApp.Platform.csproj`. El resto de la lista sigue prohibido en todo el producto (MediatR, MassTransit, etc.): el bus elegido es uno. `JasperFx` se permite solo como dependencia transitiva, no como referencia directa. El mensaje pasa a "un solo bus: Wolverine, solo desde Platform".
- Fixture válida: `Platform.csproj` con `WolverineFx.RabbitMQ`. Violación deliberada: `Hosts/Tasks/TemplateApp.Tasks.csproj` con `WolverineFx`, y un csproj con `MassTransit`. Se suman a `check-structure.test.mjs`, como el caso negativo de hoy.
- Es un cambio de gate como objetivo declarado del paso (regla 9): va con su línea en `docs/decisions.tsv`. ADR-0004 punto 9 ("Sin mediator ni bus por ahora. El paso 25 del plan elige un bus") necesita su línea de "Cambios" (choque 2).

### 10.2 Regla nueva: DUNE0025 "Wolverine solo en `Infrastructure/Messaging`"

- **Qué dice:** un `using Wolverine...`, o el uso de cualquier símbolo de un namespace `Wolverine*` o `JasperFx*`, fuera de `backend/Infrastructure/Messaging/` es error. Los tests quedan fuera. Además, una invocación de `IMessageBus.InvokeAsync` (o `InvokeAsync<T>`) es error en cualquier lugar del producto.
- **Por qué:** es la forma ejecutable de dos frases de los docs. ADR-0004 punto 9: sin mediator; `InvokeAsync` es el modo mediator de Wolverine ([FUENTE: Wolverine, Runtime](https://wolverinefx.net/guide/runtime.html), "Invoking a Message Inline [...] effectively acting as a mediator"). Y ADR-0004 punto 6: lo transversal vive en `Infrastructure`. Con (D1), una feature nunca necesita Wolverine.
- **Fixture válida:** un handler propio en `Infrastructure/Messaging/` que usa `MessageHandler<T>`; una slice en `Features/Projects/ApplyTaskEvent/` que implementa `IIntegrationEventHandler<T>` del repo.
- **Violación deliberada:** `Features/Projects/ApplyTaskEvent/TaskEventsHandler.cs` con `using Wolverine;` y un parámetro `Envelope`; y un endpoint que llama a `bus.InvokeAsync(new X())`.
- **Rojo con el build real:** se agrega un `using Wolverine;` a una slice en su propio commit y se revierte, como el rojo de DUNE0023 (`docs/decisions.tsv`, paso 24).
- Analyzer en `dune/Dune.Analyzers/`, con `DunePaths` para la carpeta, como `EventHandlerCallAnalyzer.cs`. Se suma a `Dune.props` y `.editorconfig` (`progreso.md`: hoy `.editorconfig` no lista DUNE0022).
- Alternativa descartada: "los handlers de eventos de integración usan el inbox". Es la regla que más importa para el concepto, pero un analyzer no ve si la llamada a `TryAcceptAsync` va en la misma transacción que el efecto; lo prueban T4 y T7 [INFERENCIA].

### 10.3 Reglas que cambian solo de texto

- DUNE0023 sigue igual: solo `OutboxRelay` llama a un `IEventHandler<T>`. El handler de Wolverine llama a `IIntegrationEventHandler<T>`, que es otra interfaz [INFERENCIA]. Su descripción suma una línea que lo dice.
- DUNE0015 (sufijos): [NO VERIFICADO] si `NamingAnalyzer` exige un lugar para el sufijo `IntegrationEvent`. Hoy revisa `Request` y `Response` (`NamingAnalyzer.cs:108-109`). Si el paso quiere que `*IntegrationEvent` viva solo en la carpeta de la slice que publica o consume, es un cambio de esa regla, con su fixture.

---

## 11. Alcance: qué entra en el 25 y qué se parte

Regla 1: un paso, un cambio observable. Los pasos anteriores se partieron igual (23/23.1/23.2, 24/24.1).

| Paso | Cambio observable | Qué entra |
|---|---|---|
| **25** | Con el monolito caído un día, los contadores se ponen al día cuando vuelve; un evento roto termina en la DLQ al primer intento | RabbitMQ en Compose; Wolverine en `Infrastructure/Messaging` (D1); `TaskEventPublisher`; handler de Projects con inbox; publisher confirms; reglas de reintento y DLQ; SAC; relay que no gasta intentos con el broker caído; foto de la reconstrucción con todos los ids; health `Degraded`; trazas y métricas; tests T1-T7; gate y DUNE0025. Tres commits como releases: A (el monolito escucha la cola y sigue aceptando el `PUT`), B (Tasks publica en la cola), C (borra el `PUT` y el cliente HTTP) |
| **25.1** (propuesto) | Borrar la cuenta con Tasks caído responde 204, y las tareas se borran cuando Tasks vuelve | Outbox en `users`, evento `UserDeleted` en `UserProfile`, `UserDeletedIntegrationEvent`, consumidor con inbox en Tasks, lápida contra la carrera con `POST /tasks`, borrar `OwnedTasksClient` y `DELETE /internal/v1/owners/{id}/tasks`. Puede sacar `CrossContextTransaction` de `DeleteUser` si Auth y Projects también pasan a eventos; si no, queda |
| Pendiente sin paso | Orden por tarea con paralelismo | O2 (modulus hash con N colas), un relay por partición, reenvío desde la DLQ con un comando o un endpoint de operador, alerta por `wolverine-dead-letter-queue` |

El 25 sigue siendo grande. Si hay que achicarlo más, lo que mejor se separa es T6 + SAC (el orden) en un 25.2: el 25 quedaría con at-least-once, deduplicación y mensajes venenosos, y el 25.2 con el orden. No lo recomiendo: el plan pone los cuatro conceptos juntos y SAC es un argumento de cola más un test.

---

## Choques con los docs

1. **`conventions.md:95` frente al outbox de Wolverine.** El repo dice que `OutboxMessages` e `InboxMessages` son de cada feature, en su schema. Wolverine pide sus propias tablas en un schema por aplicación ([FUENTE: Wolverine, PostgreSQL](https://wolverinefx.net/guide/durability/postgresql.html)). Con la opción (B) el choque no se activa. Si Tobi prefiere (A), hay que cambiar esa regla y DUNE0021.
2. **ADR-0004 punto 9** dice "Sin mediator ni bus por ahora. El paso 25 del plan elige un bus". Hace falta una línea en "Cambios" del ADR: bus Wolverine solo como transporte, sin mediator. `architecture.md:38` ("No hay mediator ni bus de librería") y `conventions.md:102` ("Hasta el paso 25, `check-structure.mjs` rechaza...") quedan falsos y se corrigen.
3. **`architecture.md:94-101` y `conventions.md:109`** describen el reenvío por HTTP y "un consumidor que reenvía no tiene inbox propio". Con el broker, `TaskEventPublisher` publica en vez de reenviar. La frase del inbox sigue valiendo; la del `PUT` se borra en el commit C.
4. **`conventions.md:106`** dice que el evento que cruza por HTTP viaja como `*Request`. Con el broker viaja como `*IntegrationEvent`, cada lado con su copia. La regla de las dos copias no cambia; cambia el sufijo.
5. **`architecture.md:76` y `:108`** dicen que con el monolito caído el relay reintenta 12 veces (unos 68 minutos) y que `delivery.lag` mide el atraso de la proyección. Con el broker, el relay no depende del monolito y `delivery.lag` mide hasta la confirm de RabbitMQ.
6. **`architecture.md:111`** (reconstrucción): la foto siembra "los `MessageId` todavía no entregados". Con el broker, "entregado al broker" no es "aplicado", y hay que sembrar todos (sección 3.4).
7. **`architecture.md:102`** dice que la telemetría del outbox no entra en readiness. El check de RabbitMQ entra, pero en `Degraded`. No contradice la frase, pero `architecture.md:186` lista los checks de readiness y hay que sumarlo.

## Decisiones (con mi recomendación)

1. Outbox casero + Wolverine como transporte (opción B), sin outbox ni inbox durable de Wolverine.
2. Solo los eventos de tarea pasan al broker en el 25; la cuenta borrada va en un 25.1.
3. Eventos de integración con sufijo `IntegrationEvent`, una copia `internal` en cada feature, alias estable = nombre del outbox (`tasks.task-created`) y versión 1.
4. Listener `Inline` (ack después del handler) y el inbox del repo en la misma transacción que el efecto.
5. Publisher confirms prendidas.
6. Una cola quorum por consumidor (`projects.task-counters`) con SAC y DLQ propia (`projects.task-counters.dlq`).
7. Mensaje roto → DLQ al primer intento; transitorio → reintento con espera y pausa del listener; otro error → 3 vueltas y DLQ.
8. El relay no cuenta intentos cuando RabbitMQ no responde; reintenta sin límite con tope de espera. `Outbox:MaxAttempts` de Tasks vuelve a 5.
9. La foto de la reconstrucción devuelve todos los `MessageId` del outbox, no solo los pendientes.
10. Handlers propios sin generación de código (D1), `TypeLoadMode.Static`, `ApplicationAssembly` fijo y descubrimiento por convención apagado. Plan B: D2 con `WolverineFx.RuntimeCompilation`.
11. `BrokerHealthCheck` propio, `Degraded` si RabbitMQ no responde, sin `WolverineFx.HealthChecks`.
12. `AddSource("Wolverine")` y `AddMeter("Wolverine*")`.
13. `RabbitMqFixture` con Testcontainers y `TEST_RABBITMQ_CONNECTION`; prefijo de colas por test; contenedor propio para T5.
14. Gate: `WolverineFx.*` solo en `Platform/TemplateApp.Platform.csproj`; el resto de la lista sigue prohibido.
15. DUNE0025: Wolverine solo en `Infrastructure/Messaging` y ningún `IMessageBus.InvokeAsync`.
16. Tres commits como releases (A el monolito escucha, B Tasks publica, C se borra el HTTP). El monolito se despliega antes que Tasks.

## Herramientas (regla 5)

Tobi ya eligió Wolverine y RabbitMQ (`docs/decisions.tsv`). Quedan tres piezas que son paquetes o imagen. Las dos primeras son de la librería y del broker elegidos; las anoto igual porque el modo autónomo pide preguntar por cada herramienta:

| Pieza | Recomendación | Alternativa |
|---|---|---|
| Paquete de Wolverine | `WolverineFx.RabbitMQ` 6.64.2, nada más | Sumar `WolverineFx.RuntimeCompilation` si D1 no arranca (decisión 10) |
| Health de RabbitMQ | Check propio, sin paquete (como el paso 16) | `WolverineFx.HealthChecks` (oficial de Wolverine) o `AspNetCore.HealthChecks.RabbitMQ` (Xabaril, el paso 16 ya descartó Xabaril para la base) |
| Tests | `Testcontainers.RabbitMq` 4.17.0 (mismo proveedor que PostgreSQL) | Un RabbitMQ de `compose.yaml` levantado a mano antes de los tests |

## Pendientes para `progreso.md`

- Orden por tarea con paralelismo: exchange modulus hash con N colas SAC y un relay por partición (sección 5.3).
- Reenvío desde la DLQ: hoy es un shovel a mano; falta un comando o endpoint de operador y su runbook.
- Alerta de Prometheus por `wolverine-dead-letter-queue` y por mensajes en la DLQ.
- La foto de la reconstrucción supone que ningún mensaje espera en la cola más de 7 días (la retención del outbox).
- Los pendientes de la cuenta borrada pasan del 25 al 25.1, con la lápida para la carrera de `POST /tasks`.

## Verificado / No verificado

**Verificado (leído en el repo o en una fuente primaria):**
- Todo `[CÓDIGO]`: outbox, relay, inbox, forwarder, `ApplyTaskEvent`, `SnapshotTaskCounts`, `DeleteUser`, `TasksFeature`, hosts, `compose.yaml`, gate de bus, `PostgresFixture`, readiness y observabilidad, en `f4fa3ff`.
- Versiones de NuGet de `WolverineFx`, `WolverineFx.RabbitMQ`, `WolverineFx.EntityFrameworkCore`, `WolverineFx.Postgresql`, `WolverineFx.RuntimeCompilation`, `Weasel.Postgresql` y `Testcontainers.RabbitMq`, consultadas el 2026-11-19. Etiqueta de Docker Hub `4.4.1-management`.
- En el código de Wolverine en la etiqueta `V6.64.2`: confirms apagadas por defecto, 3 intentos por defecto, `AddMessageHandler` sin generación, `RegisterMessageType(Type, string)`, `AddWolverine` sobre `IServiceCollection`, `DisableAllExternalWolverineTransports`, `UseWolverineSoloMode` y la propagación de trazas.
- En el repo de Wolverine (`main`): SAC sin helper para RabbitMQ y modo `Inline` por defecto en los endpoints de RabbitMQ (`RABBITMQ-PERF-DEEP-DIVE-PLAN.md`, del 28/07/2026).
- Docs de RabbitMQ 4.4: confirms, colas quorum, DLX, SAC, orden, modulus hash, shovel y health checks.

**No verificado (queda para el paso):**
- Que Wolverine 6.64.2 arranque con `TypeLoadMode.Static`, sin `WolverineFx.RuntimeCompilation`, cuando todos los handlers son `MessageHandler<T>` propios (decisión 10). Es lo primero que el paso tiene que probar.
- Que `SendAsync` en un endpoint `Inline` con confirms tire una excepción si RabbitMQ no confirma, y qué excepción tira `RabbitMQ.Client` 7.2.2 con el broker caído.
- Cómo fijar el id del sobre de Wolverine al publicar (para usar el `MessageId` del outbox), y cómo se expresa la versión del mensaje sin el atributo `[MessageIdentity]`.
- Cómo declarar una cola con argumentos crudos (`x-single-active-consumer`) y una DLQ por cola desde la API de Wolverine 6.64.2.
- Si Wolverine tiene un prefijo de nombres de colas para RabbitMQ, útil para aislar tests.
- Qué API pública de Wolverine expone el estado de la conexión para el health check.
- Si `WolverineFx.Newtonsoft` cambia el serializador por defecto.
- Que la imagen `management` traiga el plugin de shovel prendido.
- Qué mide exactamente `wolverine-effective-time`.
- Si DUNE0015 necesita conocer el sufijo `IntegrationEvent`.
- Ningún test corrió: T1 a T7 son diseño.
