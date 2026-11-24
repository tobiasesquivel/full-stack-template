# Paso 20: lectura dedicada por módulo, CQRS liviano (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 20 del [plan](../plan.md) (`plan.md:365-368`). Concepto: separar lectura de escritura sin tocar invariantes. Cambia: contratos de lectura y operación en `Tasks`, tests. Verifica: la lectura nueva devuelve exactamente el mismo resultado observable que la anterior; test comparativo antes de borrar la vieja.
- Cómo está hoy:
  - `GetTask` carga la entidad de escritura entera y rastreada: `_db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct)` (`backend/Features/Tasks/GetTask/GetTask.cs:22`). Si no existe, 404 (`GetTask.cs:23-24`). Después decide propietario o permiso con `_auth.CanAccessAsync(callerId, item, TaskPermissions.Read, ct)` (`GetTask.cs:26`), que recibe un `IOwnedResource` (`backend/Features/Auth/Contracts/IAuthModule.cs:10`, `backend/Shared/Domain/IOwnedResource.cs`). Si falla, 403 (`GetTask.cs:27`). Al final arma la respuesta con `TaskResponses.From(item)` (`GetTask.cs:29`).
  - `ListTasks` ya es una lectura dedicada: filtra por dueño, pagina por keyset y proyecta con `.Select(TaskResponses.Projection).AsNoTracking()` (`backend/Features/Tasks/ListTasks/ListTasks.cs:32-38`). No carga `TaskItem`.
  - `TaskResponses` guarda el único mapeo `TaskItem → TaskResponse`: la expresión `Projection` (`backend/Features/Tasks/TaskResponses.cs:8-9`), con la regla `IsCompleted = CompletedAt != null` que dejó el paso 19, y su versión compilada `From` (`TaskResponses.cs:11-13`), que usan `CreateTask.cs:48` y `UpdateTask.cs:44,66`.
  - `Version` es `xmin` de PostgreSQL: `builder.Property(t => t.Version).IsRowVersion()` (`backend/Features/Tasks/Persistence/TaskItemConfiguration.cs:28-29`; `docs/decisions.tsv:144`). La versión viaja en el cuerpo, no en un `ETag` (`docs/decisions.tsv:145`).
  - Las escrituras (`CreateTask`, `UpdateTask`, `DeleteTask`) cargan la entidad rastreada y guardan con `SaveChangesAsync` (`UpdateTask.cs:34,56`, `DeleteTask.cs:22,30`). `AppDbContext` es uno solo para todas las features hasta el paso 22 (`docs/architecture.md:43`).
  - Users tiene el mismo patrón en `GetUser`: carga `UserProfile` con `AsNoTracking` (`backend/Features/Users/GetUser/GetUser.cs:23-25`). El plan acota el paso a `Tasks`.
- Decisiones ya tomadas (este informe no las reabre): ADR-0004 (slices como clases propias, sin mediator ni repositorios); EF Core 10 con Npgsql; un solo `AppDbContext` hasta el paso 22; `xmin` como versión en el cuerpo (`decisions.tsv:144-145`); "primero 404, después 403" (`docs/architecture.md:69`, `docs/conventions.md:72`) y DUNE0007, que prohíbe combinar `Id` y `OwnerId` en el predicado de una carga individual; xUnit v3 con Testcontainers y PostgreSQL real.
- Método: solo fuentes primarias. Son Martin Fowler, la guía de patterns & practices de Microsoft con las citas de Greg Young y Udi Dahan, Microsoft Learn (guía de microservicios .NET, Azure Architecture Center y la documentación de EF Core), el código de `dotnet/efcore` en `release/10.0`, Jimmy Bogard, el repo de GitHub Scientist y Michael Feathers. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA]. Lo que salió de una corrida mía va marcado [EXPERIMENTO].
- Evidencia propia (todo fuera del repo; el repo quedó sin cambios, `git status` vacío):
  - **EX-READ:** una copia de `backend/` en `C:\Users\Tobi\AppData\Local\Temp\claude\1f8c0f6d41e142c0\22f1f201-fa8b-480e-b46d-f29bebcd75db\scratchpad\exp`. Le agregué un test descartable que corre la clase real `GetTask` y cinco variantes de lectura nueva contra el mismo PostgreSQL 18 de Testcontainers. Usa el `CapturedCommands` del repo (un interceptor de EF que guarda el SQL) y cuenta lo que queda en el `ChangeTracker` (el registro de entidades rastreadas). Cinco casos: tarea propia abierta, propia completada y renombrada, ajena (403), ajena leída por un Admin (200) e Id inexistente (404).
  - **EX-PROBE:** en la misma copia, con `Directory.Build.props` y `dune/`, una slice de prueba con la forma recomendada (sección 2.4). Compila con los analyzers de Dune sin errores y devuelve lo mismo que `GetTask`.
  - Testcontainers borró sus contenedores al terminar.

## Glosario corto

- **CQS** (Command Query Separation, separación de comandos y consultas): cada método o cambia el estado (comando) o devuelve datos (consulta), nunca las dos cosas ([Fowler, CommandQuerySeparation](https://martinfowler.com/bliki/CommandQuerySeparation.html)).
- **CQRS** (Command Query Responsibility Segregation): usar un modelo para escribir y otro para leer ([Fowler, CQRS](https://martinfowler.com/bliki/CQRS.html)).
- **CQRS liviano** (o "simplificado"): dos modelos lógicos sobre la misma base. Sin base de lectura aparte, sin eventos y sin bus.
- **Modelo de escritura:** la entidad que cuida las invariantes y se guarda con `SaveChanges`. Acá, `TaskItem`.
- **Modelo de lectura:** un tipo hecho para responder una consulta, sin reglas de negocio. En la guía de Microsoft se llama DTO o ViewModel.
- **Proyección:** un `Select` que le pide a la base solo las columnas que hacen falta y arma el modelo de lectura directo.
- **Tracking** (rastreo): EF guarda cada entidad que lee en el `ChangeTracker` y le saca una foto (snapshot) para detectar cambios al guardar.
- **Invariante:** una regla que el dato siempre cumple (por ejemplo, "una tarea completada tiene `CompletedAt`").
- **Test de caracterización:** un test que fija lo que el código hace hoy, para saber si un cambio lo altera ([Feathers](https://michaelfeathers.silvrback.com/characterization-testing)).
- **Control y candidato:** en un experimento de reemplazo, el control es el código viejo y el candidato el nuevo. Se corren los dos y se comparan ([GitHub Scientist](https://github.com/github/scientist)).

## Conclusión

- **El paso ya está hecho a medias, y es por ADR-0004.** Con vertical slices, cada pedido es una clase propia: Bogard dice que eso "gives me CQRS out of the gate" ([Bogard, VSA](https://www.jimmybogard.com/vertical-slice-architecture/)). `ListTasks` ya proyecta. Lo único que todavía lee con el modelo de escritura es `GetTask`.
- **Qué cambia:** `GetTask` deja de cargar `TaskItem` rastreada. Proyecta a un modelo de lectura privado de la slice, con `OwnerId` y la `TaskResponse` (sección 2.4). La autorización se decide sobre ese modelo, que implementa `IOwnedResource`. El filtro sigue siendo solo por `Id`: 404 y 403 quedan iguales (sección 3).
- **Qué no cambia:** el contrato HTTP. `TaskResponse` y el JSON quedan idénticos, la versión sale de `xmin` igual que antes y no hay `ETag` que dependa de la entidad (sección 4). Por la regla 3 no hace falta tocar el frontend; la e2e queda `NOT_RUN`.
- **Medido** [EXPERIMENTO]: hoy `GetTask` manda una consulta con las 7 columnas y deja 1 `TaskItem` rastreada. Con la proyección manda una consulta, calcula `"CompletedAt" IS NOT NULL` en la base y no rastrea nada. En los cinco casos el resultado es igual. La ganancia en tiempo para una fila es despreciable; el valor del paso es de diseño, no de rendimiento.
- **Trampa medida** [EXPERIMENTO]: escribir `.Select(t => new X(t.OwnerId, TaskResponses.From(t)))` parece una proyección, pero EF trae la entidad entera y la rastrea. El resultado es idéntico, así que un test que solo compara respuestas no lo detecta. El test tiene que mirar también el `ChangeTracker` (sección 5).
- **Sin herramientas nuevas.** EF Core alcanza. Dapper (lo que usa eShopOnContainers), Scientist.NET y LINQKit son librerías: si Tobi quisiera alguna, decide él (regla 5). No las recomiendo.
- **Gate:** recomiendo una regla nueva de Dune, porque el paso agrega una convención ("una lectura no escribe ni carga la entidad de escritura") y la regla 4 pide hacerla ejecutable. `GetUser` la viola hoy: queda en una lista de excepciones, con un pendiente (sección 6). Es una decisión para Tobi.

## 1. Qué es "CQRS liviano" y cuándo no conviene

**Respuesta:** dos modelos (uno para escribir, otro para leer) sobre la misma base. Sin base aparte, sin event sourcing y sin mensajería.

**La definición**

- Greg Young: "CQRS is simply the creation of two objects where there was previously only one" (citado en [Microsoft patterns & practices, Reference 2: Introducing CQRS](https://learn.microsoft.com/en-us/previous-versions/msp-n-p/jj591573(v=pandp.10))).
- Fowler: "you can use a different model to update information than the model you use to read information" ([Fowler, CQRS](https://martinfowler.com/bliki/CQRS.html)). Los dos modelos pueden compartir la base; entonces "the database acts as the communication between the two models" ([Fowler, CQRS](https://martinfowler.com/bliki/CQRS.html)).
- Bogard lo une con las slices: los pedidos se parten en "command" y "query" (POST/PUT/DELETE contra GET), y una arquitectura de vertical slices "gives me CQRS out of the gate" ([Bogard, VSA](https://www.jimmybogard.com/vertical-slice-architecture/)).

**Lo "liviano": misma base, sin eventos**

- La guía .NET de Microsoft usa "the simplest CQRS approach, which consists of just separating the queries from the commands". Cada capa tiene "its own data model (note that we say model, not necessarily a different database)". Event sourcing "is not used in this guide" ([Microsoft Learn, Applying simplified CQRS and DDD patterns](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/apply-simplified-microservice-cqrs-ddd-patterns)).
- El microservicio de pedidos de eShopOnContainers "uses a single data source or database, but two logical models" ([Microsoft Learn, Applying simplified CQRS](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/apply-simplified-microservice-cqrs-ddd-patterns)).
- El Azure Architecture Center llama a esto "the foundational level of CQRS, where both the read and write models share a single underlying database" ([Azure Architecture Center, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)).
- patterns & practices: aplicar CQRS "does not mandate that you split the data store" ([Reference 2](https://learn.microsoft.com/en-us/previous-versions/msp-n-p/jj591573(v=pandp.10))). Young: el Service Bus y lo demás, "in actuality, none of that is necessary" ([Reference 2](https://learn.microsoft.com/en-us/previous-versions/msp-n-p/jj591573(v=pandp.10))).

**Qué hace cada lado**

- Escritura: "includes validation and domain logic, and helps ensure data consistency". Lectura: "focuses on generating DTOs or projections that are optimized for the presentation layer" ([Azure Architecture Center, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)).
- "Queries never alter data. Instead, they return data transfer objects (DTOs) ... without any domain logic" ([Azure Architecture Center, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)).
- Las consultas "are idempotent and can be segregated from the domain rules", así que no hace falta pasar por agregados ni repositorios ([Microsoft Learn, Implement reads/queries in a CQRS microservice](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads)).
- En este repo, "sin tocar invariantes" del plan quiere decir esto: las invariantes de `TaskItem` (título normalizado, `CompletedAt`, versión) viven en el camino de escritura. La lectura no las toca porque no escribe [INFERENCIA].

**Cuándo NO conviene**

- Fowler: "for most systems CQRS adds risky complexity", "Many systems do fit a CRUD mental model, and so should be done in that style" y "CQRS should only be used on specific portions of a system" ([Fowler, CQRS](https://martinfowler.com/bliki/CQRS.html)).
- Azure: "might not be suitable when ... The domain or the business rules are simple" o "A simple CRUD-style user interface and data access operations are sufficient" ([Azure Architecture Center, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)).
- patterns & practices: "In most systems, the majority of bounded contexts will probably not benefit from using the CQRS pattern". Young: "Separating the read and write models can be quite costly" ([Reference 2](https://learn.microsoft.com/en-us/previous-versions/msp-n-p/jj591573(v=pandp.10))).
- El costo de estas advertencias está en la versión completa: base aparte, sincronización por eventos y consistencia eventual ([Azure Architecture Center, CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)). La versión liviana sobre la misma base no tiene consistencia eventual: la lectura ve lo último que se confirmó [INFERENCIA]. Su costo es un tipo más por lectura.
- `Tasks` es CRUD simple. Por las fuentes de arriba, en un producto real no se justificaría por necesidad. Acá se justifica porque el repo es un laboratorio y el paso enseña la separación (ver "Choques con los docs del repo") [INFERENCIA].

## 2. Cómo implementar el lado de lectura en EF Core 10, dentro de este repo

### 2.1 Proyección frente a entidad rastreada

- Una consulta que devuelve entidades rastrea por defecto. Rastrear cuesta: EF mantiene un diccionario de instancias (identity resolution) y "snapshots that instance and keeps the snapshot internally" ([EF Core, Efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#tracking-no-tracking-and-identity-resolution)).
- Una proyección pide solo lo necesario: "This can be optimized by using `Select` to tell EF which columns to project out". La misma página aclara que eso "is very useful for read-only queries" ([EF Core, Efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#project-only-properties-you-need)).
- Una proyección sin entidades no rastrea: "If the result set doesn't contain any entity types, then no tracking is done" ([EF Core, Tracking vs. No-Tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking#tracking-and-custom-projections)).
- Pero si el `Select` llama a un método de C# con la entidad, EF la materializa y la rastrea: "If EF Core materializes an entity instance for client evaluation, it will be tracked" ([EF Core, Tracking vs. No-Tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking#tracking-and-custom-projections)). Lo medí: es la variante V2 de la tabla de 2.3 [EXPERIMENTO].

### 2.2 `AsNoTracking`, `AsNoTrackingWithIdentityResolution` y `QueryTrackingBehavior`

- `AsNoTracking`: "No-tracking queries are useful when the results are used in a read-only scenario. They're generally quicker to execute" ([EF Core, Tracking vs. No-Tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking#no-tracking-queries)). El benchmark oficial da 0,71 del tiempo con 10 blogs y 200 posts ([EF Core, Efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#tracking-no-tracking-and-identity-resolution)).
- `AsNoTrackingWithIdentityResolution`: no rastrea, pero usa "a stand-alone change tracker" para que una fila repetida sea una sola instancia ([EF Core, Tracking vs. No-Tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking#identity-resolution)). Sirve cuando el resultado repite entidades por joins. `GetTask` devuelve una fila sin relaciones, así que no aplica [INFERENCIA].
- `QueryTrackingBehavior` a nivel de contexto: `UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)` hace que todas las consultas sean sin rastreo, y cada consulta que escribe tendría que pedir `AsTracking` ([EF Core, Tracking vs. No-Tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking#configuring-the-default-tracking-behavior); el enum en [dotnet/efcore, QueryTrackingBehavior.cs](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/QueryTrackingBehavior.cs) dice "You should not disable change tracking if you want to manipulate entity instances and persist those changes"). En este repo, `UpdateTask` y `DeleteTask` dependen del rastreo (`UpdateTask.cs:49-56`). Cambiar el valor por defecto de `AppDbContext` toca todas las escrituras y todas las features: fuera del alcance (regla 7) [INFERENCIA].
- Dato de paso: en `ListTasks.cs:37`, `.AsNoTracking()` va después de una proyección a `TaskResponse`, que no es entidad. Por la regla citada arriba, no cambia nada [INFERENCIA, a partir de la documentación; no lo medí]. No recomiendo tocarlo en este paso.

### 2.3 Variantes medidas [EXPERIMENTO]

Las cinco variantes nuevas comparadas con el `GetTask` de hoy, sobre los cinco casos (propia abierta, propia completada y renombrada, ajena, ajena como Admin, inexistente). En todos los casos y todas las variantes, el resultado (`ErrorOr<TaskResponse>`: valor o tipo y código de error) fue igual al de hoy.

| Variante | Consultas a `TaskItems` | Columnas que trae | `TaskItem` rastreadas después |
|---|---|---|---|
| Hoy: `SingleOrDefaultAsync` sobre la entidad | 1 | las 7 (`Id, CompletedAt, CreatedAt, OwnerId, Title, UpdatedAt, xmin`) | 1 |
| V1: `Select(t => new OwnedTask(t.OwnerId, new TaskResponse(...)))` escrito a mano | 1 | `OwnerId, Id, Title, "CompletedAt" IS NOT NULL, CreatedAt, UpdatedAt, xmin` | 0 |
| V2: `Select(t => new OwnedTask(t.OwnerId, TaskResponses.From(t)))` | 1 | las 7 | **1** (la trampa) |
| V3: `Select(expresión armada con TaskResponses.Projection.Body + OwnerId)` | 1 | igual que V1 | 0 |
| V4: `AsNoTracking().SingleOrDefaultAsync` + `TaskResponses.From` | 1 | las 7 | 0 |
| V5: V1 como consulta compilada (`EF.CompileAsyncQuery`) | 1 | igual que V1 | 0 |

- El SQL de V1, V3 y V5 es el mismo: `SELECT t."OwnerId", t."Id", t."Title", t."CompletedAt" IS NOT NULL, t."CreatedAt", t."UpdatedAt", t.xmin FROM "TaskItems" AS t WHERE t."Id" = @id LIMIT 2`.
- En los casos de ajena y Admin, el `ChangeTracker` tiene además 1 o 2 entradas que no son `TaskItem`. Vienen de Identity: `AccessPolicy` lee los roles con `RoleManager` (`backend/Features/Auth/Access/AccessPolicy.cs:62-66`). No es de este paso. Por eso el test tiene que contar solo `Entries<TaskItem>()`.

### 2.4 Dónde vive la lectura: contexto aparte, fachada o el `AppDbContext` de siempre

| Opción | Qué es | Contra en este repo |
|---|---|---|
| A. Proyección en la slice, con `AppDbContext` (recomendada) | `GetTask` sigue inyectando `AppDbContext` y proyecta a su modelo de lectura | Ninguna fuerte. El modelo de lectura es un tipo más |
| B. `DbContext` de solo lectura (`TasksReadDbContext` con `NoTracking`) | Un segundo contexto con el mismo modelo | Adelanta el paso 22, que separa los contextos por feature (`docs/architecture.md:43`; regla 8). Duplica la configuración del modelo y el registro en DI |
| C. Fachada `IQueryable` o repositorio de lectura (`ITaskReader`) | Una interfaz entre la slice y EF | `informe-vsa-deep-modules.md:175`: "Sin pass-through: el slice usa el `DbContext` de su feature directamente". Bogard: "we don't need any kind of 'shared' layer abstractions like repositories, services" ([Bogard, VSA](https://www.jimmybogard.com/vertical-slice-architecture/)). ADR-0004 también prohíbe una interface por slice (condiciones, punto 3) |
| D. Dapper o SQL a mano (lo que hace eShopOnContainers) | El lado de lectura con un micro ORM | Librería nueva (regla 5). Microsoft dice que también vale "any micro ORM, Entity Framework Core, or even plain ADO.NET" ([Microsoft Learn, Implement reads/queries](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads)). EF ya genera el SQL justo (2.3) |
| E. Consulta compilada (`EF.CompileAsyncQuery`) | La consulta como delegado estático | EF ya cachea cada consulta por su forma; la búsqueda en caché "is negligible in the majority of EF applications" ([EF Core, Advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries)). Sus parámetros tienen que ser escalares (misma página, "Limitations"). Agrega forma sin ganancia medible para una fila [INFERENCIA] |

**El modelo de lectura y el mapeo.** Hay dos formas limpias de armar la proyección (V1 y V3 de 2.3):

- **V3, recomendada:** `GetTask` arma una vez, en un campo `static readonly`, la expresión `t => new OwnedTask(t.OwnerId, <cuerpo de TaskResponses.Projection>)`. Son unas seis líneas con `System.Linq.Expressions`. El mapeo a `TaskResponse`, con la regla `IsCompleted = CompletedAt != null`, sigue en un solo lugar (`TaskResponses.cs:9`). Así se cumple `docs/conventions.md:19` ("Una regla no se copia entre slices") y `conventions.md:18` (los mapeos compartidos viven en la raíz de la feature). El paso 19 cambió justo esa regla en ese lugar; con V1 habría que acordarse de cambiarla en dos.
- **V1, alternativa:** el `Select` escribe `new TaskResponse(t.Id, t.Title, t.CompletedAt != null, ...)` a mano dentro de `GetTask`. Es LINQ común, más fácil de leer. Copia la regla de completitud en la slice.
- Microsoft avisa: "Avoid constructing queries with the expression tree API unless you really need to", porque es fácil meter una constante y recompilar la consulta cada vez ([EF Core, Advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#dynamically-constructed-queries)). En V3 la expresión se arma una sola vez, con el parámetro de `Projection` y sin constantes. El SQL salió idéntico al de V1 [EXPERIMENTO].
- **Dónde va `OwnedTask`:** como record privado anidado en `GetTask` (`private sealed record OwnedTask(string OwnerId, TaskResponse Response) : IOwnedResource`). Lo usa una sola slice (`docs/conventions.md:13`), y DUNE0010 acepta un tipo auxiliar "anidado y privado" (`docs/conventions.md:12`). No puede ser `file`: un tipo `file` no puede aparecer en la firma de un miembro de un tipo que no es `file`, y el campo `static readonly` lo nombra: el compilador da `CS9051` [EXPERIMENTO]. El nombre no termina en `Response`, porque `Response` queda para HTTP (`docs/conventions.md:16`). La forma completa compiló con los analyzers de Dune y devolvió lo mismo que `GetTask` (EX-PROBE) [EXPERIMENTO].

**Recomendación:** opción A con V3 y `OwnedTask` privado anidado. Una sola consulta, sin rastreo, sin tipos públicos nuevos y sin dependencias nuevas.

## 3. Autorización en el lado de lectura (404 y 403)

**Respuesta:** proyectar `OwnerId` junto con la respuesta, filtrar solo por `Id` y decidir después, sobre el modelo de lectura. Así 404 y 403 quedan iguales.

| Opción | Qué pasa con 404 y 403 | Veredicto |
|---|---|---|
| Filtrar por dueño en la consulta (`t.Id == id && t.OwnerId == caller`) | Una tarea ajena da 404 en vez de 403, y el Admin no la ve | Rompe `docs/architecture.md:69` y `conventions.md:72`. DUNE0007 lo frena: marca un predicado que ve `Id` y `OwnerId` a la vez (`dune/Dune.Analyzers/OwnerPredicateAnalyzer.cs:72-77`) |
| Dos consultas: una por `OwnerId`, otra por la respuesta | Iguales, pero dos idas a la base, y entre una y otra la fila puede cambiar o borrarse | Peor que hoy [INFERENCIA] |
| Proyectar un modelo con `OwnerId` que implementa `IOwnedResource` (recomendada) | Iguales: sin fila, 404; con fila, `CanAccessAsync` decide igual que hoy | Medido: mismo resultado en los cinco casos [EXPERIMENTO] |

- `AccessPolicy.CanAccessAsync` solo lee `resource.OwnerId` (`backend/Features/Auth/Access/AccessPolicy.cs:43`). No necesita la entidad.
- El predicado del `Where` sigue viendo solo `Id`. DUNE0007 mira el predicado de `Where`, `SingleOrDefault` y parecidos, no el `Select` (`OwnerPredicateAnalyzer.cs:42-70`), así que la proyección de `OwnerId` no lo dispara. Compiló sin diagnósticos en EX-PROBE [EXPERIMENTO].
- `GetTaskTests` ya cubre 200 propio, 404, 403 ajeno, 200 como Admin y 403 después de quitar el rol (`backend/Features/Tasks/GetTask/GetTaskTests.cs:17-70`). Esos tests tienen que seguir verdes sin cambios: son el test de caracterización del contrato HTTP.
- Fuentes: el único ejemplo de la guía de Microsoft es una lista que filtra por usuario (`GetOrdersFromUserAsync` en el controller, [Microsoft Learn, Implement reads/queries](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads)). Eso equivale a `ListTasks`, no a una lectura por Id. No encontré una fuente primaria que diga dónde va el chequeo de dueño en una lectura individual con CQRS. Lo que manda acá es la regla del repo: primero 404, después 403.

## 4. ¿Qué son los "contratos de lectura"? ¿Cambia `TaskResponse`?

**Respuesta:** no cambia el contrato HTTP. Lo que cambia es el contrato interno de la lectura: `GetTask` devuelve lo mismo, pero lo arma desde un modelo de lectura propio.

- El "Verifica" del plan pide "exactamente el mismo resultado observable" (`plan.md:368`). Si el JSON cambiara, no sería el mismo resultado. Entonces "contratos de lectura" no puede ser el contrato HTTP [INFERENCIA].
- Cuando se escribió el plan (2026-10-15, commit `d09fb92`), `Tasks` tenía un `TasksModule` parcial con las cinco operaciones, un constructor compartido y `TaskResponse` en `Contracts/` (`git show d09fb92:backend/Features/Tasks/TasksModule.cs`). "Contratos de lectura y operación" apuntaba a separar la parte de lectura de ese módulo. ADR-0004 (2026-10-16) ya partió el módulo en slices y sacó `TaskResponse` de `Contracts/` (ADR-0004, cambio del 2026-11-03). Lo que queda de ese objetivo es el modelo de lectura de `GetTask` [INFERENCIA]. Ver "Choques con los docs del repo".
- La versión no depende de la entidad rastreada: `Version` está mapeada a `xmin` ([Npgsql, Concurrency tokens](https://www.npgsql.org/efcore/modeling/concurrency.html)), y la proyección la lee como columna (`t.xmin` en el SQL de 2.3) [EXPERIMENTO]. No hay `ETag`: la versión viaja en el cuerpo (`docs/decisions.tsv:145`). Ningún header de `GetTaskEndpoint` sale de la entidad (`GetTaskEndpoint.cs:17-18`).
- Con la regla 3 de `AGENTS.md`: como el contrato HTTP no cambia, no hace falta tocar el frontend. La e2e queda `NOT_RUN` y el cierre lo dice (`AGENTS.md`, sección "Verificación").

## 5. El test comparativo antes de borrar la vieja

### 5.1 Cómo lo hacen los profesionales

- **Experimento en paralelo (Scientist):** "A Ruby library for carefully refactoring critical paths". El código viejo es el control (`use`), el nuevo es el candidato (`try`). `run` "will always return whatever the `use` block returns", compara los dos y publica las diferencias ([github/scientist](https://github.com/github/scientist)). Hay un port para .NET, [Scientist.NET](https://github.com/scientistproject/Scientist.net). Aviso del propio README: "Scientist is only safe for wrapping methods that aren't changing data" ([github/scientist](https://github.com/github/scientist)). Una lectura cumple eso.
- GitHub lo usó en producción para reemplazar su código de merge: corrían control y candidato, comparaban, y "Finally, we removed the old implementation" ([GitHub Blog, Move fast and fix things](https://github.blog/engineering/infrastructure/move-fast/)).
- **Tests de caracterización:** "The purpose of characterization testing is to document your system's actual behavior". Sirven "when we want to refactor or rewrite": "We can run our tests and find out immediately whether we've changed behavior" ([Feathers, Characterization Testing](https://michaelfeathers.silvrback.com/characterization-testing)).
- Scientist en producción sirve cuando no se pueden enumerar las entradas reales. Acá sí se pueden: `GetTask` tiene cinco ramas (inexistente, propia, ajena sin permiso, ajena con permiso y, dentro de las que devuelven 200, los estados de la tarea). Un test con un conjunto de datos que cubre todas las ramas es suficiente y no necesita librería [INFERENCIA].

### 5.2 Qué tiene que afirmar el test

Para cada caso, el control (el `GetTask` de hoy, copiado tal cual) y el candidato (el `GetTask` nuevo) dan:

1. El mismo resultado: igual `IsError`, y si es error, igual `Type` y `Code`; si es valor, la misma `TaskResponse`. `TaskResponse` es un record, así que `Assert.Equal` compara campo por campo.
2. Una sola consulta a `TaskItems` (con `CapturedCommands.Reading("TaskItems")`, `backend/Tests/Support/CapturedCommands.cs:14-15`).
3. Ninguna `TaskItem` rastreada después del candidato: `db.ChangeTracker.Entries<TaskItem>()` vacío. Esta afirmación es la que separa una lectura dedicada de V2 (sección 2.3), que da el mismo resultado pero rastrea.

Casos (todos en una base compartida del fixture, como `GetTaskTests`):

- Propia abierta (`CompletedAt` nulo, `UpdatedAt` nulo).
- Propia completada (con `CompletedAt` y `UpdatedAt`).
- Propia reabierta: completada y vuelta a abrir (`CompletedAt` nulo con `UpdatedAt` lleno).
- Propia renombrada (versión distinta a la inicial).
- Ajena, leída por un usuario sin permiso: `Task.Forbidden`.
- Ajena, leída por un Admin (`_fixture.SetRoleAsync`): 200.
- Id inexistente: `Task.NotFound`.
- Id de una tarea borrada: `Task.NotFound`.

El test resuelve las dos operaciones desde un scope del host aislado (`_fixture.CreateIsolatedClient(..., out IServiceProvider services)`, `backend/Tests/Support/ApiFixture.cs:33-49`) con `CapturedCommands` agregado por `ConfigureDbContext`, igual que `ListTasksPlanTests.cs:34-38`. Así lo armé en EX-READ y funcionó [EXPERIMENTO].

### 5.3 La secuencia (un commit por etapa)

1. **Caracterizar (verde con el código de hoy).** Agregar a `GetTaskTests` lo que hoy no está fijado: el JSON de `GET /tasks/{id}` es igual a la fila de la misma tarea en `GET /tasks`, para una tarea abierta, una completada y una reabierta. Este test queda para siempre: une las dos lecturas y frena que se separen sus mapeos.
2. **Lectura nueva + test comparativo.** `GetTask` pasa a la proyección. El cuerpo viejo se copia tal cual al archivo de test como control (`GetTaskComparisonTests.cs`, segundo archivo de la slice, como `ListTasksPlanTests.cs`; `docs/conventions.md:10`). El test compara control y candidato en los ocho casos.
   - Rojo antes de verde: correr el test una vez con un candidato roto a propósito (por ejemplo, V2, que rastrea, o un `IsCompleted` invertido) y guardar el FAIL como evidencia, como hicieron los pasos anteriores.
3. **Borrar la vieja.** Se borra `GetTaskComparisonTests.cs` y, con él, el control. Quedan: los `GetTaskTests` de siempre, el test de la etapa 1 y una afirmación permanente de "no rastrea `TaskItem`" (en `GetTaskTests` o en un test de la slice).

Alternativa a la etapa 2: dejar las dos lecturas en producción un commit (estilo Scientist), con la vieja en una clase aparte que el endpoint no usa. Gana que el control es código de producción. Pierde que hay dos implementaciones vivas y un tipo de más registrado en DI. Recomiendo la copia en el test: el control es literalmente el código viejo (sale de git), y producción nunca tiene dos versiones [INFERENCIA].

## 6. Dune y `check-structure.mjs`

**Qué reglas toca una lectura nueva** (revisé `dune/Dune.Analyzers/DuneDiagnostics.cs` y `dune/check-structure.mjs`):

- DUNE0002 (endpoint sin EF): no cambia, el endpoint sigue igual.
- DUNE0004 (entre features solo `Contracts`): `IOwnedResource` está en `Shared/Domain` y `IAuthModule` en `Auth/Contracts`. Sin problema.
- DUNE0007 (no combinar `Id` y `OwnerId` en una carga individual): el `Where` ve solo `Id` (sección 3).
- DUNE0008 (sufijos): `OwnedTask` no usa un sufijo reservado. Un test `GetTaskComparisonTests` cae en la regla de `*Tests` junto a su slice, que ya acepta `ListTasksPlanTests`.
- DUNE0010 (un tipo por archivo): `OwnedTask` va anidado y privado.
- `check-structure.mjs` reconoce una slice por su `<Slice>Endpoint.cs` y pide `<Slice>Tests.cs` (`check-structure.mjs:77-104`). No cambia: no hay carpeta nueva.
- EX-PROBE compiló con todos los analyzers sin errores [EXPERIMENTO]. La sonda estaba en una carpeta sin endpoint, así que `check-structure.mjs` no la miró.

**¿Hace falta una regla nueva?**

- Si el paso agrega a `docs/conventions.md` una regla como "una slice de lectura no escribe y no carga la entidad de escritura: proyecta a su modelo de lectura", la regla 4 de `AGENTS.md` pide hacerla ejecutable en Dune, con una fixture válida y una violación deliberada.
- El paso 18 eligió no hacer una regla porque no había un segundo caso (`paso-18-indices-medidos.md`, "Sin regla nueva de Dune"). Acá sí hay más casos: `ListTasks`, `GetTask` y `GetUser` leen de la base en un `GET` [INFERENCIA].
- Propuesta (DUNE0019, un analyzer):
  - **Alcance:** la operación de una slice cuyo `<Slice>Endpoint.cs` mapea solo `MapGet`. Saber eso pide mirar otro archivo, así que el análisis va al final de la compilación, como ya hace DUNE0015 (`DuneDiagnostics.cs:160-168`).
  - **Regla a, no escribe:** ninguna llamada a `SaveChanges`/`SaveChangesAsync`, `ExecuteUpdate*`/`ExecuteDelete*`, ni `Add`/`Update`/`Remove`/`Attach` de un `DbSet` o del `DbContext`. Hoy no la viola nadie [INFERENCIA, por `grep` de esas llamadas en `backend/Features`: solo aparecen en slices de escritura, `Sessions.cs` y `UsersModule.cs`].
  - **Regla b, no carga entidades:** ningún operador final de EF (`ToListAsync`, `SingleOrDefaultAsync`, `FirstOrDefaultAsync`, `FindAsync` y parecidos) cuyo resultado sea un tipo del `Domain` de una feature. Hoy la viola `Users/GetUser` (`GetUser.cs:23-25`, carga `UserProfile`).
  - **Excepción:** `Users/GetUser` en una lista explícita, con un pendiente en `progreso.md`, como hizo DUNE0018 con las migraciones viejas (`docs/conventions.md`, "Migraciones: expand/contract"). Migrar `GetUser` ahora amplía el alcance (regla 7).
- Recomiendo **sí**, con las dos reglas y la excepción. Si Tobi prefiere un paso más chico: solo la regla a, que no tiene excepciones, y la regla b queda como pendiente junto con `GetUser`. Ver "Decisiones abiertas".

## 7. Riesgos y qué medir

- **La trampa de V2** (proyección que llama a C# con la entidad): mismo resultado, pero rastrea y trae todas las columnas [EXPERIMENTO]. Se mide con `ChangeTracker.Entries<TaskItem>()` (5.2, punto 3).
- **Mapeos que se separan:** si el mapeo de `GetTask` se copia (V1), un cambio futuro en `TaskResponses.Projection` no llega a `GetTask`. Se evita con V3, y lo detecta el test permanente de la etapa 1 (detalle igual a fila de la lista).
- **Una consulta, no dos:** medir con `CapturedCommands` que `GetTask` manda exactamente una consulta a `TaskItems` [EXPERIMENTO: hoy y con V1/V3/V5, una].
- **Rendimiento:** no prometer mejora. Para una fila, el benchmark oficial de tracking mide 200 filas, no una ([EF Core, Efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#tracking-no-tracking-and-identity-resolution)). No medí tiempos: las diferencias por una fila quedan debajo del ruido de una prueba de integración [INFERENCIA].
- **Mismo `DbContext` por request:** `AddDbContext` lo registra scoped, uno por request ([EF Core, DbContext lifetime](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#dbcontext-in-dependency-injection-for-aspnet-core)). Una consulta sin rastreo da "results based on what's in the database disregarding any local changes" ([EF Core, Tracking vs. No-Tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking#no-tracking-queries)). En `GET /tasks/{id}` nada escribe `TaskItems` antes de leer en el mismo request, así que no cambia lo que se ve [INFERENCIA].
- **El SQL cambia aunque el resultado no:** la base calcula `"CompletedAt" IS NOT NULL` y ya no manda `CompletedAt` (2.3). El índice usado es la clave primaria igual que antes, por el `WHERE t."Id" = @id` [INFERENCIA; no corrí `EXPLAIN`].

## Choques con los docs del repo

No los resuelvo; los marco para Tobi.

1. **El informe empresarial dice que este caso no pide CQRS.** `docs/evolucion/informes/informe-dotnet-empresarial.md:410`: CQRS (solo lecturas) "NO cuando: Reportes simples servidos por el mismo modelo". Fowler y Azure dicen lo mismo para CRUD simple (sección 1). `Tasks` es CRUD simple. El plan igual lo incluye. Mi lectura: el paso es didáctico (el repo es un laboratorio, `AGENTS.md`, "Para qué existe"), pero el cierre debería decir que en producción no se justificaría por necesidad.
2. **El plan describe un código que ya no existe.** `plan.md:367` ("contratos de lectura y operación en `Tasks`") se escribió con `TasksModule` parcial (commit `d09fb92`, 2026-10-15). ADR-0004 (2026-10-16) ya separó cada operación en su slice, y `ListTasks` ya proyecta. Lo que queda del paso es `GetTask`. No es un choque entre dos docs, pero el "Cambia" del plan es más grande que lo que queda por hacer.
3. **"Carga el recurso por Id."** `docs/architecture.md:69` y `docs/conventions.md:72` dicen "Una operación carga el recurso por Id". Con la proyección, `GetTask` carga su modelo de lectura por Id, no la entidad. La regla de fondo (filtrar solo por Id, primero 404 y después 403) se cumple igual. Si Tobi quiere, la frase pasa a "carga por Id la entidad o su modelo de lectura".

## Recomendación para este repo

1. **`backend/Features/Tasks/GetTask/GetTask.cs`:** la consulta pasa a `_db.TaskItems.Where(t => t.Id == id).Select(Read).SingleOrDefaultAsync(ct)`. `Read` es un `static readonly Expression<Func<TaskItem, OwnedTask>>` armado una vez desde `TaskResponses.Projection.Body` más `OwnerId` (V3). `OwnedTask` es un `private sealed record` anidado que implementa `IOwnedResource`. Se decide con `_auth.CanAccessAsync(callerId, task, ...)` y se devuelve `task.Response`. Sin cambios en `GetTaskEndpoint.cs`, `TasksFeature.cs`, `TaskResponse.cs`, `TaskResponses.cs` ni en el frontend.
2. **Tests, en tres commits** (sección 5.3):
   - `GetTaskTests.cs`: detalle igual a la fila de la lista (abierta, completada, reabierta) y "la lectura no rastrea `TaskItem`". Quedan.
   - `GetTaskComparisonTests.cs`: control (el código viejo copiado) contra candidato en ocho casos, con una consulta y sin rastreo. Rojo deliberado antes de verde. Se borra en el último commit.
3. **Convención:** una línea en `docs/conventions.md` (sección "HTTP" o una nueva "Lecturas"): "Una slice de lectura (`GET`) no escribe y no carga la entidad de escritura: proyecta a su modelo de lectura en una sola consulta y decide propietario o permiso sobre ese modelo". Y una línea en `docs/architecture.md` que explique el CQRS liviano de `Tasks` (misma base, dos modelos).
4. **Gate:** DUNE0019 con las reglas a y b de la sección 6, cada una con su fixture válida y su violación deliberada en `dune/Dune.Analyzers.Tests/`, y `Users/GetUser` como excepción con motivo (decisión 2).
5. **Decisiones:** una línea en `docs/decisions.tsv` por cada decisión de la lista de abajo que Tobi tome.
6. **Verificación:** `check-all.mjs --no-e2e`. E2E `NOT_RUN`, porque no cambia el frontend ni el contrato HTTP.

## Decisiones abiertas

El modo autónomo terminó en el paso 19 (`AGENTS.md`, "Modo autónomo (pasos 10 a 19)"). Este paso vuelve a la regla 11: decide Tobi.

1. **Cómo se arma la proyección.**
   - V3, expresión compuesta desde `TaskResponses.Projection` (recomendada): el mapeo y la regla de completitud siguen en un solo lugar (`conventions.md:18-19`). Pierde algo de lectura fácil: seis líneas de `System.Linq.Expressions`, una idea nueva además de CQRS.
   - V1, `new TaskResponse(...)` escrito en el `Select` de `GetTask`: LINQ común, se entiende de un vistazo. Pierde que la regla `CompletedAt != null` queda en dos lugares; la protege solo el test de detalle contra lista.
2. **Gate.**
   - DUNE0019 con reglas a y b, y `GetUser` como excepción (recomendada): la convención nueva queda ejecutable entera.
   - Solo la regla a ("una lectura no escribe"): sin excepciones, más chica. La regla b queda pendiente con `GetUser`.
   - Sin regla, y sin línea normativa en `conventions.md` (solo describir en `architecture.md`): menos trabajo, pero la separación queda sin gate. Choca con el espíritu de la regla 4.
3. **Dónde vive el control del test comparativo.**
   - Copiado en el archivo de test (recomendado): producción nunca tiene dos versiones.
   - Las dos lecturas en producción durante un commit (estilo Scientist): más fiel al patrón, con un tipo de más en DI.
4. **Herramientas (regla 5).** Ninguna hace falta. Si Tobi quiere Dapper para el lado de lectura (como eShopOnContainers), Scientist.NET para el experimento o LINQKit para componer expresiones, es su elección. Recomiendo ninguna.

## Pendientes fuera del alcance (regla 7)

- `Users/GetUser` carga `UserProfile` (sin rastreo) en vez de proyectar. Debería seguir la misma forma en un paso propio, o al sacar la excepción de DUNE0019. Sin paso asignado.
- `ListTasks.cs:37`: `.AsNoTracking()` después de una proyección a un tipo que no es entidad no cambia nada (sección 2.2). Se puede quitar cuando se toque esa slice. Sin paso asignado.
- `AccessPolicy` deja rastreados los `IdentityRole` que lee con `RoleManager.FindByNameAsync` en cada request con permiso (sección 2.3). No afecta a `TaskItems`. Va con el pendiente de `AccessPolicy` que ya está en `progreso.md`.

## Qué quedó sin comprobar

- No corrí la suite completa ni `check-all` con el cambio. EX-READ y EX-PROBE corrieron solo sus tests.
- No escribí ni probé DUNE0019. Las reglas a y b son una propuesta; el conteo de quién las viola hoy sale de leer el código, no de un analyzer.
- No medí tiempos de respuesta ni corrí `EXPLAIN` sobre la consulta nueva.
- No verifiqué el comportamiento con `AsNoTracking()` quitado de `ListTasks`; me apoyo en la documentación.
- No leí el post original de Greg Young (la URL de codebetter da 404). Lo cito por la guía de patterns & practices de Microsoft, que lo cita textual.
- Las citas de Fowler las verifiqué por frases sueltas: la herramienta no devuelve el texto completo de su página.
- No revisé el CI de GitHub.

## Fuentes

- [Martin Fowler, CQRS](https://martinfowler.com/bliki/CQRS.html)
- [Martin Fowler, CommandQuerySeparation](https://martinfowler.com/bliki/CommandQuerySeparation.html)
- [Microsoft patterns & practices, Reference 2: Introducing the CQRS Pattern (citas de Greg Young y Udi Dahan)](https://learn.microsoft.com/en-us/previous-versions/msp-n-p/jj591573(v=pandp.10))
- [Microsoft Learn, Applying simplified CQRS and DDD patterns in a microservice](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/apply-simplified-microservice-cqrs-ddd-patterns)
- [Microsoft Learn, Implement reads/queries in a CQRS microservice](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads)
- [Azure Architecture Center, CQRS pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)
- [Jimmy Bogard, Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/)
- [EF Core, Tracking vs. No-Tracking Queries](https://learn.microsoft.com/en-us/ef/core/querying/tracking)
- [EF Core, Efficient Querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
- [EF Core, Advanced Performance Topics (compiled queries, consultas dinámicas)](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics)
- [EF Core, DbContext Lifetime, Configuration, and Initialization](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)
- [dotnet/efcore, QueryTrackingBehavior.cs (release/10.0)](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/QueryTrackingBehavior.cs)
- [Npgsql EF Core, Concurrency tokens (xmin)](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [github/scientist](https://github.com/github/scientist)
- [Scientist.NET](https://github.com/scientistproject/Scientist.net)
- [GitHub Blog, Move fast and fix things](https://github.blog/engineering/infrastructure/move-fast/)
- [Michael Feathers, Characterization Testing](https://michaelfeathers.silvrback.com/characterization-testing)
