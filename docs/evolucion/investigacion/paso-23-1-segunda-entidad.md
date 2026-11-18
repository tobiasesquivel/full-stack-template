# Paso 23.1: segunda entidad y lectura con otra forma (investigación)

- Estado: **Listo** como investigación. Antes de escribir código, Tobi decide los puntos marcados "DECISIÓN" en la [Recomendación](#recomendación).
- Fecha: 2026-11-18.
- Alcance: el paso 23.1 del [plan](../plan.md) (`plan.md:402-406`). Concepto: CQRS del lado de escritura y de lectura. Una entidad con reglas que siempre se cumplen (invariantes) muestra por qué el comando carga y protege la entidad. Una lectura que junta datos de varias features muestra por qué la consulta no la carga. Cambia: feature nueva, migración, endpoints, frontend, `smoke-api.mjs`. Verifica: un test por invariante, un test que afirma que la lectura no carga la entidad, smoke y navegador.
- Base: rama `fase7/23-1-segunda-entidad` (desde `main`, con los pasos 22, 22.1 y 23). Nombres de este informe = nombres del código real.
- Cómo leer las marcas:
  - `[CÓDIGO archivo:línea]`: lo leí en el repo.
  - `[FUENTE]`: documentación primaria, con su link.
  - `[INFERENCIA]`: deducción mía. No la medí.
  - `[NO VERIFICADO]`: no encontré una fuente que lo confirme. Queda para una prueba antes de codear.
- Método: leí el Tasks completo, `Users/DeleteUser`, Auth (contratos, acceso, roles), `Infrastructure` (idempotencia, transacciones, contextos, auditoría, arranque), Dune (analyzers y `check-structure.mjs`), los tests de apoyo, el frontend de tareas y los informes de los pasos 20, 22 y 22.1. Las fuentes externas son Microsoft Learn (DDD y EF Core), Npgsql, PostgreSQL 18 y Fowler.
- Esta pasada no corrió código. Todo sale de leer. Lo que falta medir está en [Qué quedó sin comprobar](#qué-quedó-sin-comprobar).

## Respuesta corta

Sí. La entidad recomendada es `Projects`. Con ella:

1. **Entidad:** `Project` tiene dos estados (activo y archivado). Solo cambia con métodos del agregado. El nombre es único por dueño.
2. **Relación:** `TaskItem` suma una columna `ProjectId` nullable, sin FK (decisión del paso 22). Se asigna solo al crear la tarea.
3. **Lectura:** `GET /api/v1/projects` junta tres cosas. La proyección sale de `projects`. Los contadores vienen de `Tasks` por contrato, en un lote. El nombre del dueño viene de `Users` por contrato, en un lote. Ninguna lectura carga un `Project`.
4. **Migración:** un contexto nuevo (`ProjectsDbContext`, schema `projects`). Antes de sumarlo hay que corregir un error del arranque: la línea base del paso 22 marcaría como aplicada la migración de un contexto nuevo sobre una base vieja, y la tabla nunca se crearía. Ver sección 9.

## Conclusión

1. **Entidad: `Projects`.** Es la única de las tres candidatas que enseña las dos cosas del paso: transiciones de estado con métodos y una lectura que junta datos de dos features. La recomendación completa está en la sección 1.
2. **Invariantes: cinco.** Dos se protegen solo en la entidad (nombre obligatorio y archivado no cambia de nombre). Una se protege con índice único en la base y entidad (nombre único por dueño). Una se protege con el estado en la entidad y un `CHECK` opcional (transiciones). Una cruza features y no tiene guardián en la base (tarea solo en proyecto propio y activo). Un límite de cantidad máxima queda fuera del paso: ni la entidad ni un índice la protegen sin un bloqueo.
3. **Relación: `TaskItem.ProjectId`.** Es la relación más chica que hace que la lectura tenga sentido. Sin ella no hay contadores. Mover tareas entre proyectos queda para un paso propio.
4. **Lectura: composición por contratos.** Tres consultas en total para una página, en vez de una por fila. Eso evita N+1. La prueba de "no carga la entidad" es un `ChangeTracker` vacío más un conteo de SQL.
5. **Permisos: tres nuevos.** `Projects.Create` para `User`; `Projects.Read` y `Projects.Update` para `Admin`; los tres para `SuperAdmin`. El dueño tiene acceso por ser dueño, igual que en Tasks.
6. **HTTP: cuatro rutas y ninguna ETag.** La versión viaja en el cuerpo, como en Tasks (`decisions.tsv:145`).
7. **Frontend: una página de lista** con alta, cambio de nombre y archivar. Un componente de ruta y uno de presentación.
8. **Dune: no hace falta una regla nueva.** Hay dos listas fijas que cambian a mano: `Program.cs` y las pruebas de `PerFeatureContextsTransitionTests`.
9. **Migración: dos cambios de código antes del contexto nuevo.** Corregir la línea base y agregar `projects` al `Search Path`. Las tablas nuevas no necesitan dos releases, porque ningún release anterior las conoce.

## Glosario corto

- **Agregado (aggregate):** un grupo de objetos que se cambian juntos y se guardan juntos. Tiene una raíz (`Project`) que es la única puerta para cambiarlo.
- **Invariante:** una regla que el dato cumple siempre, en todo momento y en cada escritura.
- **Contrato (Contract):** la puerta que una feature expone a otra. Vive en `Features/<F>/Contracts/` (`backend/Features/Tasks/Contracts/ITasksModule.cs:3`).
- **Lote (batch):** una sola llamada que pide datos de muchos ids a la vez. Evita una llamada por fila (N+1).
- **Keyset:** paginar con la clave del último elemento, no con un número de página (`conventions.md:75`).
- **Línea base (baseline):** anotar en el historial de migraciones que una migración ya corrió, sin correrla (paso 22, sección 3).
- **Rowversion / xmin:** la columna de versión de PostgreSQL que EF usa para detectar cambios concurrentes (`decisions.tsv:144`).

---

## 1. Qué entidad y qué invariantes

### 1.1 Tres candidatas

| Opción | Qué gana | Qué pierde | Veredicto |
|---|---|---|---|
| **A. `Projects`** (la del plan): nombre único por dueño, estados activo y archivado | Transiciones de estado con métodos. Lectura con contadores de Tasks y nombre de Users: dos features, como pide el paso. | Toca `Tasks` (columna y contrato). Agrega dos ciclos de contratos: Tasks↔Projects y Users↔Projects. | **Recomendada** |
| B. `Tags` (etiqueta por dueño, nombre único, sin estados) | Es la forma más simple. | No hay transición que mostrar. La lectura junta una sola feature, así que no enseña la parte de "varias features". | Descartada: no cumple la mitad del paso |
| C. `Teams` (equipo con miembros) | Invariantes de membresía. Lectura con nombres de Users. | Necesita membresía compartida. Eso cambia `AccessPolicy` (hoy solo hay dueño y roles, `AccessPolicy.cs:37-47`). Es un paso de autorización, no de CQRS. | Descartada: alcance |

Por qué `Projects`: el paso pide que el comando "proteja" una entidad y que la lectura "junte" features. `Projects` tiene las dos cosas sin agregar un modelo de acceso nuevo. Ver [FUENTE: Microsoft Learn, Designing a microservice domain model](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model): "Domain entities must implement behavior in addition to implementing data attributes" y "The entity's methods take care of the invariants and rules of the entity".

### 1.2 Invariantes

Son cinco. Cada una tiene un guardián (el lugar que la hace cumplir):

| # | Invariante | Guardián | Qué pasa si se rompe | Test que lo prueba |
|---|---|---|---|---|
| I1 | Nombre obligatorio: 1 a 100 caracteres después de quitar espacios | Entidad (`Project.IsValidName`, como `TaskItem.IsValidTitle`, `TaskItem.cs:17-18`) y validador (`TaskTitleRules`) | 400 con `errors.Name`. Nada se guarda | `CreateProjectTests`, con nombre vacío y de 101 caracteres |
| I2 | Nombre único por dueño, sin distinguir mayúsculas | **Índice único** en `(OwnerId, NormalizedName)` más una comprobación en la entidad para dar un 409 claro | 409 `Project.NameTaken`. Con dos altas simultáneas, el índice deja pasar una sola | `CreateProjectTests`: duplicado (409) y carrera (una sola fila) |
| I3 | Las transiciones de estado solo se hacen con métodos (`Archive`, `Restore`) | **Entidad**: `State` tiene setter privado. Un `CHECK` en la base para el texto del estado (opcional) | No se puede cambiar el estado sin pasar por el método | `UpdateProjectTests`: archivar dos veces no cambia nada y no deja fila de auditoría |
| I4 | Un proyecto archivado no cambia de nombre | **Entidad** (`Project.Rename` devuelve `Archived`). El comando no guarda | 409 `Project.Archived`. La base no cambia ni aparece fila nueva en `AuditEntries` | `UpdateProjectTests`: renombrar archivado da 409 y la fila queda igual |
| I5 | Una tarea solo se crea en un proyecto propio y activo | **Contrato** `IProjectsModule.FindAssignableProjectAsync` (lo llama `CreateTask`). No hay FK por la decisión del paso 22 | 400 `errors.ProjectId` si no es propio o no existe. 409 `Task.ProjectArchived` si está archivado | `CreateTaskTests`: proyecto ajeno, inexistente y archivado. La tabla de tareas no cambia |

Qué necesita la base y qué no:

- **Solo entidad:** I1 y I4. No hace falta más.
- **Entidad más índice único:** I2. La entidad no ve las otras filas, así que dos altas simultáneas pasan la comprobación las dos. Solo el índice las frena. Esto es lo que dicen [FUENTE: EF Core, Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes): "Attempting to insert more than one entity with the same values for the index's column set will cause an exception to be thrown". La excepción llega de Npgsql con `SqlState` `23505`: [FUENTE: Npgsql, Exceptions](https://www.npgsql.org/doc/diagnostics/exceptions_notices.html) dice que `PostgresException` expone `SqlState`, "which contains the PostgreSQL error code". Que EF la envuelva en `DbUpdateException` es [NO VERIFICADO] (lo encontré solo en blogs). Se prueba con el test de carrera.
- **Entidad más `CHECK`:** I3. [FUENTE: EF Core, Indexes, Check constraints](https://learn.microsoft.com/en-us/ef/core/modeling/indexes): "Check constraints ... allows you to define a condition that must hold for all rows in a table". Es defensa en profundidad: la entidad ya impide el valor malo.
- **Ninguno de los dos:** I5. La base no puede mirar otra tabla sin FK, y la FK entre features quedó prohibida en el paso 22 (`architecture.md:51`). La regla vive en el contrato, y la carrera con un archivado simultáneo queda como pendiente (igual que la carrera de `DeleteUser` con un `POST`, `progreso.md:197`).
- **Límite máximo de proyectos activos por dueño:** no lo recomiendo. Una entidad no cuenta filas ajenas, y un índice no cuenta. Hace falta un bloqueo o un contador por dueño. Va a pendientes.

[INFERENCIA] Vernon dice lo mismo desde el lado del agregado (regla "modelar invariantes verdaderas dentro del límite de consistencia"). No pude leer su PDF: el extractor no devolvió el texto. La guía de Microsoft lo cita y dice de la raíz: "It is the aggregate's consistency guardian, considering all the invariants and consistency rules you might need to comply with in your aggregate" ([FUENTE: Microsoft Learn, Designing a microservice domain model, sección The Aggregate Root](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model)).

---

## 2. Cómo se ve un agregado rico en este repo

### 2.1 Diferencia con `TaskItem` hoy

`TaskItem` tiene setters públicos (`backend/Features/Tasks/Domain/TaskItem.cs:11-15`: `public required string Title { get; set; }`). El comando cambia los campos a mano (`UpdateTask.cs:49-51`). Es el caso que Fowler llama anémico: "little more than bags of getters and setters" ([FUENTE: Fowler, Anemic Domain Model](https://martinfowler.com/bliki/AnemicDomainModel.html)). Para `Project` el paso enseña lo contrario. No cambio `TaskItem` en este paso (regla 7).

### 2.2 Forma de `Project`

- `internal sealed class Project : IOwnedResource, IAuditable` en `Features/Projects/Domain/`. Los dos interfaces ya existen: `Shared/Domain/IOwnedResource.cs` e `IAuditable.cs`.
- Campos: `Id` (Guid), `OwnerId` (init), `Name` y `NormalizedName` (setter privado), `State` (`Active` o `Archived`, setter privado), `ArchivedAt`, `CreatedAt`, `UpdatedAt`, `Version` (`uint`, `IsRowVersion`).
- Creación: `Project.Create(ownerId, name, now)`, un método estático. Devuelve el objeto con estado `Active`.
- Cambios: `Rename(name)`, `Archive(now)`, `Restore(now)`. Cada uno devuelve un enum de dominio (`ProjectChange`): `Renamed`, `Unchanged`, `Archived`, `Restored`, `InvalidName`. Ver el punto siguiente.
- **Dominio no puede devolver `ErrorOr`.** `DUNE0001` permite solo BCL, `Shared.Domain`, el `Domain` propio, enums y delegados (`dune/Dune.Analyzers/DomainDependencyAnalyzer.cs:340-360`; mensaje en `DuneDiagnostics.cs:10`). Por eso el dominio devuelve un enum y la slice lo traduce a `ProjectErrors` (la misma forma que `TaskErrors`, `TaskErrors.cs:7-25`).
- EF lo mapea sin constructor público: un constructor privado y setters privados. [FUENTE: EF Core, Entity types with constructors](https://learn.microsoft.com/en-us/ef/core/modeling/constructors): "EF Core sees a property with a private setter as read-write, which means that all properties are mapped as before". También puede enlazar un constructor privado si sus parámetros se llaman como las propiedades.

### 2.3 Cómo corre el comando (cargar, cambiar, guardar)

Es la misma forma que `UpdateTask` (`backend/Features/Tasks/UpdateTask/UpdateTask.cs:28-66`):

1. Valida el cuerpo (FluentValidation, `UpdateTask.cs:30-32`).
2. Carga la entidad rastreada por `Id` solo (`UpdateTask.cs:34`). DUNE0007 prohíbe combinar `Id` y `OwnerId` en el filtro (`OwnerPredicateAnalyzer.cs:72-77`).
3. Si no existe, 404 (`UpdateTask.cs:35-36`). Después decide propietario o permiso con `CanAccessAsync` y devuelve 403 (`UpdateTask.cs:38-39`). Esto sigue la regla "primero 404, después 403" (`architecture.md:71`).
4. Compara la versión (`UpdateTask.cs:42`). Si no coincide, responde 409 salvo que el estado pedido ya esté guardado (`decisions.tsv:147`).
5. Llama al método del dominio (`Rename` o `Archive`). Si devuelve `Unchanged`, no guarda (no hay fila de auditoría).
6. Fija la versión original (`_db.Entry(item).Property(t => t.Version).OriginalValue`, `UpdateTask.cs:52`) y guarda.
7. Si `DbUpdateConcurrencyException`, 409 `Project.Conflict` (`UpdateTask.cs:58-60`). Si el índice único falla (I2), 409 `Project.NameTaken`.

Un punto nuevo: la transacción es una sola, la de `SaveChanges`. Si el cambio se rechaza antes de guardar (I4), no queda nada a medias. [INFERENCIA por el código de `UpdateTask`; no leí la doc de transacciones de EF.]

### 2.4 Lo que el repo ya da gratis

- **Auditoría (paso 10):** `Project` implementa `IAuditable`. `AuditInterceptor` agrega una fila por cambio (`AuditInterceptor.cs:47-70`). Guarda solo los nombres de las propiedades cambiadas (`AuditInterceptor.cs:89-96`). El nombre del proyecto es texto libre, así que nunca se guarda su valor (`CONTEXT.md`, "Datos personales").
- **Idempotencia (paso 9):** `POST /projects` encadena `WithIdempotency<CreateProjectRequest>()`. DUNE0016 lo exige (`IdempotentPostAnalyzer.cs:12`: solo `Auth` está excluido). La clave y el proyecto se guardan en la misma transacción (`IdempotencyFilter.cs:45-80`, `decisions.tsv:151`). Un reintento con la misma clave devuelve el mismo proyecto, no un 409.
- **Concurrencia (paso 8):** `xmin` como `Version`, con `IsRowVersion` (`TaskItemConfiguration.cs:28-29`). [FUENTE: Npgsql, Concurrency tokens](https://www.npgsql.org/efcore/modeling/concurrency.html), citado en el informe del paso 20.

---

## 3. La relación entre `Tasks` y `Projects`

### 3.1 Hace falta una columna en `Tasks`

Sin ella no hay contadores. Hay tres formas de tener la relación:

| Opción | Qué gana | Qué pierde | Veredicto |
|---|---|---|---|
| **A. `TaskItem.ProjectId` (Guid?), sin FK** | Una columna. Los contadores son un `GROUP BY`. La relación vive donde está el dato. | Toca `Tasks`: columna, índice, contrato y `TaskResponse`. | **Recomendada** |
| B. Tabla de pertenencia en `projects` (`ProjectTasks(ProjectId, TaskId)`) | `Tasks` no cambia. | Borrar una tarea tiene que avisar a `Projects` (otra escritura cruzada). Los completados viven en `Tasks`, así que cada conteo sigue pidiendo datos a `Tasks`. Dos fuentes de verdad. | Descartada |
| C. Sin relación | Nada. | No hay contadores: la lectura del paso queda vacía. | Descartada |

Ver [FUENTE: Microsoft Learn, Designing a microservice domain model](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model): "disallow direct navigation between aggregates and only having the foreign key (FK) field". Un `Guid` sin navegación es lo que pide la guía, y la relación entre features ya se decidió así (`architecture.md:51`).

### 3.2 Qué toca `Tasks`

- `TaskItem`: `public Guid? ProjectId { get; set; }`. Sin navegación. Un `Guid?` porque las tareas viejas no tienen proyecto.
- `TaskItemConfiguration.cs`: el índice `IX_TaskItems_ProjectId` (ver sección 9).
- `CreateTaskRequest`: `ProjectId` opcional. `CreateTask` lo valida después de los permisos: si viene, llama `IProjectsModule.FindAssignableProjectAsync(callerId, projectId)`.
- `TaskResponse`: agrega `ProjectId`. **Decisión menor** (ver Recomendación, punto 4): sin él, el dato que se escribe no se puede leer de vuelta. Es un campo nuevo y no rompe clientes.
- `TaskResponses.Projection` (`TaskResponses.cs:8-9`) es el único lugar donde se mapea. Se agrega ahí, y `GetTask` lo hereda por `ComposeRead` (`GetTask.cs:38-43`).
- `PUT /tasks/{id}` no cambia: no mueve tareas entre proyectos. Mover queda para un paso propio (regla 7).

### 3.3 Quién valida que el proyecto existe y no está archivado

`CreateTask` pide el proyecto por contrato. El contrato nuevo de `Projects` devuelve un dato, no un error del catálogo de otra feature:

- `FindAssignableProjectAsync(string callerId, Guid projectId, CancellationToken ct)` devuelve `ProjectAssignmentInfo?` (`Id` e `IsArchived`).
- Si el proyecto no existe o no es del `callerId`, devuelve `null`. Así no se filtra si existe un proyecto ajeno.
- `CreateTask` decide: `null` → 400 `errors.ProjectId` (el mismo código para "no existe" y "es ajeno"); `IsArchived` → 409 `Task.ProjectArchived`.
- Por qué los errores los decide `Tasks`: `DUNE0014` pide que cada `Error` salga de un `*Errors.cs` de su propia feature (`conventions.md:79`). El dato de `Projects` no trae un `Error`.

Dos detalles de Dune:

- `DUNE0015` cuenta un tipo de `Contracts` como usado solo si otra feature **lo nombra** (`ContractsAnalyzer.cs:34`: `RecordUse` mira `IdentifierName`). Entonces `CreateTask` tiene que escribir `ProjectAssignmentInfo? project = ...`, no `var project = ...`. [INFERENCIA por lectura del analyzer; no lo probé.]
- Dentro del `TransactionScope` del filtro de idempotencia, las conexiones se abren de a una (`conventions.md:93`). Una consulta de `Projects` tiene que terminar antes de que `Tasks` guarde. Con `await` secuencial se cumple. [INFERENCIA]

### 3.4 Qué pasa al archivar, borrar o cambiar de dueño

- **Archivar:** las tareas se quedan con su `ProjectId`. Los contadores siguen mostrándolas. No se pueden crear tareas nuevas en ese proyecto (I5). Restaurar lo vuelve a permitir.
- **Borrar un proyecto:** no hay endpoint de borrado en 23.1. Archivar es la forma de sacarlo de la vista. Si algún día se borra, hay que decidir qué pasa con sus tareas: quedarían con un `ProjectId` que no existe (la base no lo impide). Va a pendientes, con su propio paso.
- **Borrar la cuenta (`DeleteUser`):** la cuenta deja de existir, y sus proyectos quedarían sin dueño. `DeleteUser` hoy borra tareas por contrato, claves y perfil, todo en `TransactionScope` (`DeleteUser.cs:28-51`). La regla del repo es que lo que hacía una cascada lo hace el código (`conventions.md:90`). **Decisión:** agregar `IProjectsModule.DeleteOwnedProjectsAsync(ownerId)` en `DeleteUser`, entre las tareas y el perfil. Eso hace a Users depender de Projects, un ciclo más (ver sección 8).

---

## 4. La lectura: el proyecto con contadores y nombre del dueño

### 4.1 Cómo se compone sin cargar la entidad

Patrón de la lectura de `GetTask` (`GetTask.cs:15`, `GetTask.cs:28`, `GetTask.cs:38-43`):

1. **Proyección del proyecto** a un modelo privado `OwnedProject(OwnerId, ProjectRow)`, que implementa `IOwnedResource`. Una sola consulta sobre `Projects`, sin rastreo. [FUENTE: EF Core, Tracking and custom projections](https://learn.microsoft.com/en-us/ef/core/querying/tracking#tracking-and-custom-projections), citada en `paso-20-lectura-dedicada.md` sección 2.1: "If the result set doesn't contain any entity types, then no tracking is done". Eso no lo volví a leer en esta pasada.
2. **Contadores por lote:** `ITasksModule.CountByProjectAsync(IReadOnlyCollection<Guid> projectIds)`. Devuelve `TaskCountsSummary(ProjectId, Total, Completed)`. Dentro, una consulta: `WHERE "ProjectId" IN (...)` agrupada por proyecto.
3. **Nombres por lote:** `IUsersModule.FindNamesAsync(IReadOnlyCollection<string> authIds)`. Devuelve `UserNameSummary(AuthId, DisplayName)`. Una consulta sobre `UserProfiles`.
4. Se arma `ProjectResponse` con los tres datos. La autorización (404 y después 403) se decide sobre el modelo de lectura, igual que en `GetTask`.

[INFERENCIA] Un `Select` con `TaskResponses.From(t)` dentro arrastra la entidad entera y la rastrea. Lo midió el paso 20 (`paso-20-lectura-dedicada.md:37-38`): la trampa es exactamente esa.

### 4.2 Por qué no un JOIN en SQL

Un JOIN entre `projects` y `tasks."TaskItems"` sería SQL escrito en `Projects` contra tablas de `Tasks`. Dune no lo ve (`conventions.md:92`, "nadie ve SQL crudo contra la tabla de otra feature") y rompe la regla de que una feature solo habla con otra por `Contracts`. Por eso van dos consultas y un contrato.

### 4.3 Lista y N+1

- `GET /projects` (lista propia): la página sale de `projects` con keyset (`CreatedAt DESC, Id DESC`, como `ListTasks.cs:32-38`). Después, una llamada a Tasks con los ids de la página y una a Users con los dueños distintos. Son 3 consultas por página, sin importar cuántos proyectos haya.
- En la lista propia, todos los proyectos tienen el mismo dueño. Entonces la llamada a Users es de un solo id. El lote importa para el contador, no para el nombre (sección 4.6).
- Probar el lote: `CapturedCommands.Reading("TaskItems")` debe dar una sola consulta para una página de 5 proyectos (`CapturedCommands.cs:15-16`). Esa regex ya acepta un schema delante (`(\w+\.)?"TaskItems"`), así que `tasks."TaskItems"` cuenta. Eso resuelve el riesgo que marcó el paso 22.1 (`paso-22-1-schema-por-feature.md:166`).
- [INFERENCIA] EF traduce `Contains` sobre un array a `= ANY(@p)` en Npgsql. Hay que mirar el SQL capturado en el test.

### 4.4 Cómo probar que la lectura no carga la entidad

Dos afirmaciones, como en el paso 20 (`paso-20-lectura-dedicada.md:160-165`):

1. `db.ChangeTracker.Entries<Project>()` vacío después de llamar al caso de uso. El test resuelve `GetProject` y `ProjectsDbContext` de un scope propio (como `ListTasksPlanTests.cs:34-38`).
2. Una sola consulta a `projects` y una a cada contrato.

Eso separa esta lectura de la trampa (sección 4.1, punto 1): da el mismo JSON, pero rastrea.

### 4.5 Forma de la respuesta

`ProjectResponse`: `id`, `name`, `isArchived`, `createdAt`, `updatedAt`, `version`, `ownerName`, `taskCount`, `completedTaskCount`. La vive en la raíz de la feature (no en `Contracts/`, porque solo la usa HTTP: `conventions.md:14`). Los contadores van juntos en la misma respuesta; no hay un `GET /projects/{id}/counters` aparte.

### 4.6 Qué cambia con 23.2

Cuando `Tasks` salga a su proceso (paso 23.2), `CountByProjectAsync` se vuelve HTTP. Una lista de proyectos dependerá de que `Tasks` responda. Por eso los contratos son lotes y tipos simples. Es una consecuencia que el paso 23.2 tiene que anotar (no hay plan de degradación en este paso).

---

## 5. Permisos

- **Dónde:** `Features/Projects/Contracts/ProjectPermissions.cs`, una clase estática con constantes (`DUNE0015` la acepta: `ContractsAnalyzer.cs:116-119`). Valores `Permissions.Projects.Create`, `.Read`, `.Update`. Mismo estilo que `TaskPermissions.cs:5-8`.
- **Cuáles:** tres. No hay `Delete` porque no hay endpoint de borrado (no crear permisos sin uso).
- **Cómo se siembran:** `RolePermissions.GetForRole` (`RolePermissions.cs:10-32`):
  - `User`: `Projects.Create`.
  - `Admin`: `Projects.Read`, `Projects.Update`.
  - `SuperAdmin`: los tres.
- `RoleSeeder` corre en cada arranque y agrega los claims que falten (`RoleSeeder.cs:41-61`). No hace falta una migración. Lo que no hace es quitar un claim: si se borra un permiso del mapa, el claim queda en la base (pendiente, igual que hoy).
- **Dueño:** `AccessPolicy.CanAccessAsync` devuelve verdadero si el `callerId` es el `OwnerId` (`AccessPolicy.cs:43-44`). No necesita permiso.
- **Efecto visible en HTTP:** `GET /users/me` devuelve la lista de permisos (`UserResponse.cs:11-12`). Para `User` la lista pasa de una a dos entradas. `TestAccounts.cs:48-49` afirma la lista exacta y hay que cambiarla en el mismo PR (regla 3).

---

## 6. Contrato HTTP

Todas bajo `/api/v1/projects`, con `RequireAuthorization` (como `TasksFeature.cs:24`). La versión queda en v1: agregar rutas no rompe a nadie (`decisions.tsv:139`).

| Método y ruta | Qué hace | Éxito | Errores (código) |
|---|---|---|---|
| `POST /projects` | Crea un proyecto con `{ name }`. Idempotente (`WithIdempotency`) | 201 + `Location` con `LinkGenerator` y nombre de ruta (como `CreateTaskEndpoint.cs:21`) | 400 `Name`; 409 `Project.NameTaken` |
| `GET /projects?cursor&pageSize` | Lista propia con keyset. Body `{ items, nextCursor }` | 200 | 400 `Cursor`, `PageSize` |
| `GET /projects/{id}` | Detalle con contadores y nombre del dueño. 404 y después 403 | 200 | 404 `Project.NotFound`; 403 `Project.Forbidden` |
| `PUT /projects/{id}` | Cambia `{ name, isArchived, version }`. Una sola escritura | 200 con el `ProjectResponse` | 400 `Version` (falta); 409 `Project.Conflict` (versión vieja); 409 `Project.Archived` (renombrar archivado) |

**Decisión: `PUT` con `isArchived` (recomendado) o dos rutas (`POST .../archive` y `POST .../restore`).**

- Con `PUT` y `isArchived`: una ruta, el mismo patrón que `PUT /tasks` (`isCompleted`, `decisions.tsv:144-147`). Pierde que la transición no se ve en la ruta. La entidad sigue siendo la que decide (`Archive` o `Restore`).
- Con dos rutas `POST`: la transición se ve en la URL y cada una lleva su propia clave de idempotencia (DUNE0016 la exige). Pierde: dos endpoints más y un patrón distinto al de Tasks.

Recomiendo `PUT` con `isArchived`: es lo que ya hace Tasks y no abre un segundo estilo en el repo.

Otras decisiones del contrato:

- **Versión:** en el cuerpo, no `ETag` (`decisions.tsv:145`, decisión tomada en el paso 8). El pedido de la investigación mencionaba `If-Match`; el repo no lo usa, y no lo recomiendo.
- **Estado repetido:** si la versión vieja pide el mismo nombre y el mismo estado ya guardados, responde 200 sin escribir (`decisions.tsv:147`).
- **Sin total de filas:** la lista no devuelve `totalCount` (`decisions.tsv:140`).
- **Cursor:** `TaskCursor` (`ListTasks/TaskCursor.cs:6-37`) es interno de `ListTasks`. Para no copiarlo, se mueve a `Shared/Http/` como cursor genérico (`CreatedAt` y `Id`). `InvalidCursor` y `InvalidPageSize` también (`TaskErrors.cs:19-25`). Son cambios de Tasks que este paso necesita.
- **Errores:** `ProjectErrors.cs` con prefijo `Project.` (`conventions.md:79`). Errores de campo con el nombre del campo como código (`Name`, `Version`, `Cursor`, `PageSize`, `ProjectId`).
- **Sin borrado:** no hay `DELETE` en 23.1 (sección 3.4).

---

## 7. Frontend y pruebas de punta a punta

Forma mínima, según `architecture.md:118-121` y `informe-componentes-angular.md` (sección 6):

```text
frontend/src/app/features/projects/
├── index.ts                 (exporta projectsRoutes)
├── projects.routes.ts       (path 'projects', loadComponent)
├── api/
│   ├── projects-api.ts      ProjectsApi (como TasksApi)
│   ├── project-response.ts
│   ├── project-list-response.ts
│   ├── create-project-request.ts
│   └── update-project-request.ts
├── projects/                 Projects: componente de ruta. Lista, cursor, alta con Signal Forms
└── project-row/              ProjectRow: presentación. Recibe `project`, emite `renamed` y `archiveChanged`
```

- **Componente de ruta** (`projects/`): inyecta `ProjectsApi`, guarda la página y la pila de cursores, y hace el alta con `form()` de `@angular/forms/signals` (FRONT006 prohíbe `@angular/forms`, `conventions.md:49`).
- **Componente de presentación** (`project-row/`): entradas con `input()` y salidas con `output()`. No inyecta servicios de la app (FRONT013, `conventions.md:37`). El borrador del nombre es un `linkedSignal`, como en `TaskRow` (`conventions.md:39`).
- **Paginación:** Tasks tiene `TaskPager`, que vive en `features/tasks`. Una feature no importa de otra (FRONT001: solo `index.ts`). La opción es copiarlo en `features/projects` (regla de Tasks ya lo tiene) o moverlo a `core/`. Recomiendo copiarlo en 23.1 y anotar el pendiente de moverlo a `core/`. Esto no es una regla de backend: en el frontend la copia es aceptable mientras sea presentación.
- **Ruta:** `app.routes.ts:9` agrega `projectsRoutes` a los hijos con `authGuard`. `app.html:16-17` suma el enlace "Mis proyectos".
- **Pruebas unitarias:** `project-row.spec.ts`, con `fixture.componentRef.setInput()` y `subscribe()` (`conventions.md:155`).
- **Pruebas e2e:** `frontend/e2e/projects.spec.ts`, con `getByRole` y `getByLabel` (`conventions.md:156`). Corre una sola vez al cerrar el paso, con `NOT_RUN` mientras tanto. Como el paso cambia el frontend y el contrato, la e2e sí corre al cierre.
- **Smoke:** `.agents/skills/verify-template/scripts/smoke-api.mjs` suma un flujo: crear proyecto, crear tarea con `projectId`, leer contadores, archivar, probar que el renombre da 409. Lo leí solo por su ruta; no leí su contenido, así que su estructura queda [NO VERIFICADO].

---

## 8. Dune y listas fijas

**Reglas de Dune:** ninguna regla nueva. Lo que ya cubren:

- `DUNE0004` y `DUNE0003`: Projects habla con Tasks y Users solo por `Contracts`.
- `DUNE0014`: `ProjectErrors.cs` con prefijo `Project.`.
- `DUNE0015`: cada método y tipo de `Contracts` lo usa otra feature. Por eso `FindAssignableProjectAsync` lo usa Tasks, `DeleteOwnedProjectsAsync` lo usa Users, `FindNamesAsync` lo usa Projects y `CountByProjectAsync` lo usa Projects. Las constantes de `ProjectPermissions` no se revisan (el analyzer no mira campos).
- `DUNE0016`: `POST /projects` con `WithIdempotency`.
- `DUNE0017`: `Project` es `IAuditable`, así que no se usa `ExecuteDelete` ni `ExecuteUpdate` sobre él.
- `DUNE0018` y `DUNE0019`: sección 9 y sección 4.
- `DUNE0021`: `ProjectsDbContext` va en `Features/Projects/Persistence/`, uno por feature, y aplica sus propias configuraciones. Lo exige el analyzer, sin cambios.
- `DUNE0001`: el `Domain` no usa `ErrorOr` (sección 2.2).
- `DUNE0008`: `ProjectsModule` es la implementación de `IProjectsModule` en la raíz (la única forma de `*Module` permitida, `conventions.md:31`).

**Listas fijas que hay que tocar a mano** (no las lee ningún gate):

| Dónde | Qué dice | Qué hacer |
|---|---|---|
| `backend/Program.cs:18-21` y `42-46` | Lista explícita de `Add...Feature` y `Map...Endpoints` | Agregar `AddProjectsFeature` y `MapProjectsEndpoints` |
| `backend/Infrastructure/Persistence/DatabaseSearchPath.cs:7` | `"public,auth,users,tasks,platform"` | Agregar `projects` |
| `backend/Tests/Persistence/PerFeatureContextsTransitionTests.cs:17-18` | Lista de contextos con su `_Move...` | Agregar el contexto nuevo (sin `_Move`, porque es tabla nueva) |
| `backend/Tests/Persistence/PerFeatureContextsTransitionTests.cs:21-33` | Lista de tablas en su schema | Agregar `projects."Projects"` y `tasks.TaskItems` sigue igual |
| `backend/Tests/Persistence/DatabaseSearchPathTests.cs` | Probablemente compara la cadena exacta [NO VERIFICADO] | Actualizar si compara el texto |
| `frontend/src/app/app.routes.ts:3-9` y `app.html:16-17` | Rutas y enlaces | Agregar la feature |
| `dune/check-structure.mjs:64-104` | Detecta features por carpeta y exige `<Slice>Endpoint.cs` y `<Slice>Tests.cs` | Sin cambio, pero cada slice nueva necesita su test |

Dos búsquedas que conviene repetir antes de cerrar: `grep -rn "TaskItems\|UserProfiles" backend` (para ver qué SQL escrito a mano nombra tablas) y `grep -rn "\"tasks\"\|\"users\"" dune` (no hay nombres de feature en Dune fuera de `IdempotentPostAnalyzer.cs:12`).

**Ciclos de contratos.** `ADR-0004` punto 4 documenta dos direcciones para Auth y Users (`ADR-0004:23`). Aquí aparecen dos ciclos más: Tasks↔Projects y Users↔Projects. Dune no los prohíbe, pero el ADR no los menciona. Recomiendo anotarlos en `decisions.tsv` (regla 9 para cambios de docs).

---

## 9. Migraciones, esquema y el arranque

### 9.1 El contexto nuevo

- `ProjectsDbContext` en `Features/Projects/Persistence/`, `internal`, con `public const string Schema = "projects"` (la forma de `TasksDbContext.cs:9`).
- Registro: `services.AddMigratedDbContext<ProjectsDbContext>(ProjectsDbContext.Schema)` en `ProjectsFeature.cs` (`ContextRegistration.cs:12-22`).
- `OnModelCreating`: `HasDefaultSchema`, `ApplyConfiguration` de su configuración, y `MapSharedAuditEntries()` porque `Project` es `IAuditable` (`SharedAuditEntries.cs:7-13`; `conventions.md:91`).
- Primera migración: `dotnet ef migrations add InitialCreate --context ProjectsDbContext --output-dir Features/Projects/Persistence/Migrations` (`conventions.md:96`).
- Tabla `Projects`: `Id` (uuid), `OwnerId` (`text`, 450), `Name` (`text`, 100), `NormalizedName` (`text`, 100), `State` (`text`), `ArchivedAt`, `CreatedAt`, `UpdatedAt`, `Version` (`xmin`).
- Índices: único `(OwnerId, NormalizedName)` (I2), con [FUENTE: EF Core, Indexes, Index uniqueness](https://learn.microsoft.com/en-us/ef/core/modeling/indexes). Keyset `(OwnerId, CreatedAt, Id)`, igual que `IX_TaskItems_OwnerId_CreatedAt_Id` (`decisions.tsv:141`, `conventions.md:76`).
- `CHECK` de `State` (I3), con [FUENTE: EF Core, Indexes, Check constraints](https://learn.microsoft.com/en-us/ef/core/modeling/indexes).
- Un índice único sin `NULL`: `NormalizedName` nunca es nulo, así que el `NULL` distinto de PostgreSQL no aplica ([FUENTE: PostgreSQL 18, CREATE INDEX, "UNIQUE and NULL values"](https://www.postgresql.org/docs/18/sql-createindex.html)).

### 9.2 Search Path

- Una tabla nueva en un schema nuevo **no** necesita dos releases. El ritual del paso 22.1 (`conventions.md:120`) es para mover tablas que ya existen. Ningún release anterior nombra `projects`.
- Aun así, `projects` tiene que estar en `DatabaseSearchPath.Schemas` (`DatabaseSearchPath.cs:7`) para que las consultas del contexto nuevo sin calificar encuentren el schema. [INFERENCIA]. El `HasDefaultSchema` ya califica el nombre, así que en la práctica el path importa solo para SQL escrito a mano. Lo agrego por consistencia (`conventions.md:89`: "Toda conexión de un contexto lleva Search Path...").

### 9.3 Cambio en `Tasks`: `ProjectId`

- Migración `AddTaskProjectId` en `Features/Tasks/Persistence/Migrations/`:
  - `AddColumn ProjectId uuid nullable` en `TaskItems`.
  - `CreateIndex IX_TaskItems_ProjectId` sobre `ProjectId`.
- **Expand-safe:** [INFERENCIA] el release anterior (paso 23) lee y escribe columnas por nombre. Una columna nueva nullable no lo rompe. El ensayo con binarios lo tiene que medir (como EX-ROLL del paso 22.1, `paso-22-1-schema-por-feature.md:107`).
- **DUNE0018:** `AddColumn` está permitido. `CreateIndex` sobre una tabla que no crea la misma migración exige `IsCreatedConcurrently()` (`DuneDiagnostics.cs:218-222`, `MigrationSafetyAnalyzer.cs:187`). Ahí hay una incógnita:
  - PostgreSQL rechaza `CREATE INDEX CONCURRENTLY` dentro de un bloque de transacción: [FUENTE: PostgreSQL 18, CREATE INDEX](https://www.postgresql.org/docs/18/sql-createindex.html).
  - Npgsql documenta el flag `IsCreatedConcurrently` ([FUENTE: Npgsql, Indexes](https://npgsql.org/efcore/modeling/indexes.html)), pero la página no dice si la migración sale de la transacción. [NO VERIFICADO]
  - Si el flag no basta, la salida conocida es `migrationBuilder.Sql(..., suppressTransaction: true)`. Lo vi en un resumen de búsqueda de Milan Jovanović, no lo leí; [NO VERIFICADO].
  - **Prueba antes del código:** una migración sobre una base con filas y con el flag. Si falla con "cannot run inside a transaction block", se usa la salida.
- **Down:** `DropIndex` y `DropColumn`. DUNE0018 revisa solo el `Up` de migraciones nuevas y el `Down` de las `Move*` (`conventions.md:123`), así que no hay conflicto.
- **Backfill:** no hace falta. `NULL` significa "sin proyecto".

### 9.4 El error de la línea base (encontrado al leer, no medido)

`DatabaseInitializer` hace esto cuando la base es heredada (`public."__EFMigrationsHistory"` existe):

1. Corre lo que falte del contexto viejo (`DatabaseInitializer.cs:29`).
2. Para **cada** contexto migrado, llama a `BaselineAsync` (`DatabaseInitializer.cs:30-31`).
3. `BaselineAsync` crea el historial del contexto. Si está vacío, inserta como aplicada la primera migración (`DatabaseInitializer.cs:61-62`).

El punto 2 es el problema. `contexts` sale de `MigratedContexts()`, que suma **todos** los contextos registrados (`ContextRegistration.cs:34-37`). Entonces, con `ProjectsDbContext` nuevo:

- En una base heredada, el historial de `projects` está vacío.
- `BaselineAsync` marca su `InitialCreate` como aplicada.
- `MigrateAsync` no la corre, porque ya figura aplicada.
- **La tabla `projects."Projects"` nunca se crea.**
- `PendingMigrationsHealthCheck` cuenta cero pendientes (`PendingMigrationsHealthCheck.cs:18-24`). Readiness sale en 200 con las tablas faltantes. Es el caso que `conventions.md:124` dice que readiness no detecta.

[INFERENCIA por lectura del código.] Esto no pasa en una base nueva ni en una creada con el paso 22 (EX-LIMPIA del paso 22 no crea `__EFMigrationsHistory`). Pasa en cualquier base anterior al paso 22, que sigue existiendo en un entorno real o en un volumen viejo.

**Corrección propuesta:** la línea base solo para los contextos del paso 22.
- `MigratedContext(Type ContextType, bool LegacyBaseline)`.
- `AddMigratedDbContext<TContext>(schema, legacyBaseline: true)` en Auth, Users, Tasks y Platform. Es un argumento por registro, dentro de cada feature. `Infrastructure` no nombra features (`DUNE0003`, `conventions.md:23`).
- `BaselineAsync` corre solo para los contextos con `LegacyBaseline = true`.
- `ProjectsDbContext` registra con `legacyBaseline: false`.

**Prueba roja primero:** una base heredada (la del binario anterior al paso 22, como en `PerFeatureContextsTransitionTests`) más el contexto nuevo. Afirmar que `projects."Projects"` existe y que `POST /projects` da 201. Con el código de hoy, la prueba falla con la tabla faltante.

Esto también choca con la descripción de `architecture.md:49`: "anota la `InitialCreate` de cada contexto nuevo como aplicada". Es una frase escrita con el comportamiento que hoy está mal. Ver Recomendación, choques.

### 9.5 Expand/contract del paso en conjunto

- Tablas nuevas: un release. El release anterior no las conoce ni las necesita.
- Columna nueva en `TaskItems`: un release. Es aditiva.
- Ningún borrado, ningún rename. DUNE0018 no tiene excepciones que pedir.
- Volver atrás: el binario viejo ignora `projects` y la columna `ProjectId`. Eso se mide con el ensayo de binarios, como en el paso 22.1 (`rehearse-migrations.mjs`).

---

## Choques con los docs

Rigen la regla 10 de `AGENTS.md`. No los resuelvo: los dejo para Tobi, con la recomendación.

1. **"CQRS completo" contra "CQRS liviano".** El plan dice "CQRS completo del lado de escritura y de lectura" (`plan.md:403`). `architecture.md:37` define el repo como "CQRS liviano: misma base, dos modelos", y el informe del paso 20 lo define igual (`paso-20-lectura-dedicada.md:23`). Una lectura con base y modelo propios (lo "completo") es el paso 24.1 (`plan.md:422`), con eventos. Recomiendo que 23.1 sea liviano y que el nombre del plan se corrija. Si Tobi quiere lo completo en 23.1, choca con la regla 8 (no adelantar pasos).
2. **La línea base y los contextos nuevos.** `architecture.md:49` describe la línea base para "cada contexto nuevo", y eso es lo que rompe la sección 9.4. Hay que cambiar la frase junto con el código.
3. **El alcance del plan.** `plan.md:405` dice "feature nueva con Domain/Contracts/Persistence/Tests, migración, endpoints, frontend, `smoke-api.mjs`". Falta decir que cambian `Tasks` (columna, contrato, `TaskResponse`) y `Users` (un contrato nuevo y `DeleteUser`). Lo que hace falta sigue siendo el mismo paso, pero el "Cambia" del plan está incompleto.
4. **ADR-0004 punto 4.** Documenta dos direcciones entre Auth y Users (`ADR-0004:23`). Aparecen dos ciclos más (sección 8). El ADR no los menciona.
5. **`architecture.md:65` y `:57`.** Describen que el borrado de cuenta no deja filas de tareas (`architecture.md:65`) y que los contextos son uno (`architecture.md:57`). Ya cambiaron con el paso 22 y con esta propuesta se repiten.
6. **ETag.** El pedido de la investigación menciona ETag e If-Match del paso 8. No es un choque: el repo decidió el cuerpo con `version` (`decisions.tsv:145`). Lo dejo anotado porque la pregunta lo trae.
7. **Término nuevo.** `CONTEXT.md` no tiene "proyecto". Hace falta agregarlo (regla de `domain-modeling`).

---

## Qué quedó sin comprobar

- **No corrí código.** Ni la suite, ni los tests de la nueva feature, ni el ensayo con binarios. Todo sale de leer.
- **Migración con `IsCreatedConcurrently` dentro de transacción** (sección 9.3): no sé si EF/Npgsql la saca de la transacción. Prueba antes de codear.
- **Excepción de unicidad:** que EF envuelva el `23505` en `DbUpdateException` (sección 1.2). Prueba de carrera.
- **Línea base** (sección 9.4): el error se ve en el código, no lo reproduje. Prueba roja primero.
- **Vernon:** no pude leer sus PDF. Las reglas de agregado se citan por la guía de Microsoft que las enlaza.
- **`Contains` a `= ANY`** y el `GROUP BY` con `FILTER` de conteo: mirar el SQL capturado.
- **Smoke y e2e:** no leí el contenido de `smoke-api.mjs` ni los tests de `DatabaseSearchPathTests`.
- **Tiempos:** no hay medición. El índice de `ProjectId` se justifica por el patrón del paso 18 (medir), no por un número.
- **Carrera de archivar con crear tarea (I5):** queda sin guardián en la base. Pendiente.

## Fuentes

- Microsoft Learn, [Designing a microservice domain model](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model): entidades con comportamiento, agregado raíz, navegación entre agregados solo por FK.
- Martin Fowler, [The Anemic Domain Model](https://martinfowler.com/bliki/AnemicDomainModel.html): "little more than bags of getters and setters".
- Microsoft Learn, [Entity types with constructors (EF Core)](https://learn.microsoft.com/en-us/ef/core/modeling/constructors): constructores privados, setters privados.
- Microsoft Learn, [Indexes (EF Core)](https://learn.microsoft.com/en-us/ef/core/modeling/indexes): índices únicos y check constraints.
- Microsoft Learn, [Tracking vs. no-tracking, custom projections (EF Core)](https://learn.microsoft.com/en-us/ef/core/querying/tracking#tracking-and-custom-projections): sin entidades, sin rastreo.
- Npgsql, [Exceptions and notices](https://www.npgsql.org/doc/diagnostics/exceptions_notices.html): `PostgresException.SqlState`.
- Npgsql, [Indexes (EF Core)](https://npgsql.org/efcore/modeling/indexes.html): `IsCreatedConcurrently`, sin detalle de transacción.
- PostgreSQL 18, [CREATE INDEX](https://www.postgresql.org/docs/18/sql-createindex.html): `CONCURRENTLY` no va en transacción; `UNIQUE` y los `NULL`.
- Vaughn Vernon, [Effective Aggregate Design, Part I](https://dddcommunity.org/wp-content/uploads/files/pdf_articles/Vernon_2011_1.pdf) y [Part II](https://dddcommunity.org/wp-content/uploads/files/pdf_articles/Vernon_2011_2.pdf): citados por la guía de Microsoft; **no leídos** (el PDF no devolvió texto).
- Concurrencia con `xmin`: [Npgsql, Concurrency tokens](https://www.npgsql.org/efcore/modeling/concurrency.html) (ya usada en el informe del paso 20).
- Repo: `docs/evolucion/investigacion/paso-20-lectura-dedicada.md`, `paso-22-dbcontext-por-feature.md`, `paso-22-1-schema-por-feature.md`; `docs/evolucion/informe-componentes-angular.md` (sección 6); `docs/evolucion/informe-vsa-deep-modules.md`.

---

## Recomendación

**Estado:** listo para decidir. No hay herramienta ni librería nueva (regla 5): todo es EF Core, Npgsql, PostgreSQL, FluentValidation, ErrorOr y Dune, que el repo ya usa.

### Decisiones para Tobi

1. **DECISIÓN: nombre del paso.** Recomiendo llamarlo "CQRS liviano" (misma base, dos modelos), como dice `architecture.md:37`. El "completo" del plan es el paso 24.1 (choque 1).
2. **DECISIÓN: `TaskResponse` gana `projectId`.** Recomiendo sí: un dato que se escribe en `POST /tasks` tiene que poder leerse en `GET /tasks/{id}`. Es un campo nullable y aditivo.
3. **DECISIÓN: `DeleteUser` borra los proyectos.** Recomiendo sí, en la misma transacción que las tareas. Sin eso, la cuenta borrada deja proyectos sin dueño. Agrega un ciclo Users↔Projects (sección 8).
4. **DECISIÓN: `PUT` con `isArchived` contra dos rutas `POST`.** Recomiendo `PUT` (sección 6).
5. **DECISIÓN: índice `IX_TaskItems_ProjectId` ahora o después de medir.** Recomiendo ahora, con la migración concurrente probada antes (sección 9.3). Si la prueba falla con transacción, el paso lo dice y se usa `suppressTransaction`.

### Entidad y reglas

- **Entidad:** `Project` (`Features/Projects/Domain/`), con estados `Active` y `Archived`, métodos `Create`, `Rename`, `Archive`, `Restore`, y enum `ProjectChange` (sección 2.2).
- **Invariantes:** I1 a I5 de la sección 1.2. El límite máximo de proyectos queda pendiente.
- **Guardianes en la base:** índice único `(OwnerId, NormalizedName)` (I2) y `CHECK` de `State` (I3).

### Relación

- `TaskItem.ProjectId` (`Guid?`), sin FK y sin navegación. Índice `IX_TaskItems_ProjectId`.
- `POST /tasks` acepta `projectId` opcional. Lo valida `IProjectsModule.FindAssignableProjectAsync`.
- Mover tareas, borrar proyectos y archivar en cadena quedan para pendientes.

### Contratos nuevos (lotes, tipos simples)

- `IProjectsModule`: `FindAssignableProjectAsync(callerId, projectId)` → `ProjectAssignmentInfo?` (lo usa Tasks); `DeleteOwnedProjectsAsync(ownerId)` (lo usa Users).
- `ITasksModule`: `CountByProjectAsync(IReadOnlyCollection<Guid>)` → `IReadOnlyList<TaskCountsSummary>` (lo usa Projects).
- `IUsersModule`: `FindNamesAsync(IReadOnlyCollection<string>)` → `IReadOnlyList<UserNameSummary>` (lo usa Projects).

### Lectura

- `GET /projects/{id}`: proyección a `OwnedProject` (sin entidad), luego `CountByProjectAsync([id])` y `FindNamesAsync([ownerId])`. Orden 404 y después 403.
- `GET /projects`: keyset igual que Tasks, luego una llamada de cada contrato por página.
- Forma: `ProjectResponse` con `id`, `name`, `isArchived`, `createdAt`, `updatedAt`, `version`, `ownerName`, `taskCount`, `completedTaskCount`.

### Endpoints

| Método y ruta | Notas |
|---|---|
| `POST /api/v1/projects` | `WithIdempotency<CreateProjectRequest>()`, 201 con `Location` |
| `GET /api/v1/projects?cursor&pageSize` | Keyset `(CreatedAt DESC, Id DESC)`, `{ items, nextCursor }` |
| `GET /api/v1/projects/{id}` | 404 y después 403 |
| `PUT /api/v1/projects/{id}` | `{ name, isArchived, version }`, `version` obligatorio |

### Pruebas, una por invariante

| Invariante | Caso que debe fallar sin cambiar la base | Qué se afirma |
|---|---|---|
| I1 | Nombre vacío o de 101 caracteres | 400 `errors.Name`; sin fila nueva |
| I2 | Mismo nombre que otro proyecto (cambiando mayúsculas) | 409 `Project.NameTaken`; una sola fila |
| I2 (carrera) | Dos altas simultáneas con el mismo nombre | Una 201, una 409 |
| I3 | Archivar un archivado | Sin cambio; sin fila nueva en `AuditEntries` |
| I4 | Renombrar un archivado | 409 `Project.Archived`; el nombre no cambia |
| I5 | Tarea en proyecto ajeno, inexistente o archivado | 400 `errors.ProjectId` (dos casos), 409 `Task.ProjectArchived`; `TaskItems` sin cambio |
| Lectura | Página de 5 proyectos | `ChangeTracker` sin `Project`; una consulta a `TaskItems` y una a `UserProfiles` |
| Versión | `PUT` con versión vieja | 409 `Project.Conflict`; con mismo estado, 200 sin escribir |
| Idempotencia | `POST` repetido con la misma clave | Mismo id, `Idempotent-Replayed: true` |
| Migración | Base heredada más `ProjectsDbContext` (prueba roja primero) | `projects."Projects"` existe; `POST` da 201 |
| Permisos | `User` crea; `Admin` lee y actualiza otro dueño; `User` no lee ajeno | 201, 200, 403 |

### Frontend

- `features/projects/` con `projects/` (ruta), `project-row/` (presentación), `api/` y `index.ts`.
- Enlace "Mis proyectos" en `app.html`, ruta en `app.routes.ts` con `authGuard`.
- Vitest para `project-row`. Playwright para `projects.spec.ts` (una corrida al cierre).
- Smoke: un flujo nuevo en `smoke-api.mjs`.

### Cambios que no son de código nuevo

- `Program.cs`, `DatabaseSearchPath.Schemas`, `PerFeatureContextsTransitionTests`, `TestAccounts.cs:48-49`, `CONTEXT.md` (término "Proyecto"), `decisions.tsv` (filas nuevas), `architecture.md:37`, `:49`, `:57`, `:65` (choques 1, 2, 5), y `ADR-0004` o una línea en `decisions.tsv` por los ciclos de contratos.

### Choques con los docs (para Tobi)

1. **"CQRS completo"** del plan (`plan.md:403`) contra "CQRS liviano" (`architecture.md:37`). Recomiendo liviano.
2. **Línea base para "cada contexto nuevo"** (`architecture.md:49`): describe el comportamiento que rompe un contexto nuevo sobre una base heredada (sección 9.4).
3. **Alcance del plan** (`plan.md:405`): no dice que cambian Tasks y Users.
4. **Ciclos de contratos** no mencionados en ADR-0004 (sección 8).
5. **ETag** pedido en la investigación contra `decisions.tsv:145`. El repo usa el cuerpo. No es choque, pero lo dejo visible.

Necesito que decidas: los cinco puntos de "Decisiones para Tobi", y si el paso sigue como CQRS liviano o se amplía a lo completo (que sería el paso 24.1).
