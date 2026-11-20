# Paso 28: durable execution para un proceso largo con auditoría (investigación)

- Fecha: 2026-11-20.
- Estado: **Bloqueado** por la decisión de herramienta de Tobi (regla 5 y excepción del modo autónomo): qué plataforma corre el proceso largo. Las demás decisiones van con mi recomendación (sección "Decisiones").
- Alcance: el paso 28 del [plan](../plan.md) ("28. Durable execution para un proceso largo con auditoría"). Concepto: cuándo conviene una plataforma de workflow en lugar de una saga casera. Cambia: worker nuevo, `compose.yaml`, tests de reanudación. Verifica: proceso de varios días simulado con timers acelerados; historial consultable y auditable (`[CÓDIGO docs/evolucion/plan.md:456-459]`).
- Base: `main` en `a805053` (merge del PR #91, paso 27), rama `fase8/28-durable-execution`.
- Límites con los pasos vecinos: la resiliencia de clientes HTTP (retry, circuit breaker) es del paso 29; el scheduler de jobs (Quartz.NET o Hangfire) es una decisión del paso 31 (`[CÓDIGO docs/evolucion/plan.md:475]`). Este paso no adelanta ninguno.

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `a805053`.
- `[FUENTE]`: lo leí en un documento primario (docs oficiales, repos de los mantenedores, NuGet, archivos de licencia). Cada uno tiene su link.
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Respuesta corta

1. **El concepto.** *Durable execution* (ejecución durable) = escribís el proceso largo como una función común (esperar 7 días, mandar un aviso, esperar otros 7) y una plataforma garantiza que esa función sigue desde donde estaba aunque el proceso se caiga. La plataforma guarda un historial de cada paso y, al volver, re-ejecuta el código contra ese historial (*replay*). Por eso el código del workflow tiene que ser determinista. La saga del paso 27 resuelve lo mismo con otra forma: una fila con el estado y un handler por mensaje.
2. **Cuándo paga una plataforma.** Paga cuando hay muchas esperas largas por instancia, ramas y bucles que en una tabla de transiciones se vuelven difíciles de leer, muchos procesos distintos que amortizan la infraestructura, y un equipo que acepta las reglas de determinismo. No paga con un solo proceso corto que ya entra en una transacción de tu base (sección 1.3).
3. **Qué proceso (pregunta 2).** Recomiendo **el cierre de cuenta con período de gracia**: `DELETE /users/me` ya no cierra al instante; agenda el cierre a 14 días, deja dos recordatorios (día 7 y día 13), el usuario lo puede cancelar mientras tanto, y al vencer arranca la saga del paso 27 sin cambios. Es un proceso de días, cancelable, con timers y con una historia que se puede auditar, sobre un flujo que ya existe.
4. **Qué ve el usuario.** En su perfil: "Tu cuenta se cierra el 4 de diciembre. [Cancelar cierre]". El `DELETE` responde 202 con `status: "scheduled"` y `closesAt`. Entran `POST /users/closures/{closureId}/cancel` y `GET /users/closures/{closureId}/history`. Cambia el contrato HTTP, así que van backend, frontend y tests juntos (regla 3).
5. **Historial auditable.** Una tabla propia, append-only (solo se agregan filas), `users."AccountClosureHistory"`, con una fila por hecho del proceso (pedido, recordatorio, cancelación, vencimiento, inicio del cierre, resultado). La recomiendo con cualquier herramienta: el historial de una plataforma es técnico, guarda payloads y se borra al vencer su retención (sección 3.2).
6. **Herramientas (decide Tobi).** Tres opciones: **A** seguir con Wolverine (una segunda saga con mensajes programados durables, sin herramienta nueva); **B** Temporal (SDK `Temporalio` 1.22.0, MIT; servidor 1.32.2 y Temporal UI en Compose); **C** Elsa Workflows 3.10.2 (MIT). Descarto Durable Task SDK de Microsoft (su backend de producción es un servicio pago de Azure y el emulador guarda todo en memoria), Dapr Workflow (obliga a adoptar todo el runtime de Dapr con sidecar), Restate y DBOS (sin SDK oficial de .NET).
7. **Mi recomendación: B (Temporal).** Es la única opción que enseña el concepto del paso (replay, determinismo, timers durables, workers) y la que el informe del repo nombra para "procesos largos con requisitos de auditoría" (`[CÓDIGO docs/evolucion/informe-dotnet-empresarial.md:398]`). A no enseña nada nuevo después del paso 27. C es un motor de workflows con diseñador visual, no durable execution, y sus timers durables piden además Quartz. La elección es de Tobi.
8. **Worker nuevo.** Con B, un proceso nuevo `backend/Hosts/Workflows` corre el worker de Temporal. Sus *activities* (los pasos con efectos) le hablan al monolito por HTTP interno, como ya hace Tasks. El monolito arranca el workflow desde su outbox, así no hay escritura doble entre PostgreSQL y Temporal.
9. **Timers acelerados.** Con B, el entorno de tests de Temporal salta el tiempo: 14 días pasan en milisegundos (`WorkflowEnvironment.StartTimeSkippingAsync`). El smoke de Compose usa plazos de segundos por configuración. Con A, plazos de segundos como hizo el paso 27, o `PlayScheduledMessagesAsync` de Wolverine.
10. **Choques con los docs.** Seis (sección "Choques"). El más grande: con la opción A no hay "worker nuevo", y con B o C el gate de estructura rechaza una feature sin endpoint HTTP.

## Glosario corto

- **Durable execution** (ejecución durable): "Durable execution is a fault-tolerant approach to running code that handles failures and interruptions through automatic retries and state persistence" ([FUENTE: Microsoft Learn, Durable Task Scheduler](https://learn.microsoft.com/en-us/azure/durable-task/scheduler/durable-task-scheduler)). Temporal lo resume como "crash-proof execution" ([FUENTE: Temporal, What is durable execution](https://temporal.io/blog/what-is-durable-execution)).
- **Workflow** (= el proceso largo escrito como código): "Workflow Definition: the code that defines your Workflow"; una *Workflow Execution* es "a running Workflow" ([FUENTE: Temporal, Workflows](https://docs.temporal.io/workflows)). Acá = el período de gracia de un cierre.
- **Activity** (= un paso con efectos externos: HTTP, base, correo): "Put API calls, LLM invocations, database queries, and other external interactions in Activities. Activities run outside the replay path and are retried automatically" ([FUENTE: Temporal, Workflow Definition](https://docs.temporal.io/workflow-definition)).
- **Event History** (historial de eventos): "An append-only log of Events for your application" ([FUENTE: Temporal, Events](https://docs.temporal.io/workflow-execution/event)).
- **Replay** (repetición): al retomar, "the Workflow Function re-executes from the start" y cada comando que emite se compara con el historial ([FUENTE: Temporal, Workflow Definition](https://docs.temporal.io/workflow-definition)).
- **Determinismo** (= mismo input, mismas llamadas en el mismo orden): el código del workflow "must be deterministic to support replay" (misma fuente).
- **Timer durable** (= una espera que vive en el servidor, no en tu proceso): "Timers are recorded as events and don't 'wait' again during replay" ([FUENTE: Temporal, Workflows](https://docs.temporal.io/workflows)).
- **Signal** (señal = mensaje asíncrono a un workflow en curso): se define con `[WorkflowSignal]` y se manda con `handle.SignalAsync`; "The call returns when the server accepts the Signal" ([FUENTE: Temporal, .NET message passing](https://docs.temporal.io/develop/dotnet/workflows/message-passing)). Acá = "el usuario canceló".
- **Worker** (= tu proceso que ejecuta el código de workflows y activities; el servidor solo guarda y reparte trabajo). En .NET, `AddHostedTemporalWorker` crea "a hosted Temporal worker" ([FUENTE: Temporalio.Extensions.Hosting](https://github.com/temporalio/sdk-dotnet/blob/main/src/Temporalio.Extensions.Hosting/README.md)).
- **Período de gracia** (= plazo en el que un pedido de cierre todavía se puede deshacer). Término nuevo para `CONTEXT.md`.

---

## 1. El concepto: durable execution y cuándo paga

### 1.1 Qué es, en cuatro piezas

1. **Workflow como código.** El proceso se escribe como un método `async` secuencial. Con Temporal .NET: `[Workflow]` en la clase y `[WorkflowRun]` en el método de entrada ([FUENTE: Temporal, .NET workflow basics](https://docs.temporal.io/develop/dotnet/workflows/basics)). Un período de gracia queda así (pseudocódigo):
   ```
   registrar "pedido"
   esperar 7 días o cancelación   → si cancelaron: registrar "cancelado" y terminar
   registrar "recordatorio 1"
   esperar 6 días o cancelación   → ídem
   registrar "recordatorio 2"
   esperar 1 día o cancelación    → ídem
   arrancar el cierre (saga del paso 27)
   ```
2. **Historial de eventos y replay.** La plataforma guarda cada decisión del workflow (timer iniciado, activity completada, señal recibida) en el historial. Si el worker se cae, otro worker vuelve a correr la función desde el principio; cada paso que ya está en el historial no se repite, se lee el resultado guardado ([FUENTE: Temporal, Workflow Definition](https://docs.temporal.io/workflow-definition)).
3. **Timers durables.** "Esperar 7 días" no es un `Task.Delay` en memoria: es un evento en el servidor. El SDK lo pide explícito: "To wait, use `Workflow.DelayAsync` instead of `Task.Delay` or `Thread.Sleep`" ([FUENTE: .NET workflow basics](https://docs.temporal.io/develop/dotnet/workflows/basics)). Las esperas con plazo (`Workflow.WaitConditionAsync` con timeout) "also use Temporal timers" ([FUENTE: README del paquete Temporalio](https://github.com/temporalio/sdk-dotnet/blob/main/src/Temporalio/README.md)).
4. **Determinismo.** Como el código se re-ejecuta, no puede leer el reloj del sistema, generar azar ni hacer IO dentro del workflow. La lista oficial: nada de IO, nada de "threading", nada de `DateTime.Now`, nada de `Task.Run` ni `ConfigureAwait(false)`; en su lugar `Workflow.UtcNow`, `Workflow.Random`, `Workflow.NewGuid()`, `Workflow.RunTaskAsync` ([FUENTE: .NET workflow basics](https://docs.temporal.io/develop/dotnet/workflows/basics)). Un cambio de código que altera el orden de los pasos rompe a los workflows en curso con un "non-deterministic error"; para eso existen el *patching* y el *Worker Versioning* ([FUENTE: Workflow Definition](https://docs.temporal.io/workflow-definition)).

### 1.2 En qué se diferencia de la saga del paso 27

| | Saga del paso 27 (`AccountClosureSaga`) | Durable execution (Temporal) |
|---|---|---|
| Dónde vive el estado | Una fila `users."AccountClosureSagas"` con `State` y fechas (`[CÓDIGO backend/Features/Users/OrchestrateAccountClosure/AccountClosureSaga.cs:9-17]`) | Las variables del método, reconstruidas desde el historial en el servidor |
| Cómo se escribe el flujo | Un `Handle` por mensaje y una guarda "esta transición vale desde estos estados" (`TryMove`, `[CÓDIGO AccountClosureSaga.cs:107-121]`) | Un método secuencial con `await` |
| Esperas | `TimeoutMessage` programado en las tablas de Wolverine (`[CÓDIGO AccountClosureOverdue.cs:5]`, `[CÓDIGO backend/Infrastructure/Messaging/SagaStorage.cs:47]`) | Timer en el servidor |
| Atomicidad con tu base | Fila, efectos y comando siguiente en **una** transacción de PostgreSQL (`[CÓDIGO docs/architecture.md:153]`) | El servidor está fuera de tu base: cada activity se reintenta y tiene que ser idempotente |
| Qué se ve | La fila y `GET /users/closures/{id}` (`[CÓDIGO docs/architecture.md:157]`) | Temporal UI con el historial de cada ejecución, más lo que expongas vos |
| Infraestructura | Ninguna nueva (Wolverine y PostgreSQL ya están) | Servidor, base propia, UI, worker |

### 1.3 Criterios concretos: plataforma o saga persistida

Una plataforma de workflow conviene cuando se cumplen **varias** de estas condiciones [INFERENCIA, sobre las fuentes de cada línea]:

- **Esperas largas y varias por instancia.** Días o meses, con plazos intermedios. Temporal: los workflows "can run—and keep running—for years" ([FUENTE: Workflows](https://docs.temporal.io/workflows)). Una saga también lo hace (Wolverine programa `ScheduleAsync(timeout, 3.Days())`, [FUENTE: Wolverine, Message Bus](https://wolverinefx.net/guide/messaging/message-bus.html)), pero cada espera es un tipo de mensaje y un handler más.
- **Ramas y bucles.** "Esperar señal o plazo", "reintentar tres veces con espera", "repetir cada día hasta X". En código secuencial son un `if` y un `for`; en una saga son estados y transiciones.
- **Operación de instancias.** Ver, terminar o reintentar una ejecución sin construir la pantalla. Temporal UI y el dashboard de Durable Task lo traen ([FUENTE: Durable Task Scheduler](https://learn.microsoft.com/en-us/azure/durable-task/scheduler/durable-task-scheduler)).
- **Varios procesos distintos.** La infraestructura se paga una vez. Con un solo proceso, el costo pesa más.
- **Equipo dispuesto al determinismo y al versionado** de workflows (sección 1.1, punto 4).

Una saga persistida (paso 27) conviene cuando:

- El proceso tiene pocos pasos y cada transición entra en **una transacción de tu base**: la saga del paso 27 guarda estado y comando juntos, sin escritura doble.
- No querés operar otro servicio con estado. El informe del repo lo pone como costo: durable execution "Plataforma extra que operar" (`[CÓDIGO docs/evolucion/informe-dotnet-empresarial.md:418]`).

En este repo, el proceso de gracia **no necesita** una plataforma: dos recordatorios y un vencimiento entran en una saga. AGENTS.md pide aplicar el patrón igual y anotar el choque (`[CÓDIGO AGENTS.md]`, "Un patrón del plan se aplica aunque `Tasks` no lo necesite"). Ver choque 1.

---

## 2. Qué proceso largo construye el paso

### 2.1 Candidatos

| Opción | Qué es | Gana | Pierde |
|---|---|---|---|
| **(P1) Cierre de cuenta con período de gracia** (recomendada) | `DELETE` agenda el cierre a N días; recordatorios; el usuario cancela; al vencer arranca la saga del paso 27 | Proceso de días, cancelable, con timers, sobre un flujo con tests. Una instancia por cierre (fácil de seguir). La historia del cierre ya es un dato que se audita (`[CÓDIGO docs/architecture.md:86]`). No toca Tasks ni Auth | Cambia el contrato de `DELETE` y la página de perfil. El cierre real pasa a tardar N días |
| (P2) Purga de cuentas inactivas | Una cuenta sin sesión en N meses recibe avisos y después se cierra | Proceso de verdad largo (meses), muchas instancias | Hay que medir la "última actividad" (hoy no existe), elegir quién dispara (un barrido periódico, que es un job del paso 31), y cancelar es "iniciar sesión": más dominio nuevo |
| (P3) Recordatorios de vencimiento de tareas | Cada tarea con fecha límite avisa antes de vencer | Muchas instancias, una por tarea | `TaskItem` no tiene fecha límite (`[CÓDIGO backend/Features/Tasks/Domain/TaskItem.cs]`, sin `Due`): campo nuevo, contrato de `POST/PATCH /tasks`, y vive en el proceso de Tasks. Sin canal de aviso, el "recordatorio" no lo ve nadie |

**Recomiendo P1.** Regla 1: un cambio observable ("borrar la cuenta tiene un plazo de arrepentimiento"). P2 y P3 suman dominio y un segundo cambio.

Referencia externa del plazo: el RGPD pide responder un pedido de borrado "without undue delay and in any event within one month of receipt of the request" (art. 12.3, [FUENTE: texto del RGPD en gdpr-info.eu](https://gdpr-info.eu/art-12-gdpr/); el texto oficial está en [EUR-Lex](https://eur-lex.europa.eu/eli/reg/2016/679/oj/eng), que no cargó). Un plazo de 14 días entra con margen.

### 2.2 Cómo queda (P1)

```
DELETE /users/me ─► Users, una transacción de UsersDbContext:
                     perfil Scheduled + historial "requested" + evento en el outbox de Users
                       │ relay de Users (paso 25.1)
                       ▼
                     arranca el workflow de gracia (id = closureId, idempotente)
                       │
            ┌──────────┴───────────────────────────────┐
     día 7: recordatorio 1                    POST .../cancel en cualquier momento:
     día 13: recordatorio 2                   perfil Active + historial "cancelled"
     día 14: vence                            + señal al workflow → termina "cancelled"
            │
            ▼
     activity "iniciar cierre" ─► Users: perfil Scheduled → Closing (FOR UPDATE)
                                  + AccountClosureSaga.Open (paso 27, sin cambios)
                                  + historial "closure-started"
                                  ▼
                                saga del paso 27: HoldingTasks … Completed | Rejected
                                  + historial "completed" | "rejected"
```

- **El mismo `closureId`** nace en el `DELETE` (como hoy, `[CÓDIGO backend/Features/Users/DeleteUser/DeleteUser.cs:51-52]`) y sigue en el workflow, la saga y el historial.
- **La saga del paso 27 no cambia.** Lo que hoy hace `DeleteUser` después del `FOR UPDATE` (`RequestClosure` y `AccountClosureSaga.Open`, `[CÓDIGO DeleteUser.cs:53-60]`) pasa a una slice nueva que corre al vencer.
- **Durante la gracia la cuenta funciona.** El usuario inicia sesión y crea tareas. Recién en `Closing` aplica el 409 `Task.OwnerClosing` del paso 26.

### 2.3 Contrato HTTP (regla 3)

| Ruta | Antes | Después |
|---|---|---|
| `DELETE /api/v1/users/me` y `/users/{authId}` | 202 `{closureId, status: "closing"}` (`[CÓDIGO docs/architecture.md:157]`) | 202 `{closureId, status: "scheduled", closesAt}`. Un segundo `DELETE` durante la gracia devuelve el mismo cierre |
| `GET /api/v1/users/me` | `accountStatus`: `active` \| `closing`; `lastClosure` con `outcome: "rejected"` (`[CÓDIGO frontend/src/app/features/users/api/user-response.ts]`) | `accountStatus` suma `scheduled` y `closesAt`; `lastClosure.outcome` suma `cancelled` |
| `GET /api/v1/users/closures/{closureId}` | Fila de la saga; 404 si no hay fila (`[CÓDIGO backend/Features/Users/GetAccountClosure/GetAccountClosure.cs:23-28]`) | `state` suma `scheduled` y `cancelled`, y el campo `closesAt`. Antes del vencimiento no hay fila de saga: el estado sale del perfil y del historial |
| `POST /api/v1/users/closures/{closureId}/cancel` | — | **Nueva.** 204; 409 `User.ClosureNotCancellable` si el cierre ya arrancó; 404; 403. Lleva `WithIdempotency` porque DUNE0016 lo exige a todo `MapPost` (`[CÓDIGO dune/Dune.Analyzers/DuneDiagnostics.cs:171-177]`) |
| `GET /api/v1/users/closures/{closureId}/history` | — | **Nueva.** 200 con la lista ordenada del historial; mismo permiso que la lectura del cierre (dueño o `Users.Read`) |

- **Frontend.** `closureNotice` suma el caso `scheduled` ("Tu cuenta se cierra el {fecha}") y el caso `cancelled`; la página de perfil suma el botón "Cancelar cierre" (`[CÓDIGO frontend/src/app/features/users/profile/closure-notice.ts]`). El historial en pantalla queda opcional (decisión 12).
- **E2E.** Corre al cerrar el paso: cambia el frontend y un contrato (`[CÓDIGO AGENTS.md]`, "Verificación").

---

## 3. Diseño independiente de la herramienta

### 3.1 Estados

| Estado del cierre | Entra cuando | Sale con |
|---|---|---|
| `scheduled` (nuevo) | `DELETE` aceptado | Cancelación → `cancelled`; vencimiento → `holdingTasks` (saga del paso 27) |
| `cancelled` (nuevo, final) | El dueño o un admin cancela durante la gracia | — |
| `holdingTasks` … `completed` \| `rejected` | Los del paso 27, sin cambio (`[CÓDIGO docs/architecture.md:142-148]`) | Los del paso 27 |

- `AccountClosureStatus` del perfil suma `Scheduled` (`[CÓDIGO backend/Features/Users/Domain/AccountClosureStatus.cs]`). `UserProfile` suma `ScheduleClosure`, `CancelClosure` y cambia `RequestClosure` para que solo acepte desde `Scheduled`.

### 3.2 Historial auditable

**Por qué una tabla propia y no el historial de la plataforma:**
- El historial de Temporal es técnico (`TimerStarted`, `ActivityTaskCompleted`) y tiene retención: al vencer el *Retention Period* de un namespace, "a Timer task cleans up that execution's data"; `temporal operator namespace create` usa 3 días si no se fija otro ([FUENTE: Temporal Server, Retention Period](https://docs.temporal.io/temporal-service/temporal-server#retention-period)). Temporal mismo lo presenta como "audit log for debugging" ([FUENTE: Events](https://docs.temporal.io/workflow-execution/event)).
- Elsa guarda un *journal* por instancia y lo expone en `GET /workflow-instances/{id}/journal` ([FUENTE: Elsa, Workflow state and journal](https://docs.elsaworkflows.io/operate/workflow-state-and-journal.md)), pero su retención borra esas filas con la instancia ("is not an archive, backup, or preview feature", [FUENTE: Elsa, Retention](https://docs.elsaworkflows.io/optimize/retention.md)).
- La auditoría del repo guarda "ids opacos y nombres de propiedades, nunca valores" (`[CÓDIGO docs/conventions.md:101]`) y sobrevive al borrado de la cuenta (`[CÓDIGO docs/architecture.md:86]`). El historial del cierre sigue la misma regla y no depende de una herramienta.

**Forma propuesta** (`users."AccountClosureHistory"`, en `UsersDbContext`):

| Columna | Qué guarda |
|---|---|
| `Id` | GUID v7 (ordena por tiempo) |
| `ClosureId` | El cierre |
| `Sequence` | Orden dentro del cierre (1, 2, 3…) |
| `Kind` | `requested`, `reminder-sent`, `cancelled`, `grace-expired`, `closure-started`, `completed`, `rejected` |
| `OccurredAt` | Cuándo pasó (reloj de quien lo registra) |
| `Actor` | `AuthId` de quien pidió o canceló, o `system` |
| `Detail` | Un dato chico sin datos personales: número de recordatorio, motivo del rechazo (`Auth.LastSuperAdmin`) |

- **Append-only.** Las filas solo se agregan. Índice único `(ClosureId, Kind, Detail)` para que una activity reintentada no duplique (`reminder-sent` 1 y 2 son filas distintas por `Detail`) [INFERENCIA, diseño].
- **Regla ejecutable (regla 4).** Propongo un marcador `IAppendOnly` (`Shared/Domain`), propiedades `init`, y extender DUNE0017 para que rechace `Update`, `Remove`, `RemoveRange`, `ExecuteUpdate` y `ExecuteDelete` sobre un tipo `IAppendOnly`, con su fixture válida y su violación deliberada. Como defensa en la base, un trigger `BEFORE UPDATE OR DELETE` que tira error, con un test. Hoy `AuditEntries` no es append-only (pendiente en `progreso.md`, `[CÓDIGO docs/architecture.md:86]`): esto no lo cambia.
- **Quién escribe.** Users, siempre en la transacción del cambio que registra: el `DELETE` escribe `requested` con el perfil; la cancelación escribe `cancelled`; el inicio escribe `grace-expired` y `closure-started` con la apertura de la saga; la saga escribe `completed` o `rejected` en su transacción final. El recordatorio lo escribe una slice que llama el workflow.
- **Quién lee.** `GET /users/closures/{closureId}/history`, slice de lectura que proyecta sin rastrear (DUNE0019, `[CÓDIGO docs/conventions.md]`, "Lecturas"). Nunca llama a la plataforma: DUNE0024 prohíbe que una lectura llame a otro proceso (`[CÓDIGO dune/Dune.Analyzers/DuneDiagnostics.cs:379-385]`).

### 3.3 Timers y aceleración

- **Plazos por configuración**, como `AccountClosure:OverdueAfterSeconds` del paso 27 (`[CÓDIGO backend/Features/Users/OrchestrateAccountClosure/AccountClosureOptions.cs:9-12]`): `AccountClosure:GracePeriodSeconds` (1 209 600 = 14 días) y `AccountClosure:ReminderOffsetsSeconds` (días 7 y 13). El workflow los recibe como input al arrancar, así un cambio de configuración no altera a los que están en curso (y con Temporal no rompe el determinismo) [INFERENCIA].
- **Smoke de Compose:** plazos de segundos (gracia 20 s, recordatorios a 5 s y 15 s).
- **Tests:** según la herramienta (sección 5).

### 3.4 Cancelación y la carrera con el vencimiento

1. El usuario cancela en el segundo en que vence el plazo.
2. La activity "iniciar cierre" y el `POST .../cancel` llegan casi juntos al monolito.

- **La fila del perfil decide.** Las dos operaciones toman el perfil con `SELECT ... FOR UPDATE`, como `DeleteUser` hoy (`[CÓDIGO DeleteUser.cs:42-44]`). La cancelación solo vale desde `Scheduled`; el inicio también. La que llega segunda ve el estado nuevo: la cancelación responde 409 `User.ClosureNotCancellable`, o el inicio responde "ya cancelado" y el workflow termina `cancelled`.
- **La señal es un atajo, no la verdad.** El `POST .../cancel` guarda el cambio y deja un evento en el outbox; el relay manda la señal al workflow para que termine antes. Si la señal se pierde o llega tarde, el inicio igual ve `Active` y no cierra. Así la carrera no depende de la plataforma.

### 3.5 Arranque sin escritura doble

- El `DELETE` guarda perfil, historial y un evento de dominio `AccountClosureScheduled` en una transacción; el relay de Users llama un `IEventHandler` que arranca el workflow (`[CÓDIGO backend/Infrastructure/Messaging/OutboxRegistration.cs:18-35]`; DUNE0023: solo `OutboxRelay` llama a un handler). Es el error que el paso 24 vino a evitar: no se llama a otro sistema dentro de la transacción ni antes de confirmarla.
- **Idempotencia del arranque.** El relay entrega al menos una vez. Con Temporal, el id del workflow es el `closureId`: "Temporal guarantees that there can be at most one Workflow Execution with a given ID running at any point in time", y con la política *Use Existing* el segundo arranque "returns a successful response with the Open Workflow Execution's Run Id" ([FUENTE: Temporal, Workflow Id](https://docs.temporal.io/workflow-execution/workflowid-runid)).
- **Plataforma caída.** El `DELETE` responde 202 igual; el workflow arranca cuando vuelve (el relay reintenta) [INFERENCIA, por el relay del paso 24].

### 3.6 Dónde corre el proceso

| Opción | Qué es | Gana | Pierde |
|---|---|---|---|
| **(H1) Proceso nuevo `backend/Hosts/Workflows`** (recomendada con B o C) | Un host .NET que solo corre el worker; sus activities llaman al monolito por `/internal/v1` con un cliente tipado, como Tasks (`[CÓDIGO docs/conventions.md:27]`) | Es el "worker nuevo" del plan. Un proyecto por proceso (ADR-0005). El worker escala y se despliega solo | Un salto HTTP por activity, endpoints internos nuevos, otro health, otro Dockerfile, CI |
| (H2) Worker dentro del monolito | `AddHostedTemporalWorker` en el monolito; las activities llaman slices directo | Menos piezas | Contradice "worker nuevo" (`plan.md:458`). El monolito carga el SDK nativo de Temporal |
| (H3) Sin worker (opción A) | La saga de gracia vive en Users, en el monolito | Cero procesos nuevos | No hay worker nuevo (choque 2) |

- **Endpoints internos que pide H1** (Users, grupo `/internal/v1`): `POST /internal/v1/users/closures/{id}/reminders/{n}` (registra el recordatorio) y `POST /internal/v1/users/closures/{id}/start` (perfil `Closing`, abre la saga, historial). Los dos idempotentes por `(closureId, n)` y por estado.
- **Health.** El worker expone `/api/v1/health/ready` con un check de la conexión a la plataforma, como cada proceso (ADR-0005, punto 3).

---

## 4. Tests: el Verifica y los de reanudación

| Test | Qué hace | Qué afirma | Rojo primero |
|---|---|---|---|
| V1 Varios días acelerados | Gracia completa de 14 días | Recordatorio 1 y 2 en su orden, inicio del cierre al vencer, historial con 5 filas | Sin el segundo timer, falta `reminder-sent 2` |
| V2 Cancelación | Cancela el día 3 | Ningún recordatorio, ningún inicio, `cancelled` en historial y perfil `active` | Sin la señal ni la guarda, el cierre arranca igual |
| V3 Reanudación | Se cae el worker (o el monolito, con A) con el timer del día 7 en curso; se levanta otro | El proceso sigue, sin repetir lo hecho | Con el plazo en memoria, nunca llega el recordatorio |
| V4 Carrera cancelación-vencimiento | Cancelación e inicio concurrentes | Exactamente uno gana; el otro 409 o "ya cancelado" | Sin `FOR UPDATE`, el perfil queda `active` con la saga abierta |
| V5 Arranque idempotente | El relay entrega `AccountClosureScheduled` dos veces | Un solo workflow | Con la política *Fail*, la segunda entrega va a reintentos y DLQ |
| V6 Historial | Lectura del historial por el dueño, por un admin y por otro usuario | 200, 200, 403; orden por `Sequence`; un `UPDATE` directo a la tabla falla | Sin el trigger, el `UPDATE` pasa |
| V7 Plataforma caída | `DELETE` con la plataforma parada | 202; el workflow arranca al volver | Si el `DELETE` llama a la plataforma en la request, responde 500 |

**Cómo se aceleran los timers en cada opción:**
- **A (Wolverine).** Plazos de segundos por configuración, como el test de plazo del paso 27 (`OverdueAfterSeconds = 1`, `[CÓDIGO backend/Features/Users/OrchestrateAccountClosure/AccountClosureSagaTests.cs:90-92]`). Wolverine 6.64.2 trae `PlayScheduledMessagesAsync`, que "will try to immediately play any scheduled messages" ([FUENTE: docs de Wolverine en `V6.64.2`](https://github.com/JasperFx/wolverine/blob/V6.64.2/docs/guide/testing.md)); los repite todos juntos, no en orden de vencimiento. El avance por ventanas con un reloj virtual (`PlayScheduledMessagesAsync(advanceBy:)`) es de Wolverine 7.0, hoy alpha, y "nothing in Wolverine's runtime reads the session's clock" ([FUENTE: docs de Wolverine en `main`](https://github.com/JasperFx/wolverine/blob/main/docs/guide/testing.md)). El reloj que lee la saga sigue siendo `FakeTimeProvider` (`[CÓDIGO backend/Tests/Support/IsolatedApi.cs:60]`).
- **B (Temporal).** `WorkflowEnvironment.StartTimeSkippingAsync`: "the time-skipping server automatically skips to the next event when we wait on the result"; antes de esperar, el tiempo avanza con `DelayAsync` ([FUENTE: Temporal, .NET testing suite](https://docs.temporal.io/develop/dotnet/testing-suite)). V1, V2 y V3 corren ahí con activities falsas; 14 días pasan sin esperar. `WorkflowReplayer` vuelve a correr un historial guardado y "throws if replay detects non-determinism" (misma fuente): un test que protege a los workflows en curso de un cambio de código. Límites: ese servidor "is not thread safe" (uno por test, misma fuente) y "does not work on musl-based environments such as Alpine" ([FUENTE: README de Temporalio](https://github.com/temporalio/sdk-dotnet/blob/main/src/Temporalio/README.md)); el CI usa `ubuntu-latest` (`[CÓDIGO .github/workflows/ci.yml:15]`). El entorno se descarga "lazily ... on first use" (misma fuente de testing): el primer test pide red.
- **C (Elsa).** La guía de tests no cubre relojes falsos ni saltar un `Delay`; solo pide probar "both sides: bookmark creation and the stimulus that resumes it" ([FUENTE: Elsa, Testing](https://docs.elsaworkflows.io/guides/testing-debugging.md)). Quedan plazos de segundos por configuración.

- [NO PROBADO] Ningún test corrió. Todo es diseño.

---

## 5. Dune y gates

| Cambio | A | B | C |
|---|---|---|---|
| Nombres de la herramienta en features | Sin cambio: DUNE0025 y el gate de bus ya aceptan el archivo de un orquestador (un tipo que hereda `Saga` o `TimeoutMessage`, `[CÓDIGO dune/check-structure.mjs:231-245]`) | Regla nueva: `Temporalio` solo en `Infrastructure/Workflows` (el arranque y la señal) y en la feature del worker; paquete solo en `Platform` y en el csproj del worker | Igual que B, con `Elsa` |
| Determinismo | No aplica | **Regla nueva**: dentro de una clase `[Workflow]`, nada de `DateTime.Now/UtcNow`, `DateTimeOffset.Now/UtcNow`, `TimeProvider`, `Task.Delay`, `Task.Run`, `Thread.Sleep`, `Guid.NewGuid`, `new Random`, `ConfigureAwait(false)`, `HttpClient` ni `DbContext` (lista de [.NET workflow basics](https://docs.temporal.io/develop/dotnet/workflows/basics)). Hay analyzers de la comunidad ([FUENTE: NuGet, `TemporalCommunity.Extensions.Analyzers`](https://www.nuget.org/packages/TemporalCommunity.Extensions.Analyzers), no instalado) y no encontré uno oficial; recomiendo escribirlo en Dune, que es donde viven las reglas del repo | No aplica (Elsa no hace replay) |
| Público fuera de `Contracts` (DUNE0020) | Ya aceptado para sagas (`[CÓDIGO dune/Dune.Analyzers/PublicSurfaceAnalyzer.cs:15,59]`) | [NO VERIFICADO] si el SDK registra un workflow `internal`; si no, excepción como la de sagas | [NO VERIFICADO] |
| Feature sin endpoint | No aplica | El gate rechaza una feature sin `<Slice>Endpoint.cs` (`[CÓDIGO dune/check-structure.mjs:83-111]`): hay que aceptar una feature cuyas slices son workflows y activities, con su test (choque 3) | Igual |
| Límite de despliegue | Sin cambio | El gate conoce solo el monolito y el host de Tasks (`[CÓDIGO dune/check-structure.mjs:51-53,114-115]`): suma el host del worker | Igual |
| Llamada remota en transacción (DUNE0022) | Sin cambio | Hoy cuenta solo clientes HTTP (`[CÓDIGO DuneDiagnostics.cs:361-367]`); el cliente gRPC de Temporal no entra. Extenderla a `ITemporalClient` [INFERENCIA] | Si se llama la API REST de Elsa, ya entra |
| Append-only | DUNE0017 extendida (sección 3.2) | Igual | Igual |
| Código generado (`check-codegen`) | La saga nueva regenera handlers | Sin cambio | Sin cambio |

Cada regla nueva o cambiada lleva su fixture válida, su violación deliberada y su línea en `decisions.tsv` (reglas 4 y 9).

---

## 6. Releases (rolling deploy)

Mismo motivo que los pasos 26 y 27: dos versiones vecinas tienen que entenderse.

| Commit | Qué entra | Por qué en este orden |
|---|---|---|
| **R1** | Tabla de historial (expand), endpoints internos (con B o C), el worker y la plataforma en Compose (con B o C), el handler del arranque registrado pero sin eventos que lo disparen | Todo lo que recibe existe antes de que alguien lo mande |
| **R2** | Frontend entiende `scheduled`, `closesAt` y `cancelled` (todavía no llegan) | Un frontend viejo con un backend nuevo mostraría un estado desconocido |
| **R3** | `DELETE` agenda en vez de cerrar; `POST .../cancel`; `GET .../history`; smoke y e2e | Con R1 y R2 desplegados, cada pieza tiene quién la atienda |

- Los cierres abiertos antes de R3 siguen por la saga del paso 27 sin gracia: no tienen fila `requested` en el historial. La lectura del historial devuelve lista vacía para ellos [INFERENCIA, diseño].

---

## 7. Docs a actualizar

| Doc | Qué cambia |
|---|---|
| `architecture.md` | Sección "Cierre de cuenta" suma el período de gracia, el historial y (con B o C) el worker, la plataforma y su UI |
| `conventions.md` | Historial append-only (`IAppendOnly`); con B, reglas de determinismo y dónde vive el SDK |
| ADR | Con B o C, un ADR nuevo (ADR-0006): la plataforma de workflow y el worker como proceso; cambia el punto 9 de ADR-0004 si la herramienta nombra algo fuera de `Infrastructure` |
| `CONTEXT.md` | **Período de gracia**, **Historial del cierre** |
| `README.md` | Comandos y puertos nuevos (UI de la plataforma) |
| `docs/decisions.tsv` | Las decisiones de este archivo y la herramienta |

---

## Choques con los docs

1. **AGENTS.md y `informe-dotnet-empresarial.md:418`.** El informe dice que durable execution paga en "Procesos de días/meses, auditoría, reintentos con estado" y no paga en "Flujo de un request". El período de gracia es de días y tiene auditoría, pero entra en una saga (sección 1.3). AGENTS.md dice que el patrón se aplica igual y el choque se anota en `decisions.tsv`.
2. **`plan.md:458` ("Cambia: worker nuevo") con la opción A.** Una segunda saga de Wolverine vive en el monolito: no hay proceso nuevo. Si Tobi elige A, el paso cambia ese renglón del plan.
3. **Gate de estructura con B o C.** `checkSlicesHaveTests` exige que cada feature tenga al menos una slice con `<Slice>Endpoint.cs` (`[CÓDIGO dune/check-structure.mjs:83-111]`). La feature del worker no tiene endpoints públicos. Cambiar el gate es objetivo del paso (regla 9), con su fixture.
4. **ADR-0005, punto 2 ("ningún proceso de la API publica puertos en Compose") con B o C.** La UI de la plataforma no es la API, pero publica un puerto. Además, el gateway ya usa el 8080 (`[CÓDIGO compose.yaml:150-151]`) y la UI de Temporal escucha en 8080 en el ejemplo oficial ([FUENTE: compose de samples-server](https://raw.githubusercontent.com/temporalio/samples-server/main/compose/docker-compose-postgres.yml)). Temporal avisa que sus hosts "Should Not Be Exposed to the Open Internet" ([FUENTE: Deployment](https://docs.temporal.io/self-hosted-guide/deployment)). Propongo publicar la UI solo en `127.0.0.1` y en otro puerto.
5. **ADR-0004, punto 9 ("sin mediator") con C.** [NO VERIFICADO] si Elsa trae un mediator interno; si lo trae, ninguna slice lo usaría, como pasa con Wolverine.
6. **Contrato de `DELETE` (paso 26 y 27).** `architecture.md:157` documenta `status: "closing"`. El paso lo cambia a `"scheduled"` a propósito (regla 3: backend, frontend y tests juntos).

## Decisiones (con mi recomendación)

1. El proceso largo es el cierre de cuenta con período de gracia (P1).
2. Gracia de 14 días con recordatorios en los días 7 y 13, configurables en segundos (`AccountClosure:GracePeriodSeconds`, `AccountClosure:ReminderOffsetsSeconds`); el workflow los recibe como input.
3. Durante la gracia la cuenta funciona sin restricciones; el 409 de tareas empieza en `Closing`.
4. El recordatorio es una fila de historial, un log y una métrica (`templateapp.account_closures.reminders`); no hay correo (no hay canal: pendiente).
5. Mismo `closureId` en `DELETE`, workflow, saga e historial.
6. La saga del paso 27 no cambia; una slice nueva hace lo que hoy hace `DeleteUser` después del `FOR UPDATE`.
7. Historial en `users."AccountClosureHistory"`, append-only, con `IAppendOnly`, DUNE0017 extendida y un trigger en la base.
8. El historial nunca se lee de la plataforma (DUNE0024).
9. La fila del perfil decide la carrera entre cancelación y vencimiento (`FOR UPDATE`); la señal es un atajo.
10. El arranque y la señal salen del outbox de Users por un `IEventHandler`, con id de workflow = `closureId` y política *Use Existing* (con B).
11. Con B o C, el worker es un proceso nuevo (H1) que le habla al monolito por `/internal/v1`.
12. Contrato: `DELETE` 202 con `scheduled` y `closesAt`; `POST /users/closures/{id}/cancel` con idempotencia; `GET /users/closures/{id}/history`; frontend con aviso y botón de cancelar. Mostrar el historial en pantalla queda fuera.
13. Un admin con `Users.Manage` también puede cancelar; el historial guarda quién.
14. Tres commits como releases: R1 recibe, R2 frontend tolerante, R3 activa.
15. Con B, Temporal en Compose con su propia base `postgres:16` (la versión que fija el ejemplo oficial), no la base del repo (`postgres:18`, sin compatibilidad verificada); retención del namespace de 7 días.
16. Con B, regla de determinismo propia en Dune, no un analyzer de la comunidad.

## Herramientas (regla 5)

**Esta sección es la decisión de Tobi.** Abajo están las opciones con lo que gana y lo que pierde. Mi recomendación va aparte, al final.

### Opción A: seguir con Wolverine (saga con mensajes programados durables)

- **Paquetes:** ninguno nuevo. `WolverineFx.EntityFrameworkCore`, `WolverineFx.Postgresql` y `WolverineFx.RuntimeCompilation` 6.64.2 ya están (`[CÓDIGO backend/Platform/TemplateApp.Platform.csproj:30-34]`). 6.64.2 es la última estable (16/11/2026); hay una 7.0.0-alpha.3 del 19/11/2026 ([FUENTE: NuGet, WolverineFx](https://www.nuget.org/packages/WolverineFx)).
- **Licencia:** MIT (misma fuente). Mantiene JasperFx, con releases casi diarias en noviembre (misma fuente).
- **Cómo funciona:** `AccountClosureGracePeriod`, una segunda `Wolverine.Saga` en Users con tres `TimeoutMessage` (recordatorio 1, recordatorio 2, vencimiento) y un handler para la cancelación. Al vencer abre `AccountClosureSaga` en la misma transacción. Con el almacén durable, "This option is durable in case of process exits" ([FUENTE: Wolverine, Message Bus](https://wolverinefx.net/guide/messaging/message-bus.html)).
- **Infraestructura nueva en Compose:** ninguna.
- **Encaje con ADR-0004 y ADR-0005:** el mismo del paso 27 (punto 9 de ADR-0004). Sin proceso nuevo (choque 2).
- **Dune:** sin cambio de reglas de herramienta; solo `IAppendOnly` (sección 5).
- **Historial:** la tabla propia; la fila de la saga muestra el estado actual.
- **Timers en tests:** segundos por configuración o `PlayScheduledMessagesAsync` (sección 4).
- **Gana:** cero infraestructura y cero reglas nuevas; atomicidad de cada transición con PostgreSQL; el código se lee como el del paso 27.
- **Pierde:** no enseña durable execution: es el paso 27 con otro proceso. No hay worker nuevo ni UI para operar instancias. El flujo con tres esperas y una cancelación queda repartido en cinco handlers.

### Opción B: Temporal

- **Paquetes:** `Temporalio` **1.22.0** (10/11/2026; versiones cada 2 a 4 semanas desde julio), MIT, para .NET Core 3.1+, .NET Standard 2.0 y .NET Framework 4.6.2 ([FUENTE: NuGet, Temporalio](https://www.nuget.org/packages/Temporalio)). Además `Temporalio.Extensions.Hosting` (worker como servicio hospedado y DI de activities, con "A new scope ... for each activity invocation") y `Temporalio.Extensions.OpenTelemetry` (`TracingInterceptor`) ([FUENTE: Hosting](https://github.com/temporalio/sdk-dotnet/blob/main/src/Temporalio.Extensions.Hosting/README.md), [FUENTE: .NET observability](https://docs.temporal.io/develop/dotnet/observability)). El SDK usa una librería nativa en Rust, con binarios para `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64` y otros ([FUENTE: README de Temporalio](https://github.com/temporalio/sdk-dotnet/blob/main/src/Temporalio/README.md)); la imagen del repo es Debian (`mcr.microsoft.com/dotnet/aspnet:10.0.12`, `[CÓDIGO backend/Dockerfile:9]`).
- **Licencia:** servidor MIT ("Copyright (c) 2025 Temporal Technologies Inc.", [FUENTE: LICENSE](https://github.com/temporalio/temporal/blob/main/LICENSE)), SDK MIT ([FUENTE: sdk-dotnet](https://github.com/temporalio/sdk-dotnet)), UI MIT ([FUENTE: temporalio/ui](https://github.com/temporalio/ui)). Temporal Cloud es el servicio pago; no hace falta.
- **Quién lo mantiene:** Temporal Technologies. Servidor 1.32.2 publicado el 2 de noviembre, junto con parches de las ramas 1.31 y 1.30; la 1.33 anuncia quitar APIs viejas de Worker Versioning ([FUENTE: releases del servidor](https://github.com/temporalio/temporal/releases), la página no muestra el año). UI: ui-server v2.58.1 del 6 de noviembre ([FUENTE: releases de ui-server](https://github.com/temporalio/ui-server/releases)).
- **Infraestructura nueva en Compose** (según el ejemplo oficial con PostgreSQL, [FUENTE: docker-compose-postgres.yml](https://raw.githubusercontent.com/temporalio/samples-server/main/compose/docker-compose-postgres.yml) y su [`.env`](https://raw.githubusercontent.com/temporalio/samples-server/main/compose/.env)):
  - `temporal-db`: `postgres:16`, con dos bases, `temporal` y `temporal_visibility` ([FUENTE: setup-postgres.sh](https://raw.githubusercontent.com/temporalio/samples-server/main/compose/scripts/setup-postgres.sh)). PostgreSQL sirve de persistencia y de *visibility* (búsqueda de ejecuciones) desde la v12 ([FUENTE: Visibility](https://docs.temporal.io/self-hosted-guide/visibility)): no hace falta Elasticsearch.
  - `temporal-admin-tools`: `temporalio/admin-tools:1.32.2`, corre una vez y crea el esquema con `temporal-sql-tool`. Con la imagen `temporalio/server` "you must manage schema updates yourself" ([FUENTE: Deployment](https://docs.temporal.io/self-hosted-guide/deployment)). La imagen `auto-setup` ya no se mantiene y el repo `temporalio/docker-compose` está archivado; sus ejemplos pasaron a `samples-server` ([FUENTE: Docker Hub, auto-setup](https://hub.docker.com/r/temporalio/auto-setup), [FUENTE: temporalio/docker-compose](https://github.com/temporalio/docker-compose)).
  - `temporal`: `temporalio/server:1.32.2`, puerto 7233, con sus cuatro servicios (frontend, history, matching, worker) en un proceso; en producción "we recommend that each service runs independently" ([FUENTE: Temporal Server](https://docs.temporal.io/temporal-service/temporal-server)).
  - `temporal-create-namespace`: corre una vez; fija la retención.
  - `temporal-ui`: `temporalio/ui` (el ejemplo fija 2.56.1), puerto 8080 adentro.
  - `workflows`: el worker del repo (H1).
- **Encaje con ADR-0004 y ADR-0005:** el worker es un proceso con su proyecto (ADR-0005, punto 1). La feature del worker no tiene tablas: guarda todo en Users por HTTP interno, así cada feature sigue siendo dueña de sus datos. El SDK queda en `Infrastructure/Workflows` (arranque y señal) y en la feature del worker, como Wolverine en `Infrastructure/Messaging`.
- **Dune:** dos reglas nuevas (dónde vive el SDK y determinismo) y tres gates cambiados (feature sin endpoint, límite de despliegue, DUNE0022). Sección 5.
- **Historial:** la tabla propia para la auditoría; Temporal UI y `ListWorkflowsAsync` para operar ([FUENTE: .NET observability](https://docs.temporal.io/develop/dotnet/observability)). El historial de Temporal se borra según la retención del namespace (sección 3.2). Límites: aviso a los 10 240 eventos y fin de la ejecución a los 51 200 ([FUENTE: Events](https://docs.temporal.io/workflow-execution/event)); un período de gracia tiene unas decenas.
- **Timers en tests:** time skipping y replay (sección 4).
- **Gana:** aprende durable execution de verdad (replay, determinismo, timers en el servidor, señales, workers, versionado), la herramienta más usada del rubro (sección "Industria"), y una UI para operar instancias. El flujo de gracia se lee como un método de 20 líneas.
- **Pierde:** seis contenedores más en Compose y un esquema que se migra aparte; un proceso nuevo con su CI; dos reglas de Dune nuevas; las activities pierden la transacción única (cada una se reintenta y tiene que ser idempotente); el SDK trae una librería nativa; el entorno de tests se descarga en el primer uso.

### Opción C: Elsa Workflows 3

- **Paquetes:** `Elsa` **3.10.2** (18/11/2026), MIT, para net8.0, net9.0 y net10.0 ([FUENTE: NuGet, Elsa](https://www.nuget.org/packages/Elsa)). Para PostgreSQL, `Elsa.Persistence.EFCore.PostgreSql`, con dos contextos propios (`ManagementDbContext` y `RuntimeDbContext`) que se migran con `dotnet ef` ([FUENTE: Elsa, Database configuration](https://docs.elsaworkflows.io/getting-started/database-configuration.md)).
- **Licencia:** MIT. El ecosistema Elsa+ vende soporte y capacitación ([FUENTE: elsa-core](https://github.com/elsa-workflows/elsa-core)).
- **Quién lo mantiene:** la organización `elsa-workflows` (7,9k estrellas); las 3.8.x y 3.9.0 las publicó sfmskywalker y las 3.10.x cuentas automáticas; la 3.10.2 es una actualización de seguridad ([FUENTE: releases](https://github.com/elsa-workflows/elsa-core/releases)).
- **Cómo funciona:** un motor de workflows con actividades (código, diseñador visual o JSON, [FUENTE: docs de Elsa](https://docs.elsaworkflows.io/)). Una espera crea un *bookmark* (marcador) y la instancia se suspende; al llegar el estímulo, Elsa la retoma "from its matched bookmark" ([FUENTE: Elsa, Execution model](https://docs.elsaworkflows.io/guides/architecture/execution-model.md)). Guarda una foto del estado, no un historial que se re-ejecuta: no es durable execution en el sentido de Temporal [INFERENCIA, por esa fuente].
- **Timers:** `Delay` "creates a bookmark with a specific resume time". El scheduler por defecto es en memoria y "not the right operational model for durable multi-node timer execution"; para timers durables pide Quartz o Hangfire con su propia persistencia ([FUENTE: Elsa, Timers](https://docs.elsaworkflows.io/guides/running-workflows/timer-and-scheduled-workflows.md)). Eso adelanta la decisión del paso 31 (Quartz.NET o Hangfire, `[CÓDIGO docs/evolucion/plan.md:475]`).
- **Infraestructura nueva en Compose:** el worker con Elsa embebido (H1) o `elsaworkflows/elsa-server-v3`; `elsaworkflows/elsa-studio-v3` como UI ([FUENTE: Elsa, Server + Studio](https://docs.elsaworkflows.io/getting-started/containers/docker-compose/elsa-server-+-studio.md)); tablas de Elsa y de Quartz en la base.
- **Encaje con ADR-0004 y ADR-0005:** el worker como proceso (H1). Elsa trae su API REST de administración, otra superficie que proteger.
- **Dune:** como B sin la regla de determinismo; más el esquema y las migraciones de dos contextos que DUNE0021 no ve (están en el paquete).
- **Historial:** journal por instancia (`GET /workflow-instances/{id}/journal`); la retención no borra nada hasta que se configura una política, y entonces borra el journal con la instancia ([FUENTE: Retention](https://docs.elsaworkflows.io/optimize/retention.md)).
- **Timers en tests:** sin reloj falso documentado (sección 4).
- **Gana:** un motor de workflows .NET puro, sin servidor aparte; diseñador visual; activities en C#.
- **Pierde:** no enseña replay ni determinismo; dos herramientas en vez de una (Elsa y Quartz); tablas y migraciones que el repo no controla; tests de tiempo sin soporte documentado.

### Evaluadas y descartadas

- **Durable Task SDK de Microsoft** (`Microsoft.DurableTask.Worker` 1.28.0, 04/11/2026, MIT, [FUENTE: NuGet](https://www.nuget.org/packages/Microsoft.DurableTask.Worker)). Mismo modelo que Temporal, pero "Each SDK connects to the Durable Task Scheduler as its managed backend" ([FUENTE: Durable Task SDKs](https://learn.microsoft.com/en-us/azure/durable-task/sdks/durable-task-overview)), un servicio de Azure con SKU pagos, y el emulador local "stores orchestration and entity state in local memory, so it isn't suitable for production use" ([FUENTE: Durable Task Scheduler](https://learn.microsoft.com/en-us/azure/durable-task/scheduler/durable-task-scheduler)). **Descartado:** en Compose no hay backend durable.
- **Dapr Workflow** (`Dapr.Workflow` 1.19.1, 10/11/2026, Apache-2.0, [FUENTE: NuGet](https://www.nuget.org/packages/Dapr.Workflow)). Corre dentro del sidecar de Dapr, sobre actores, con servicio de placement y un state store con actores (PostgreSQL sirve) ([FUENTE: Dapr, Workflow architecture](https://docs.dapr.io/developing-applications/building-blocks/workflow/workflow-architecture/)). **Descartado:** obliga a adoptar el runtime de Dapr (sidecar por proceso, placement, scheduler), una decisión de plataforma mayor que el paso, y su pub/sub compite con Wolverine (el gate de bus exige "un solo bus, Wolverine", `[CÓDIGO dune/check-structure.mjs:306]`, decidido en `[CÓDIGO docs/decisions.tsv:358]`).
- **Restate.** Sin SDK oficial de .NET: los oficiales son TypeScript, Java/Kotlin, Python, Go y Rust ([FUENTE: restatedev](https://github.com/restatedev)); `Restate.Sdk` 0.5.0 es "a community-driven project, not an official Restate SDK" ([FUENTE: NuGet](https://www.nuget.org/packages/Restate.Sdk)). **Descartado.**
- **DBOS.** Sin SDK oficial de .NET: Python, TypeScript, Go, Java y Rust ([FUENTE: dbos-inc](https://github.com/dbos-inc)); `Dbos.Transact` es un port de la comunidad en 0.0.0-alpha.0.47 ([FUENTE: NuGet](https://www.nuget.org/packages/Dbos.Transact/)). **Descartado.**
- **AWS Lambda durable functions** (SDK de .NET GA el 23/07/2026, [FUENTE: AWS](https://aws.amazon.com/about-aws/whats-new/2026/07/lambdadf-dotnet/)). **Descartado:** solo corre en Lambda.

### Qué gana y qué pierde Tobi, en simple

| | Si elegís A | Si elegís B | Si elegís C |
|---|---|---|---|
| Qué aprendés | Una segunda saga, ahora con varias esperas y una cancelación | Durable execution: workflow como código, replay, determinismo, timers en el servidor, señales, workers | Un motor de workflows con diseñador y bookmarks |
| Qué ves en pantalla | El aviso y el botón en el perfil; la fila y el historial por HTTP | Lo mismo, más Temporal UI con cada ejecución paso a paso | Lo mismo, más Elsa Studio |
| Qué se suma al repo | Casi nada | 6 contenedores, 1 proceso, 2 reglas de Dune, 1 ADR | 2 a 3 contenedores, 1 proceso, Quartz, 1 regla de Dune, 1 ADR |
| Riesgo | Bajo | Medio: infraestructura nueva y una forma nueva de escribir código | Medio: dos herramientas y tablas ajenas |
| Para el objetivo del repo (arquitectura .NET empresarial) | Repite el paso 27 | Es lo que el informe pone en una empresa grande (`informe-dotnet-empresarial.md:398`) | Común en apps .NET con flujos editables por usuarios [INFERENCIA] |

### Qué hace la industria

- **Durable execution pasó a producto de los grandes proveedores.** Azure ofrece el Durable Task Scheduler, que "provides durable execution in Azure" ([FUENTE: Microsoft Learn](https://learn.microsoft.com/en-us/azure/durable-task/scheduler/durable-task-scheduler)); AWS sacó el SDK de .NET de Lambda durable functions, que "checkpoint progress automatically and pause execution for up to a year" ([FUENTE: AWS](https://aws.amazon.com/about-aws/whats-new/2026/07/lambdadf-dotnet/)).
- **Temporal es la plataforma independiente de referencia.** Su sitio nombra a OpenAI, Replit, NVIDIA, Salesforce, Twilio y Snap entre sus usuarios ([FUENTE: temporal.io](https://temporal.io/)). El informe del repo la elige para "procesos largos con requisitos de auditoría" en una empresa grande (`[CÓDIGO docs/evolucion/informe-dotnet-empresarial.md:398]`) y documenta su patrón saga (`[CÓDIGO informe-dotnet-empresarial.md:364]`).
- **Los equipos .NET con bus resuelven procesos de días con la saga del bus** y sus timeouts (Wolverine `ScheduleAsync(timeout, 3.Days())`, [FUENTE: Message Bus](https://wolverinefx.net/guide/messaging/message-bus.html)). Es lo que ya hace el repo.
- **La auditoría de negocio queda en tu base.** Los historiales de las plataformas tienen retención (Temporal, Elsa, el *autopurge* de Durable Task, [FUENTE: Durable Task Scheduler](https://learn.microsoft.com/en-us/azure/durable-task/scheduler/durable-task-scheduler)) y se presentan como herramientas de depuración y operación.

### Mi recomendación: opción B (Temporal)

1. **Es la que enseña el concepto del paso.** El plan pide "cuándo conviene una plataforma de workflow en lugar de una saga casera" (`plan.md:457`). Con A el paso no tiene con qué comparar; con B, el mismo cierre queda con una saga (paso 27) y un workflow durable (paso 28), uno al lado del otro.
2. **Es la que el repo ya señaló** para este caso (`informe-dotnet-empresarial.md:398` y `:418`), y la que más se usa fuera de un proveedor de nube.
3. **Cumple el Verifica mejor que las otras.** 14 días acelerados sin tocar la configuración (time skipping), reanudación con el worker caído y un test de replay que protege a los workflows en curso.
4. **El costo es el que el paso quiere enseñar:** "plataforma extra que operar" (`informe-dotnet-empresarial.md:418`). Medirlo es parte del aprendizaje.
5. **Licencias limpias:** servidor, SDK y UI MIT, sin pago para self-hosted.

Si Tobi prefiere no sumar infraestructura, **A** es la segura: cumple el Verifica con lo que ya hay. C la recomiendo solo si el interés es un motor con diseñador visual.

## Pendientes para `progreso.md`

**Se abren:**
- Canal real para los recordatorios (correo, con un servidor SMTP de prueba en Compose): decisión de herramienta.
- Mostrar el historial del cierre en la pantalla de perfil.
- Limpieza de las filas de `users."AccountClosureHistory"` (como las de `AccountClosureSagas`).
- `AuditEntries` append-only con la misma regla `IAppendOnly`.
- Con B: archivo (*archival*) del historial de Temporal más allá de la retención; servicios de Temporal en procesos separados; versionado de workflows en un despliegue real (Worker Versioning o *patching*).
- Con B: medir la compatibilidad de Temporal con PostgreSQL 18 para usar una sola instancia de base.

## Verificado / No verificado

**Verificado (leído en el repo o en una fuente primaria):**
- Todo `[CÓDIGO]` en `a805053`: `AccountClosureSaga`, `AccountClosureOptions`, `AccountClosureOverdue`, `AccountClosureSteps`, `AccountClosureSagaTests` (test del plazo), `AccountClosureSagaConfiguration`, `DeleteUser`, `GetAccountClosure`, `UserProfile`, `AccountClosureStatus`, `UsersFeature`, `MessagingExtension`, `SagaOutbox`, `SagaStorage`, `OutboxRegistration`, `IsolatedApi`, `TemplateApp.Platform.csproj`, `TemplateApp.Tasks.csproj`, los Dockerfile, `compose.yaml`, `ci.yml`, `check-structure.mjs` (gate de bus, límite de despliegue, slices con test), `DuneDiagnostics.cs` (DUNE0016, 0017, 0020, 0022 a 0025), `PublicSurfaceAnalyzer`, el frontend de perfil (`closure-notice.ts`, `user-response.ts`, `last-closure.ts`), y los docs citados (`plan.md`, `progreso.md`, ADR-0004, ADR-0005, `architecture.md`, `conventions.md`, `informe-dotnet-empresarial.md`, `paso-27-saga-orquestada.md`).
- NuGet: `Temporalio` 1.22.0, `Elsa` 3.10.2, `Microsoft.DurableTask.Worker` 1.28.0, `Dapr.Workflow` 1.19.1, `WolverineFx` 6.64.2 y 7.0.0-alpha.3, `Restate.Sdk` 0.5.0 (comunidad), `Dbos.Transact` 0.0.0-alpha.0.47 (comunidad), con licencias y fechas.
- Temporal: docs de workflows, Workflow Definition (replay, determinismo, versionado), Events (historial y límites), .NET workflow basics (reglas de determinismo), message passing (señales), testing suite (time skipping, replayer), observability, Workflow Id (unicidad y políticas), Visibility (PostgreSQL 12+), Temporal Server (servicios, retención), Deployment (imagen `server`, admin-tools, no exponer); README del SDK y de Hosting; LICENSE del servidor; compose, `.env` y script de esquema de `samples-server`; releases del servidor y de ui-server.
- Elsa: README de elsa-core, docs (inicio, execution model, journal, timers, long-running, testing, database configuration, retention, Server + Studio), releases.
- Microsoft Learn (Durable Task SDKs, Durable Task Scheduler), Dapr (Workflow architecture), organizaciones de GitHub de Restate y DBOS, anuncio de AWS, docs de Wolverine (Message Bus; testing en `V6.64.2` y en `main`).

**No verificado (queda para el paso o para Tobi):**
- El texto oficial del RGPD en EUR-Lex no cargó; el art. 12.3 lo leí en gdpr-info.eu, que reproduce el reglamento.
- Las fechas de releases del servidor de Temporal y de ui-server se ven sin año en GitHub; las tomo como 2026 por el orden y por el `.env` de samples-server, que fija 1.32.2.
- Que Temporal funcione con PostgreSQL 18 (la doc dice "v12+" y el ejemplo usa 16).
- Que el SDK de Temporal acepte workflows y activities `internal` (DUNE0020).
- Que `WaitConditionAsync` con timeout devuelva `bool` en .NET: la página de señales no lo documenta; el README del paquete dice que esos timeouts usan timers de Temporal.
- Si el time-skipping server corre en Linux ARM.
- Si Elsa trae un mediator interno, cómo corre sus migraciones al arrancar, y la forma de sus tablas.
- La adopción real de Elsa en empresas (fila de "Para el objetivo del repo").
- Los analyzers de la comunidad para Temporal: solo los vi en la búsqueda, no los instalé.
- Que el relay de Users reintente un `IEventHandler` que falla por la plataforma caída sin marcar la fila como fallida antes de que vuelva (V7): depende de sus reintentos configurados.
- Ningún test corrió: todo lo de las secciones 2 a 6 es diseño.
