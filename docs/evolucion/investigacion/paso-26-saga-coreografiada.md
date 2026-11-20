# Paso 26: saga coreografiada, cierre de cuenta en tres pasos con compensación (investigación)

- Fecha: 2026-11-20.
- Estado: **Listo** como investigación. No hace falta ninguna herramienta nueva: todo sale de lo que ya está (EF Core, Npgsql, PostgreSQL, el outbox y el inbox caseros de los pasos 24 y 24.1, y Wolverine con RabbitMQ del paso 25). Hay 8 choques con los docs y 22 decisiones, cada una con mi recomendación (modo autónomo). Ninguna pregunta bloquea.
- Alcance: el paso 26 del [plan](../plan.md) ("26. Saga coreografiada (3 pasos con compensación)"). Concepto: transacción distribuida sin locks; compensación explícita. Verifica: test donde el último paso falla y la compensación deja el sistema consistente y observable.
- Base: `main` en `19f91aa` (merge del PR #88, paso 25.1), rama `fase8/26-saga-coreografiada`.
- Límites con los pasos vecinos: la saga orquestada con estado persistido es del paso 27 (con su decisión de herramienta pendiente) y la durable execution del 28. Este paso **no** usa el tipo `Saga` de Wolverine ni un orquestador (regla 8). La resiliencia de clientes HTTP es del paso 29.

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `19f91aa`.
- `[FUENTE]`: lo leí en un documento primario (Garcia-Molina y Salem 1987, microservices.io, Azure Architecture Center, RFC 9110, OpenTelemetry, RabbitMQ, PostgreSQL, Wolverine). Cada uno tiene su link.
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Respuesta corta

1. **Qué flujo (pregunta 1).** El cierre de cuenta. Hoy `DeleteUser` borra proyectos, claves, perfil y cuenta en un `TransactionScope` (`[CÓDIGO backend/Features/Users/DeleteUser/DeleteUser.cs:40-53]`), y los docs ya le asignan esto al paso 26: "pasarlos a eventos es una saga (paso 26)" (`[CÓDIGO docs/architecture.md:88]`, `[CÓDIGO docs/decisions.tsv:392]`). La saga tiene tres participantes con su propia base lógica y su outbox: Users (monolito), Tasks (otro proceso) y Auth (monolito).
2. **Los tres pasos.** T1 Users pasa el perfil a **en cierre** y publica `users.account-closure-requested`. T2 Tasks **retiene** al propietario (no acepta tareas nuevas) y publica `tasks.owner-tasks-held`. T3 Auth decide: borra la identidad (el punto de no retorno) o **rechaza** el cierre por una regla de negocio, "no se borra el último SuperAdmin". Si T3 rechaza, corren las compensaciones C2 (Tasks suelta la retención) y C1 (Users vuelve el perfil a activo y anota el rechazo).
3. **Por qué así se puede compensar un borrado.** Ningún borrado se compensa. Lo irreversible (borrar la identidad) es el **pivote** y va último; antes solo hay **bloqueos semánticos** (estados "en cierre" y "retenido"), que se deshacen sin pérdida. Lo que se borra después del pivote (proyectos, claves, perfil, tareas) son pasos **reintentables**: idempotentes, sin compensación. Es el orden de Azure: compensables, pivote, reintentables.
4. **Qué ve el usuario (contrato HTTP, regla 3).** `DELETE /api/v1/users/me` pasa de 204 a **202 Accepted** con `{closureId, status: "closing"}`. `GET /api/v1/users/me` suma `accountStatus` y `lastClosure`. `POST /api/v1/tasks` durante el cierre responde **409 `Task.OwnerClosing`**. Si el cierre se completa, la sesión muere (401). Si se rechaza, el perfil vuelve a `active` y `lastClosure` dice `rejected` con el motivo. El frontend no tiene botón de borrar cuenta; cambia el modelo `UserResponse` y la página de perfil muestra el estado.
5. **Mensajería (pregunta 3).** Cuatro eventos de integración nuevos, uno por paso o resultado, cada uno con el `closureId` (el id de la saga) en el payload. Tres colas quorum nuevas, una por participante: `tasks.account-closures`, `auth.account-closures` y `users.account-closures`, todas con SAC y DLQ, armadas por el `MessagingExtension` que ya existe. Cada consumidor usa su inbox en la misma transacción que su efecto. No se usa `Wolverine.Saga`.
6. **Observabilidad (pregunta 4).** El `closureId` está en cada evento, en cada log de paso y como tag de la actividad. Cada participante guarda su parte del estado con ese id (perfil, `tasks."OwnerHolds"`, `auth."AccountClosures"`), y la traza de cada salto queda enlazada con la anterior por el `traceparent` del outbox. Una métrica cuenta los cierres por resultado.
7. **El test del Verifica (pregunta 5).** Sin inyectar fallas: el último paso falla por la regla de negocio. Alice es la única SuperAdmin, pide el cierre, el test drena los tres outbox y comprueba: a mitad, el 409 al crear tarea y `accountStatus: closing`; al final, la cuenta, la sesión, las tareas y el proyecto intactos, sin retención, `accountStatus: active`, `lastClosure.outcome: rejected`, la métrica en 1 y los cinco logs del cierre con el mismo id. Rojo primero: sin los consumidores de compensación, la retención y el estado "en cierre" quedan para siempre.
8. **Dune (pregunta 6).** Ninguna regla nueva. La garantía nueva (ninguna tarea nueva durante el cierre) la da otra vez el tipo `LiveOwner`, que ahora solo se emite si no hay lápida ni retención.
9. **Releases (pregunta 7).** Tres commits: **A** Tasks escucha y retiene; **B** el monolito (Auth decide, Users pide y termina, contrato HTTP, frontend, smoke); **C** se borra `IAuthModule.DeleteAccountAsync` y lo que queda del camino síncrono.
10. **Tamaño.** Es un cambio observable ("borrar la cuenta pasa a ser un cierre que se puede rechazar y compensa"), del tamaño del 25.1. No propongo partirlo. Lo que queda afuera va a pendientes (sección 12).

## Glosario corto

- **Saga**: una transacción larga escrita como una secuencia de transacciones locales que pueden intercalarse con otras. "A LLT is a saga if it can be written as a sequence of transactions that can be interleaved with other transactions" ([FUENTE: Garcia-Molina y Salem, *Sagas*, SIGMOD 1987, p. 249](https://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf)). Acá = el cierre de cuenta.
- **Coreografía**: los participantes se pasan eventos sin un coordinador. "each local transaction publishes domain events that trigger local transactions in other services" ([FUENTE: microservices.io, Saga](https://microservices.io/patterns/data/saga.html)).
- **Compensación** (transacción compensatoria): deshace "desde el punto de vista semántico" un paso ya confirmado, sin volver necesariamente al estado de antes. "The compensating transaction undoes, from a semantic point of view, any of the actions performed by Ti, but does not necessarily return the database to the state that existed when the execution of Ti began" ([FUENTE: Garcia-Molina y Salem, p. 250](https://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf)).
- **Compensable, pivote, reintentable**: los tres tipos de paso. "Compensable transactions can be undone or compensated for by other transactions with the opposite effect"; "Pivot transactions serve as the point of no return in the saga"; "Retryable transactions are idempotent and help ensure that the saga can reach its final state" ([FUENTE: Azure, Saga distributed transactions pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)).
- **Bloqueo semántico** (*semantic lock*): un estado de la aplicación que marca "esto está en medio de una saga". "The semantic lock counter-measure is a kind of application-level locking" ([FUENTE: microservices.io, Developing sagas part 1](https://microservices.io/post/microservices/2019/07/09/developing-sagas-part-1.html)). Acá = perfil "en cierre" y propietario "retenido".
- **Id del cierre** (`closureId`, = id de la saga): un GUID que nace en T1 y viaja en cada evento. "The begin-saga command can return a saga identifier to the program" ([FUENTE: Garcia-Molina y Salem, p. 252](https://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf)).
- **Retención** (*hold*): en Tasks, una fila que dice "este propietario está en cierre": no acepta tareas nuevas hasta que el cierre termine o se rechace. Distinta de la **lápida** del paso 25.1, que es para siempre.

---

## 1. Qué flujo es la saga

### 1.1 Qué pide el concepto

- Una saga garantiza uno de dos resultados: "Either the sequence T1, T2, … Tn (which is the preferable one) or the sequence T1, T2, … Tj, Cj, … C2, C1 for some 0 ≤ j < n will be executed" ([FUENTE: Garcia-Molina y Salem, p. 250](https://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf)). El Verifica es el segundo resultado con j = n−1: el último paso falla y corren todas las compensaciones anteriores.
- El último paso no necesita compensación: "if an end-saga command ends both the last transaction and the saga, there is no need to have a compensating transaction for the last transaction" ([FUENTE: misma, p. 252](https://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf)). Por eso el paso que puede fallar va último y es el pivote.
- Microservices.io pide compensación para cada paso "followed by a step that can fail for business reasons" ([FUENTE: Developing sagas part 1](https://microservices.io/post/microservices/2019/07/09/developing-sagas-part-1.html)): la falla que dispara la compensación es **de negocio**, no técnica. Una falla técnica se reintenta: "Only stop the operation and trigger compensation if the step fails repeatedly or you can't recover it" ([FUENTE: Azure, Compensating Transaction pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)).

### 1.2 Candidatos

| Opción | Pasos | Dónde falla | Gana | Pierde |
|---|---|---|---|---|
| **(F1) Cierre de cuenta** (recomendada) | T1 Users "en cierre" → T2 Tasks retiene → T3 Auth borra o rechaza | T3, por "último SuperAdmin" | Es lo que los docs ya le asignan al paso 26 (`architecture.md:88`, `decisions.tsv:392`, `progreso.md:202`). Tres participantes con su contexto, uno en otro proceso. Saca `CrossContextTransaction` de `DeleteUser`. El último paso falla por una regla de negocio real. Ningún borrado se compensa | Cambia el contrato de `DELETE /users/me` (204 → 202). Suma outbox e inbox a Auth e inbox a Users. Agrega una regla de negocio nueva (último SuperAdmin) |
| (F2) Tarea en proyecto como saga | T1 Tasks crea la tarea "pendiente" → T2 Projects acepta o rechaza → T3 Tasks confirma o rechaza la tarea | T2 (Projects rechaza un proyecto archivado o ajeno) | Compensación natural (la tarea pasa a rechazada). Saca la llamada HTTP de `CreateTask` (`[CÓDIGO backend/Features/Tasks/CreateTask/CreateTask.cs:39-47]`) y cerraría la carrera archivar/crear (`progreso.md:230`) | Dos participantes, no tres. Falla el paso del medio, no el último: T3 es reintentable y no puede fallar. Cambia el contrato del flujo principal (`POST /tasks` devuelve una tarea pendiente), toda la página de tareas, los contadores de proyectos y varias e2e |
| (F3) Archivar proyecto con veto de Tasks | T1 Projects "archivando" → T2 Tasks revisa tareas abiertas → T3 Projects confirma | T2 (Tasks veta si hay tareas abiertas) | Bloqueo semántico claro en Projects | Dos participantes. La regla "no se archiva con tareas abiertas" no existe y contradice `CONTEXT.md` ("Conserva sus tareas y sus contadores"). Falla el paso del medio |

**(F1).** Es la única de las tres donde el **último** paso falla por negocio y hay **tres** participantes. Y la regla 10 la favorece: los docs ya dicen que pasar `DeleteUser` a eventos es la saga de este paso.

### 1.3 Cómo está hoy

- `DeleteUser` (Users) valida el permiso y, en un `TransactionScope`, llama a `IProjectsModule.DeleteOwnedProjectsAsync`, `IdempotencyKeys.ForgetOwnerAsync`, `profile.MarkDeleted` + `Remove` + `SaveChanges` (que deja `users.user-deleted` en el outbox) y `IAuthModule.DeleteAccountAsync` (`[CÓDIGO DeleteUser.cs:40-53]`).
- `DeleteAccountAsync` borra la identidad con `UserManager.DeleteAsync`; las sesiones caen por FK (`[CÓDIGO backend/Features/Auth/AuthModule.cs:45-58]`, `[CÓDIGO docs/architecture.md:54]`).
- Tasks consume `users.user-deleted` y borra las tareas con lápida (`[CÓDIGO backend/Features/Tasks/DeleteOwnedTasks/DeleteOwnedTasks.cs:18-34]`). Ese tramo del 25.1 **queda igual**: es el último paso reintentable de la saga nueva.
- Hay dos rutas: `DELETE /users/me` y `DELETE /users/{authId}` (un SuperAdmin borra a otro); las dos responden 204 (`[CÓDIGO backend/Features/Users/DeleteUser/DeleteUserEndpoint.cs]`).
- SuperAdmin es el único rol con `UserPermissions.Manage` (`[CÓDIGO backend/Features/Auth/Access/RolePermissions.cs:13-24]`). Hoy nada impide borrar al último: después nadie puede administrar usuarios. Es la regla de negocio del pivote.

### 1.4 Cómo queda

```
POST/DELETE /users/me ──► T1 Users: perfil "en cierre" (closureId)          [compensable: C1]
                            outbox users ── users.account-closure-requested ──┐
                                                                               ▼
                          T2 Tasks: retención del propietario                [compensable: C2]
                            outbox tasks ── tasks.owner-tasks-held ───────────┐
                                                                               ▼
                          T3 Auth: ¿último SuperAdmin?                        [pivote]
                            ├─ no  → borra identidad (+ sesiones) ── auth.account-deleted ──► T4 Users: proyectos, claves, perfil
                            │                                                                     └─ users.user-deleted ──► T5 Tasks: lápida y tareas (paso 25.1)
                            └─ sí  → decisión "rechazado" ── auth.account-closure-rejected ──┬─► C2 Tasks: suelta la retención
                                                                                              └─► C1 Users: perfil activo + rechazo anotado
```

- T4 y T5 son **reintentables**: idempotentes, sin compensación, se repiten hasta terminar.
- C1 y C2 corren en paralelo, cada uno en su proceso. No dependen del orden entre sí (sección 2.4).

---

## 2. Diseño de la compensación

### 2.1 Compensación semántica, no deshacer

- Ninguna compensación restaura una foto vieja. Azure: "A compensating transaction doesn't necessarily return the system data to its state at the start of the original operation", porque "this approach can overwrite changes from other concurrent application instances" ([FUENTE: Azure, Compensating Transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)).
- **C1** no escribe "el perfil como estaba": cambia solo el estado del cierre (`Closing` → `Active`) y anota el rechazo. Si el usuario cambió su nombre visible durante el cierre, se conserva.
- **C2** no "desbloquea todo": borra **la retención de ese `closureId`**. Si llegara una retención de otro cierre, no la toca.
- Por eso las compensaciones son semánticas y además dejan rastro (el rechazo queda anotado): Azure pide "ensure that you can correlate and audit both the original operation and its compensation end-to-end" (misma fuente).

### 2.2 Tipos de paso y orden

| Paso | Participante | Tipo | Por qué |
|---|---|---|---|
| T1 perfil en cierre | Users | Compensable (C1) | Un estado; volver a `Active` no pierde nada |
| T2 retención | Tasks | Compensable (C2) | Una fila; borrarla no pierde nada |
| T3 decisión | Auth | **Pivote** | Borrar la identidad no tiene vuelta. Es el único paso que puede fallar por negocio |
| T4 purga del monolito | Users | Reintentable | Borrar lo que ya no existe no falla: idempotente |
| T5 purga de tareas | Tasks | Reintentable | Ya lo es desde el paso 25.1 (inbox y lápida) |

- Es la contramedida "pessimistic view" de Azure: "Reorder the sequence of the saga so that data updates occur in retryable transactions to eliminate dirty reads" ([FUENTE: Azure, Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)). Ningún borrado ocurre antes del pivote, así que nadie lee datos "medio borrados" de una saga que después se rechaza.
- Orden descartado: Auth primero. Sin pasos compensables antes del pivote, el rechazo no tiene nada que compensar y el Verifica no se puede probar.

### 2.3 Falta de aislamiento y contramedidas

Una saga no tiene la "I" de ACID: "sagas may view the partial results of other sagas" ([FUENTE: Garcia-Molina y Salem, p. 251](https://www.cs.cornell.edu/andru/cs711/2002fa/reading/sagas.pdf)). Azure nombra tres anomalías: *lost updates*, *dirty reads* y *fuzzy reads* ([FUENTE: Azure, Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)). Las seis contramedidas de esa página, aplicadas acá:

| Contramedida (Azure) | ¿Se usa? | Dónde |
|---|---|---|
| Semantic lock: "Use application-level locks when a saga's compensable transaction uses a semaphore" | **Sí** | Perfil `Closing` (un segundo `DELETE` no abre otra saga: devuelve 202 con el mismo `closureId`) y retención en Tasks (`POST /tasks` → 409) |
| Commutative updates: "Design updates so that they can be applied in any order while still producing the same result" | **Sí** | C1 y C2 tocan cosas distintas, en cualquier orden. Las purgas T4 y T5 son borrados idempotentes |
| Pessimistic view: reordenar para que las escrituras queden en pasos reintentables | **Sí** | Sección 2.2: los borrados van después del pivote |
| Reread values: "Confirm that data remains unchanged before you make updates" | **Sí** | C1 cambia el perfil solo si sigue `Closing` con el mismo `closureId`; C2 borra la retención solo con ese `closureId`; T3 vuelve a contar SuperAdmins dentro de su transacción |
| Version files: "Maintain a log of all operations performed on a record" | No | Las tres filas de estado por `closureId` (sección 4) alcanzan; un log de operaciones es otro concepto |
| Risk-based concurrency based on value | No | Un solo flujo, un solo riesgo |

### 2.4 Orden de las compensaciones

- Garcia-Molina y microservices.io las ejecutan en orden inverso (C2 y después C1): "executes the compensating transactions in bottom to top order" ([FUENTE: Developing sagas part 4](https://microservices.io/post/sagas/2019/12/12/developing-sagas-part-4.html)). Azure acepta otro orden: "The compensating transaction steps don't always reverse the original operation in the exact opposite order" ([FUENTE: Azure, Compensating Transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)).
- **Recomendación: en paralelo**, las dos consumen el mismo `auth.account-closure-rejected`. Son conmutativas (2.3), así que el orden no cambia el resultado. Encadenarlas (C2 publica, C1 consume) sumaría un evento y un salto sin ganar consistencia [INFERENCIA].
- Durante una ventana corta, el perfil puede estar `Active` con Tasks todavía reteniendo (o al revés). Es la consistencia eventual que la saga acepta; el test espera a que las dos terminen.

### 2.5 La regla de negocio del pivote

- **Regla:** Auth rechaza el cierre si la cuenta es SuperAdmin y no hay otro SuperAdmin. Motivo `Auth.LastSuperAdmin` en `AuthErrors`.
- **Carrera:** dos SuperAdmins piden el cierre a la vez. Cada uno ve "hay otro" y los dos se borran (*write skew*, [INFERENCIA]). La cola `auth.account-closures` tiene un solo consumidor activo, así que en la práctica los procesa de a uno (`[CÓDIGO backend/Infrastructure/Messaging/MessagingExtension.cs:73-77]`). No alcanza como garantía: un cambio de rol por otro camino no pasa por la cola. **Recomendación:** T3 toma un advisory lock de transacción fijo ("plantel de SuperAdmins") antes de contar, como el paso 25.1 hace por propietario (`[CÓDIGO backend/Features/Tasks/OwnerTombstones.cs:22-23]`). "they are automatically released at the end of the transaction" ([FUENTE: PostgreSQL, Advisory Locks](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS)).
- **Sin chequeo previo en T1.** Un 409 inmediato en el `DELETE` sería mejor experiencia, pero duplica la regla y no sirve como garantía (la carrera de arriba). Queda como pendiente; la autoridad es T3.
- **Falla técnica en T3** (la base no responde, `IdentityResult` con error): no compensa. Se reintenta con las reglas del bus (`[CÓDIGO MessagingExtension.cs:44-50]`) y, si es un bug, termina en la DLQ con el cierre visible en `Closing` (sección 4.4).

---

## 3. Mensajería

### 3.1 Eventos

Cada evento nace de una entidad con `IHasDomainEvents`, pasa por el outbox de su contexto y lo publica el relay de su proceso, como los del 25 y el 25.1 (`[CÓDIGO docs/conventions.md:106-110]`).

| Evento de integración | Publica | Exchange (`direct`) | Payload | Consume |
|---|---|---|---|---|
| `users.account-closure-requested` v1 | Users (`UserProfile.RequestClosure`) | `users.user-events` (existe) | `closureId`, `userId`, `requestedAt` | Tasks |
| `tasks.owner-tasks-held` v1 | Tasks (`OwnerHold.Hold`) | `tasks.task-events` (existe) | `closureId`, `userId`, `heldAt` | Auth |
| `auth.account-deleted` v1 | Auth (`AccountClosure.Complete`) | `auth.account-events` (nuevo) | `closureId`, `userId`, `deletedAt` | Users |
| `auth.account-closure-rejected` v1 | Auth (`AccountClosure.Reject`) | `auth.account-events` | `closureId`, `userId`, `reason`, `rejectedAt` | Users y Tasks |
| `users.user-deleted` v1 (existe) | Users (`UserProfile.MarkDeleted`, ahora en T4) | `users.user-events` | `userId`, `deletedAt` | Tasks (paso 25.1, sin cambio) |

- **El `closureId` va en el payload**, no en un header. Es dato del negocio (el usuario lo recibe en el 202 y lo ve en `lastClosure`), y los headers actuales son del transporte (`[CÓDIGO backend/Infrastructure/Messaging/IntegrationEventPublisher.cs:26-29]`). Wolverine tiene su propio `SagaId` en un header ("Wolverine propagates the identity automatically through the `SagaId` envelope header", [FUENTE: Wolverine, Sagas](https://wolverinefx.net/guide/durability/sagas.html)), pero es parte de su tipo `Saga`, que no se usa.
- **`users.user-deleted` no cambia.** T4 lo emite igual que hoy `DeleteUser`; el consumidor de Tasks y su lápida siguen tal cual.
- Dos copias `internal` de cada evento de integración, una por slice que publica y otra por slice que consume, como pide `conventions.md:106`. Auth es feature nueva en el bus: `AuthEvents` (constantes) en Auth y sus copias en Users y Tasks.

### 3.2 Colas

| Cola (quorum, SAC, DLQ `<cola>.dlq`) | Proceso | Bindings | Consumidor del inbox |
|---|---|---|---|
| `tasks.account-closures` | Tasks | `users.account-closure-requested`, `auth.account-closure-rejected` | `tasks.account-closures` |
| `auth.account-closures` | Monolito | `tasks.owner-tasks-held` | `auth.account-closures` |
| `users.account-closures` | Monolito | `auth.account-deleted`, `auth.account-closure-rejected` | `users.account-closures` |

- `MessagingExtension` ya arma todo con `AddIntegrationEventHandler`: cola quorum, `x-single-active-consumer`, DLQ quorum y listener `Inline` (`[CÓDIGO MessagingExtension.cs:68-83]`). Varias suscripciones a la misma cola ya funcionan: Projects registra cuatro eventos en `projects.task-counters` (`[CÓDIGO backend/Features/Projects/ProjectsFeature.cs:24-27]`).
- **Una cola por participante, no por evento**: con SAC, la retención y su compensación en Tasks salen en el orden en que llegaron a la cola (`[CÓDIGO docs/architecture.md:121]`). `tasks.user-deletions` (paso 25.1) queda aparte: el paso no la toca.
- **Orden.** Los pasos se encadenan por causa: Auth no decide hasta recibir `tasks.owner-tasks-held`, que Tasks publica después de confirmar la retención. No hace falta orden entre colas. La única carrera posible es una entrega repetida, que el inbox descarta.
- **DLQ.** Un mensaje ilegible va a su DLQ al primer intento; un error transitorio se reintenta a 1, 5 y 30 s y después se reencola con el listener en pausa (`[CÓDIGO MessagingExtension.cs:44-50]`). Para RabbitMQ, dead-lettering es republicar un mensaje "rejected", "expired", por "length limit" o por "delivery-limit" ([FUENTE: RabbitMQ, Dead Letter Exchanges](https://www.rabbitmq.com/docs/dlx)). Un cierre con un mensaje en la DLQ queda trabado en `Closing`: es observable (sección 4.4) y su reenvío es el pendiente manual del paso 25.

### 3.3 Consumidores idempotentes

- Cada consumidor abre una transacción de su contexto, pasa el inbox y aplica su efecto, todo junto (`[CÓDIGO backend/Infrastructure/Messaging/Inbox.cs:8-28]`). Es el patrón de `ApplyTaskEvent` y `DeleteOwnedTasks` (`[CÓDIGO DeleteOwnedTasks.cs:20-33]`).
- **Inbox nuevo en Users y en Auth**: `MapInbox()` en `UsersDbContext` y `AuthDbContext`, cada uno con su migración que solo crea la tabla. **Outbox nuevo en Auth**: `AddOutbox<AuthDbContext>()` y `MapOutbox()`. El relay de Auth corre en el monolito, al lado del de Users.
- **T4 sin `CrossContextTransaction`.** La purga del monolito corre en este orden: `IProjectsModule.DeleteOwnedProjectsAsync` (su propio `SaveChanges`), `IdempotencyKeys.ForgetOwnerAsync` (su propio `ExecuteDelete`), y al final una transacción de `UsersDbContext` con el inbox, `MarkDeleted` y el `Remove` del perfil. Si el proceso muere en el medio, RabbitMQ reentrega (el ack es después del handler, `[CÓDIGO docs/architecture.md:119]`), el inbox todavía no tiene la fila y los dos primeros borrados se repiten sin efecto. Es la definición de reintentable: "Retryable transactions are idempotent" ([FUENTE: Azure, Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)). Ya no hay usuario que cree datos en esa ventana: la identidad y las sesiones murieron en T3.
- Esto también evita un choque: `Inbox.TryAcceptAsync` exige una transacción de EF (`Database.CurrentTransaction`, `[CÓDIGO Inbox.cs:12-13]`) y un `TransactionScope` no la crea [INFERENCIA, de la condición]. Con T4 en transacciones locales, `Inbox` no cambia.
- **Auth decide dentro de una transacción de `AuthDbContext`:** inbox, advisory lock, conteo de SuperAdmins, una fila `AccountClosure` y, si corresponde, `UserManager.DeleteAsync`. El `UserManager` usa el mismo `AuthDbContext` del scope, así que su `SaveChanges` guarda la identidad borrada, la fila de decisión y su outbox en la misma transacción [INFERENCIA; [NO PROBADO] que `UserStore` no abra otra transacción adentro].

### 3.4 Lo que no se usa: `Wolverine.Saga`

- Wolverine tiene sagas con estado persistido: "A saga state document type that is persisted between saga messages that must inherit from the `Wolverine.Saga` type" ([FUENTE: Wolverine, Sagas](https://wolverinefx.net/guide/durability/sagas.html)). Un tipo que guarda el estado de todo el proceso es un coordinador: es la saga orquestada del paso 27, y su herramienta es una decisión pendiente de Tobi (`[CÓDIGO docs/evolucion/plan.md:451]`).
- Además, `using Wolverine` fuera de `Infrastructure/Messaging` es error (DUNE0025, `[CÓDIGO docs/conventions.md:102]`). Este paso usa solo `IntegrationEventPublisher` e `IIntegrationEventHandler<T>`.

---

## 4. Observabilidad: "consistente y observable"

Azure: "Monitoring and tracking the workflow of a saga are essential tasks" y la coreografía tiene la contra de que "It's difficult to track which commands each saga participant responds to" ([FUENTE: Azure, Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)). Microservices.io: en coreografía "the implementation of the saga is scattered around the participating services" ([FUENTE: Developing sagas part 3](https://microservices.io/post/sagas/2019/08/15/developing-sagas-part-3.html)). Sin orquestador, el estado se reconstruye por el `closureId`.

### 4.1 Estado por participante (lectura sobre el agregado)

| Dónde | Qué guarda | Quién lo ve |
|---|---|---|
| `users."UserProfiles"`: columnas nuevas `ClosureStatus` (`None`, `Closing`, `Rejected`), `ClosureId`, `ClosureReason`, `ClosureChangedAt` | El estado que ve el usuario | `GET /users/me` (`accountStatus`, `lastClosure`) |
| `tasks."OwnerHolds"` (`OwnerId` clave, `ClosureId`, `HeldAt`) | La retención viva | El operador; `POST /tasks` → 409 |
| `auth."AccountClosures"` (`ClosureId` clave, `UserId`, `Outcome`, `Reason`, `DecidedAt`) | La decisión del pivote | El operador |

- Es lo que Azure pide al compensar: "As the original operation runs, the system records information about each step and how to undo it" ([FUENTE: Azure, Compensating Transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)).
- Las columnas del perfil van con el perfil porque `UserProfile` es `IAuditable` (`[CÓDIGO backend/Features/Users/Domain/UserProfile.cs:6]`): cada cambio de estado deja su fila de auditoría sin código nuevo.
- Microservices.io describe el mismo cliente: "The service sends back a response (e.g. containing the orderID) after initiating the saga and the client periodically polls" ([FUENTE: microservices.io, Saga](https://microservices.io/patterns/data/saga.html)).

### 4.2 Logs y trazas

- **Un log por paso** con `{ClosureId}` y `{UserId}` (id opaco, no dato personal según `CONTEXT.md`), con `LoggerMessage` como `ApplyTaskEvent` (`[CÓDIGO backend/Features/Projects/ApplyTaskEvent/ApplyTaskEvent.cs:48-49]`). Cinco en el camino del rechazo: pedido (T1), retenido (T2), rechazado (T3), retención suelta (C2), perfil restituido (C1).
- **Tag en la actividad:** cada paso hace `Activity.Current?.SetTag("templateapp.account_closure.id", closureId)` (es `System.Diagnostics`, no Wolverine).
- **Cadena de trazas.** El outbox guarda el `traceparent` de la actividad que guardó el evento (`[CÓDIGO backend/Infrastructure/Messaging/OutboxInterceptor.cs:75]`), y el relay abre su span con un **link** a esa traza (`[CÓDIGO backend/Infrastructure/Messaging/OutboxRelay.cs:116-118,218-219]`). El `receive` de Wolverine es hijo del span del relay (medido en el paso 25, `[CÓDIGO docs/architecture.md:123]`). Como cada consumidor guarda su evento dentro de ese `receive`, el salto siguiente queda enlazado al anterior. Es lo que pide OpenTelemetry: "These conventions use spans links as the default mechanism to correlate producers and consumer(s)" ([FUENTE: OpenTelemetry, Messaging spans](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/)). [NO PROBADO] la cadena completa de cinco saltos; el paso 25 midió uno.

### 4.3 Métrica

- Meter `TemplateApp.Users` (entra solo por `AddMeter("TemplateApp.*")`, `[CÓDIGO backend/Infrastructure/Observability/ObservabilityExtensions.cs:58]`), con:
  - `templateapp.account_closures.finished` (counter, tag `outcome` = `completed` | `rejected`).
  - `templateapp.account_closures.duration` (histograma en segundos, de `requestedAt` al final).
- Se registra en Users, que abre y cierra la saga (T1 y C1 o T4), **después** de confirmar la transacción. Medir antes repetiría la "métrica fantasma" del paso 24 (`progreso.md`, pendientes).

### 4.4 Un cierre trabado

- Si un mensaje termina en una DLQ, el perfil queda `Closing` para siempre. Se ve en `GET /users/me`, en la fila sin decisión de `auth."AccountClosures"` y en `wolverine-dead-letter-queue` (`[CÓDIGO docs/architecture.md:123]`).
- Azure: "Sometimes manual intervention is the only way to recover from a failed step" y "the system should raise an alert that includes detailed information about the reason for the failure" ([FUENTE: Azure, Compensating Transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)). La alerta por "cierre más viejo en `Closing`" va a pendientes, junto a las alertas de DLQ del paso 25.

---

## 5. Contrato HTTP y frontend (regla 3)

### 5.1 Qué cambia

| Ruta | Antes | Después |
|---|---|---|
| `DELETE /api/v1/users/me` y `/users/{authId}` | 204 | **202** con `{ "closureId": "…", "status": "closing" }` y `Location: /api/v1/users/me`. Un segundo `DELETE` durante el cierre devuelve 202 con el mismo `closureId` |
| `GET /api/v1/users/me` | perfil | perfil + `accountStatus` (`"active"` \| `"closing"`) + `lastClosure` (`{ closureId, outcome: "rejected", reason, decidedAt }` o `null`) |
| `POST /api/v1/tasks` | 201, 401 `Task.OwnerDeleted` | además **409 `Task.OwnerClosing`** durante el cierre |

- **202.** "The 202 (Accepted) status code indicates that the request has been accepted for processing, but the processing has not been completed", y "The representation sent with this response ought to describe the request's current status and point to (or embed) a status monitor" ([FUENTE: RFC 9110, 202 Accepted](https://www.rfc-editor.org/rfc/rfc9110#name-202-accepted)). Para `DELETE`: "a 202 (Accepted) status code if the action will likely succeed but has not yet been enacted" ([FUENTE: RFC 9110, DELETE](https://www.rfc-editor.org/rfc/rfc9110#name-delete)). El monitor es `GET /users/me`.
- **Idempotencia del `DELETE`.** "PUT, DELETE, and safe request methods are idempotent" ([FUENTE: RFC 9110, Idempotent Methods](https://www.rfc-editor.org/rfc/rfc9110#name-idempotent-methods)): por eso el segundo `DELETE` devuelve el mismo cierre y no abre otro. Es también el bloqueo semántico de Users.
- **409 y no 401** para la retención: la credencial sigue valiendo y el cierre puede rechazarse. 401 queda para la lápida (`[CÓDIGO backend/Features/Tasks/TaskErrors.cs:23-25]`).
- **`Location` a `/users/me`** aunque el `DELETE` sea por `/{authId}`: el SuperAdmin que borra a otro mira el estado con `GET /users/{authId}` (la ruta existe, `[CÓDIGO backend/Features/Users/GetUser/GetUserEndpoint.cs:13]`).

### 5.2 Frontend

- El frontend **no** llama a `DELETE /users/me` (`[CÓDIGO frontend/src/app/features/users/api/users-api.ts:11-17]`): no hay botón que cambiar.
- Sí lee `GET /users/me`: `UserResponse` (`[CÓDIGO frontend/src/app/features/users/api/user-response.ts]`) suma `accountStatus` y `lastClosure`. La página de perfil muestra una línea cuando la cuenta está en cierre o el último cierre se rechazó, con su test de Vitest. Es lo mínimo para que el contrato vaya completo.
- El 409 `Task.OwnerClosing` sale con el texto genérico de 409 del frontend. No hace falta cambio.
- E2E: el paso cambia un contrato HTTP; corre la suite de Playwright al cerrar (`AGENTS.md`, "Verificación").

### 5.3 Scripts que llaman a `DELETE /users/me`

- `smoke-api.mjs` espera 204 en tres lugares (`[CÓDIGO .agents/skills/verify-template/scripts/smoke-api.mjs:357,405,407]`) y `rehearse-migrations.mjs` en uno (`[CÓDIGO .agents/skills/verify-template/scripts/rehearse-migrations.mjs:306]`). Pasan a 202 y esperan a que la sesión dé 401 (el cierre se completó). El caso "Tasks caído" del smoke (`:357`) cambia de sentido: con Tasks parado, el cierre queda en `Closing` hasta que vuelve.

---

## 6. Tests

### 6.1 Base

- Todos los tests de la saga corren sobre `IsolatedApi` (base propia y prefijo de colas propio, `[CÓDIGO backend/Tests/Support/IsolatedApi.cs:33-70]`), con el relay automático apagado (`[CÓDIGO backend/Tests/Support/ApiPair.cs:83]`).
- **Helpers nuevos:** `AuthRelay` (como `UsersRelay`, `[CÓDIGO IsolatedApi.cs:31]`) y `DrainClosureAsync`, que corre los tres relays en ronda hasta que no queda nada pendiente y cada mensaje está en el inbox de **cada** consumidor que lo espera. `UnconsumedUserEventsAsync` hoy supone un solo consumidor por evento (`[CÓDIGO IsolatedApi.cs:107-113]`); `auth.account-closure-rejected` tiene dos.
- **SuperAdmin en `IsolatedApi`:** `TestAccounts.SetRoleAsync` recibe un `ApiFixture` (`[CÓDIGO backend/Tests/Support/TestAccounts.cs:86-96]`); hace falta una sobrecarga para `ApiPair`.
- **Paso a paso:** el test corre un relay por vez (`PublishUsersOutboxAsync`, etc.) para mirar el estado a mitad de la saga.

### 6.2 Los tests

| Test | Qué hace | Qué afirma | Rojo primero |
|---|---|---|---|
| **V1 Verifica: el pivote rechaza y todo compensa** | Alice es la única SuperAdmin, con 2 tareas y 1 proyecto. `DELETE /users/me`. Relay de Users; relay de Tasks; relay de Auth; espera los dos inbox de la compensación | A mitad (después de T2): `POST /tasks` 409 `Task.OwnerClosing` y `accountStatus: closing`. Al final: `GET /users/me` 200 con `accountStatus: active`, `lastClosure.outcome: rejected`, `reason: Auth.LastSuperAdmin` y el mismo `closureId` del 202; la sesión sigue; 2 tareas y 1 proyecto; 0 filas en `tasks."OwnerHolds"`; `auth."AccountClosures"` con `Rejected`; `POST /tasks` 201; `finished{outcome=rejected}` = 1; los cinco logs con el mismo `closureId` (`CapturedLogs`) | Sin registrar los dos consumidores de `auth.account-closure-rejected`: la retención y `Closing` quedan, el `POST` final da 409 |
| **V2 El cierre se completa** | Bob, usuario común, con tareas, proyecto y una clave de idempotencia. `DELETE /users/me` y drenaje completo | 202; después la sesión da 401; 0 filas de Bob en todas las features (`OwnedRows`), lápida, sin retención, `Completed`, `finished{outcome=completed}` = 1, un `users.user-deleted` | Con el `DeleteUser` de hoy, el `DELETE` responde 204 y no hay `closureId` |
| **V3 La compensación dos veces** | Se publica el mismo `auth.account-closure-rejected` dos veces (mismo `MessageId`) | Un inbox por consumidor; el perfil y la retención cambian una vez; la métrica cuenta 1 | Sin el inbox en C1, la métrica cuenta 2 |
| **V4 Una compensación vieja no suelta una retención nueva** | Retención con `closureId` B; llega un rechazo con `closureId` A | La retención B sigue | C2 que borra por `OwnerId` y no por `closureId` la suelta |
| **V5 Dos SuperAdmins a la vez** | Dos SuperAdmins, las dos piden el cierre; el test llama a la slice de T3 en paralelo con el candado tomado a mano desde el test (como `HeldOwnerLock`, `[CÓDIGO backend/Features/Tasks/DeleteOwnedTasks/DeleteOwnedTasksTests.cs:58-69]`) | Uno `Completed` y uno `Rejected` | Sin el advisory lock en T3, los dos se borran |
| **V6 La purga se repite** | T4 falla después de borrar los proyectos (una falla de prueba en la transacción de Users) y el bus reentrega | Al final, todo en 0 y un solo `users.user-deleted` | Con el inbox al principio de T4 y no al final, la reentrega no borra el perfil |
| **V7 Una falla técnica en T3 no compensa** | La transacción de Auth tira `NpgsqlException` una vez | Se reintenta y el cierre termina `Completed`; nunca hubo `auth.account-closure-rejected` | Si T3 traduce cualquier excepción a "rechazado", sale el rechazo |

- **Por qué sin inyección de fallas en V1:** la falla de negocio es determinista y es la del dominio. La inyección (V6, V7) prueba lo otro: que una falla técnica se reintenta y no compensa.
- **Tests que cambian:** `DeleteUserTests` (204 → 202; `WhenAuthFailsAfterTheProfileIsDeleted_...` pierde sentido porque `DeleteUser` ya no llama a Auth, y lo reemplaza un test de atomicidad de T1: perfil `Closing` y fila del outbox juntos), `DeleteUserBrokerTests` (drenaje completo de la saga), `AuditTrailTests`, que borra la cuenta y mira la auditoría (`[CÓDIGO backend/Tests/Auditing/AuditTrailTests.cs:138]`). `FailingAccountDeletion` (`[CÓDIGO backend/Tests/Support/FailingAccountDeletion.cs]`) se borra en el commit C.
- [NO PROBADO] Ningún test corrió. Todo esto es diseño.

---

## 7. Dune (reglas 4 y 9)

- **Ninguna regla nueva.** La regla de diseño nueva es "ninguna tarea nueva durante el cierre". La hace cumplir el compilador como en el 25.1: `OwnerTombstones.FindLiveAsync` (`[CÓDIGO OwnerTombstones.cs:34-39]`) pasa a mirar la lápida **y** la retención, con el mismo candado compartido, y solo así emite un `LiveOwner`; `TaskItem.Create` ya lo pide (`[CÓDIGO backend/Features/Tasks/Domain/TaskItem.cs:27]`). El resultado deja de ser `LiveOwner?` y pasa a distinguir "borrado" (401) de "en cierre" (409). Su violación deliberada natural: llamar a `TaskItem.Create` con un `string` no compila.
- **Candidatas descartadas:** "todo `IIntegrationEventHandler<T>` llama a `TryAcceptAsync`" (el analyzer tendría que seguir la llamada a la slice, en otro archivo; los tests V3 lo prueban mejor) y "`CrossContextTransaction.Begin` solo en una lista de archivos" (una lista frágil; DUNE0021 ya limita `new TransactionScope`, `[CÓDIGO docs/conventions.md:96]`).
- **Reglas que siguen valiendo sin cambio:**
  - DUNE0017: los cambios de estado del perfil van por `SaveChanges`, nunca `ExecuteUpdate`.
  - DUNE0021: `AuthDbContext` y `UsersDbContext` suman `MapOutbox()`/`MapInbox()`, helpers de `Infrastructure` que la regla acepta.
  - DUNE0022: `DeleteUser` deja de abrir `CrossContextTransaction`; ningún paso de la saga llama por HTTP.
  - DUNE0023: los publicadores de Users, Tasks y Auth son `IEventHandler<T>` que solo llama `OutboxRelay`.
  - DUNE0025: ninguna feature nombra Wolverine.
- **Gates:** ningún script lista colas ni exchanges. `check-structure.mjs` no cambia.

---

## 8. Commits ordenados como releases

| Commit | Release | Qué entra | Por qué en este orden |
|---|---|---|---|
| **A** | Tasks | Migración `tasks."OwnerHolds"`, `OwnerHold` (entidad con eventos), `HoldOwnerTasks` (T2) y `ReleaseOwnerTasks` (C2), la cola `tasks.account-closures`, `FindLiveAsync` con retención y el 409, `DeleteOwnedTasks` borra la retención en su transacción, las copias de los eventos de Users y Auth, la publicación de `tasks.owner-tasks-held` | Tasks declara su cola antes de que alguien publique `users.account-closure-requested`. Si el monolito publicara antes, el exchange no lo rutea a nada y RabbitMQ lo confirma igual ("For unroutable messages, the broker will issue a confirm once the exchange verifies a message won't route to any queue", [FUENTE: RabbitMQ, Confirms](https://www.rabbitmq.com/docs/confirms#when-publishes-are-confirmed)). Mientras nadie pida un cierre, A no cambia nada |
| **B** | Monolito | Auth: outbox, inbox, `AccountClosure`, `DecideAccountClosure` (T3), cola `auth.account-closures`, exchange `auth.account-events`. Users: inbox, columnas de cierre, `DeleteUser` como T1 (202), `CompleteAccountClosure` (T4) y `CancelAccountClosure` (C1), cola `users.account-closures`, métrica. `GET /users/me` con el estado. Frontend. Smoke y ensayo. Docs | Las colas de Auth y Users se declaran al arrancar B, antes de que B acepte un `DELETE`; `tasks.owner-tasks-held` solo sale cuando B ya pidió un cierre. Durante el despliegue, un monolito viejo sigue borrando en una transacción, y uno nuevo abre la saga: los dos dejan la cuenta borrada o intacta |
| **C** | Monolito | Se borra `IAuthModule.DeleteAccountAsync` (`[CÓDIGO backend/Features/Auth/Contracts/IAuthModule.cs:14]`) y su implementación, `FailingAccountDeletion` y el test de atomicidad viejo | Ningún monolito desplegado lo llama. Es el contract del expand/contract |

- Las migraciones de A y B solo agregan (tablas nuevas y columnas nullable o con default): expand, sin contract (`[CÓDIGO docs/conventions.md:123]`, sección "Migraciones: expand/contract").
- B es grande porque Users y Auth viven en el mismo proceso y salen juntos. Partir B en dos (Auth primero, Users después) no cambia el orden de despliegue: es un solo binario.

---

## 9. Docs a actualizar

| Doc | Qué cambia |
|---|---|
| `architecture.md:54` | `DeleteUser` ya no borra en una transacción: abre el cierre; el párrafo describe los tres pasos, el pivote y la purga |
| `architecture.md:74` | "borrar la cuenta responde 204" pasa a 202, y con Tasks caído el cierre espera en `Closing` |
| `architecture.md:88` | Sale `DeleteUser` de la lista de `CrossContextTransaction` y la frase "pasarlos a eventos es una saga (paso 26)" |
| `architecture.md`, sección nueva "Cierre de cuenta (paso 26)" | Pasos, eventos, colas, estados, compensación y cómo seguir un `closureId` |
| `architecture.md:90-101` (outbox) | Tres outbox (Tasks, Users, Auth) y los consumidores nuevos |
| `conventions.md:85` | Las claves se borran en la purga del cierre, no en `DeleteUser` |
| `conventions.md:93` | "`DeleteUser` llama a `IProjectsModule.DeleteOwnedProjectsAsync`..." pasa a la purga del cierre |
| `conventions.md:97` | Sale `DeleteUser` de la lista de `TransactionScope` |
| `conventions.md` (eventos) | Una línea: un paso de saga lleva el id de la saga en el payload; una compensación cambia solo lo suyo y verifica el id (reread value) |
| `CONTEXT.md` | **Cierre de cuenta**, **Cuenta en cierre** y **Retención de tareas** (evitar "soft delete", "baja", "lock"); la regla del último SuperAdmin en **Rol** |
| ADR-0004, punto 4 | "Users usa `Auth.Contracts` (lee y borra la cuenta)" pasa a "lee la cuenta": el borrado llega por evento. Entrada en "Cambios" |
| `docs/decisions.tsv` | Las decisiones de este archivo |

---

## Choques con los docs

1. **ADR-0004 punto 4** dice que Users borra la cuenta por contrato con Auth. Con la saga, Auth la borra al consumir un evento. El ADR cambia con una entrada en "Cambios", como cambió el punto 9 en el paso 25 en modo autónomo.
2. **`architecture.md:74`, `:88` y `conventions.md:85`, `:93`, `:97`** describen el borrado en una transacción y el 204. El paso los vuelve falsos a propósito; se corrigen en el commit B.
3. **`decisions.tsv:392`** (paso 25.1) dejó `CrossContextTransaction` en `DeleteUser`. Este paso lo saca; es lo que esa misma fila anunciaba.
4. **Regla de negocio nueva.** Hoy el último SuperAdmin puede borrar su cuenta; después, el cierre se rechaza. Es un cambio de comportamiento que ningún doc pidió. Lo propongo porque es la falla de negocio del pivote y porque deja al sistema sin administrador; queda como decisión.
5. **`informe-dotnet-empresarial.md:417`** (tabla de la sección 10) dice que el riesgo de la coreografía es "Imposible reconstruir el estado del proceso". La sección 4 lo mitiga con una fila de estado por participante y el `closureId`; no lo elimina (el estado sigue repartido en tres tablas).
6. **Azure advierte "a risk of cyclic dependency between saga participants"** ([FUENTE: Azure, Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga)). Acá hay un ciclo de eventos Users → Tasks → Auth → Users. No es un ciclo de código (cada feature tiene su copia del evento, `conventions.md:106`), pero cada participante conoce el evento del anterior. Es el costo que el paso 27 (orquestación) saca; lo anoto para la comparación.
7. **Métricas del outbox.** El Meter `TemplateApp.Outbox` no separa por contexto (`[CÓDIGO backend/Infrastructure/Messaging/OutboxMetrics.cs:46-55]`). Con dos outbox en el monolito (Users y Auth), `pending` y `oldest_pending.age` mezclan los dos. Ya estaba anotado en la investigación del 25.1 (sección 1.2); sube de prioridad.
8. **Smoke `--compose`**: el caso "Tasks caído" del paso 25.1 esperaba 204 y las tareas borradas al volver. Con la saga, el `DELETE` responde 202 y el cierre entero espera a Tasks (T2 es Tasks). Cambia el sentido del caso, no solo el número.

## Decisiones (con mi recomendación)

1. La saga es el cierre de cuenta (F1). Descartadas la tarea en proyecto (F2) y el archivado con veto (F3).
2. Tres pasos: T1 Users (perfil en cierre), T2 Tasks (retención), T3 Auth (pivote). Después, T4 Users y T5 Tasks reintentables.
3. La falla del último paso es de negocio: "no se borra el último SuperAdmin", motivo `Auth.LastSuperAdmin`.
4. T3 toma un advisory lock de transacción fijo antes de contar SuperAdmins.
5. Sin chequeo previo de la regla en T1; queda como pendiente.
6. Compensaciones semánticas: C1 cambia solo el estado del cierre y anota el rechazo; C2 borra solo la retención de ese `closureId`.
7. C1 y C2 corren en paralelo sobre el mismo `auth.account-closure-rejected`.
8. Contramedidas: bloqueo semántico, actualizaciones conmutativas, vista pesimista y releer el valor.
9. Una falla técnica se reintenta con las reglas del bus y nunca compensa.
10. `closureId` = GUID v7 creado en T1, en el payload de cada evento, en el 202 y en `lastClosure`.
11. Cuatro eventos nuevos (`users.account-closure-requested`, `tasks.owner-tasks-held`, `auth.account-deleted`, `auth.account-closure-rejected`), versión 1, exchange nuevo `auth.account-events`; `users.user-deleted` sin cambio.
12. Tres colas quorum con SAC y DLQ: `tasks.account-closures`, `auth.account-closures`, `users.account-closures`; el nombre de la cola es el consumidor del inbox.
13. Outbox e inbox nuevos en Auth; inbox nuevo en Users.
14. T4 sin `CrossContextTransaction`: proyectos y claves en sus propias escrituras, y el inbox con el perfil al final, en una transacción de Users. `Inbox` no cambia.
15. Auth guarda su decisión en `auth."AccountClosures"`; la fila lleva los eventos al outbox en la misma transacción que el borrado de la identidad.
16. `DELETE /users/me` y `/users/{authId}` responden 202 con `closureId` y `Location: /api/v1/users/me`; un segundo `DELETE` durante el cierre devuelve el mismo cierre.
17. `GET /users/me` suma `accountStatus` y `lastClosure`; el perfil guarda las columnas de cierre.
18. `POST /tasks` durante el cierre responde 409 `Task.OwnerClosing`; `FindLiveAsync` mira lápida y retención con el mismo candado.
19. Observabilidad: log por paso con `closureId`, tag `templateapp.account_closure.id`, Meter `TemplateApp.Users` con `finished` por `outcome` y `duration`, registrados después de confirmar.
20. No se usa `Wolverine.Saga` (paso 27, regla 8).
21. Ninguna regla nueva de Dune; la garantía es `LiveOwner`.
22. Tres commits: A Tasks, B monolito (con frontend, smoke y docs), C se borra `IAuthModule.DeleteAccountAsync`. Tests V1 a V7 con su rojo; e2e al cerrar.

## Herramientas (regla 5)

**No hace falta ninguna herramienta, librería ni tecnología nueva.** Todo es lo que ya está: EF Core, Npgsql y PostgreSQL (outbox, inbox, advisory locks, migraciones), Wolverine 6.64.2 y RabbitMQ 4.4.1 (elegidos por Tobi en el paso 25) solo como transporte, `System.Diagnostics` para trazas y métricas, y `Testcontainers.RabbitMq` para los tests. El hilo no tiene que frenar por la excepción de herramientas del modo autónomo.

## Pendientes para `progreso.md`

**Se cierran o cambian con el paso:**
- `progreso.md:202` (`TransactionScope` síncrono): `DeleteUser` deja de usarlo; siguen `RegisterUser`, `IdempotencyFilter` y `CreateTask`.

**Se abren:**
- Alerta por el cierre más viejo en `Closing` (un mensaje en una DLQ lo traba para siempre), con su runbook. Va con las alertas de DLQ del paso 25.
- Chequeo previo de "último SuperAdmin" en `DELETE` para responder 409 enseguida; la autoridad sigue en T3.
- Projects como participante propio de la purga (consume `auth.account-deleted` con su inbox) para sacar `IProjectsModule.DeleteOwnedProjectsAsync` de Users.
- Durante el cierre, crear un proyecto sigue andando; la purga lo borra si el cierre se completa. Un bloqueo semántico en Projects pide que lea el estado del perfil.
- El Meter `TemplateApp.Outbox` mezcla los outbox de Users y Auth en el monolito (choque 7).
- Un cierre de un SuperAdmin por otro (`DELETE /users/{authId}`) muestra el estado en `GET /users/{authId}`; no hay notificación al dueño de la cuenta.

## Verificado / No verificado

**Verificado (leído en el repo o en una fuente primaria):**
- Todo `[CÓDIGO]` en `19f91aa`: `DeleteUser` y su endpoint, `UsersFeature`, `UserProfile`, `UsersDbContext`, `AuthModule`, `IAuthModule`, `AuthDbContext`, `AppRoles`, `RolePermissions`, `ProjectsModule`, `IProjectsModule`, `Project`, `UpdateProject`, `FindAssignableProject`, `CreateTask`, `TaskItem`, `LiveOwner`, `OwnerTombstones`, `DeleteOwnedTasks` y sus tests, `TasksFeature`, `TaskErrors`, `ProjectsFeature`, `ApplyTaskEvent`, `TaskEventsConsumer`, `MessagingExtension`, `MessagingRegistration`, `IntegrationEventPublisher`, `IntegrationEventHandler`, `Inbox`, `OutboxInterceptor`, `OutboxRelay`, `OutboxMetrics`, `OutboxRegistration`, `IdempotencyKeys`, `CrossContextTransaction`, `ObservabilityExtensions`, `IsolatedApi`, `ApiPair`, `TestAccounts`, `FailingAccountDeletion`, `TasksListener`, los scripts del smoke y del ensayo, el modelo y la API de usuarios del frontend, y los docs citados.
- Garcia-Molina y Salem 1987, pp. 249-253 (leído sobre el PDF escaneado de Cornell): definición de saga, compensación semántica, la garantía T1…Tn o T1…Tj, Cj…C1, que el último paso no necesita compensación, el id de saga, la falta de aislamiento y la recuperación hacia atrás y hacia adelante.
- microservices.io (página del patrón y artículos 1, 3 y 4 de "Developing sagas"): saga, coreografía, compensación, bloqueo semántico, estado `PENDING`, orden inverso de compensación, el cliente que consulta el estado.
- Azure Architecture Center (Saga y Compensating Transaction): tipos de paso, anomalías, las seis contramedidas, idempotencia, compensación que no restaura, orden no estrictamente inverso, registro de pasos, alerta e intervención manual, ventajas y contras de la coreografía.
- Wolverine: el tipo `Saga` con estado persistido y el header `SagaId`; la fuente de trazas `Wolverine`. RabbitMQ: los disparadores de dead-lettering, el confirm de un mensaje sin ruta. OpenTelemetry: links como mecanismo por defecto entre productor y consumidor. PostgreSQL: advisory locks de transacción.

**No verificado (queda para el paso):**
- Las citas de RFC 9110 (202, `DELETE`, métodos idempotentes) vienen de un resumen de la página, no del texto leído palabra por palabra; coinciden con el RFC que conozco, pero no las contrasté línea por línea.
- Las páginas de microservices.io no definen compensable/pivote/reintentable ni las seis contramedidas: esas definiciones salen de Azure. El capítulo 4 de *Microservices Patterns* (Richardson), donde están, no lo leí.
- Que `UserManager.DeleteAsync` guarde dentro de la transacción explícita de `AuthDbContext` sin abrir otra.
- Que la cadena de trazas quede enlazada en los cinco saltos (el paso 25 midió uno).
- Que `GET /users/{authId}` devuelva a un SuperAdmin el mismo modelo que `/users/me`, con el estado del cierre.
- Ningún test corrió: todo lo de la sección 6 es diseño.
