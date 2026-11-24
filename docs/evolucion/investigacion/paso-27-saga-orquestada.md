# Paso 27: saga orquestada con máquina de estados persistida (investigación)

- Fecha: 2026-11-20.
- Estado: **Listo** como investigación. Tobi eligió la herramienta el 2026-11-20: **B completa** (`Wolverine.Saga` con el outbox durable de Wolverine, sección "Herramientas"). La recomendación de este archivo era A; el diseño de la sección 2 vale igual, y la implementación ajusta las secciones 3 a 7 a la opción B completa (ver `docs/decisions.tsv`).
- Alcance: el paso 27 del [plan](../plan.md) ("27. Saga orquestada con máquina de estados persistida"). Concepto: orquestación, visibilidad del proceso, reanudación tras caída. Verifica: test que reinicia el proceso a mitad de saga y esta continúa desde su estado persistido.
- Base: `main` en `95959da` (merge del PR #90, paso 26), rama `fase8/27-saga-orquestada`.
- Límites con los pasos vecinos: la durable execution (Temporal, Elsa) es del paso 28 (regla 8). La resiliencia de clientes HTTP es del 29. Este paso no adelanta ninguno.

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `95959da`.
- `[FUENTE]`: lo leí en un documento primario (docs y código de Wolverine en la etiqueta `V6.64.2`, NuGet, docs de MassTransit, Azure Architecture Center, microservices.io, Particular, EF Core, Npgsql). Cada uno tiene su link.
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Respuesta corta

1. **Qué proceso (pregunta 1).** El mismo cierre de cuenta del paso 26, pasado de coreografía a orquestación. Así el paso enseña la diferencia sobre un flujo que ya existe y tiene tests, y cierra los dos huecos que el paso 26 dejó anotados: el estado repartido en tres tablas y el cierre trabado que nadie ve por HTTP después del pivote (`[CÓDIGO docs/architecture.md:155]`). Un proceso nuevo sumaría dominio, contrato y frontend: serían dos cambios (regla 1).
2. **Qué cambia para Tobi.** Hoy, si querés saber en qué va un cierre, tenés que mirar tres tablas en tres schemas. Después del paso, hay **una fila por cierre** en `users."AccountClosureSagas"` con su estado (`HoldingTasks`, `Deciding`, `ReleasingTasks`, `Completed`, `Rejected`), y un SuperAdmin la ve con `GET /api/v1/users/closures/{closureId}`, también después de que la cuenta se borró. Si el monolito se cae a mitad de un cierre, al volver sigue desde esa fila.
3. **Quién manda.** Un orquestador en Users (la feature que abre y cierra el proceso) manda **comandos** a Tasks y a Auth (`tasks.hold-owner-tasks`, `auth.decide-account-closure`, `tasks.release-owner-tasks`) y escucha sus **respuestas** (`tasks.owner-tasks-held`, `auth.account-deleted`, `auth.account-closure-rejected` y una nueva, `tasks.owner-tasks-released`). Los participantes dejan de escucharse entre sí. Los pasos de Users (T1, T4, C1) corren dentro del orquestador, en la misma transacción que su fila.
4. **Herramienta (pregunta 2, decide Tobi).** Tres opciones: **A** orquestador a mano sobre lo que ya está (EF Core, outbox e inbox caseros, Wolverine solo como transporte); **B** el tipo `Saga` de Wolverine con `WolverineFx.EntityFrameworkCore` 6.64.2 (MIT); **C** la state machine de MassTransit (v8 Apache-2.0 con mantenimiento hasta el 31/12/2026, v9 comercial). Mi recomendación es **A** (sección "Herramientas"). B rompe cuatro reglas del repo (DUNE0025, DUNE0020, el gate de bus y `TypeLoadMode.Static`) y sus timeouts se pierden al reiniciar si no adopta además el outbox durable de Wolverine, que el paso 25 descartó. C suma un segundo bus que Tobi ya descartó.
5. **Contrato HTTP (regla 3).** `DELETE /users/me` sigue igual (202 con `closureId`). `GET /users/me` sigue igual. Entra una lectura nueva, `GET /api/v1/users/closures/{closureId}`, para el dueño o un SuperAdmin. El frontend no la consume (no hay pantalla de administración): no cambia (choque 6).
6. **Compensación.** El orquestador la ordena: primero C2 (Tasks suelta la retención), y recién con la respuesta C1 (Users vuelve el perfil a activo). Hoy corren en paralelo y hay una ventana con el perfil `active` y `POST /tasks` en 409 (`[CÓDIGO docs/architecture.md:157]`); en orden inverso esa ventana desaparece.
7. **Concurrencia e idempotencia.** Cada respuesta pasa por el inbox de Users y por una guarda de estado ("esta transición solo vale desde `HoldingTasks` y con este `closureId`"). La fila se toma con `SELECT ... FOR UPDATE`, como ya hace T1 (`[CÓDIGO backend/Features/Users/DeleteUser/DeleteUser.cs:29-31]`).
8. **Timeouts.** Cada estado guarda desde cuándo está ahí. Recomiendo que un cierre que pasa el plazo quede **marcado como atrasado** (visible en la lectura nueva, en un log y en una métrica), sin reenviar ni compensar solo. El reenvío automático tiene una carrera real (sección 3.6) y va a pendientes.
9. **El Verifica (pregunta 4).** `IsolatedApi` ya permite levantar un segundo par de hosts sobre la misma base y el mismo prefijo de colas (`[CÓDIGO backend/Tests/Support/IsolatedApi.cs:50-73]`). El test pide el cierre, deja que Tasks responda, **descarta los dos hosts** con la respuesta todavía en la cola, levanta hosts nuevos y espera: el cierre termina `Completed` con un solo comando por paso. Rojo primero: el orquestador guarda su estado en un diccionario en memoria; al reiniciar, la respuesta no encuentra el cierre y queda `closing` para siempre.
10. **Releases (pregunta 5).** Cinco commits: R1 Tasks entiende los comandos; R2 el monolito conoce la saga pero no la abre; R3 el monolito la abre; R4a y R4b sacan el camino coreografiado cuando no queda ningún cierre viejo en curso. Es el mismo motivo del paso 26: dos versiones del monolito comparten `users."OutboxMessages"` durante un rolling deploy.

## Glosario corto

- **Orquestación**: un coordinador central dice a cada participante qué hacer. "In orchestration, a centralized controller, or *orchestrator*, handles all the transactions and tells the participants which operation to perform based on events" ([FUENTE: Azure, Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)). Acá = el orquestador del cierre en Users.
- **Orquestador** (= dueño del estado del proceso): "The orchestrator performs saga requests, stores and interprets the states of each task, and handles failure recovery by using compensating transactions" (misma fuente).
- **Comando** (= pedido a un participante concreto): "To execute a saga step, it sends a command message to a participant telling it what operation to perform" ([FUENTE: microservices.io, Developing sagas part 4](https://microservices.io/post/sagas/2019/12/12/developing-sagas-part-4.html)). Nombre en imperativo: `tasks.hold-owner-tasks`.
- **Respuesta** (= evento que el participante devuelve): "After the saga participant has performed the operation, it sends a reply message to the orchestrator" (misma fuente). Nombre en pasado: `tasks.owner-tasks-held`.
- **Máquina de estados persistida**: una fila con el estado actual del proceso y reglas de qué mensaje lo mueve a qué estado. El framework de Richardson "persists each CreateOrderSagaData instance in the database" (misma fuente). Acá = `users."AccountClosureSagas"`.
- **Correlación** (= encontrar la fila de la saga desde un mensaje): "Correlation is needed in order to find existing saga instances based on data in the incoming message" ([FUENTE: Particular, Sagas](https://docs.particular.net/nservicebus/sagas/)). Acá = el `closureId` del payload.
- **Timeout de saga**: un mensaje programado a futuro que avisa "este paso tardó demasiado". Wolverine: "subclass the Wolverine `TimeoutMessage` for any logical message that will be scheduled in the future" ([FUENTE: Wolverine, Sagas](https://wolverinefx.net/guide/durability/sagas.html)).
- **Concurrencia optimista** (= no bloquear, fallar al guardar si otro cambió la fila): "optimistic concurrency takes no locks, but arranges for the data modification to fail on save if the data has changed since it was queried" ([FUENTE: EF Core, Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)).

---

## 1. Qué proceso es la saga orquestada

### 1.1 Qué pide el concepto

- **Orquestación.** Azure resume la diferencia: la orquestación "Avoids cyclic dependencies because the orchestrator manages the flow", y la coreografía tiene "a risk of cyclic dependency between saga participants" ([FUENTE: Azure, Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)). El paso 26 dejó ese ciclo anotado: Users → Tasks → Auth → Users (choque 6 de su investigación).
- **Visibilidad.** "Monitoring and tracking the workflow of a saga are essential tasks to maintain operational oversight" (misma fuente). La tabla del informe del repo lo dice igual: orquestación = "Un orquestador = un lugar donde ver el estado"; coreografía = "Estado distribuido, difícil de reconstruir" (`[CÓDIGO docs/evolucion/informes/informe-dotnet-empresarial.md:351]`).
- **Reanudación.** El orquestador no guarda nada en memoria: cada transición se guarda antes de mandar el comando siguiente. Así lo hace el framework de microservices.io: "Manages the execution of sagas including persisting their state in the database and communicating with saga participants" ([FUENTE: Developing sagas part 4](https://microservices.io/post/sagas/2019/12/12/developing-sagas-part-4.html)).

### 1.2 Candidatos

| Opción | Qué es | Gana | Pierde |
|---|---|---|---|
| **(P1) Orquestar el cierre de cuenta** (recomendada) | El cierre del paso 26, con un orquestador en vez de eventos entre participantes | Mismo flujo, mismos tests de base (V1 a V7 del paso 26): la diferencia entre coreografía y orquestación se ve sobre el mismo código. Cierra el "estado repartido" (`informe-dotnet-empresarial.md:417`) y el cierre trabado invisible después del pivote (`architecture.md:155`). No cambia lo que ve el usuario | Hay que migrar sin cortar cierres en curso (sección 7). Cada participante cambia su disparador |
| (P2) Un proceso nuevo, con la coreografía del 26 intacta | Por ejemplo, "transferir los proyectos de un usuario a otro" con Projects, Tasks y Users | Las dos formas de saga conviven y se comparan lado a lado | Dominio nuevo, reglas nuevas, contrato HTTP nuevo, frontend y e2e: el paso pasa a ser dos cambios (regla 1). El plan no lo pide |

**(P1).** "Un paso = un cambio observable" (regla 1): el cambio es "el cierre de cuenta tiene un dueño que guarda su estado y sigue después de una caída". El paso 26 ya lo anunció: "Es el costo que el paso 27 (orquestación) saca" (choque 6 de `paso-26-saga-coreografiada.md`).

### 1.3 Cómo está hoy (paso 26)

- T1 `DeleteUser` marca el perfil `Closing` bajo `FOR UPDATE`, y `UserProfile.RequestClosure` anota `AccountClosureRequested` (`[CÓDIGO backend/Features/Users/DeleteUser/DeleteUser.cs:28-41]`, `[CÓDIGO backend/Features/Users/Domain/UserProfile.cs:24-35]`).
- T2 Tasks consume `users.account-closure-requested` y escribe la retención (`[CÓDIGO backend/Features/Tasks/HoldOwnerTasks/HoldOwnerTasks.cs:22-43]`).
- T3 Auth consume `tasks.owner-tasks-held` y decide (`[CÓDIGO backend/Features/Auth/DecideAccountClosure/DecideAccountClosure.cs:30-63]`, `[CÓDIGO backend/Features/Auth/AuthFeature.cs:30]`).
- T4 y C1 Users consumen `auth.account-deleted` y `auth.account-closure-rejected` en la cola `users.account-closures` (`[CÓDIGO backend/Features/Users/UsersFeature.cs:24-25]`). C2 Tasks consume `auth.account-closure-rejected` en `tasks.account-closures` (`[CÓDIGO backend/Features/Tasks/TasksFeature.cs:41]`).
- El estado vive en tres tablas: `users."UserProfiles"`, `tasks."OwnerHolds"` y `auth."AccountClosures"` (`[CÓDIGO docs/architecture.md:158]`).

### 1.4 Cómo queda

```
DELETE /users/me ─► Orquestador (Users), una transacción de UsersDbContext:
                     perfil Closing (T1) + fila saga HoldingTasks + comando en el outbox
                       │ tasks.hold-owner-tasks
                       ▼
                     Tasks: retención (T2) ── responde tasks.owner-tasks-held ──┐
                                                                                 ▼
                     Orquestador: saga Deciding + comando auth.decide-account-closure
                       ▼
                     Auth: pivote (T3) ── auth.account-deleted ──────► Orquestador: purga (T4), saga Completed,
                       │                                                 users.user-deleted ──► Tasks T5 (25.1, sin cambio)
                       └─ auth.account-closure-rejected ──► Orquestador: saga ReleasingTasks + comando tasks.release-owner-tasks
                                                              ▼
                                                            Tasks: suelta la retención (C2) ── tasks.owner-tasks-released ──┐
                                                                                                                             ▼
                                                            Orquestador: perfil Rejected (C1) + saga Rejected
```

- Ningún participante escucha a otro participante: Tasks y Auth escuchan solo comandos y responden. El ciclo de dependencias desaparece.
- **T5 queda coreografiado** (decisión 9): el borrado de tareas sigue escuchando `users.user-deleted` del paso 25.1, que tiene su propia lápida y su propio contrato. El orquestador termina en `Completed` cuando T4 confirmó.

---

## 2. La máquina de estados (diseño independiente de la herramienta)

### 2.1 Estados y transiciones

| Estado | Entra cuando | Qué hace el orquestador al entrar (misma transacción) | Sale con |
|---|---|---|---|
| `HoldingTasks` | `DELETE` aceptado | Perfil `Closing` (T1), comando `tasks.hold-owner-tasks` | `tasks.owner-tasks-held` → `Deciding` |
| `Deciding` | Tasks retuvo | Comando `auth.decide-account-closure` | `auth.account-deleted` → `Completed`; `auth.account-closure-rejected` → `ReleasingTasks` |
| `ReleasingTasks` | Auth rechazó | Comando `tasks.release-owner-tasks`; guarda el motivo | `tasks.owner-tasks-released` → `Rejected` |
| `Completed` (final) | Auth borró | Purga T4 (proyectos, claves, perfil) y `users.user-deleted` | — |
| `Rejected` (final) | Tasks soltó | Perfil `Rejected` con el motivo (C1), métrica | — |

- Un mensaje que no corresponde al estado actual (duplicado, viejo o de otro `closureId`) **no cambia nada** y se registra con un log. Es la contramedida "Reread values: Confirm that data remains unchanged before you make updates" ([FUENTE: Azure, Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)), aplicada a la fila del orquestador.
- La fila **no se borra** al terminar: es la historia visible del cierre. Su limpieza va a pendientes.
- **T4 dentro del orquestador.** La purga hace tres escrituras separadas, en el orden que fijó el paso 26: proyectos, claves y, al final, una transacción de Users con inbox y perfil (`[CÓDIGO backend/Features/Users/CompleteAccountClosure/CompleteAccountClosure.cs:36-57]`). La fila del orquestador pasa a `Completed` en esa última transacción, la misma del perfil. Si el proceso muere antes, RabbitMQ reentrega y la purga se repite sin efecto (es reintentable).

### 2.2 Comandos y respuestas

| Mensaje | Tipo | Publica | Exchange (`direct`) | Payload | Consume (cola) |
|---|---|---|---|---|---|
| `tasks.hold-owner-tasks` v1 | Comando | Users (orquestador) | `tasks.commands` (nuevo) | `closureId`, `userId`, `requestedAt` | Tasks (`tasks.account-closures`) |
| `tasks.release-owner-tasks` v1 | Comando | Users | `tasks.commands` | `closureId`, `userId` | Tasks (`tasks.account-closures`) |
| `auth.decide-account-closure` v1 | Comando | Users | `auth.commands` (nuevo) | `closureId`, `userId` | Auth (`auth.account-closures`) |
| `tasks.owner-tasks-held` v1 (existe) | Respuesta | Tasks | `tasks.task-events` | sin cambio | Users (`users.account-closures`, binding nuevo) |
| `tasks.owner-tasks-released` v1 (nuevo) | Respuesta | Tasks | `tasks.task-events` | `closureId`, `userId`, `releasedAt` | Users (`users.account-closures`) |
| `auth.account-deleted`, `auth.account-closure-rejected` (existen) | Respuesta | Auth | `auth.account-events` | sin cambio | Users (`users.account-closures`, ya existe) |

- **El exchange de un comando es del que lo recibe.** En orquestación el participante define su "canal de comandos" y el orquestador le escribe ahí: "it sends a command message to a participant" ([FUENTE: microservices.io, part 4](https://microservices.io/post/sagas/2019/12/12/developing-sagas-part-4.html)). Por eso el nombre empieza con el participante (`tasks.`), no con Users [INFERENCIA, por analogía con los nombres de eventos del repo].
- **Misma plomería.** Un comando viaja igual que un evento de integración: `AddIntegrationEventPublication` del lado que manda y `AddIntegrationEventHandler` del que recibe, con su inbox (`[CÓDIGO backend/Infrastructure/Messaging/MessagingRegistration.cs:27-37]`). Ninguna regla de Dune exige el sufijo `IntegrationEvent` (busqué en `NamingAnalyzer`, `EventHandlerCallAnalyzer` y `BusBoundaryAnalyzer`, sin resultados). Propongo el sufijo `IntegrationCommand` para el record y una línea en `conventions.md`.
- **Un tipo por nombre dentro del proceso.** Users manda y Auth consume `auth.decide-account-closure` en el mismo monolito. Wolverine acepta un solo tipo de C# por nombre en un proceso (`[CÓDIGO docs/architecture.md:151]`), así que el tipo va en `Auth.Contracts` (`DecideAccountClosureInfo`), como ya hace `AccountDeletedInfo` (`[CÓDIGO docs/conventions.md:106]`).
- **Respuestas existentes.** `tasks.owner-tasks-held`, `auth.account-deleted` y `auth.account-closure-rejected` no cambian de nombre ni de payload: solo cambia quién las escucha. Menos contrato que migrar.

### 2.3 Correlación e idempotencia

- **Correlación.** El `closureId` (GUID v7 que ya nace en T1, `[CÓDIGO DeleteUser.cs:39]`) es la clave primaria de la fila del orquestador y va en el payload de cada comando y respuesta, como en el paso 26 (`[CÓDIGO docs/conventions.md:111]`).
- **Dos capas de idempotencia en el orquestador:**
  1. El inbox de Users (`Inbox.TryAcceptAsync`, `[CÓDIGO backend/Infrastructure/Messaging/Inbox.cs:8-28]`) descarta la misma entrega dos veces (mismo `MessageId`).
  2. La guarda de estado descarta un mensaje distinto con el mismo significado (otro `MessageId`, la misma respuesta).
- **Idempotencia en los participantes, por `closureId`.** Ya la tienen: `HoldOwnerTasks` renueva la misma retención (`[CÓDIGO HoldOwnerTasks.cs:31-35]`), `DecideAccountClosure` no decide dos veces el mismo cierre (`[CÓDIGO DecideAccountClosure.cs:34]`) y `ReleaseOwnerTasks` borra solo la retención de ese cierre (`[CÓDIGO backend/Features/Tasks/ReleaseOwnerTasks/ReleaseOwnerTasks.cs:27-29]`).

### 2.4 Concurrencia sobre la fila del orquestador

- **Quién escribe la fila:** el `DELETE` (crea), el consumidor de respuestas (avanza) y, si se elige un timeout activo, el que revisa plazos.
- **El consumidor ya va de a uno.** La cola `users.account-closures` tiene un solo consumidor activo (`x-single-active-consumer`, `[CÓDIGO backend/Infrastructure/Messaging/MessagingExtension.cs:66-70]`): con dos monolitos, solo uno procesa las respuestas.
- **Opciones:**
  - (K1) **`SELECT ... FOR UPDATE`** al cargar la fila en cada transición, como T1 (`[CÓDIGO DeleteUser.cs:29-31]`). Bloquea un instante y nunca falla.
  - (K2) **Concurrencia optimista con `xmin`**, como `TaskItem` y `Project` desde el paso 8: un `uint Version` con `IsRowVersion()` (`[CÓDIGO backend/Features/Tasks/Persistence/TaskItemConfiguration.cs:29-30]`), que Npgsql mapea a la columna `xmin` ("xmin holds the ID of the latest updating transaction", [FUENTE: Npgsql, Concurrency Tokens](https://www.npgsql.org/efcore/modeling/concurrency.html)). Si otro escribió primero, EF tira `DbUpdateConcurrencyException` ([FUENTE: EF Core, Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)). En este bus esa excepción hoy cae en la regla genérica: dos reintentos y DLQ (`[CÓDIGO MessagingExtension.cs:50]`). Habría que sumarla a la regla de reintento con espera.
- **Recomendación: K1.** Es el patrón que el repo ya usa para el mismo perfil, y no agrega una política de errores al bus. Con la opción B (Wolverine) la herramienta trae K2 hecho (sección "Herramientas").

### 2.5 Timeouts de un participante trabado

- **Qué se guarda en cualquier opción:** `StateChangedAt` en la fila. Con eso, "atrasado" es `now - StateChangedAt > plazo`, y no hace falta un temporizador para verlo.
- **Opciones:**
  - (T0) **Marcar atrasado** (recomendada): la lectura nueva muestra `overdue: true`, una métrica cuenta los cierres en curso por estado y la edad del más viejo, y no se hace nada solo. Mantiene lo que dice `architecture.md:74`: con Tasks caído, el cierre espera en `closing` hasta que Tasks vuelve.
  - (T1) **Reenviar el comando pendiente** al vencer el plazo (un servicio que revisa filas vencidas con `FOR UPDATE SKIP LOCKED`, como el relay). Cubriría el pendiente de prioridad alta del paso 26 (un pedido publicado antes de que exista la cola se pierde, `progreso.md`, Pendientes). Pero tiene una carrera (sección 3.6).
  - (T2) **Compensar al vencer** antes del pivote (pasar a `ReleasingTasks` sin decisión de Auth). Es el uso de libro de los timeouts ("You may frequently want to create "timeout" messages as part of a `Saga` to enforce time limitations", [FUENTE: Wolverine, Sagas](https://wolverinefx.net/guide/durability/sagas.html)), pero cambia el comportamiento documentado con Tasks caído y el caso "carol" del smoke `--compose`.
- **Recomendación: T0** en este paso; T1 a pendientes con su carrera escrita. El Verifica no pide timeouts, y T1 y T2 cambian comportamiento que el paso 26 dejó probado (regla 7).

### 2.6 Compensación dirigida por el orquestador

- **Orden inverso (C2 y después C1).** Microservices.io: "If a transaction fails, it then executes the compensating transactions in bottom to top order" ([FUENTE: part 4](https://microservices.io/post/sagas/2019/12/12/developing-sagas-part-4.html)). En coreografía el paso 26 las corrió en paralelo (decisión 7 de su investigación). Con un orquestador, el orden sale gratis y evita la ventana documentada en `architecture.md:157`: el perfil ya no dice `active` mientras Tasks todavía responde 409.
- **C2 necesita responder.** Hoy `ReleaseOwnerTasks` borra con `ExecuteDeleteAsync` y no deja evento (`[CÓDIGO ReleaseOwnerTasks.cs:27-29]`). Para responder por el outbox, la retención tiene que cargarse, anotar `OwnerTasksReleased` y borrarse con `Remove` + `SaveChanges`. Si no hay retención para ese `closureId` (una entrega repetida), no responde: la primera ya respondió.
- **Una falla técnica sigue sin compensar.** Igual que en el paso 26: se reintenta con las reglas del bus y, si es un bug, el mensaje va a la DLQ y el cierre queda en su estado, ahora visible en una sola fila (`[CÓDIGO docs/conventions.md:111]`).

---

## 3. Lo que tiene que cumplir cualquier herramienta

### 3.1 Atomicidad: estado y comando juntos

- El error que el paso 24 vino a evitar vale igual acá: si el orquestador guarda "estoy en `Deciding`" y el proceso muere antes de mandar el comando a Auth, el cierre queda trabado. Por eso **la fila del orquestador y el comando siguiente se guardan en la misma transacción**.
- Con el outbox casero sale solo: la fila del orquestador es una entidad de `UsersDbContext` con `IHasDomainEvents`, y el `OutboxInterceptor` guarda sus eventos en `users."OutboxMessages"` en el mismo `SaveChanges` (`[CÓDIGO docs/architecture.md:95]`).
- Con Wolverine, los mensajes que devuelve un handler de saga ("cascading messages") salen por el outbox de Wolverine. Ese outbox es durable solo con un almacén de mensajes: "You'll need to independently tell Wolverine where and how to store messages as part of the transactional inbox/outbox" ([FUENTE: Wolverine, EF Core](https://wolverinefx.net/guide/durability/efcore/)). El repo no tiene ese almacén (`[CÓDIGO docs/architecture.md:115]`: "sin su outbox ni su inbox").

### 3.2 Reanudación

- **Estado:** en la base, nunca en memoria.
- **Mensajes en vuelo:** RabbitMQ guarda la respuesta en la cola quorum hasta el ack, y el listener `Inline` confirma después del handler (`[CÓDIGO docs/architecture.md:119]`). Si el monolito muere, la respuesta espera.
- **Comandos sin publicar:** quedan en el outbox de Users y el relay los publica al volver (`[CÓDIGO docs/architecture.md:96]`).
- **Timers:** si existen, en la base. Wolverine sin almacén de mensajes programa en memoria: su `NullMessageStore` hace `ScheduledJobs?.Enqueue(envelope.ScheduledTime.Value, envelope)` ([FUENTE: código, `NullMessageStore.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Persistence/Durability/NullMessageStore.cs)), y la doc presenta esa vía como el último recurso ("In lieu of any other support, Wolverine has an in memory option", [FUENTE: Wolverine, Message Bus](https://wolverinefx.net/guide/messaging/message-bus.html)). Un reinicio la pierde [INFERENCIA, por estar en memoria].

### 3.3 Visibilidad

| Dónde | Qué muestra |
|---|---|
| `users."AccountClosureSagas"` | `ClosureId`, `UserId`, `State`, `Reason`, `StartedAt`, `StateChangedAt`, `FinishedAt`. Es `IAuditable`: cada transición deja su fila en `AuditEntries` con las columnas que cambiaron (`[CÓDIGO docs/architecture.md:78]`) |
| `GET /api/v1/users/closures/{closureId}` | La fila, más `overdue`. Dueño o `Users.Manage` |
| Meter `TemplateApp.Users` | Además de `finished` y `duration` (paso 26): un gauge de cierres en curso por estado y la edad del más viejo, como `templateapp.outbox.pending` (`[CÓDIGO backend/Infrastructure/Messaging/OutboxMetrics.cs:22-29]`) |
| Logs y trazas | Un log por transición con `{ClosureId}`, `{From}` y `{To}`; el tag `templateapp.account_closure.id` sigue |

- La lectura nueva resuelve el caso que el paso 26 no cubre: después del pivote, `GET /users/{authId}` responde 404 aunque el cierre esté trabado (`[CÓDIGO docs/architecture.md:155]`). La fila del orquestador sobrevive al borrado de la cuenta.
- Lo que no entra: un historial de transiciones consultable con valores. Es el "historial consultable y auditable" del paso 28 (`[CÓDIGO docs/evolucion/plan.md:458]`, regla 8).

### 3.4 Dónde vive el orquestador

| Opción | Gana | Pierde |
|---|---|---|
| **(O1) En Users** (recomendada) | Users abre y cierra el cierre y ya es dueño del perfil. T1, T4 y C1 quedan en la misma transacción que la fila del orquestador. Es la forma de Richardson: la saga vive en el servicio que la inicia (el ejemplo es `CreateOrderSaga` en Order Service, [FUENTE: part 4](https://microservices.io/post/sagas/2019/12/12/developing-sagas-part-4.html)) | Users crece |
| (O2) Feature nueva `AccountClosures` | Un módulo que solo orquesta | Tiene que mandar comandos también a Users (T1, T4, C1 dejan de ser locales), con otro schema, otro outbox y otro inbox. El doble de mensajes |

### 3.5 Contrato HTTP (regla 3)

| Ruta | Antes | Después |
|---|---|---|
| `DELETE /api/v1/users/me` y `/users/{authId}` | 202 `{closureId, status}` | Igual |
| `GET /api/v1/users/me` | `accountStatus`, `lastClosure` | Igual. Durante `ReleasingTasks` el perfil sigue `closing` un poco más (orden inverso) |
| `GET /api/v1/users/closures/{closureId}` | — | **Nueva.** 200 `{closureId, userId, state, reason, startedAt, stateChangedAt, finishedAt, overdue}`, 404 `User.ClosureNotFound`, 403 si no es el dueño ni tiene `Users.Manage` |

- Es una slice de lectura: proyecta la fila sin rastrearla (DUNE0019, `[CÓDIGO docs/conventions.md]`, sección "Lecturas").
- El `Location` del 202 no cambia (regla 7). Apuntarlo a la lectura nueva queda como opción.

### 3.6 La carrera del reenvío (por qué T1 va a pendientes)

1. El plazo de `HoldingTasks` vence y el orquestador reenvía `tasks.hold-owner-tasks` (otro `MessageId`).
2. Mientras tanto llega la respuesta del primer envío, Auth rechaza y el orquestador manda `tasks.release-owner-tasks`.
3. Tasks suelta la retención (C2) y responde. El perfil vuelve a `active`.
4. Si el reenvío llega **después** del release (con dos monolitos hay dos relays y el orden entre ellos no está garantizado, pendiente "orden con dos relays" del paso 24), Tasks vuelve a retener. El usuario queda con 409 en `POST /tasks` para siempre.

- El inbox no lo frena: el reenvío tiene otro `MessageId`. Lo frenaría una marca "este `closureId` ya se soltó" en Tasks. Es un cambio más de Tasks y de su migración: va a pendientes con esta descripción.

---

## 4. El test del Verifica

### 4.1 Lo que ya hay

- `IsolatedApi.StartAsync` crea una base vacía y un prefijo de colas propio; `IsolatedApi.StartOn(database, broker, prefix)` levanta otro par de hosts sobre la misma base y las mismas colas (`[CÓDIGO backend/Tests/Support/IsolatedApi.cs:36-73]`). `TaskEventConsumerTests` ya lo usa para dos monolitos (`[CÓDIGO backend/Tests/Messaging/TaskEventConsumerTests.cs:91]`).
- `Dispose` del par detiene los dos `WebApplicationFactory` (`[CÓDIGO backend/Tests/Support/ApiPair.cs:60-64]`): se pierde todo lo que estaba en memoria.
- Los relays no corren solos en los tests (`Outbox:RelayEnabled=false`, `[CÓDIGO ApiPair.cs:83]`); el test los llama con `PublishUsersOutboxAsync`, `PublishOutboxAsync` y `PublishAuthOutboxAsync`, y `DrainAsync` espera a que cada mensaje esté en el inbox de cada consumidor (`[CÓDIGO IsolatedApi.cs:89-155]`).
- `TasksListener` y `MonolithListener` paran y arrancan un listener (`[CÓDIGO backend/Tests/Support/TasksListener.cs]`, `[CÓDIGO backend/Tests/Support/MonolithListener.cs]`).

### 4.2 V1, el Verifica: reiniciar a mitad del cierre

1. Bob (usuario común) con 2 tareas y 1 proyecto pide el cierre: 202 con `closureId`.
2. Se para el listener de respuestas del monolito (un `MonolithListener` para `users.account-closures`; hoy solo mira `projects.task-counters`).
3. `PublishUsersOutboxAsync` (sale `tasks.hold-owner-tasks`), Tasks retiene, `PublishOutboxAsync` (sale `tasks.owner-tasks-held`, que espera en la cola del monolito parado).
4. Afirma a mitad: fila `HoldingTasks`, retención en `tasks."OwnerHolds"`, un mensaje esperando en la cola.
5. `api.Dispose()`. `IsolatedApi.StartOn(api.Database, broker, api.Prefix)`: hosts nuevos, nada en memoria.
6. `DrainAsync()` en los hosts nuevos.
7. Afirma al final: fila `Completed` con el mismo `closureId`, un solo `tasks.hold-owner-tasks` y un solo `auth.decide-account-closure` en el outbox de Users (no empezó de nuevo), todas las filas de Bob en 0 (`OwnedRows`), lápida en Tasks, `finished{outcome=completed}` = 1.

- **Rojo primero:** el orquestador guarda su estado en un diccionario de un singleton en vez de la tabla. Al reiniciar, `tasks.owner-tasks-held` no encuentra el cierre, se descarta con su log, y el perfil queda `closing`: falla el paso 7.
- **Por qué con la respuesta en la cola y no entre dos `DrainAsync`:** prueba las dos piezas de la reanudación juntas, el estado en la base y el mensaje en RabbitMQ. Que el proceso muera con un mensaje sin ack es lo que hace un corte real.

### 4.3 Los otros tests

| Test | Qué hace | Qué afirma | Rojo primero |
|---|---|---|---|
| V2 Muere después de guardar la transición | `FailAfterSaving` (`[CÓDIGO backend/Tests/Support/FailAfterSaving.cs:7-16]`) tira después del `SaveChanges` del paso `Deciding`; el bus reentrega | Un solo `auth.decide-account-closure` en el outbox, fila `Deciding` | Sin el inbox en el consumidor del orquestador, salen dos comandos |
| V3 Rechazo con orden inverso | Alice, la única SuperAdmin, pide el cierre (el V1 del paso 26 adaptado) | Pasa por `ReleasingTasks`; mientras tanto el perfil sigue `closing`; al final `Rejected`, retención en 0, perfil `active` con `lastClosure` | C1 antes de la respuesta de C2: el perfil dice `active` con la retención todavía puesta |
| V4 Respuesta fuera de estado | Llega `tasks.owner-tasks-held` para un cierre en `Completed` | La fila no cambia; un log de "transición ignorada" | Sin la guarda de estado, vuelve a `Deciding` y manda otro comando |
| V5 Atrasado | `FakeTimeProvider` (`[CÓDIGO IsolatedApi.cs:57]`) avanza más que el plazo con Tasks parado | `GET /users/closures/{id}` da `overdue: true`; el gauge cuenta 1 en `HoldingTasks` | Sin `StateChangedAt`, `overdue` no se puede calcular |
| V6 Lectura después del pivote | Cierre completo de Bob; un SuperAdmin lee el cierre | 200 con `Completed`; el dueño ya no tiene sesión (401) | Hoy `GET /users/{authId}` da 404 |
| V7 Cierres viejos durante el deploy | Un cierre abierto como en el paso 26 (sin fila del orquestador) con el código de R2 | Termina por el camino coreografiado | Si el consumidor de respuestas descarta todo lo que no tiene fila, el cierre viejo queda trabado |

- Cambian: `DeleteUserBrokerTests` (las afirmaciones de estado miran la fila del orquestador), `DecideAccountClosureTests`, `HoldOwnerTasksTests` y `ReleaseOwnerTasksTests` (comandos en vez de eventos), y la lista `expected` de `IsolatedApi.UndeliveredAsync` (`[CÓDIGO IsolatedApi.cs:129-139]`).
- [NO PROBADO] Ningún test corrió. Todo esto es diseño.

---

## 5. Dune (reglas 4, 9 y 10)

- **Con la opción A, ninguna regla nueva.** La regla de diseño nueva es "el estado del proceso vive en la base y cada transición guarda su comando en la misma transacción". La segunda mitad la da la construcción (entidad con eventos + `OutboxInterceptor`). La primera la prueba el rojo de V1. Una regla "nada de estado mutable en un singleton de una feature" no es analizable con seguridad (un singleton con un `Dictionary` adentro no se distingue de un caché legítimo sin seguir el registro de DI) [INFERENCIA].
- **Reglas que siguen valiendo sin cambio con A:** DUNE0017 (la fila del orquestador es `IAuditable` y solo se cambia con `SaveChanges`), DUNE0019 (la lectura nueva proyecta), DUNE0021 (la tabla nueva va en `UsersDbContext`), DUNE0023 (los publicadores siguen siendo `IEventHandler<T>` que solo llama `OutboxRelay`), DUNE0025 (ninguna feature nombra Wolverine).
- **Con la opción B, cuatro cambios obligatorios**, cada uno con su fixture válida y su violación deliberada (regla 4) y su línea en `decisions.tsv` (regla 9): ver "Herramientas", opción B.
- **Con la opción C:** el gate de bus de `check-structure.mjs` rechaza MassTransit en todo el producto (`[CÓDIGO dune/check-structure.mjs:206-219]`); habría que abrirlo.

---

## 6. Observabilidad del cierre trabado (relación con pendientes del 26)

- El pendiente "alerta por el cierre más viejo en `closing`" (`progreso.md`, Pendientes) pasa a tener su métrica: la edad del cierre en curso más viejo, por estado. La regla de alerta de Prometheus y su runbook siguen pendientes, con las alertas de DLQ del paso 25.
- El pendiente "cierre de un SuperAdmin por otro sin aviso al dueño" no cambia.

---

## 7. Rolling deploy y commits ordenados como releases

### 7.1 El riesgo

- Igual que en el paso 26: durante un rolling deploy dos versiones del monolito comparten `users."OutboxMessages"`. Si un monolito viejo reclama una fila con un tipo que no conoce (`tasks.hold-owner-tasks`), su relay gasta los intentos y la marca `FailedAt` (`[CÓDIGO docs/architecture.md:159]`).
- **Nuevo en este paso: cierres en curso.** Cuando sale la versión que orquesta, puede haber cierres abiertos por coreografía. Tienen que terminar por su camino. El consumidor de `auth.account-deleted` y `auth.account-closure-rejected` en Users es uno solo por tipo (un tipo por nombre en el proceso), así que decide por la fila: con fila del orquestador, avanza la saga; sin fila, corre la slice del paso 26 [INFERENCIA, diseño].
- **Bindings que quedan.** RabbitMQ guarda los bindings de una cola durable aunque el código deje de declararlos [INFERENCIA; NO PROBADO que Wolverine 6.64.2 no los borre]. `auth.account-closures` seguiría recibiendo `tasks.owner-tasks-held`. En la etapa de contract hay que quitar ese binding de forma explícita, y medir antes qué hace Wolverine con un mensaje sin handler en una cola escuchada.

### 7.2 Commits

| Commit | Release | Qué entra | Por qué en este orden |
|---|---|---|---|
| **R1** | Tasks | Consumidores de `tasks.hold-owner-tasks` y `tasks.release-owner-tasks` (bindings nuevos en `tasks.account-closures`), `OwnerHold.Release` con `OwnerTasksReleased` y su publicación. Sigue escuchando `users.account-closure-requested` y `auth.account-closure-rejected` | Tasks declara sus bindings de comandos antes de que alguien los mande; un comando sin binding se pierde con confirm (`[CÓDIGO docs/architecture.md:160]`). Mientras nadie mande comandos, no cambia nada |
| **R2** | Monolito | Migración de `users."AccountClosureSagas"` (expand). Users registra los comandos en su outbox y sus publicaciones, y los consumidores del orquestador (bindings nuevos en `users.account-closures`). Auth consume `auth.decide-account-closure`. El consumidor de las respuestas de Auth decide por la fila. `DELETE` sigue abriendo la coreografía | Cada par de versiones vecinas del monolito entiende todo lo que escribe la otra |
| **R3** | Monolito | `DELETE` abre la saga orquestada (fila + T1 + comando). Lectura nueva `GET /users/closures/{id}`, métrica, logs. Docs | Con R1 y R2 desplegados, cada comando tiene quién lo reciba |
| **R4a** | Monolito | Contract: Auth deja de escuchar `tasks.owner-tasks-held` (y se quita ese binding), Users deja el camino coreografiado y el registro de `users.account-closure-requested` | Solo cuando no queda ningún cierre coreografiado en curso ni filas pendientes de ese tipo en el outbox. La condición va escrita en `architecture.md` con la consulta que la comprueba |
| **R4b** | Tasks | Contract: Tasks deja de escuchar `users.account-closure-requested` y `auth.account-closure-rejected` (y se quitan esos bindings) | Después de R4a |

- Las migraciones solo agregan (tabla nueva): expand, sin contract de esquema (`[CÓDIGO docs/conventions.md]`, "Migraciones: expand/contract").
- Si Tobi prefiere un PR más chico, R4a y R4b pueden quedar como pendiente de un paso siguiente; el costo es convivir con dos caminos hasta entonces.

---

## 8. Docs a actualizar

| Doc | Qué cambia |
|---|---|
| `architecture.md`, sección "Cierre de cuenta (paso 26)" | Pasa a "Cierre de cuenta (pasos 26 y 27)": el orquestador, sus estados, comandos y respuestas, el orden inverso de la compensación, "Falla técnica" con el estado en una fila, y el despliegue en cinco releases |
| `architecture.md:74` | Sin cambio de sentido (con Tasks caído el cierre espera); suma que la lectura nueva lo muestra atrasado |
| `architecture.md:99-100` | Los consumidores nuevos y los comandos |
| `conventions.md:106-111` | Una línea para comandos: nombre en imperativo con el prefijo del que lo recibe, exchange `<feature>.commands`, sufijo `IntegrationCommand`; el orquestador guarda su transición y su comando en una transacción y descarta un mensaje fuera de estado |
| `CONTEXT.md` | **Cierre de cuenta** nombra al orquestador; término nuevo **Orquestador del cierre** (evitar "workflow", "manager") |
| ADR-0004 punto 4 | Sin cambio de texto: Users sigue recibiendo la decisión como evento. Con la opción B, el punto 9 cambia (choque 2) |
| `docs/decisions.tsv` | Las decisiones de este archivo y la herramienta elegida |

---

## Choques con los docs

1. **Dos docs empujan a herramientas distintas.** `plan.md:457` (paso 28) dice "workflow durable (p. ej. Temporal, Elsa) vs saga persistida propia del paso 27": da por hecho que el 27 deja una saga propia. `decisions.tsv:353` (paso 25) eligió Wolverine en parte por sus "sagas para el paso 27". La regla 10 dice que si dos docs se contradicen se pregunta a Tobi; acá coincide con su decisión de herramienta.
2. **Opción B y ADR-0004 punto 9** ("Ninguna slice lo usa como mediator ni lo conoce (DUNE0025)", `[CÓDIGO docs/adr/0004-vertical-slices-deep-modules.md:28]`): una clase que hereda `Wolverine.Saga` vive en una feature. Cambia el ADR, DUNE0025 y el gate de `check-structure.mjs` (`[CÓDIGO dune/check-structure.mjs:222-238]`).
3. **Opción B y DUNE0020** ("Tipo public fuera de Contracts"): el descubrimiento de Wolverine busca "public, concrete classes" ([FUENTE: Wolverine, Discovery](https://wolverinefx.net/guide/handlers/discovery.html)). [NO PROBADO] si `IncludeType` acepta una clase `internal` con el código generado en otro ensamblado.
4. **Opción B completa y la decisión 1 del paso 25** (outbox casero, sin el de Wolverine; `paso-25-broker-wolverine.md`, decisión 1) y `conventions.md:95` (cada feature es dueña de sus tablas de mensajes): los timeouts durables y los mensajes de la saga atómicos con su estado piden `PersistMessagesWithPostgresql` y tablas de Wolverine en un schema propio.
5. **Opción C y `decisions.tsv:353` y `:358`**: Tobi descartó MassTransit y fijó "un solo bus".
6. **Regla 3 con una lectura nueva sin consumidor en el frontend.** La regla pide backend, frontend y tests juntos cuando cambia un contrato. `GET /users/closures/{id}` no cambia nada que el frontend use, y no hay pantalla de administración. Propongo frontend sin cambio, e2e al cerrar igual (cambia la superficie HTTP). Si Tobi quiere, la página de perfil podría mostrar el estado del cierre en curso; es otro cambio.
7. **`informe-dotnet-empresarial.md:416`**: "Saga orquestada — Cuándo: Flujo largo con dinero, plazos y compensaciones". El cierre de cuenta no tiene dinero ni plazos. AGENTS.md dice que el patrón se aplica igual y el choque se anota en `decisions.tsv`.
8. **Decisión 7 del paso 26** (C1 y C2 en paralelo): este paso la reemplaza por el orden inverso. Va en `decisions.tsv` como cambio, con el motivo (`architecture.md:157`).
9. **`plan.md:452`** ("Cambia: módulo orquestador o infraestructura de sagas, tests"): no nombra un endpoint. La lectura nueva es la "visibilidad del proceso" del concepto; si Tobi la prefiere fuera, la visibilidad queda en la tabla, los logs y la métrica.
10. **T5 sigue coreografiado.** "Saga orquestada" podría leerse como "todo el cierre orquestado". Dejo T5 con el evento del paso 25.1 para no tocar su contrato ni su lápida (regla 7); el orquestador termina en `Completed` sin saber si Tasks ya borró las tareas.

## Decisiones (con mi recomendación)

1. La saga orquestada es el cierre de cuenta del paso 26 (P1), no un proceso nuevo.
2. El orquestador vive en Users (O1); T1, T4 y C1 corren en su transacción.
3. Tabla `users."AccountClosureSagas"`, clave `ClosureId`, `IAuditable`; la fila no se borra al terminar.
4. Estados: `HoldingTasks`, `Deciding`, `ReleasingTasks`, `Completed`, `Rejected`.
5. Tres comandos (`tasks.hold-owner-tasks`, `tasks.release-owner-tasks`, `auth.decide-account-closure`) y una respuesta nueva (`tasks.owner-tasks-released`); las tres respuestas existentes no cambian.
6. Exchange de comandos por participante (`tasks.commands`, `auth.commands`); colas existentes con bindings nuevos.
7. Sufijo `IntegrationCommand`; el tipo de `auth.decide-account-closure` va en `Auth.Contracts`.
8. Idempotencia en dos capas: inbox de Users y guarda de estado.
9. T5 sigue escuchando `users.user-deleted` (paso 25.1); el orquestador termina en T4.
10. Concurrencia con `SELECT ... FOR UPDATE` (K1), no `xmin`.
11. Compensación en orden inverso: C2, respuesta, C1.
12. C2 responde por el outbox: la retención se carga, anota `OwnerTasksReleased` y se borra con `Remove`.
13. Timeouts T0: plazo visible (`overdue`) y métrica, sin reenvío ni compensación automática.
14. Lectura nueva `GET /api/v1/users/closures/{closureId}` para el dueño o `Users.Manage`; `DELETE` y `GET /users/me` sin cambio; frontend sin cambio.
15. Gauge de cierres en curso por estado y edad del más viejo en el Meter `TemplateApp.Users`; log por transición.
16. Una falla técnica sigue sin compensar.
17. El consumidor de respuestas de Auth decide por la fila (orquestado o coreografiado) mientras convivan.
18. Cinco commits como releases: R1 Tasks, R2 monolito conoce, R3 monolito abre, R4a y R4b contract.
19. Ninguna regla nueva de Dune con la opción A.
20. Tests V1 a V7 con su rojo; e2e al cerrar.
21. El reenvío de comandos (T1) y la marca "ya soltado" en Tasks van a pendientes.

## Herramientas (regla 5)

**Esta sección es la decisión de Tobi.** Abajo están las tres opciones con lo que gana y lo que pierde con cada una. Mi recomendación va aparte, al final.

### Opción A: orquestador a mano sobre lo que ya está

- **Paquetes:** ninguno nuevo. EF Core 10.0.12, Npgsql 10.0.3, `WolverineFx.RabbitMQ` 6.64.2 solo como transporte (`[CÓDIGO backend/Platform/TemplateApp.Platform.csproj:30-31,40-44]`).
- **Licencia:** sin cambio (Wolverine MIT, ya elegido).
- **Cómo se guarda el estado:** una entidad de dominio `AccountClosureSaga` con métodos de transición (`OnTasksHeld`, `OnAccountDeleted`…) que anotan sus comandos como eventos de dominio. El `OutboxInterceptor` los guarda en el mismo `SaveChanges` que la fila.
- **Concurrencia:** `FOR UPDATE` (o `xmin` como en el paso 8).
- **Timeouts:** una columna `StateChangedAt`. Si algún día hay reenvío, un servicio que revisa filas vencidas con `FOR UPDATE SKIP LOCKED`, como `OutboxRelay`. Sobrevive a un reinicio porque está en la base.
- **Reanudación y su test:** sección 4.
- **Qué cambia en el repo:** Users (entidad, tabla, consumidores, lectura), Tasks y Auth (comandos en vez de eventos), docs. Dune sin cambio.
- **Qué aprende Tobi:** la máquina de estados escrita a mano (cada transición, cada guarda, dónde se guarda el comando), que es lo que una librería esconde. El paso 28 compara justo esto contra una plataforma (`plan.md:457`).
- **Gana:** cero choques con las reglas de los pasos 24, 25 y 26; atomicidad con el outbox que ya está probado; timers en la base; el código de la saga se lee sin conocer una librería.
- **Pierde:** más código propio (guardas, carga de la fila, despacho por estado). No aprende la API de sagas de una librería, que es lo más común en equipos .NET.

### Opción B: `Wolverine.Saga`

- **Paquetes:** `WolverineFx.EntityFrameworkCore` **6.64.2** (16/11/2026, MIT), que depende de `WolverineFx.RDBMS` ≥ 6.64.2, `Weasel.EntityFrameworkCore` ≥ 9.50.0 y EF Core ≥ 10.0.4 ([FUENTE: NuGet](https://www.nuget.org/packages/WolverineFx.EntityFrameworkCore/6.64.2)). Coincide con el 6.64.2 de hoy: **no hace falta subir de versión**. 6.64.2 es la última estable de la rama 6; hay una 7.0.0-alpha.3 ([FUENTE: NuGet, WolverineFx](https://www.nuget.org/packages/WolverineFx)). Según la variante, además:
  - `WolverineFx.Postgresql` 6.64.2 (MIT, depende de `Weasel.Postgresql` ≥ 9.50.0, [FUENTE: NuGet](https://www.nuget.org/packages/WolverineFx.Postgresql/6.64.2)) para el almacén de mensajes durable.
  - `WolverineFx.RuntimeCompilation` 6.64.2 (MIT, depende de `JasperFx.RuntimeCompiler` ≥ 5.1.0, [FUENTE: NuGet](https://www.nuget.org/packages/WolverineFx.RuntimeCompilation/6.64.2)) si no se pregenera el código.
- **Licencia:** MIT ([FUENTE: NuGet, WolverineFx](https://www.nuget.org/packages/WolverineFx)).
- **Cómo funciona:** el estado es una clase que hereda `Wolverine.Saga` ("A saga state document type that is persisted between saga messages that must inherit from the `Wolverine.Saga` type", [FUENTE: Wolverine, Sagas](https://wolverinefx.net/guide/durability/sagas.html)), con métodos `Start`, `Handle`, `NotFound` y `MarkCompleted()` (misma fuente). La identidad sale de un miembro `[SagaIdentity]`, `{Saga}Id`, `SagaId` o `Id` del mensaje (misma fuente).
- **Cómo se guarda:** con EF Core, basta que un `DbContext` registrado mapee el tipo ("having a mapping for whatever `Saga` type you need to persist in a registered `DbContext` type", [FUENTE: Wolverine, EF Core Sagas](https://wolverinefx.net/guide/durability/efcore/sagas.html)). El código que genera Wolverine llama a `SaveChangesAsync` del contexto ([FUENTE: código, `EFCorePersistenceFrameProvider.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs)). Alternativa: el almacenamiento liviano de `WolverineFx.Postgresql`, una tabla por saga con el estado en `jsonb` y una columna `version` ([FUENTE: código, `DatabaseSagaSchema.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.Postgresql/Internals/DatabaseSagaSchema.cs)).
- **Concurrencia:** "Both the Marten and EF Core backed saga support has built in support for optimistic concurrency checks" ([FUENTE: Wolverine, Sagas](https://wolverinefx.net/guide/durability/sagas.html)). Con EF Core, si la saga tiene una propiedad `Version` pública de lectura y escritura, el código generado la incrementa cuando la entidad cambió (`if (context.Entry(saga).State == Modified) { saga.Version += 1; }`, [FUENTE: código, `EFCorePersistenceFrameProvider.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Persistence/Wolverine.EntityFrameworkCore/Codegen/EFCorePersistenceFrameProvider.cs)). [INFERENCIA] Hay que marcarla como token de concurrencia en EF; [NO PROBADO] qué pasa si se mapea a `xmin` como hace el repo, porque Wolverine le escribe un valor.
- **Timeouts:** `TimeoutMessage` programado. Sin almacén de mensajes, Wolverine lo programa en memoria (`NullMessageStore`, sección 3.2) y un reinicio lo pierde. Con `PersistMessagesWithPostgresql` "Wolverine will store the scheduled messages in the outgoing envelope storage for later execution" y "This option is durable in case of process exits" ([FUENTE: Wolverine, Message Bus](https://wolverinefx.net/guide/messaging/message-bus.html)). RabbitMQ no aparece entre los transportes con programación nativa en esa página.
- **Con el outbox e inbox caseros:** las dos variantes posibles.
  - **B-completa:** comandos como "cascading messages" de Wolverine, timeouts `TimeoutMessage`, y el outbox durable de Wolverine (`WolverineFx.Postgresql` + `UseEntityFrameworkCoreTransactions`). Es Wolverine como se documenta. Revierte la decisión 1 del paso 25 para los mensajes de la saga y suma las tablas y el agente de durabilidad de Wolverine (choque 4).
  - **B-híbrida:** solo `WolverineFx.EntityFrameworkCore` para cargar y guardar la saga; la clase de la saga anota sus comandos como eventos de dominio y el `OutboxInterceptor` del repo los guarda en el mismo `SaveChanges` [INFERENCIA; NO PROBADO que el interceptor vea la entidad en el `SaveChanges` generado]. Los timers van en la base como en A. Usa poco de lo que Wolverine trae.
- **Qué pide en los handlers:**
  - La saga es un handler de Wolverine con código generado. Hoy `MessagingExtension` usa `TypeLoadMode.Static` y apaga el descubrimiento por convención (`[CÓDIGO MessagingExtension.cs:40-41]`), y registra cada mensaje con un `MessageHandler` propio, sin generación (`[CÓDIGO MessagingExtension.cs:75,88-91]`). Con `Static`, Wolverine "throws a MissingPreBuiltTypesException naming the chains it could not load" si falta el código pregenerado ([FUENTE: Wolverine, Code Generation](https://wolverinefx.net/guide/codegen.html)). Hay dos salidas: `codegen write` y versionar el código generado, o `TypeLoadMode.Dynamic` con `WolverineFx.RuntimeCompilation` ("Applications running TypeLoadMode.Dynamic (the default) or Auto now must reference WolverineFx.RuntimeCompilation", misma fuente). Es la decisión 10 del paso 25, que se reabre.
  - La saga se suma con `Discovery.IncludeType(...)` ([FUENTE: Wolverine, Sagas](https://wolverinefx.net/guide/durability/sagas.html), sección de tests), y el descubrimiento busca clases públicas (choque 3).
  - Los mensajes que consume la saga no pasan por `IntegrationEventHandler<T>`, que hoy valida la versión y los headers del sobre y arma el `EventContext` del inbox (`[CÓDIGO backend/Infrastructure/Messaging/IntegrationEventHandler.cs:18-32]`). Esa validación hay que rehacerla para la saga [INFERENCIA].
  - Wolverine prohíbe que dos sagas manejen el mismo tipo: "Wolverine does not allow multiple saga types to handle the same message type" ([FUENTE: Wolverine, Sagas](https://wolverinefx.net/guide/durability/sagas.html)). [NO PROBADO] qué hace si un tipo tiene a la vez un `MessageHandler` registrado a mano y una saga; afecta a `auth.account-deleted` durante la convivencia con el camino coreografiado (sección 7).
  - La saga no puede llamar a `IMessageBus.InvokeAsync` sobre sí misma (misma fuente, advertencia de "How it works"); DUNE0025 ya prohíbe `InvokeAsync` en todo el producto.
- **Qué cambia en Dune y en los docs (reglas 9 y 10):** DUNE0025 acepta nombres de Wolverine en el archivo de la saga; el gate de `check-structure.mjs` acepta `WolverineFx.EntityFrameworkCore` (y en B-completa `WolverineFx.Postgresql`) en `Platform` y `using Wolverine` en ese archivo; DUNE0020 o la clase en otro lugar; ADR-0004 punto 9; `conventions.md:102`. Cada cambio con su fixture válida y su violación deliberada.
- **Qué aprende Tobi:** el modelo de sagas de una librería de mensajería (estado como documento, convenciones de métodos, identidad por header, concurrencia y timeouts de la librería), y el costo de meterla en una arquitectura que la dejó afuera a propósito.
- **Gana:** menos código propio de despacho; concurrencia optimista y timeouts que ya existen; es la herramienta que el paso 25 eligió pensando en este paso (`decisions.tsv:353`).
- **Pierde:** cuatro reglas del repo cambian; generación de código (pregenerado o en tiempo de ejecución); en B-completa, dos outbox conviviendo y las tablas de Wolverine; en B-híbrida, usa poco de Wolverine y los timers igual son propios. La saga conoce Wolverine, y las slices dejan de ser independientes del bus.

### Opción C: state machine de MassTransit

- **Paquetes:** `MassTransit` con su transporte de RabbitMQ y la persistencia de EF Core para sagas. No consulté versiones exactas: la opción no es viable en este repo (abajo).
- **Licencia:** v8 Apache-2.0, mantenimiento oficial hasta el 31/12/2026, o sea seis semanas desde hoy (`[CÓDIGO docs/decisions.tsv:353]`, [FUENTE: MassTransit, v9 announcement](https://masstransit.io/introduction/v9-announcement)). v9: "MassTransit v9 (and beyond) requires a license to use, which gives you a license key file" ([FUENTE: MassTransit, License](https://masstransit.massient.com/configuration/license)).
- **Cómo funciona:** una clase que deriva de `MassTransitStateMachine<T>` con estados y eventos, correlacionados por `CorrelationId` ([FUENTE: MassTransit, Saga State Machines](https://masstransit.massient.com/guides/saga-state-machines)). [NO VERIFICADO] qué programador de mensajes necesitan sus timeouts y cómo resuelve la concurrencia con EF Core: la página no lo dice.
- **¿Es viable con Wolverine como bus?** Técnicamente sí: serían dos librerías sobre el mismo RabbitMQ, y Wolverine sabe hablar el formato de MassTransit ("Wolverine can interoperate bi-directionally with MassTransit using RabbitMQ", con la condición de que "MassTransit is using its standard 'envelope' serialization approach", [FUENTE: Wolverine, RabbitMQ Interoperability](https://wolverinefx.net/guide/messaging/transports/rabbitmq/interoperability.html)). En la práctica, no: dos buses, dos topologías y dos formatos de sobre, contra "un solo bus" (`decisions.tsv:358`) y contra el descarte de Tobi del paso 25 (`decisions.tsv:353`). v8 sale de mantenimiento antes de que el repo termine la fase 8.
- **Gana:** la state machine más documentada de .NET; la sintaxis declarativa (`Initially`, `During`) se lee como un diagrama.
- **Pierde:** un segundo bus, un paquete sin mantenimiento en semanas o una licencia paga, y deshacer una decisión de Tobi.

### Qué hace la industria

- Los equipos .NET con bus de librería usan la saga de esa librería: NServiceBus, MassTransit y Wolverine traen la suya (`[CÓDIGO docs/evolucion/informes/informe-dotnet-empresarial.md:359-363]`). Comparten tres piezas: estado guardado entre mensajes ("All public get/set properties are persisted by default"), correlación por un dato del mensaje y timeouts ([FUENTE: Particular, Sagas](https://docs.particular.net/nservicebus/sagas/)).
- Todas insisten en que la saga solo decide y manda mensajes: "A saga should only interact with its own internal state and send or publish messages" (misma fuente). Es lo que hace el orquestador de este diseño con Tasks y Auth.
- Fuera de .NET, el framework de referencia de Richardson (Eventuate Tram) guarda la saga en la base y le habla a los participantes por comandos ([FUENTE: part 4](https://microservices.io/post/sagas/2019/12/12/developing-sagas-part-4.html)). Las plataformas de durable execution (Temporal) son el paso 28.

### Mi recomendación: opción A

1. Es la única que no choca con las reglas que Tobi aceptó en los pasos 24, 25 y 26: outbox casero, Wolverine solo como transporte, ninguna feature conoce el bus, sin generación de código.
2. Cumple el Verifica sin piezas nuevas: estado, comandos y timers en PostgreSQL. B solo lo cumple para los timers si suma el outbox durable de Wolverine (B-completa), que el paso 25 descartó por publicar antes de confirmar dentro del `TransactionScope`.
3. Deja al paso 28 la comparación que el plan escribe: "workflow durable vs saga persistida propia del paso 27" (`plan.md:457`).
4. Lo que se pierde (aprender la API de sagas de Wolverine) se puede recuperar después: el diseño de la sección 2 es el mismo con cualquier herramienta, y B-híbrida sobre ese diseño sería un cambio acotado.

Si Tobi elige B, recomiendo **B-híbrida** primero: mantiene el outbox casero y la atomicidad, y limita los cambios de Dune al archivo de la saga.

## Pendientes para `progreso.md`

**Se cierran o cambian con el paso:**
- "El estado vive repartido en tres tablas" (paso 26, choque 5 de su investigación): queda una fila por cierre.
- "Un cierre con un mensaje en una DLQ queda trabado para siempre": sigue trabado, pero se ve en una fila, en la lectura nueva y en un gauge; falta la regla de alerta y el runbook.

**Se abren:**
- Reenvío del comando pendiente cuando vence el plazo (T1), con una marca "este cierre ya se soltó" en Tasks contra la carrera de la sección 3.6. Cerraría el pendiente de prioridad alta del paso 26 (pedido publicado antes de que exista la cola).
- Limpieza de las filas terminadas de `users."AccountClosureSagas"`.
- T5 sigue coreografiado: el orquestador no sabe si Tasks borró las tareas.
- Pantalla para que un SuperAdmin vea los cierres en curso (hoy solo hay endpoint).
- Medir qué hace Wolverine 6.64.2 con un mensaje sin handler en una cola escuchada, y cómo quitar un binding viejo (contract R4).
- Projects como participante (pendiente del paso 26): con el orquestador sería un comando más.

## Verificado / No verificado

**Verificado (leído en el repo o en una fuente primaria):**
- Todo `[CÓDIGO]` en `95959da`: `DeleteUser`, `UserProfile`, `AccountClosureStatus`, `UsersFeature`, `CompleteAccountClosure`, `CancelAccountClosure`, `DecideAccountClosure` y su consumidor, `AccountClosure`, `AuthFeature`, `HoldOwnerTasks`, `ReleaseOwnerTasks`, `TasksFeature`, `TaskItemConfiguration`, `MessagingRegistration`, `MessagingExtension`, `IntegrationEventHandler`, `IntegrationEventPublisher`, `Inbox`, `OutboxMetrics`, `IsolatedApi`, `ApiPair`, `TasksListener`, `MonolithListener`, `RabbitMqFixture`, `FailAfterSaving`, `DeleteUserBrokerTests`, `TaskEventConsumerTests`, los `.csproj`, DUNE0025 y el gate de bus de `check-structure.mjs`, y los docs citados.
- Wolverine: docs de sagas (tipo `Saga`, convenciones, identidad, concurrencia, timeouts, almacenamiento liviano, una saga por tipo de mensaje), de EF Core (paquete, almacén de mensajes aparte, `SaveChangesAsync` y flush), de sagas con EF Core, de scheduling (nativo, durable, en memoria), de codegen (`MissingPreBuiltTypesException`, `RuntimeCompilation`), de discovery (clases públicas) y de interoperabilidad con MassTransit. Código en `V6.64.2`: `EFCorePersistenceFrameProvider.cs` (incremento de `Version`), `DatabaseSagaSchema.cs` (tabla `jsonb` + `version`), `NullMessageStore.cs` (programación en memoria).
- NuGet: `WolverineFx`, `WolverineFx.EntityFrameworkCore`, `WolverineFx.Postgresql` y `WolverineFx.RuntimeCompilation` en 6.64.2, MIT, y sus dependencias.
- Azure (Saga), microservices.io (Saga y part 4), Particular (Sagas), EF Core (Concurrency), Npgsql (Concurrency Tokens), MassTransit (License y Saga State Machines).

**No verificado (queda para el paso o para Tobi):**
- La fecha de fin de mantenimiento de MassTransit v8 viene de `decisions.tsv:353` y de una búsqueda; la página del anuncio redirige y no la leí entera.
- Los timeouts y la concurrencia de MassTransit con EF Core.
- Que `IncludeType` acepte una saga `internal`; qué pasa con una saga y un `MessageHandler` manual para el mismo tipo; que el `OutboxInterceptor` vea los eventos de una saga guardada por el código de Wolverine; qué pasa con `Version` mapeada a `xmin`.
- Que RabbitMQ conserve los bindings que el código deja de declarar, y qué hace Wolverine con un mensaje sin handler.
- Que RabbitMQ no tenga programación nativa en el transporte de Wolverine (la doc nombra solo Azure Service Bus como ejemplo).
- Ningún test corrió: todo lo de las secciones 2, 4 y 7 es diseño.
