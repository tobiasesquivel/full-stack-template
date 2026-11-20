# Paso 25.1: evento de cuenta borrada por el broker (investigación)

- Fecha: 2026-11-19.
- Estado: **Listo** como investigación. No hace falta ninguna herramienta nueva: todo sale de lo que ya está (EF Core, Npgsql, PostgreSQL, Wolverine y RabbitMQ del paso 25). Hay 5 choques con los docs y 18 decisiones, cada una con mi recomendación (modo autónomo).
- Alcance: el paso 25.1 del [plan](../plan.md) ("25.1. Evento de cuenta borrada por el broker"). Concepto: un segundo productor y un segundo consumidor sobre el mismo bus, el outbox en el monolito y el consumidor idempotente en Tasks, con una lápida contra la carrera con `POST /tasks`. Verifica: borrar la cuenta con Tasks caído responde 204 y las tareas se borran cuando Tasks vuelve; el mismo evento entregado dos veces borra una sola vez; una tarea creada después del borrado de la cuenta no queda huérfana.
- Base: `main` en `edcc7d7` (merge del PR #87, paso 25).
- Límites con los pasos vecinos: las sagas son de los pasos 26 y 27. Pasar Auth y Projects a eventos sería una saga coreografiada (paso 26): este paso no lo adelanta (regla 8).

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `edcc7d7`.
- `[FUENTE]`: lo leí en un documento primario (docs de PostgreSQL, RabbitMQ, Microsoft, Npgsql, microservices.io, RFC 9110). Cada uno tiene su link.
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Respuesta corta

1. **Qué cambia para Tobi.** Hoy, si Tasks está caído y alguien borra su cuenta, la API responde 503 y no borra nada, porque `DeleteUser` llama a Tasks por HTTP antes de su transacción (`[CÓDIGO backend/Features/Users/DeleteUser/DeleteUser.cs:40]`). Después del paso, borrar la cuenta responde 204 siempre que el monolito y la base respondan. El borrado de las tareas viaja como evento por RabbitMQ y Tasks lo aplica cuando vuelve.
2. **Outbox en `users` (pregunta 1).** Se reusa todo lo genérico del paso 24 sin tocarlo: `AddOutbox<UsersDbContext>()` registra el interceptor y un relay propio para ese contexto, y `MapOutbox()` crea `users."OutboxMessages"` con una migración de Users. El relay corre en el proceso del monolito. El monolito ya tiene Wolverine (`AddMessaging`): solo suma la publicación del evento en el exchange `users.user-events`.
3. **Evento (pregunta 2).** `UserProfile` implementa `IHasDomainEvents` y gana `MarkDeleted(DateTimeOffset now)`, que anota `UserDeleted(AuthId, DeletedAt)`. Un publicador de Users lo traduce a `UserDeletedIntegrationEvent(UserId, DeletedAt)`, nombre estable `users.user-deleted`, versión 1. Tasks tiene su propia copia `internal` del evento de integración.
4. **Consumidor (pregunta 3).** Cola quorum `tasks.user-deletions`, con un solo consumidor activo y DLQ `tasks.user-deletions.dlq`, igual que `projects.task-counters`. El consumidor, en una sola transacción de `TasksDbContext`, toma un candado por propietario, pasa el inbox, escribe la lápida y borra las tareas con `MarkDeleted` + `RemoveRange`. Así cada tarea borrada sigue emitiendo `TaskDeleted` por el outbox de Tasks, y los contadores de proyectos siguen bien.
5. **Lápida (pregunta 4), la pieza central.** Tabla `tasks."DeletedOwners"` (propietarios borrados). El consumidor la escribe y `CreateTask` la consulta. Para que las dos transacciones no se crucen hace falta un candado: el consumidor toma `pg_advisory_xact_lock` **exclusivo** por propietario y `CreateTask` lo toma **compartido** justo antes de insertar. Con eso, en `READ COMMITTED`, los dos órdenes quedan cubiertos: si el consumidor va primero, `CreateTask` espera, ve la lápida y responde **401 `Task.OwnerDeleted`**; si `CreateTask` va primero, el consumidor espera, y su `SELECT` siguiente ve la tarea nueva y la borra. Sin el candado, una consulta simple a la lápida no alcanza. La lápida no se borra nunca (los ids de Identity no se reusan).
6. **`DeleteUser` (pregunta 5).** Deja de llamar a Tasks. `CrossContextTransaction` **queda**, con Projects, las claves de idempotencia, el perfil y la cuenta de Auth: los tres están en el mismo proceso y pasarlos a eventos es otro concepto (saga, paso 26). La fila del outbox entra en el mismo `SaveChanges` que borra el perfil, dentro del `TransactionScope`: se confirma o se pierde con la cuenta.
7. **Borrar el camino HTTP (pregunta 6).** Tres commits ordenados como releases, como el paso 25: **A** Tasks escucha la cola y consulta la lápida (el endpoint interno sigue); **B** el monolito publica y deja de llamar; **C** se borran `OwnedTasksClient`, `DeleteOwnedTasksEndpoint` y la ruta `DELETE /internal/v1/owners/{ownerId}/tasks`. Tasks sale antes que el monolito: si el monolito publica antes de que exista la cola, RabbitMQ confirma y el evento se pierde (pendiente del paso 25).
8. **Tests (pregunta 7).** Tres tests del Verifica sobre `IsolatedApi` + `RabbitMqFixture`, más dos tests de la carrera (uno por orden) que fuerzan el cruce con el candado tomado a mano desde el test, como ya hace `ApplyTaskEventTests` con el candado de la reconstrucción. Cada uno con su rojo primero.
9. **Dune (pregunta 8).** Ninguna regla nueva. La garantía "toda tarea nueva pasa por la lápida" se vuelve ejecutable por el compilador: `TaskItem.Create` pide un `LiveOwner` que solo puede crear la consulta a la lápida. No cambia ningún gate.

## Glosario corto

- **Lápida** (*tombstone*): una fila que dice "este propietario ya fue borrado". Sirve para que un borrado que llegó por evento no lo deshaga una escritura atrasada. Acá: `tasks."DeletedOwners"`.
- **Advisory lock** (= candado de aplicación): un candado de PostgreSQL con un significado que define la aplicación. "PostgreSQL provides a means for creating locks that have application-defined meanings [...] the system does not enforce their use" ([FUENTE: PostgreSQL, Explicit Locking](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS)). El repo ya lo usa para la reconstrucción de contadores (`[CÓDIGO backend/Features/Projects/TaskCountersProjection.cs:10-18]`).
- **Candado de transacción** (*transaction-level*): se suelta solo al terminar la transacción. "they are automatically released at the end of the transaction, and there is no explicit unlock operation" (misma fuente).
- **READ COMMITTED**: el nivel de aislamiento por defecto de PostgreSQL y el que fija `CrossContextTransaction` (`[CÓDIGO backend/Infrastructure/Persistence/CrossContextTransaction.cs:7-11]`). Cada consulta ve lo confirmado antes de que ella empiece ([FUENTE: PostgreSQL, Transaction Isolation](https://www.postgresql.org/docs/current/transaction-iso.html#XACT-READ-COMMITTED)).
- **Outbox transaccional**: el evento se guarda en una tabla en la misma transacción que el cambio, y un relay lo publica después. "Messages are guaranteed to be sent if and only if the database transaction commits" ([FUENTE: microservices.io, Transactional outbox](https://microservices.io/patterns/data/transactional-outbox.html)).
- **Release** (= versión desplegada): acá, cada commit del PR se puede desplegar solo, en orden.

---

## 1. Outbox en el schema `users`

### 1.1 Cómo está hoy

- El outbox es genérico por contexto. `AddOutbox<TContext>()` registra las opciones, el interceptor (un singleton), las métricas y un `OutboxRelay` con clave por tipo de contexto, que corre como `IHostedService` (`[CÓDIGO backend/Infrastructure/Messaging/OutboxRegistration.cs:9-21]`).
- El interceptor no conoce a Tasks: antes de cada `SaveChanges` toma los eventos de toda entidad rastreada que implemente `IHasDomainEvents` y los agrega como filas `OutboxMessage` (`[CÓDIGO backend/Infrastructure/Messaging/OutboxInterceptor.cs:63-77]`). Cada evento necesita un nombre estable registrado con `AddOutboxEvent`; si falta, tira una excepción (`[CÓDIGO backend/Infrastructure/Messaging/OutboxEvents.cs]`, método `For`).
- La tabla la mapea cada contexto con `MapOutbox()` (`[CÓDIGO backend/Infrastructure/Messaging/MessagingTables.cs:7-12]`) y la migra en su schema (`[CÓDIGO docs/conventions.md:95]`).
- Hoy solo Tasks tiene outbox (`[CÓDIGO backend/Features/Tasks/TasksFeature.cs:18-22]`, `[CÓDIGO backend/Features/Tasks/Persistence/TasksDbContext.cs:23-24]`). `UsersDbContext` no mapea outbox (`[CÓDIGO backend/Features/Users/Persistence/UsersDbContext.cs:17-22]`).
- El monolito ya registra Wolverine (`[CÓDIGO backend/Program.cs:19]`). `MessagingExtension` declara un exchange `direct` por cada evento publicado y lo envía inline; y una cola quorum con SAC, DLQ y listener `Inline` por cada suscripción (`[CÓDIGO backend/Infrastructure/Messaging/MessagingExtension.cs:61-83]`).

### 1.2 Qué gana el monolito

| Pieza | Dónde | Qué hace |
|---|---|---|
| `services.AddOutbox<UsersDbContext>()` | `Features/Users/UsersFeature.cs` | Interceptor en `UsersDbContext` y un relay propio de ese contexto, en el proceso del monolito |
| `services.AddOutboxEvent<UserDeleted>(UserEvents.Deleted, UserEvents.Version)` | ídem | Nombre estable en la tabla |
| `modelBuilder.MapOutbox()` | `UsersDbContext.OnModelCreating` | La tabla `users."OutboxMessages"` |
| Migración `AddOutboxMessages` de Users | `Features/Users/Persistence/Migrations/` | Solo crea la tabla (expand); igual que la de Tasks (`[CÓDIGO backend/Features/Tasks/Persistence/Migrations/20261119150538_AddOutboxMessages.cs]`) |
| `services.AddIntegrationEventPublication<PublishUserEvents.UserDeletedIntegrationEvent>(UserEvents.Deleted, UserEvents.Version, UserEvents.Exchange)` | ídem | Exchange `users.user-events` y clave de ruteo `users.user-deleted` (`[CÓDIGO backend/Infrastructure/Messaging/MessagingRegistration.cs:27-29]`) |
| `services.AddEventHandler<UserDeleted, PublishUserEvents.UserEventPublisher>()` | ídem | El único consumidor del outbox de Users: traduce y publica con `IntegrationEventPublisher` |
| `UserEvents` (constantes) | `Features/Users/UserEvents.cs` | Como `TaskEvents` (`[CÓDIGO backend/Features/Tasks/TaskEvents.cs]`) |

- **No hace falta** inbox en Users: el monolito no consume eventos de Users. Tampoco cambia `Infrastructure/Messaging` [INFERENCIA, por el código citado].
- **Dónde corre el relay:** en cada instancia del monolito. Con dos instancias, `FOR UPDATE SKIP LOCKED` evita que dos tomen la misma fila (`[CÓDIGO backend/Infrastructure/Messaging/OutboxRelay.cs:101-112]`).
- **Opciones del relay:** `OutboxOptions` es una sola sección `Outbox` por proceso (`[CÓDIGO backend/Infrastructure/Messaging/OutboxOptions.cs]`). El monolito no tiene esa sección en `appsettings.json`, así que usa los valores por defecto: 5 intentos y 7 días de retención. Con RabbitMQ caído no gasta intentos (`[CÓDIGO backend/Infrastructure/Messaging/OutboxRelay.cs:142-147]`). Alcanza.
- **Métricas:** el Meter `TemplateApp.Outbox` no separa por contexto. En el monolito es el único outbox, así que no se mezcla. En `architecture.md:195` dice "mide el outbox de Tasks" y hay que corregirlo.

### 1.3 Por qué la fila entra en la misma transacción que el borrado

- `DeleteUser` borra el perfil con `_db.SaveChangesAsync` dentro de `CrossContextTransaction.Begin()` (`[CÓDIGO backend/Features/Users/DeleteUser/DeleteUser.cs:42-53]`).
- El interceptor agrega la fila del outbox en `SavingChangesAsync`, o sea, dentro del mismo `SaveChanges` (`[CÓDIGO backend/Infrastructure/Messaging/OutboxInterceptor.cs:30-40]`). Una entidad en estado `Deleted` sigue rastreada, así que sus eventos se leen. Es lo mismo que ya hace `DeleteTask` (`MarkDeleted` y después `Remove`, `[CÓDIGO backend/Features/Tasks/DeleteTask/DeleteTask.cs:28-29]`).
- La conexión de `UsersDbContext` se anota sola en el `TransactionScope`: el parámetro `Enlist` de Npgsql vale `true` por defecto, "Whether to enlist in an ambient TransactionScope" ([FUENTE: Npgsql, Connection String Parameters](https://www.npgsql.org/doc/connection-string-parameters.html)).
- Entonces la fila y el borrado del perfil confirman juntos en `transaction.Complete()`. Si Auth falla después (`DeleteAccountAsync` con error, `[CÓDIGO DeleteUser.cs:49-51]`), el scope no se completa y la fila se revierte con el perfil. El relay no la ve antes del commit, porque en `READ COMMITTED` una consulta "never sees either uncommitted data" ([FUENTE: PostgreSQL, Transaction Isolation](https://www.postgresql.org/docs/current/transaction-iso.html#XACT-READ-COMMITTED)).
- Es exactamente la garantía del paso 24: publicar solo lo confirmado aunque haya un `TransactionScope` alrededor (`[CÓDIGO docs/architecture.md:93]`).
- [NO PROBADO] que el `SaveChanges` dentro del scope deje la fila en la misma conexión. El test T-atomic (sección 7) lo prueba: Auth falla a propósito y no queda fila.

---

## 2. Evento de dominio y evento de integración

### 2.1 `UserProfile` con eventos

Hoy `UserProfile` implementa `IOwnedResource` e `IAuditable`, con `AuthId` `init` y sin lista de eventos (`[CÓDIGO backend/Features/Users/Domain/UserProfile.cs:5-19]`). Queda así, copiando a `TaskItem` (`[CÓDIGO backend/Features/Tasks/Domain/TaskItem.cs:6-10,25,58-61]`):

- `internal sealed class UserProfile : IOwnedResource, IAuditable, IHasDomainEvents`.
- Un campo `private readonly List<IDomainEvent> _domainEvents = [];` y `DomainEvents` / `ClearDomainEvents()` de `IHasDomainEvents` (`[CÓDIGO backend/Shared/Domain/IHasDomainEvents.cs]`).
- `public void MarkDeleted(DateTimeOffset now) => _domainEvents.Add(new UserDeleted(AuthId, now));`
- `UserDeleted` en `Features/Users/Domain/Events/UserDeleted.cs`: `internal sealed record UserDeleted(string AuthId, DateTimeOffset DeletedAt) : IDomainEvent;`, como `TaskDeleted` (`[CÓDIGO backend/Features/Tasks/Domain/Events/TaskDeleted.cs]`).
- EF ignora la lista: es un campo privado sin propiedad mapeada [INFERENCIA, mismo caso que `TaskItem`; `UserProfileConfiguration` no la mapea]. [NO PROBADO] que la migración de Users salga sin cambios en `UserProfiles`; el paso lo mira en el diff de la migración.

`DeleteUser` pide el reloj (`TimeProvider`) y llama `profile.MarkDeleted(_clock.GetUtcNow())` antes de `Remove`.

### 2.2 El contrato en el cable

| Campo | Valor | Por qué |
|---|---|---|
| Nombre estable | `users.user-deleted` | Mismo formato que `tasks.task-created` (`[CÓDIGO TaskEvents.cs]`) |
| Versión | 1 | `conventions.md:108`: un cambio incompatible es un tipo nuevo con la versión siguiente |
| Exchange | `users.user-events` (`direct`) | Uno por feature que publica, como `tasks.task-events` (`docs/decisions.tsv`, paso 25) |
| Payload | `UserId` (string), `DeletedAt` (`DateTimeOffset`) | Lo mínimo: Tasks borra por `OwnerId`. `DeletedAt` sirve para la lápida y para los logs |
| Headers | `templateapp-message-id`, `templateapp-message-version`, `templateapp-occurred-at` | Los pone `IntegrationEventPublisher` (`[CÓDIGO backend/Infrastructure/Messaging/IntegrationEventPublisher.cs:26-29]`) |

- **Nombre del campo:** `UserId`, no `AuthId` (nombre interno de Users) ni `OwnerId` (nombre de Tasks). Las dos copias se leen por JSON en camelCase (`[CÓDIGO MessagingExtension.cs:43]`), así que los nombres de las propiedades tienen que coincidir. `UserId` es el nombre neutro del contrato [INFERENCIA, diseño]. El publicador traduce `AuthId` → `UserId` y el consumidor `UserId` → `OwnerId`.
- **Dos copias, cada una en su feature,** `internal`, con sufijo `IntegrationEvent`: `Features/Users/PublishUserEvents/UserDeletedIntegrationEvent.cs` y `Features/Tasks/DeleteOwnedTasks/UserDeletedIntegrationEvent.cs`. Es la regla del paso 25 (`[CÓDIGO docs/conventions.md:106,110]`): el monolito no compila Tasks, así que `Contracts/` no llega al otro proceso.
- **Constantes del lado que consume:** Tasks escribe su copia de `UserEvents` (nombre, versión, exchange), como Projects tiene su copia de `TaskEvents` (`[CÓDIGO backend/Features/Projects/TaskEvents.cs]`).

### 2.3 Quién actúa en la auditoría

- Hoy, el borrado de tareas por HTTP lleva `X-On-Behalf-Of`, y la auditoría de cada tarea nombra a quien borró la cuenta (`[CÓDIGO docs/architecture.md:70,87]`, test `DeleteOwnedTasksTests.TheAuditNamesTheUserTheCallerActsFor`).
- El consumidor corre fuera de un request HTTP. `AuditInterceptor` usa el actor `system` cuando no hay `HttpContext` (`[CÓDIGO backend/Infrastructure/Persistence/AuditInterceptor.cs:14-15,75-79]`). Las filas `Deleted` de las tareas pasan a decir `system`.
- **Recomendación:** aceptarlo y anotarlo. Es verdad: quien borra las tareas es el consumidor, por un evento. La fila `Deleted` del perfil sigue nombrando al usuario. Llevar el actor dentro del evento pide que `AuditInterceptor` lea un actor fuera de HTTP, que es otro cambio (regla 7). Va como choque 3 y como pendiente.

---

## 3. Consumidor en Tasks, con inbox

### 3.1 Registro

```csharp
services.AddIntegrationEventHandler<DeleteOwnedTasks.UserDeletedIntegrationEvent, DeleteOwnedTasks.UserDeletionsConsumer>(
    UserEvents.Deleted, UserEvents.Version, UserEvents.Exchange, OwnerTombstones.Consumer);
```

- `OwnerTombstones.Consumer = "tasks.user-deletions"` es a la vez la cola y el nombre del consumidor en el inbox, como `TaskCountersProjection.Consumer = "projects.task-counters"` (`[CÓDIGO backend/Features/Projects/TaskCountersProjection.cs:8]`, `[CÓDIGO backend/Features/Projects/ProjectsFeature.cs:24-27]`).
- `MessagingExtension` ya hace el resto sin cambios: cola quorum, `x-single-active-consumer`, DLQ `tasks.user-deletions.dlq` quorum, listener `Inline`, y el binding con la clave `users.user-deleted` (`[CÓDIGO MessagingExtension.cs:68-83]`).
- Tasks ya mapea su inbox (`[CÓDIGO TasksDbContext.cs:24]`). No hay tabla de inbox nueva.
- Las reglas de error son las globales del paso 25 (`[CÓDIGO MessagingExtension.cs:44-50]`): versión desconocida o header roto → DLQ al primer intento; `NpgsqlException` o `TimeoutException` → reintento a 1, 5 y 30 s y después pausa del listener; otro error → 3 vueltas y DLQ.

### 3.2 La operación

`DeleteOwnedTasks` (la slice que hoy usa el endpoint, `[CÓDIGO backend/Features/Tasks/DeleteOwnedTasks/DeleteOwnedTasks.cs:16-24]`) pasa a recibir el `EventContext` y queda así:

```
BEGIN (Database.BeginTransactionAsync)
  pg_advisory_xact_lock(<espacio>, <hash del propietario>)      -- exclusivo
  inbox.TryAcceptAsync(context)                                  -- si ya estaba: COMMIT y salir
  INSERT INTO tasks."DeletedOwners" ... ON CONFLICT DO NOTHING   -- la lápida
  SELECT tareas del propietario; MarkDeleted(); RemoveRange();   -- con el ChangeTracker
  SaveChanges                                                    -- filas TaskDeleted en tasks."OutboxMessages"
COMMIT
```

- **Inbox en la misma transacción que el efecto.** `Inbox.TryAcceptAsync` exige una transacción abierta (`[CÓDIGO backend/Infrastructure/Messaging/Inbox.cs:12-13]`). Es el patrón de `ApplyTaskEvent` (`[CÓDIGO backend/Features/Projects/ApplyTaskEvent/ApplyTaskEvent.cs:24-44]`) y el de microservices.io: el handler guarda el id del mensaje en la misma transacción ([FUENTE: microservices.io, Idempotent Consumer](https://microservices.io/patterns/communication-style/idempotent-consumer.html)).
- **El candado va primero**, antes del inbox. Así toda la operación queda en serie con `CreateTask` para ese propietario (sección 4).
- **Ack después del handler.** El listener es `Inline`: RabbitMQ recibe el ack cuando la transacción ya confirmó (`[CÓDIGO docs/architecture.md:119]`). Si Tasks muere entre el commit y el ack, RabbitMQ reentrega y el inbox lo descarta.
- **Error → excepción.** Como `TaskEventsConsumer` (`[CÓDIGO backend/Features/Projects/ApplyTaskEvent/TaskEventsConsumer.cs:31-36]`), el consumidor convierte un resultado de error en `ConsumerUnavailableException`. En este caso no hay resultado de error esperado; una falla de la base sale como `NpgsqlException` y se reintenta.

### 3.3 Por qué borrar con `RemoveRange` y no con `ExecuteDelete`

- `ExecuteDelete` sobre `TaskItem` está prohibido: DUNE0017 lo rechaza en una entidad `IAuditable`, que además no dejaría su evento en el outbox (`[CÓDIGO docs/conventions.md:101]`).
- Con `MarkDeleted` + `RemoveRange`, cada tarea deja su `TaskDeleted` en `tasks."OutboxMessages"` en el mismo `SaveChanges` (`[CÓDIGO TaskItem.cs:58-59]`). El relay de Tasks lo publica y el monolito resta en `projects."ProjectTaskCounters"` (`[CÓDIGO docs/architecture.md:108]`).
- **Por qué importa:** hoy `DeleteUser` borra los proyectos en la misma transacción (`[CÓDIGO DeleteUser.cs:44]`), así que esos `TaskDeleted` llegan a proyectos sin fila y no tienen efecto (`ApplyTaskEvent` lo loguea, `[CÓDIGO ApplyTaskEvent.cs:40-41]`). Pero el evento tiene que salir igual: la foto de reconstrucción cuenta las tareas que existen en Tasks (`architecture.md:110`). Si las tareas se borraran sin evento, una reconstrucción y la proyección incremental darían números distintos en un caso futuro donde el proyecto sobreviva (por ejemplo, un proyecto compartido) [INFERENCIA].

---

## 4. La lápida contra la carrera con `POST /tasks`

### 4.1 La ventana que queda aunque Tasks valide la sesión

- Tasks valida la sesión contra el monolito en cada request autenticado: `RemoteSession.ValidatePrincipalAsync` llama a `POST /internal/v1/session-checks` y, si la sesión no existe, rechaza la cookie (`[CÓDIGO backend/Features/Tasks/Access/RemoteSession.cs:9-25]`, `[CÓDIGO backend/Features/Tasks/Access/AuthSessionClient.cs:17-32]`). Borrar la cuenta borra sus sesiones, así que un `POST` que **empieza** después del borrado recibe 401.
- La ventana es un `POST` que **validó la sesión antes** de que `DeleteUser` confirmara y **guarda después**. `CreateTask` hace todavía cosas lentas entre la validación y el `INSERT`: el validador y, si hay proyecto, una llamada HTTP a Projects de hasta 2 s, todo dentro del `TransactionScope` del filtro de idempotencia (`[CÓDIGO backend/Features/Tasks/CreateTask/CreateTask.cs:37-50]`, `[CÓDIGO backend/Infrastructure/Http/IdempotencyFilter.cs:44]`).
- Hoy esa ventana ya existe y está anotada (`[CÓDIGO docs/evolucion/progreso.md:198]`, `:238`). Con el evento cambia de forma, no desaparece: el consumidor corre un poco después del commit (el relay mira cada segundo, `[CÓDIGO OutboxOptions.cs]`), y un `POST` lento puede guardar antes o después de él.

### 4.2 Los dos órdenes

| Orden | Qué pasa sin lápida | Qué tiene que pasar |
|---|---|---|
| **O1: el consumidor procesa el evento antes de que el `POST` guarde** | El consumidor borra las tareas que hay; el `POST` inserta después; la tarea queda huérfana | El `POST` ve la lápida y no guarda |
| **O2: el `POST` confirma antes de que el consumidor lea las tareas** | El consumidor la ve y la borra | Igual: el consumidor la borra |
| **O3 (el cruce): el `POST` consulta la lápida, el consumidor escribe la lápida y lee las tareas, el `POST` inserta y confirma, el consumidor confirma** | Ninguno ve lo del otro: el `POST` no vio la lápida (no estaba confirmada) y el consumidor no vio la tarea (tampoco). Tarea huérfana | Uno de los dos tiene que esperar al otro |

O3 es el problema real. En `READ COMMITTED` cada consulta ve solo lo confirmado antes de empezar ([FUENTE: PostgreSQL, Read Committed](https://www.postgresql.org/docs/current/transaction-iso.html#XACT-READ-COMMITTED)), así que dos transacciones que leen lo que la otra está por escribir pueden confirmar las dos [INFERENCIA, de la cita].

### 4.3 Opciones

| Opción | Cómo | Cubre O3 | Costo |
|---|---|---|---|
| (L0) Consultar la lápida sin candado | `SELECT 1 FROM "DeletedOwners" WHERE "OwnerId" = @id` en `CreateTask` | **No**: es el cruce de la tabla de arriba | Ninguno |
| (L1) `FOR SHARE` sobre la lápida | `SELECT ... FOR SHARE` en `CreateTask` | **No**: `FOR SHARE` "acquires a shared lock [...] on each retrieved row" ([FUENTE: PostgreSQL, Row-Level Locks](https://www.postgresql.org/docs/current/explicit-locking.html#LOCKING-ROWS)). Si la lápida todavía no existe, no hay fila que bloquear. PostgreSQL no bloquea "huecos" en `READ COMMITTED` [INFERENCIA, de la cita] | — |
| (L2) `SERIALIZABLE` | Las dos transacciones en serializable; PostgreSQL detecta el cruce con *predicate locking* y aborta una ([FUENTE: PostgreSQL, Serializable](https://www.postgresql.org/docs/current/transaction-iso.html#XACT-SERIALIZABLE)) | Sí | `CreateTask` corre dentro del scope `ReadCommitted` del filtro de idempotencia (`[CÓDIGO CrossContextTransaction.cs:10]`); habría que cambiar el nivel solo para esta slice y reintentar ante el error de serialización ("it should abort the current transaction and retry the whole transaction from the beginning", misma fuente). Un 500 o un reintento automático nuevos |
| (L3) Fila por propietario con `INSERT ... ON CONFLICT DO UPDATE` | `CreateTask` hace un *upsert* de `tasks."Owners"(OwnerId)` y lee `DeletedAt`; el consumidor hace el *upsert* con `DeletedAt = now`. El conflicto toma el candado de la fila: "ON CONFLICT DO UPDATE guarantees an atomic INSERT or UPDATE outcome [...] even under high concurrency" ([FUENTE: PostgreSQL, INSERT](https://www.postgresql.org/docs/current/sql-insert.html)) | Sí | Una escritura más en **cada** `POST /tasks` (y una versión muerta de la fila en cada una, por MVCC) solo para tener una fila que bloquear |
| **(L4) Advisory lock por propietario** (recomendada) | El consumidor toma `pg_advisory_xact_lock` exclusivo; `CreateTask` toma `pg_advisory_xact_lock_shared` justo antes de consultar la lápida e insertar | Sí (sección 4.4) | Una consulta liviana en cada `POST /tasks`; ninguna escritura. Es el patrón que el repo ya usa para la reconstrucción (`[CÓDIGO TaskCountersProjection.cs:12-18]`) |

### 4.4 Por qué (L4) cierra los dos órdenes

Funciones: "`pg_advisory_xact_lock` [...] Obtains an exclusive transaction-level advisory lock, waiting if necessary" y "`pg_advisory_xact_lock_shared` [...] Obtains a shared transaction-level advisory lock, waiting if necessary" ([FUENTE: PostgreSQL, Advisory Lock Functions](https://www.postgresql.org/docs/current/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS)). Un compartido y un exclusivo sobre la misma clave se excluyen; dos compartidos no (dos `POST` del mismo usuario no se esperan entre sí).

- **El consumidor tiene el candado (O1 o el cruce con el consumidor adelante).** `CreateTask` espera en `pg_advisory_xact_lock_shared`. El consumidor confirma la lápida y el borrado y suelta el candado al terminar ("automatically released at the end of the transaction", [FUENTE: PostgreSQL, Advisory Locks](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS)). Recién ahí `CreateTask` sigue y consulta la lápida en una **consulta nueva**. En `READ COMMITTED` "two successive SELECT commands can see different data, even though they are within a single transaction, if other transactions commit changes after the first SELECT starts and before the second SELECT starts" ([FUENTE: PostgreSQL, Read Committed](https://www.postgresql.org/docs/current/transaction-iso.html#XACT-READ-COMMITTED)). Ve la lápida y responde el error sin insertar.
- **`CreateTask` tiene el candado (O2 o el cruce con el `POST` adelante).** El consumidor espera en `pg_advisory_xact_lock`. `CreateTask` inserta y confirma; el candado se suelta con su transacción. El consumidor sigue y su `SELECT` de tareas es una consulta nueva: ve la tarea confirmada y la borra (por la misma cita).
- **Orden fijo dentro de cada transacción:** candado → lápida → escritura. Ninguna de las dos toma un segundo candado del otro tipo, así que no hay *deadlock* [INFERENCIA]. `CreateTask` toma el candado **después** de la llamada a Projects, para no tener al consumidor esperando 2 s.
- **Tiene que ser de transacción, no de sesión.** Un candado de sesión no se suelta al confirmar y, con el pool de Npgsql, quedaría en una conexión reutilizada [INFERENCIA, de la diferencia entre los dos tipos en la misma fuente].

**Detalles de la clave:**
- Se usa la forma de dos claves de 32 bits: la primera es un número fijo del "espacio" lápida y la segunda un hash del `OwnerId`. "a single 64-bit key value or two 32-bit key values (note that these two key spaces do not overlap)" ([FUENTE: PostgreSQL, Advisory Lock Functions](https://www.postgresql.org/docs/current/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS)). Así no choca con la clave de 64 bits de la reconstrucción (`7_164_253_024`).
- El hash se calcula en C# con un algoritmo estable entre procesos (por ejemplo, los primeros 4 bytes de SHA-256 del `OwnerId` en UTF-8). **No** `string.GetHashCode()`, que cambia en cada proceso [INFERENCIA; dos procesos de Tasks tienen que calcular la misma clave]. Dos propietarios con el mismo hash solo se esperan entre sí; no rompe nada.

### 4.5 Cómo entra en `CreateTask`

- **El `INSERT` y el candado tienen que estar en la misma transacción.** Con `Idempotency-Key`, `CreateTask` corre dentro del `TransactionScope` del filtro y la conexión de `TasksDbContext` se anota sola. Sin el header, hoy `SaveChangesAsync` abre y confirma su propia transacción (`[CÓDIGO CreateTask.cs:49-50]`), y un candado tomado con un `ExecuteSql` anterior se soltaría antes del `INSERT`.
- **Recomendación:** `CreateTask` abre `using TransactionScope transaction = CrossContextTransaction.Begin();` después de la llamada a Projects, toma el candado, consulta la lápida, inserta y llama a `transaction.Complete()`. `TransactionScopeOption.Required` "uses an ambient transaction if one already exists. Otherwise, it creates a new transaction" ([FUENTE: Microsoft, TransactionScopeOption](https://learn.microsoft.com/en-us/dotnet/api/system.transactions.transactionscopeoption)): con el filtro, se une a su transacción; sin el filtro, crea una. La llamada remota queda antes de `Begin()`, como pide DUNE0022 (`[CÓDIGO docs/conventions.md:28]`).
- Alternativa descartada: `Database.BeginTransactionAsync` cuando no hay scope. Pide preguntar si hay transacción ambiente y tener dos caminos. [NO VERIFICADO] qué hace EF Core si se llama `BeginTransactionAsync` con un `TransactionScope` activo.
- **Hacerlo ejecutable sin Dune:** la consulta a la lápida devuelve un `LiveOwner` (un tipo de Tasks con constructor privado) y `TaskItem.Create` pasa a pedir `LiveOwner` en vez de `string ownerId`. Ninguna slice futura puede crear una tarea sin pasar por la lápida, porque no compila [INFERENCIA, diseño]. El compilador no ve si el candado y el `INSERT` comparten transacción; eso lo prueban T-race-1 y T-race-2.

### 4.6 Qué responde el `POST` rechazado

- **Recomendación: 401 con `code = "Task.OwnerDeleted"`**, en `TaskErrors` como `Error.Unauthorized(...)`. `ApiResults` mapea `Unauthorized` a 401 (`[CÓDIGO backend/Shared/Http/ApiResults.cs:28]`, `[CÓDIGO docs/conventions.md:80-82]`).
- Por qué 401: la cuenta ya no existe, así que la credencial con la que entró el `POST` dejó de valer. RFC 9110: "The 401 (Unauthorized) status code indicates that the request has not been applied because it lacks valid authentication credentials for the target resource" ([FUENTE: RFC 9110, 401](https://www.rfc-editor.org/rfc/rfc9110#name-401-unauthorized)). Es lo mismo que recibe el request siguiente de esa cookie.
- El frontend ya lo maneja sin cambios: ante un 401 con la sesión abierta, el interceptor llama `session.end()` (`[CÓDIGO frontend/src/app/features/auth/session/auth-interceptor.ts:15-17]`), y el texto genérico del 401 es "La sesión ha caducado. Vuelve a entrar." si no hay `detail` (`[CÓDIGO frontend/src/app/core/http-error.ts:47-48]`).
- Alternativas: 409 `Task.OwnerDeleted` (el frontend mostraría el `detail` en inglés con el texto genérico de 409 y no cerraría la sesión) o 404 (no hay recurso que no exista). Las descarto.
- Efecto lateral bueno: con `Idempotency-Key`, un resultado de error no confirma, así que tampoco queda una clave de idempotencia nueva del propietario borrado (`[CÓDIGO docs/conventions.md:85]`).
- RFC 9110 pide `WWW-Authenticate` en un 401 ("The server generating a 401 response MUST send a WWW-Authenticate header field", misma fuente). [NO VERIFICADO] si los 401 actuales del repo lo mandan; el paso sigue lo que hagan ellos.

### 4.7 Retención de la lápida

- **No se borra nunca.** La fila es chica (`OwnerId` como clave primaria y `DeletedAt`). Los ids de Identity son GUID aleatorios y no se reusan [INFERENCIA; el `Id` de `IdentityUser` es un GUID en texto por defecto], así que la lápida nunca rechaza a otro usuario.
- Borrarla a los N días pide suponer que ningún `POST` tarda más que N y que ningún evento espera más que N en la cola. Con Tasks caído una semana el evento espera una semana: la lápida tiene que existir desde que el consumidor corre, no desde el borrado [INFERENCIA].
- Guarda un id opaco, igual que `AuditEntries` después del borrado (`[CÓDIGO docs/architecture.md:87]`). Si algún día se pide olvidar también el id, es un pendiente de privacidad.

---

## 5. `DeleteUser` sin la llamada HTTP

### 5.1 Cómo queda

```
cargar perfil; permiso
using TransactionScope transaction = CrossContextTransaction.Begin();
  _projects.DeleteOwnedProjectsAsync(authId)
  _keys.ForgetOwnerAsync(authId)
  profile.MarkDeleted(now); _db.UserProfiles.Remove(profile); SaveChanges   -- + fila en users."OutboxMessages"
  _auth.DeleteAccountAsync(authId)
transaction.Complete()
```

- Sale `await _tasks.DeleteOwnedTasksAsync(authId, ct);` (`[CÓDIGO DeleteUser.cs:40]`) y el parámetro `OwnedTasksClient` del constructor (`:21`).
- Con Tasks caído, nada de `DeleteUser` toca la red: responde 204. Es el primer "Verifica".

### 5.2 ¿Sale `CrossContextTransaction`?

El plan dice: "Puede sacar `CrossContextTransaction` de `DeleteUser` si Auth y Projects también pasan a eventos; si no, queda".

| Opción | Qué pasa | Gana | Pierde |
|---|---|---|---|
| **(T1) Queda** (recomendada) | Projects, claves, perfil y Auth siguen en un `TransactionScope`; solo Tasks pasa a evento | Un cambio observable (regla 1). Los tres están en el mismo proceso y la transacción los deja consistentes sin ventana | El acople en la transacción sigue (`architecture.md:89`) |
| (T2) Todo por eventos | Users borra el perfil y publica; Projects y Auth consumen `users.user-deleted` con su inbox | Saca el acople | Son dos consumidores más, dos inbox, y el orden importa: si Auth borra la cuenta después, hay una ventana donde la cuenta sigue y el perfil no; si Auth falla, nadie compensa. Es una saga coreografiada, el concepto del paso 26 (regla 8) |

**(T1).** El evento de este paso cruza de proceso; dentro del monolito la transacción es la herramienta correcta. Lo dejo como pendiente para el paso 26.

### 5.3 Qué pasa con el pendiente de `TransactionScope` síncrono

`progreso.md:202` dice que el evento "lo puede reemplazar en `DeleteUser`". Con (T1) no se reemplaza: el pendiente sigue, con una operación menos adentro.

---

## 6. Borrar el camino HTTP: commits como releases

### 6.1 Qué se borra

- `Features/Users/OwnedTasksClient.cs` (`[CÓDIGO :15-19]`), su registro `AddRemoteClient<OwnedTasksClient>(TasksService)` y la constante `UsersFeature.TasksService` (`[CÓDIGO backend/Features/Users/UsersFeature.cs:10,16]`). La configuración `Services:Tasks` del monolito **queda**: la usa `TaskCountsSnapshotClient` de Projects (`[CÓDIGO backend/Features/Projects/ProjectsFeature.cs:11,28]`, `compose.yaml:86`).
- `Features/Tasks/DeleteOwnedTasks/DeleteOwnedTasksEndpoint.cs` (`[CÓDIGO :5]`) y su línea en `MapTasksInternalEndpoints` (`[CÓDIGO TasksFeature.cs:64]`). La slice `DeleteOwnedTasks` queda, ahora llamada por el consumidor.
- La fila de la ruta en `architecture.md:67`.
- Tests que prueban el camino HTTP: `DeleteOwnedTasksTests` (pasa a probar la slice por el consumidor), y en `RemoteBoundaryTests` `TasksAnswerLost_AfterDeletingTheTasks_...` y `TasksUnreachable_DeletingTheAccount_Answers503_...` (`[CÓDIGO backend/Tests/Remote/RemoteBoundaryTests.cs:99,176]`), que se reemplazan por el test T1 de la sección 7.

### 6.2 Orden de releases

| Commit | Release | Qué entra | Por qué en este orden |
|---|---|---|---|
| **A** | Tasks | Migración `tasks."DeletedOwners"`, `OwnerTombstones`, `LiveOwner`, `CreateTask` con candado y lápida, `UserDeletedIntegrationEvent` (copia de Tasks), `UserDeletionsConsumer`, registro de la suscripción. El endpoint interno sigue | Tasks declara la cola `tasks.user-deletions` al arrancar. Si el monolito publicara antes, el exchange no rutea a nada: "For unroutable messages, the broker will issue a confirm once the exchange verifies a message won't route to any queue" ([FUENTE: RabbitMQ, Confirms](https://www.rabbitmq.com/docs/confirms#when-publishes-are-confirmed)). El relay marcaría `ProcessedAt` de un evento perdido (pendiente del paso 25, `progreso.md:271`) |
| **B** | Monolito | Migración `users."OutboxMessages"`, `UserProfile` con eventos, `UserDeleted`, publicador, `AddOutbox<UsersDbContext>`, `DeleteUser` sin la llamada | Con A desplegado, la cola existe. Durante el despliegue, un monolito viejo sigue llamando al endpoint (que existe) y uno nuevo publica: los dos caminos borran igual, y la lápida protege los dos |
| **C** | Tasks | Se borran `DeleteOwnedTasksEndpoint`, `OwnedTasksClient` y los tests del camino HTTP; docs | Ningún monolito desplegado llama ya al endpoint (endpoints internos con expand/contract, `progreso.md` pendiente del paso 23.2) |

- **¿La cola también la tendría que poder crear el monolito?** Declarar una cola es idempotente si los atributos coinciden: "The declaration will have no effect if the queue does already exist and its attributes are the same as those in the declaration. When the existing queue attributes are not the same [...] a channel-level exception with code 406 (PRECONDITION_FAILED) will be raised" ([FUENTE: RabbitMQ, Queues](https://www.rabbitmq.com/docs/queues)). Que el monolito declare la cola de Tasks obliga a copiar exacto los argumentos de Wolverine (quorum, SAC, DLX), con el riesgo del 406. Recomiendo no hacerlo en este paso y depender del orden de releases, como el paso 25. El pendiente del paso 25 sube de prioridad: con este evento, perderlo deja tareas huérfanas para siempre (la proyección se podía reconstruir; esto no).
- Durante A, la carrera vieja (borrado HTTP + `POST`) sigue sin lápida: el endpoint no la escribe. Se cierra en B. No vale la pena escribir la lápida desde un endpoint que C borra [INFERENCIA].

---

## 7. Tests

### 7.1 Base

- `IsolatedApi` levanta el monolito y Tasks con una base propia y un prefijo de colas propio sobre el `RabbitMqFixture` (`[CÓDIGO backend/Tests/Support/IsolatedApi.cs:30-67]`). `ApiPair` apaga el relay automático (`Outbox:RelayEnabled=false`, `[CÓDIGO backend/Tests/Support/ApiPair.cs:83]`) y los tests lo corren a mano.
- Hace falta, como helpers de test: el relay de Users (`GetRequiredKeyedService<OutboxRelay>(typeof(UsersDbContext))`), un `DrainUsersOutboxAsync` que publique y espere la fila del inbox `tasks.user-deletions`, y un `TasksListener` igual a `MonolithListener` (`[CÓDIGO backend/Tests/Support/MonolithListener.cs:9-13]`), que para y arranca el listener de Tasks.
- `DeleteUserTests` corre hoy sobre `ApiPair` sin broker (`[CÓDIGO backend/Features/Users/DeleteUser/DeleteUserTests.cs:7]`). `DeleteMe_LeavesNoRowOfTheAccountInAnyFeature` va a fallar: las tareas ya no se borran en el request. Pasa a `IsolatedApi` con el drenaje, o separa "filas del monolito en 0 enseguida" de "tareas en 0 después del evento" [INFERENCIA].

### 7.2 Los tests del Verifica

| Test | Qué hace | Qué afirma | Rojo primero |
|---|---|---|---|
| **T1 Tasks caído** | Alice crea tareas. `TasksListener.StopAsync` y un `RemoteFault` que deja a Tasks inalcanzable por HTTP. `DELETE /api/v1/users/me`. Se drena el outbox de Users. `TasksListener.StartAsync` | 204 enseguida; las filas del monolito en 0; las tareas siguen mientras Tasks está parado y quedan en 0 cuando vuelve; un `TaskDeleted` por tarea en `tasks."OutboxMessages"`; la lápida existe | Con el código de hoy responde 503 (es el test `TasksUnreachable_DeletingTheAccount_Answers503_...`, invertido) |
| **T2 Dos entregas** | Se publica a mano el mismo `users.user-deleted` dos veces con el mismo `templateapp-message-id` (con un helper como `TaskEventsBroker.PublishAsync`, `[CÓDIGO backend/Tests/Messaging/TaskEventsBroker.cs:44]`) | Una fila en el inbox; los `TaskDeleted` del outbox son uno por tarea, no dos | Con el inbox apagado (comentar `TryAcceptAsync`), la segunda entrega no encuentra tareas pero escribe otra vez la lápida y nada falla: el rojo se ve contando ejecuciones del efecto. Mejor: sembrar una tarea entre las dos entregas; sin inbox la borra, con inbox no |
| **T3 Tarea creada después (O1)** | El evento ya se procesó. Un `POST /tasks` que ya pasó la sesión (se llama a la slice `CreateTask` directo, con el `callerId` borrado) | 401 `Task.OwnerDeleted`; ninguna tarea del propietario | Sin la lápida, crea la tarea |
| **T-race-1 (cruce, consumidor adelante)** | El test toma el candado exclusivo del propietario desde una conexión propia y escribe la lápida sin confirmar; lanza `CreateTask`; espera a que quede bloqueado (`pg_stat_activity` con `wait_event_type = 'Lock'`); confirma | `CreateTask` termina con 401 y no inserta | Sin candado en `CreateTask`, no espera, no ve la lápida e inserta |
| **T-race-2 (cruce, `POST` adelante)** | El test toma el candado compartido y deja una tarea insertada sin confirmar; lanza el consumidor; espera a que quede bloqueado; confirma | El consumidor borra también esa tarea | Sin candado en el consumidor, no ve la tarea y queda huérfana |
| **T-atomic** | `DeleteUser` con Auth que falla después del `SaveChanges` del perfil (un `IAuthModule` de prueba) | Ninguna fila en `users."OutboxMessages"`; el perfil sigue | Si el publicador corriera fuera de la transacción, el evento saldría igual |

- La forma de T-race imita a `ApplyTaskEventTests`, que ya toma `pg_advisory_xact_lock` desde el test con un `NpgsqlCommand` propio (`[CÓDIGO backend/Features/Projects/ApplyTaskEvent/ApplyTaskEventTests.cs:147]`).
- [NO PROBADO] Ninguno corrió. La espera por `pg_stat_activity` es para no usar `Task.Delay`; si no se ve estable, alcanza con un `TaskCompletionSource` que el test marca antes de confirmar.
- E2E: el paso borra un contrato HTTP interno y suma un 401 a `POST /tasks`; corre la suite de Playwright al cerrar, como el paso 25 (`AGENTS.md`, "Verificación").

---

## 8. Dune (reglas 4 y 9)

- **Ninguna regla nueva.** La regla de diseño nueva es "toda tarea nueva pasa por la lápida". Un analyzer no ve si el candado y el `INSERT` comparten transacción. Con `TaskItem.Create(LiveOwner owner, ...)` lo hace cumplir el compilador (sección 4.5), y los tests T-race prueban el candado. La regla 4 pide una regla ejecutable; acá es el sistema de tipos, con su "violación deliberada" natural: llamar a `TaskItem.Create` con un `string` no compila [INFERENCIA]. Lo anoto como decisión.
- **Reglas que siguen valiendo sin cambio:**
  - DUNE0017: el consumidor borra con `RemoveRange`, no con `ExecuteDelete` (sección 3.3).
  - DUNE0021: `UsersDbContext` gana `MapOutbox()`, un helper de `Infrastructure` que la regla ya acepta (`[CÓDIGO docs/conventions.md:96]`).
  - DUNE0022: `CreateTask` llama a Projects antes de `CrossContextTransaction.Begin()`. Ojo: hoy DUNE0022 solo mira `CrossContextTransaction`; con la nueva línea en `CreateTask`, la regla **sí** ve la slice y obliga a dejar la llamada antes [INFERENCIA].
  - DUNE0023: el publicador de Users es un `IEventHandler<UserDeleted>` que solo llama `OutboxRelay`.
  - DUNE0025: ninguna feature nombra Wolverine: Users usa `IntegrationEventPublisher` y Tasks `IIntegrationEventHandler<T>`.
- **Gates:** `check-structure.mjs` no lista endpoints internos ni clientes remotos. El único script que nombra una ruta interna es `smoke-api.mjs:341` (`task-count-snapshot`), que no cambia. Las fixtures de `RemoteCallInTransactionAnalyzerTests` usan el nombre `OwnedTasksClient` como texto de ejemplo (`[CÓDIGO dune/Dune.Analyzers.Tests/RemoteCallInTransactionAnalyzerTests.cs:33-67]`); no leen el repo y pueden quedar.
- **Hueco anotado:** `.editorconfig` no lista DUNE0022 (pendiente). Si `CreateTask` pasa a depender de DUNE0022, conviene cerrarlo, pero es otro paso (regla 7).

---

## 9. Docs a actualizar

| Doc | Qué cambia |
|---|---|
| `architecture.md:50` | `UsersDbContext` mapea `UserProfiles` y `OutboxMessages`; `TasksDbContext` suma `DeletedOwners` |
| `architecture.md:67` | Se borra la fila de `DELETE /internal/v1/owners/{ownerId}/tasks` |
| `architecture.md:70` | El ejemplo de `X-On-Behalf-Of` (borrado de tareas) ya no aplica |
| `architecture.md:75` | "Con Tasks caído [...] borrar la cuenta responde 503" pasa a 204, con las tareas borradas cuando Tasks vuelve; sale el caso de la respuesta perdida |
| `architecture.md:87` | Las filas `Deleted` de las tareas llevan actor `system` (choque 3) |
| `architecture.md:89` | Sale "por eso `DeleteUser` borra las tareas antes de abrirla"; queda el scope con Projects, claves, perfil y Auth |
| `architecture.md:96-100, 115` | Hay dos outbox (Tasks y Users), dos productores y dos consumidores en el bus; "Consumidores hoy" suma `UserEventPublisher` y `UserDeletionsConsumer` |
| `architecture.md:195` | El Meter `TemplateApp.Outbox` mide el outbox de cada proceso |
| `conventions.md:93` | Hoy dice que `DeleteUser` llama a `ITasksModule.DeleteOwnedTasksAsync` (ya era falso desde el 23.2, `progreso.md:265`). Pasa a: Users publica `users.user-deleted` y Tasks borra con lápida |
| `conventions.md` (sección de eventos) | Una línea nueva: un consumidor que borra lo de un propietario escribe una lápida, y la creación toma el candado compartido antes de consultarla |
| `CONTEXT.md` | Término nuevo **Propietario borrado (lápida)**, evitando "tombstone" y "soft delete" |
| `docs/decisions.tsv` | Las decisiones de este archivo |
| ADR | No hace falta uno nuevo: ADR-0004 punto 9 ya dice "bus solo como transporte" y esto es un segundo uso [INFERENCIA] |

---

## Choques con los docs

1. **`architecture.md:75` y `:89`** dicen que con Tasks caído borrar la cuenta responde 503 y que `DeleteUser` borra las tareas antes de su transacción. El paso los vuelve falsos a propósito; se corrigen en el commit B.
2. **`conventions.md:93`** dice `ITasksModule.DeleteOwnedTasksAsync`, que no existe desde el paso 23.2 (ya anotado en `progreso.md:265`). El paso lo corrige porque cambia justo esa frase.
3. **`architecture.md:87`** promete que cada tarea borrada al borrar la cuenta lleva como actor al usuario que pidió el borrado. Con el evento, el actor es `system` (sección 2.3).
4. **`progreso.md:202`** sugiere que el evento reemplaza `CrossContextTransaction` en `DeleteUser`. La recomendación (T1) la deja; el plan lo permite ("si no, queda").
5. **`progreso.md:198`** propone como salidas "la lápida o un bloqueo de la cuenta que `CreateTask` comparta". La recomendación usa las dos juntas: la lápida sola no alcanza (sección 4.3, L0).

## Decisiones (con mi recomendación)

1. Reusar el outbox genérico sin tocar `Infrastructure/Messaging`: `AddOutbox<UsersDbContext>()`, `MapOutbox()` y una migración de Users que solo crea la tabla. Sin inbox en Users.
2. El relay de Users corre en el proceso del monolito, con las opciones por defecto (sin sección `Outbox` nueva).
3. `UserProfile` implementa `IHasDomainEvents`; `MarkDeleted(now)` anota `UserDeleted(AuthId, DeletedAt)`. `DeleteUser` lo llama antes de `Remove`.
4. Evento de integración `users.user-deleted` v1 en el exchange `direct` `users.user-events`, payload `UserId` y `DeletedAt`, una copia `internal` en Users (`PublishUserEvents/`) y otra en Tasks (`DeleteOwnedTasks/`), y la copia de constantes `UserEvents` en Tasks.
5. Cola quorum `tasks.user-deletions` con SAC y DLQ `tasks.user-deletions.dlq`; el mismo nombre para el consumidor del inbox.
6. El consumidor reusa la slice `DeleteOwnedTasks`: candado exclusivo → inbox → lápida → `MarkDeleted` + `RemoveRange` → `SaveChanges`, en una transacción. Sigue emitiendo `TaskDeleted`.
7. Lápida `tasks."DeletedOwners"` (`OwnerId` clave primaria, `DeletedAt`), escrita con `ON CONFLICT DO NOTHING`.
8. Advisory lock de transacción por propietario (L4): exclusivo en el consumidor, compartido en `CreateTask`, forma de dos claves de 32 bits con un hash estable del `OwnerId`. Descartadas L0, L1, L2 y L3.
9. `CreateTask` abre `CrossContextTransaction.Begin()` después de la llamada a Projects, para que el candado y el `INSERT` compartan transacción con o sin el filtro de idempotencia.
10. `TaskItem.Create` pide un `LiveOwner` que solo crea la consulta a la lápida.
11. El `POST` rechazado responde 401 `Task.OwnerDeleted`. Sin cambio en el frontend.
12. La lápida no se borra.
13. La auditoría de las tareas borradas por el evento dice `system`; llevar el actor en el evento queda pendiente.
14. `CrossContextTransaction` queda en `DeleteUser` con Projects, claves, perfil y Auth (T1).
15. Tres commits como releases: A Tasks escucha y protege `CreateTask`; B el monolito publica y deja de llamar; C se borra el camino HTTP. Tasks se despliega antes que el monolito.
16. El monolito no declara la cola de Tasks; se depende del orden de releases y el pendiente del paso 25 sube de prioridad.
17. Ninguna regla nueva de Dune; ningún gate cambia.
18. Tests T1, T2, T3, T-race-1, T-race-2 y T-atomic, cada uno con su rojo; `DeleteUserTests` y `RemoteBoundaryTests` se adaptan; e2e al cerrar.

## Herramientas (regla 5)

**No hace falta ninguna herramienta, librería ni tecnología nueva.** Todo es lo que ya está: EF Core y Npgsql (outbox, inbox, migraciones), PostgreSQL (advisory locks, `ON CONFLICT`), Wolverine 6.64.2 y RabbitMQ 4.4.1 (elegidos por Tobi en el paso 25, `docs/decisions.tsv`), `System.Security.Cryptography` del framework para el hash, y `Testcontainers.RabbitMq` para los tests. El hilo no tiene que frenar por la excepción de herramientas del modo autónomo.

## Pendientes para `progreso.md`

**Se cierran con el paso:**
- `progreso.md:197`: el evento de cuenta borrada ya no espera paso (las tareas huérfanas por un borrado de cuenta; el borrado por SQL a mano sigue sin detector).
- `progreso.md:198`: la carrera entre `DeleteUser` y `POST /tasks` (lápida + candado).
- `progreso.md:238`: el hueco de `DeleteUser` del paso 23.2 (tarea creada entre el borrado remoto y la transacción local, y la respuesta perdida).
- `progreso.md:247`: los pendientes de la cuenta borrada movidos al 25.1.
- `progreso.md:265`: `conventions.md` con `ITasksModule.DeleteOwnedTasksAsync`.

**Se abren o cambian:**
- Un `users.user-deleted` publicado antes de que exista `tasks.user-deletions` se pierde y deja tareas huérfanas para siempre. Es el pendiente del paso 25 (`progreso.md:271`), ahora con un costo mayor. Publicar con `mandatory` o declarar la cola desde los dos lados lo cerraría.
- La auditoría de lo que borra un consumidor dice `system`, no el usuario que originó el evento.
- `CrossContextTransaction` sigue en `DeleteUser` con Projects, claves, perfil y Auth: pasarlos a eventos es una saga (paso 26).
- Una clave de idempotencia de `POST /tasks` confirmada justo antes de que corra el consumidor (orden O2) queda en `platform."IdempotencyKeys"`: el consumidor borra la tarea pero no la clave, porque `ForgetOwnerAsync` corrió antes en el monolito [INFERENCIA].
- La lápida guarda el id opaco del propietario para siempre.
- La carrera entre archivar un proyecto y crear una tarea (`progreso.md:230`) sigue: es otro recurso, en otro proceso, y el candado de este paso es por propietario.

## Verificado / No verificado

**Verificado (leído en el repo o en una fuente primaria):**
- Todo `[CÓDIGO]` en `edcc7d7`: `DeleteUser`, `OwnedTasksClient`, `UsersFeature`, `UserProfile`, `UsersDbContext`, `CrossContextTransaction`, `DeleteOwnedTasks` y su endpoint, `CreateTask`, `TasksFeature`, `TasksDbContext`, `TaskItem`, `TaskErrors`, `RemoteSession`, `AuthSessionClient`, `ApplyTaskEvent`, `TaskEventsConsumer`, `TaskCountersProjection`, el outbox, el relay, el inbox, `MessagingExtension`, `MessagingRegistration`, `IntegrationEventPublisher`, `IdempotencyFilter`, `AuditInterceptor`, `ApiResults`, los dos `Program.cs`, `IsolatedApi`, `ApiPair`, `MonolithListener`, `RemoteBoundaryTests`, `DeleteUserTests`, el interceptor y los textos de error del frontend, y los docs citados.
- En la documentación de PostgreSQL (versión actual): `READ COMMITTED` y consultas sucesivas con datos distintos, *predicate locking* en `SERIALIZABLE` y el reintento, `FOR SHARE` sobre filas leídas, advisory locks de transacción, sus funciones y los dos espacios de claves, y la atomicidad de `ON CONFLICT DO UPDATE`.
- RFC 9110, sección 15.5.2: la definición del 401 y la exigencia de `WWW-Authenticate`.
- RabbitMQ: confirm de un mensaje sin ruta y declaración idempotente de colas (repo `rabbitmq-website`, `docs/confirms.md` y `docs/queues.md`). Microsoft: `TransactionScopeOption.Required`. Npgsql: `Enlist` en `true` por defecto. microservices.io: outbox transaccional.

**No verificado (queda para el paso):**
- Que la fila del outbox de Users viaje en la misma conexión del `TransactionScope` y se revierta si Auth falla (T-atomic).
- Que la migración de Users no cambie `UserProfiles` al sumar la lista de eventos.
- Qué hace EF Core con `BeginTransactionAsync` dentro de un `TransactionScope` (alternativa descartada).
- Si los 401 actuales mandan `WWW-Authenticate`.
- Que `pg_stat_activity` sirva para esperar el bloqueo en T-race sin `Task.Delay`.
- Ningún test corrió: todo lo de la sección 7 es diseño.
