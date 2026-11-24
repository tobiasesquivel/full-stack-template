# Paso 10: auditoría de cambios (investigación)

- Fecha: 2026-11-16.
- Alcance: el paso 10 del [plan](../plan.md) (`plan.md:309-312`). Cada cambio de una entidad de negocio deja una fila que dice quién lo hizo, qué cambió y cuándo, sin guardar datos personales. El plan pide un interceptor de EF en `Infrastructure/Persistence`, una migración y tests. El test del plan verifica el registro de una actualización y que el registro no tiene datos personales.
- Decisiones que ya están tomadas y que este informe no reabre: EF Core 10 con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`backend/TemplateApp.csproj:35`), PostgreSQL 18, un solo `AppDbContext` hasta el paso 22 (`docs/architecture.md:43`), la versión `xmin` del paso 8 (`docs/decisions.tsv:144`), el filtro de idempotencia del paso 9 con su transacción (`docs/decisions.tsv:149-155`), `FakeTimeProvider` en los tests (`docs/decisions.tsv:143`), sin trabajos programados (`plan.md:33`) y la forma de ADR-0004.
- Método: solo fuentes primarias. Son Microsoft Learn (EF Core, ASP.NET Core 10 y .NET), el código de `dotnet/aspnetcore` (Identity), la documentación de PostgreSQL 18, el OWASP Logging Cheat Sheet, el texto oficial del GDPR en EUR-Lex y el repo y el paquete NuGet de Audit.NET. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA].
- Evidencia propia: un experimento descartable fuera del repo.
  - "Experimento": una app .NET 10 de un solo archivo (`dotnet run audit.cs`, SDK 10.0.401) en el directorio temporal del sistema, con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, contra un contenedor `postgres:18` descartable. Reproduce las piezas del repo: una cuenta (`Account`, como `AppIdentityUser`), un perfil (`Profile`, como `UserProfile`, con FK en cascada a la cuenta), tareas (`TaskItem` con `xmin` como `Version` y FK en cascada al perfil), una tabla `AuditEntries` y un `SaveChangesInterceptor` que lee el usuario con `IHttpContextAccessor` y la hora con `TimeProvider`. Corre 11 casos (E0 a E10). Lo corrí tres veces con el mismo resultado. Después borré el contenedor.
  - No se tocó código del repo.

## Glosario corto

- **Auditoría de cambios** (audit trail): una tabla donde cada fila cuenta un cambio de datos: quién, qué, cuándo. OWASP la nombra como "Audit trails e.g. data addition, modification and deletion, data exports" ([OWASP Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)).
- **ChangeTracker** (rastreador de cambios): la parte de EF Core que recuerda qué entidades cargaste o agregaste y cuáles cambiaste. `SaveChanges` lee el ChangeTracker y arma los `INSERT`, `UPDATE` y `DELETE`.
- **Interceptor** (`SaveChangesInterceptor`): una clase que EF Core llama antes y después de cada `SaveChanges`. Puede mirar el ChangeTracker y agregar entidades antes de que EF escriba ([Learn, Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)).
- **Trigger** (disparador): una función de PostgreSQL que la base corre sola cuando una fila se inserta, se actualiza o se borra.
- **Actor**: quien hizo el cambio. Puede ser un usuario con sesión, un pedido anónimo (el registro) o el sistema (el arranque, sin pedido HTTP).
- **PII** (Personally Identifiable Information, información que identifica a una persona) y **dato personal**: el GDPR define dato personal como "any information relating to an identified or identifiable natural person" ([GDPR art. 4(1)](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32016R0679)). En el repo, `CONTEXT.md:8` dice que el perfil son "los datos personales que acompañan a una identidad, incluidos el nombre visible y la zona horaria".
- **Seudónimo**: un identificador que no dice quién es la persona sin otra información guardada aparte. El GDPR lo define en el art. 4(5) (misma fuente).
- **Append-only** (solo agregar): una tabla donde se insertan filas y nunca se modifican ni se borran.
- **TraceId**: el id de 16 bytes que comparten todas las operaciones de un mismo pedido en la traza distribuida ([Learn, Distributed tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)).

## Conclusión

- **Mecanismo:** recomiendo un `SaveChangesInterceptor` propio en `Infrastructure/Persistence`, que en `SavingChanges` y `SavingChangesAsync` agrega las filas de auditoría al mismo `SaveChanges`. Es lo que pide el plan y lo que muestra Microsoft como ejemplo de auditoría. Override de `SaveChangesAsync`, triggers de PostgreSQL y Audit.NET (MIT) son alternativas; la librería es decisión de Tobi (regla 5).
- **Atomicidad:** la fila de auditoría viaja en el mismo `SaveChanges` que el cambio, entonces entra en la misma transacción. Dentro del filtro de idempotencia del paso 9 entra en la transacción del filtro: si el filtro hace rollback, la auditoría también se va. El experimento lo confirmó (E3a, E3b) y también lo inverso: si falla la fila de auditoría, el cambio de la tarea no se guarda (E3c).
- **Huecos:** `ExecuteUpdate`, `ExecuteDelete`, SQL crudo y la cascada de la base no pasan por el ChangeTracker, y el interceptor no los ve (E5, E7). Hoy el repo los usa solo en `Sessions` e `IdempotencyKeys`, que quedan fuera de la auditoría. La cascada sí importa: al borrar una cuenta, las tareas caen en la base sin fila propia de auditoría. Queda la fila `Deleted` del perfil.
- **Quién:** el interceptor lee `ClaimTypes.NameIdentifier` con `IHttpContextAccessor`, como ya hace el filtro de idempotencia (`IdempotencyFilter.cs:141-148`), sin nombrar Auth. Distingue tres casos: `user` (con id), `anonymous` (pedido sin sesión, como el registro) y `system` (sin pedido HTTP, como el arranque o un test que escribe por scope).
- **Qué, sin datos personales:** guardar tipo de entidad, id, operación y **solo los nombres** de las propiedades cambiadas, nunca valores. El `Title` de una tarea es texto libre de una actividad personal: lo trato como dato personal. El id de usuario es un seudónimo: es dato personal para el GDPR, pero no es un dato de los que `CONTEXT.md` nombra. Ese límite lo decide Tobi.
- **Cuándo y correlación:** `TimeProvider` (en UTC, ya registrado) y `Activity.Current?.TraceId`, que une la fila con el log y la traza del pedido. El experimento confirmó que el TraceId guardado es el de la actividad en curso (E1).
- **Alcance:** recomiendo auditar `TaskItem` y `UserProfile`, marcadas con una interfaz vacía en `Shared/Domain` (como `IOwnedResource`). Sesiones, claves de idempotencia y tablas de Identity quedan fuera.
- **Inmutabilidad y retención:** un trigger append-only funciona incluso contra el superusuario con el que se conecta la API (E9), pero necesita SQL a mano en la migración, y `conventions.md:90` dice que las migraciones no se editan a mano. Además el plan no la pide. Recomiendo dejarla pendiente. La tabla de auditoría no lleva FK al usuario: con FK en cascada y append-only, borrar la cuenta falla (E9). Sin trabajos programados, no se borra nada en este paso.
- **Contrato HTTP:** no cambia. No rige la regla 3 y las e2e quedan `NOT_RUN`.
- **Dune:** el paso trae una regla nueva implícita: "una entidad auditada se escribe solo con `SaveChanges`". Recomiendo DUNE0017, que rechaza `ExecuteUpdate`/`ExecuteDelete` sobre una entidad marcada como auditada. Hoy no tiene violaciones.
- **Rojo real:** el test del plan falla hoy porque la tabla `AuditEntries` no existe. Cuidado: la mitad "no hay datos personales" pasa sola con una tabla vacía; el test tiene que exigir primero que exista la fila.

## 1. Mecanismo: interceptor, override, trigger o librería

**Respuesta:** un `SaveChangesInterceptor` propio que escribe en `SavingChanges`/`SavingChangesAsync`. Las otras tres opciones funcionan; cada una pierde algo concreto en este repo.

**Qué ofrece EF Core**

- `ISaveChangesInterceptor` intercepta "SavingChanges/SavedChanges, SaveChangesFailed/SaveChangesCanceled, Optimistic concurrency handling", y la clase base `SaveChangesInterceptor` trae los métodos vacíos ([Learn, Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)).
- Microsoft usa justo la auditoría como ejemplo: "SaveChanges can be intercepted to create an independent audit record of the changes made" y aclara "This is not intended to be a robust auditing solution" (misma fuente, sección "Example: SaveChanges interception for auditing").
- El ejemplo de Microsoft escribe en **otra base** (`AuditContext` con `audit.db`): primero un registro en `SavingChanges` y después lo marca como exitoso en `SavedChanges` o fallido en `SaveChangesFailed` (misma fuente). Por eso no es atómico con el cambio. Este repo puede hacerlo mejor: agregar la fila al mismo `SaveChanges` (sección 3).
- Sync y async: "Overriding both sync and async methods ensures that auditing will happen regardless of whether `SaveChanges` or `SaveChangesAsync` are called". Microsoft sugiere como alternativa lanzar una excepción en el sync para obligar a usar async (misma fuente). El experimento E6 mostró el hueco: con solo el método async, un `SaveChanges()` sync no deja fila. Hoy el backend no llama a `SaveChanges()` sync en ningún lado (búsqueda en `backend/`, sin resultados), e Identity usa `SaveChangesAsync` ([UserStore.cs](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/UserStore.cs), método `SaveChanges`). Recomiendo implementar los dos, porque cuesta una línea.
- `SavedChanges` no sirve para escribir la auditoría en la misma transacción: corre "at the end of a successful SaveChanges" ([Learn, .NET events](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/events)); escribir ahí pide otro `SaveChanges`, fuera de la transacción implícita del primero. [INFERENCIA]
- Un concurrency conflict no llama a `SaveChangesFailed`: "A DbUpdateConcurrencyException calls ThrowingConcurrencyException [...] It does not call SaveChangesFailed" ([Learn, Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)). Importa para el caso de la sección 3.
- Registro: "Interceptors are registered per DbContext instance", con `AddInterceptors`, que se puede llamar dentro de `AddDbContext` (misma fuente). Solo las interfaces marcadas como singleton (materialización, query expression, identity resolution) tienen el problema de crear un proveedor interno por instancia; `ISaveChangesInterceptor` no es singleton (misma fuente, tabla "Available interceptors"). El experimento E10 resolvió el interceptor desde DI en 30 scopes con `ManyServiceProvidersCreatedWarning` configurado para lanzar excepción, y no lanzó.
- Microsoft también dice: "in many cases it may be easier to override the SaveChanges method or use the .NET events for SaveChanges" (misma fuente). Los eventos son "sync only" ([Learn, .NET events](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/events)).

**PostgreSQL**

- El manual de PL/pgSQL trae un trigger de auditoría: `AFTER INSERT OR UPDATE OR DELETE ... FOR EACH ROW` que inserta en `emp_audit` con `now()` y `current_user` ([PostgreSQL 18, Trigger Functions, ejemplo 41.4](https://www.postgresql.org/docs/18/plpgsql-trigger.html)). `current_user` es el **rol de la base**, no el usuario de la app: en este repo sería siempre `postgres` (`compose.yaml:44`).
- Para que el trigger sepa el usuario de la app, la app tiene que pasarlo en cada transacción con `set_config(nombre, valor, true)`: "If is_local is true, the new value will only apply during the current transaction" ([PostgreSQL 18, System Administration Functions](https://www.postgresql.org/docs/18/functions-admin.html)). El experimento E5 lo probó: dentro de la transacción el trigger leyó `u1`; fuera de ella leyó `''` (texto vacío, no `NULL`) en la misma conexión, y sin `set_config` leyó `NULL`.

**Audit.NET**

- Paquete `Audit.EntityFramework.Core` 33.0.0, licencia MIT, publicado el 2026-09-15, con destino `net10.0` y dependencia de EF Core 10 ([catálogo de NuGet](https://api.nuget.org/v3/catalog0/data/2026.09.15.06.53.34/audit.entityframework.core.33.0.0.json), [LICENSE](https://github.com/thepirat000/Audit.NET/blob/master/LICENSE)).
- Se engancha heredando de `AuditDbContext`/`AuditIdentityDbContext`, llamando a un helper desde el override de `SaveChanges`, o con `AuditSaveChangesInterceptor`. Tiene modo OptIn con `[AuditInclude]`, y `[AuditIgnore]`/`Override` para sacar o enmascarar valores. Su proveedor `EntityFrameworkDataProvider` usa por defecto el mismo `DbContext`, y su README recomienda otra instancia; guardar en la misma transacción pide configurarlo a mano ([README de Audit.EntityFramework](https://github.com/thepirat000/Audit.NET/blob/master/src/Audit.EntityFramework/README.md)).

**Comparación**

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. `SaveChangesInterceptor` propio (plan) | Lo que pide el plan. La lógica vive en `Infrastructure/Persistence` y `AppDbContext` queda solo con mappings. Recibe `TimeProvider` e `IHttpContextAccessor` por DI. En el paso 22 cada `DbContext` por feature lo suma con `AddInterceptors`, sin heredar de una base común. Atómico con el cambio (sección 3). | No ve `ExecuteUpdate`, `ExecuteDelete`, SQL crudo ni la cascada de la base (sección 2). |
| B. Override de `SaveChangesAsync` en `AppDbContext` | Lo más simple según Microsoft. Mismo alcance que A. | `AppDbContext` pasa a tener lógica y dependencias en el constructor (hoy recibe solo `options`, `AppDbContext.cs:12`). En el paso 22, cada contexto repite el override o hereda de una base. |
| C. Trigger de PostgreSQL | Ve todo: `ExecuteUpdate`, SQL crudo y cascadas (E5, E7). | El actor llega solo con `set_config` en cada transacción. La lógica queda en PL/pgSQL dentro de una migración escrita a mano, que choca con `conventions.md:90` (sección 8). El plan pide un interceptor. |
| D. Audit.NET (MIT) | Código probado por otros, con opt-in y enmascarado. | Un paquete y un modelo de eventos propios. La atomicidad pide configuración extra. Por defecto guarda valores, justo lo que el paso evita. Herramienta: decide Tobi (regla 5). |

Recomendación: **A**. Es el mismo criterio del paso 9, donde Tobi eligió un mecanismo propio sin librería (`docs/decisions.tsv:154`).

## 2. Huecos: lo que no pasa por el ChangeTracker

**Respuesta:** el interceptor no ve `ExecuteUpdate`, `ExecuteDelete`, SQL crudo ni la cascada de la base. En el repo, los tres primeros solo tocan tablas que no se auditan. La cascada sí deja un hueco al borrar una cuenta.

**Qué dice EF Core**

- `ExecuteUpdate` y `ExecuteDelete` "are completely unaware of EF's change tracker, and have no interaction with it whatsoever" ([Learn, ExecuteUpdate and ExecuteDelete](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)). Tampoco abren transacción solos: "do not implicitly start a transaction" (misma fuente).
- La cascada tiene dos lugares: EF borra las dependientes **cargadas** ("EF Core always applies configured cascading behaviors to tracked entities") y la base borra las que **no** se cargaron ("Dependents deleted by database") ([Learn, Cascade Delete](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete)).
- Experimento E5: un `ExecuteUpdateAsync`, otro dentro de una transacción y un `UPDATE` por `ExecuteSqlAsync` dieron **0** llamadas al interceptor y **0** filas de auditoría. El trigger de prueba sí vio las tres.

**Qué usa el repo hoy**

| Lugar | Qué hace | ¿Pasa por el interceptor? |
|---|---|---|
| `Sessions.Open` (`Features/Auth/Sessions.cs:21-26`) | `Add` + `SaveChanges` del que llama | Sí |
| `Sessions.IsActiveAsync` (`Sessions.cs:46-48`) | `ExecuteUpdateAsync` de `LastSeenAt` | No |
| `Sessions.CloseAsync` (`Sessions.cs:54-55`) | `ExecuteDeleteAsync` | No |
| `IdempotencyFilter` (`Infrastructure/Http/IdempotencyFilter.cs:45-51`, `:67-75`) | `INSERT ... ON CONFLICT` con `ExecuteSqlAsync` y `ExecuteUpdateAsync` | No |
| `DeleteTask` (`Features/Tasks/DeleteTask/DeleteTask.cs:29-30`) | `Remove` + `SaveChangesAsync` | Sí |
| `DeleteUser` (`Features/Users/DeleteUser/DeleteUser.cs:23-31`) | Carga el perfil **con tracking** y llama a `IAuthModule.DeleteAccountAsync`, que hace `UserManager.DeleteAsync` (`Features/Auth/AuthModule.cs:47-51`) | Sí para la cuenta y el perfil; no para tareas, sesiones ni claves |

- `UserManager.DeleteAsync` termina en `UserStore.DeleteAsync`, que hace `Context.Remove(user)` y `SaveChangesAsync` ([UserStore.cs](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/UserStore.cs)). El store usa el `AppDbContext` del request (`AddEntityFrameworkStores<AppDbContext>()`, `Features/Auth/AuthFeature.cs:48`), el mismo que cargó el perfil. [INFERENCIA, a partir de la DI scoped; no lo corrí en el repo]
- La FK `UserProfiles → AspNetUsers` es en cascada en el modelo de EF (`Infrastructure/Persistence/AppDbContext.cs:31-36`). Entonces, al borrar la cuenta, EF marca el perfil cargado como `Deleted` en el mismo `SaveChanges`. Las tareas (`AppDbContext.cs:38-42`), las claves de idempotencia (`:44-48`) y las sesiones (`Features/Auth/Persistence/SessionConfiguration.cs:23-27`) no están cargadas: las borra la base.
- Experimento E7, que imita `DeleteUser`: después de `Remove(account)` el perfil cargado pasó a `Deleted`, y el interceptor escribió dos filas (`Deleted Account` y `Deleted Profile`, con el actor que borró). Las dos tareas se borraron en la base sin fila de auditoría; el trigger de prueba sí vio los dos `DELETE`.

**Qué implica**

- Mientras `Sessions` e `IdempotencyKeys` no se auditen, sus `Execute*` no son un hueco.
- Si mañana una slice usa `ExecuteUpdate` sobre `TaskItem`, ese cambio no deja fila. La regla de Dune de la sección 9 lo frena.
- El borrado en cascada de las tareas queda representado por la fila `Deleted` del perfil. Para tener una fila por tarea, `DeleteUser` tendría que cargar las tareas, y eso obliga a Users a leer la tabla de Tasks, que DUNE0004 prohíbe (`docs/conventions.md:23`). La otra salida es un trigger (opción C). Recomiendo aceptar el hueco y nombrarlo en `architecture.md`.

## 3. Atomicidad: la fila de auditoría en la misma transacción

**Respuesta:** agregar las filas en `SavingChanges` hace que viajen en el mismo `SaveChanges`, y por eso en la misma transacción que el cambio. Dentro del filtro de idempotencia entran en la transacción del filtro sin hacer nada más.

- "By default, if the database provider supports transactions, all changes in a single call to SaveChanges are applied in a transaction" ([Learn, Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)).
- Con una transacción ya abierta, `SaveChanges` crea un savepoint y, si falla, "it automatically rolls the transaction back to the savepoint" (misma fuente). El filtro del paso 9 abre la transacción antes del handler (`IdempotencyFilter.cs:44`), confirma solo con un 2xx (`:76`) y si no hace rollback (`:80`). `RegisterUser` abre la suya (`Features/Auth/RegisterUser/RegisterUser.cs:45`, `:62-63`).

Experimento:

| Caso | Qué hice | Resultado |
|---|---|---|
| E3a | Transacción explícita, cambio de la tarea con `SaveChanges`, `Rollback` (como el filtro con un error) | 0 filas de auditoría nuevas |
| E3b | Lo mismo con `Commit` | 1 fila nueva |
| E3c | Forcé que la fila de auditoría fallara (texto más largo que la columna) | `DbUpdateException` con `PostgresException`, y el título de la tarea **no cambió** |
| E2 | Asigné a `Title` el mismo valor que ya tenía | 0 filas: EF no marca la entidad como `Modified` |

- E3c importa: la auditoría puede tumbar una escritura de negocio. Es la consecuencia buscada de "misma transacción": no hay cambio sin su fila ni fila sin su cambio.

**Un detalle que encontré: la fila que queda colgada después de un conflicto**

- E4: un `UPDATE` con versión vieja lanzó `DbUpdateConcurrencyException`. La fila de auditoría que el interceptor agregó quedó en el ChangeTracker como `Added` (1 entrada). Al reintentar en el mismo contexto, se escribieron **2** filas: la vieja y la nueva.
- E4b: si el interceptor primero desconecta (`Detached`) las filas de auditoría que hayan quedado `Added`, el mismo reintento escribe 1.
- Hoy el caso no ocurre: `UpdateTask` devuelve 409 después del conflicto y no vuelve a llamar a `SaveChanges` (`Features/Tasks/UpdateTask/UpdateTask.cs:52-55`). Recomiendo igual la limpieza al empezar, porque es un `foreach` y deja el interceptor correcto ante cualquier reintento. [INFERENCIA sobre el uso futuro]

## 4. "Quién": el actor sin romper ADR-0004

**Respuesta:** el interceptor lee el claim `ClaimTypes.NameIdentifier` con `IHttpContextAccessor`, igual que `IdempotencyFilter`, y guarda un tipo de actor (`user`, `anonymous` o `system`).

**El límite**

- "`Infrastructure/` y `Shared/` no nombran ninguna feature" (`docs/conventions.md:22`, ADR-0004 punto 6). Las slices usan `user.GetCallerId()` de `Auth.Contracts` (`Features/Auth/Contracts/ClaimsPrincipalExtensions.cs:7-15`). DUNE0003 permitiría usar `Contracts` desde Infrastructure (`dune/Dune.Analyzers/InfrastructureBoundaryAnalyzer.cs:81-92`), pero conventions.md no.
- El paso 9 lo resolvió leyendo el claim estándar directamente (`IdempotencyFilter.cs:141-148`). El claim lo escribe `SessionPrincipal.Create` (`Features/Auth/SessionPrincipal.cs:13`). Repetir esa lectura una tercera vez es el costo de no nombrar Auth.
- Diferencia con el filtro: el filtro corre solo en rutas con sesión (`Features/Tasks/TasksFeature.cs:17`) y lanza si falta el claim. El interceptor corre en todo `SaveChanges`, también sin sesión, entonces **no** puede lanzar.

**Cómo llega el `HttpContext` al interceptor**

- El interceptor no recibe el `HttpContext`; lo pide a `IHttpContextAccessor`, que se registra con `AddHttpContextAccessor()` ([Learn, Use HttpContext](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/use-http-context?view=aspnetcore-10.0)). Hoy el repo no lo registra (búsqueda de `AddHttpContextAccessor`, sin resultados).
- Advertencias de Microsoft (misma fuente): "IHttpContextAccessor.HttpContext might be null if accessed outside of the request flow"; depende de `AsyncLocal<T>`, "which can have a negative performance impact"; crea "a dependency on 'ambient state'"; y "Don't capture IHttpContextAccessor.HttpContext in a constructor". Recomienda pasar el dato explícito cuando se pueda.

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. `IHttpContextAccessor` dentro del interceptor | Ninguna slice cambia. Cubre también las escrituras de Identity (`UserManager`), que no pasan por código propio. | Estado ambiental (la advertencia de Microsoft). Una lectura más del claim. |
| B. Un servicio scoped de Infrastructure (`AuditActor`) que llena un middleware después de `UseAuthentication` | El interceptor recibe el dato sin `HttpContext`. | Un middleware y un servicio más, para el mismo dato. El middleware también depende del orden del pipeline (`Program.cs:35-37`). |
| C. Cada slice pasa el actor (`db.SetActor(callerId)`) | Explícito, como pide Microsoft. | Cada slice lo repite y una que lo olvide escribe sin actor. Identity escribe sin pasar por la slice. |

Recomendación: **A**, leyendo el accessor en cada `SavingChanges` (nunca en el constructor).

**Los tres actores**

| Caso en el repo | `HttpContext` | Claim | Se guarda |
|---|---|---|---|
| Slices con sesión (`PUT /tasks/{id}`) | sí | sí | `ActorKind = user`, `ActorId = <id>` |
| `RegisterUser` (anónimo, crea cuenta y perfil, `RegisterUser.cs:49`, `:57`) | sí | no | `ActorKind = anonymous`, `ActorId = null` |
| `DatabaseInitializer` y sus seeders (`Infrastructure/Persistence/DatabaseInitializer.cs:14`, `:24-26`), helpers de test con `fixture.Services.CreateScope()` (`backend/Tests/Support/TestAccounts.cs:98-103`) | no | — | `ActorKind = system`, `ActorId = null` |

- Experimento E0: el registro anónimo dejó `Added Account` y `Added Profile` con `anonymous`; un alta sin `HttpContext` dejó `system`. E1: un usuario dejó `user` con su id.
- Hoy ningún seeder escribe entidades auditadas: `RoleSeeder` escribe roles y claims de Identity. El caso `system` existe para el futuro y se puede probar con un helper de test que escribe por scope.
- En el registro, el que actúa es el nuevo usuario, pero todavía no tiene sesión. Guardar `anonymous` con el id de la entidad creada alcanza para saber quién se registró. [INFERENCIA]

## 5. "Qué" sin datos personales

**Respuesta:** tipo de entidad, id, operación y nombres de las propiedades cambiadas. Ningún valor. Un test con valores centinela lo comprueba.

**Qué es dato personal acá**

- GDPR: dato personal es "any information relating to an identified or identifiable natural person", y se identifica "by reference to an identifier such as a name, an identification number, location data, an online identifier" ([art. 4(1)](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32016R0679)). Los datos seudonimizados "should be considered to be information on an identifiable natural person" ([considerando 26](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32016R0679)).
- OWASP: la identidad del actor se registra ("User identity [...] e.g. user database table primary key-value, username"). Lo que no va al log es "Sensitive personal data and some forms of personally identifiable information (PII)", y también los datos personales no sensibles ("personal names, telephone numbers, email addresses") piden tratamiento especial. Recomienda "pseudonymization of direct and indirect identifiers where the individual's identity is not required" ([OWASP Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)).
- En el repo: `CONTEXT.md:8` declara nombre visible y zona horaria como datos personales. Identity guarda nombre de login, email y credenciales (`docs/architecture.md:47`). `conventions.md:99` ya prohíbe loguear bodies, y el informe empresarial pide "Sin PII ni secretos en logs" (`docs/evolucion/informes/informe-dotnet-empresarial.md:261`).

**¿El `Title` de una tarea es dato personal?**

- Sí, lo trato así. Una tarea es "una actividad personal" de un propietario (`CONTEXT.md:16`), entonces su título es información sobre una persona identificable. Además es texto libre: el usuario puede escribir un nombre, una dirección o un dato de salud. [INFERENCIA a partir del art. 4(1)]
- El ejemplo de Microsoft guarda los valores ("Inserting Post with Id: '...' BlogId: '...' Title: 'EF Core 3.1!'", [Learn, Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)). Con ese ejemplo, el test de "sin datos personales" fallaría.

**¿El id de usuario es dato personal?**

- `AppIdentityUser` hereda de `IdentityUser` (`Features/Auth/Contracts/AppIdentityUser.cs:5`), cuyo constructor hace `Id = Guid.NewGuid().ToString()` ([IdentityUser.cs](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Stores/src/IdentityUser.cs)). Es un GUID al azar: no dice quién es sin la tabla `AspNetUsers`. Es un seudónimo.
- Para el GDPR sigue siendo dato personal mientras se pueda volver a la persona (considerando 26). Entonces "sin datos personales" al pie de la letra es imposible si se guarda "quién". La lectura posible es la de `CONTEXT.md`: no guardar nombre visible, zona horaria, nombre de login, email ni texto libre, y guardar solo ids opacos. Lo marco como conflicto en la sección 11.

**Qué columnas**

| Columna | Tipo | De dónde sale | ¿Dato personal? |
|---|---|---|---|
| `Id` | `bigint` identity | la base | no |
| `OccurredAt` | `timestamptz` | `TimeProvider.GetUtcNow()` | no |
| `ActorKind` | `text` (`user`, `anonymous`, `system`) | sección 4 | no |
| `ActorId` | `text`, nulo | claim `NameIdentifier` | seudónimo |
| `EntityType` | `text` | `entry.Metadata.ClrType.Name` (`TaskItem`) | no |
| `EntityId` | `text` | clave primaria de la entrada | `TaskItem.Id` no; `UserProfile.AuthId` es el seudónimo |
| `Operation` | `text` (`Added`, `Modified`, `Deleted`) | `entry.State` | no |
| `ChangedProperties` | `text[]` | `entry.Properties.Where(p => p.IsModified)` | no |
| `TraceId` | `text`, nulo | `Activity.Current?.TraceId` | no |

- Experimento E1, con la misma secuencia que `UpdateTask` (cambia `Title`, `IsCompleted` y `UpdatedAt`, y asigna el `OriginalValue` de `Version`, `UpdateTask.cs:45-48`): la fila quedó con `{IsCompleted,Title,UpdatedAt}`. `Version` no aparece porque la escribe la base.
- Npgsql mapeó `string[]` a una columna de arreglo sin configuración (E8 muestra `"ChangedProperties":[]` en la fila).
- `Added` y `Deleted` guardan la lista vacía: la operación ya dice que cambió todo.
- La clave primaria de una entidad con id generado por la base todavía no existe en `SavingChanges`. En el repo no pasa: `TaskItem.Id` sale de `Guid.NewGuid()` en `CreateTask.cs:36` y `UserProfile.AuthId` viene de Identity. [INFERENCIA]

**Opciones para el "qué"**

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Solo nombres de propiedades | El test de datos personales es simple: ningún valor de la entidad aparece. | No se ve de qué valor a qué valor cambió. |
| B. Nombres + valores de una lista permitida (`IsCompleted`) | Se ve "completó la tarea". | Una lista que mantener, y alguien tiene que decidir qué valor es seguro. `IsCompleted` también habla de la actividad de una persona. |
| C. Valores completos (ejemplo de Microsoft, Audit.NET por defecto) | Historia completa. | Guarda títulos, nombres visibles y zonas horarias. Va contra el concepto del paso. |

Recomendación: **A**.

**Cómo comprueba un test que no hay datos personales**

- Registrar un usuario con valores centinela únicos (nombre visible `Zoe-Sentinel-<guid>`, zona horaria, nombre de login, email), crear una tarea con título centinela, actualizarla y actualizar el perfil.
- Leer las filas de auditoría como texto (`SELECT row_to_json(a)::text FROM "AuditEntries" a WHERE ...`) y afirmar que ningún centinela aparece.
- **Antes**, afirmar que existe la fila esperada. Sin eso, el test pasa con una tabla vacía.
- Experimento E8: con la opción A, el volcado de toda la tabla no contenía el nombre visible, el título, la zona horaria ni el nombre de login.
- Complemento posible: un test que afirma la lista exacta de columnas de `AuditEntries`. Así una columna nueva de valores no entra sin que alguien cambie el test a propósito. [INFERENCIA]

## 6. "Cuándo" y correlación con la traza

**Respuesta:** `OccurredAt` sale del `TimeProvider` que ya registra el host, y `TraceId` de `Activity.Current`.

- El repo ya registra `TimeProvider.System` como singleton (`Infrastructure/DependencyInjection/BackendInfrastructure.cs:30`) y la convención pide el tiempo en UTC desde `TimeProvider` (`docs/conventions.md:89`). Los tests lo cambian por `FakeTimeProvider` con `CreateIsolatedClient` (`Features/Tasks/ListTasks/ListTasksTests.cs:63-64`). Entonces un test puede afirmar el instante exacto de la fila.
- Ojo con la precisión: PostgreSQL guarda microsegundos y .NET ticks de 100 ns; el repo ya lo vio con `createdAt` (`docs/evolucion/progreso.md:128`). El test tiene que comparar truncando a microsegundos o usar un instante redondo. [INFERENCIA]
- `Activity.Current` "automatically flows along all synchronous calls on a thread and follows async calls", y cada actividad tiene un `TraceId` de 16 bytes del estándar W3C ([Learn, Distributed tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)).
- El repo ya pone `TraceId` y `SpanId` en cada log desde `Activity.Current` (`Infrastructure/Observability/ActivityTraceEnricher.cs:11-18`) y registra la instrumentación de ASP.NET Core (`Infrastructure/Observability/ObservabilityExtensions.cs:48`).
- OWASP pide un "Interaction identifier", "a method of linking all (relevant) events for a single user interaction" ([OWASP Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)). El TraceId cumple ese papel: une la fila de auditoría con el log del request y con su traza.
- Experimento E1: la fila de la tarea creada dentro de una actividad guardó el mismo TraceId que la actividad. La fila escrita sin actividad guardó `null`.
- Para el paso 15 (correlación log↔traza automatizada, `plan.md:338-341`), esta columna da un tercer punto de unión. No lo adelanta: el paso 10 solo la guarda.

## 7. Alcance de entidades y cómo se marcan

**Respuesta:** auditar `TaskItem` y `UserProfile`, marcadas con una interfaz vacía `IAuditable` en `Shared/Domain`.

**Qué entidades**

| Entidad | ¿Auditar? | Por qué |
|---|---|---|
| `TaskItem` (`Features/Tasks/Domain/TaskItem.cs`) | Sí | Es el dato de negocio. El plan pide "el registro de una actualización". |
| `UserProfile` (`Features/Users/Domain/UserProfile.cs`) | Sí | Se modifica (`UpdateProfile.cs:39-41`) y su borrado es la única huella del borrado de la cuenta (sección 2). OWASP pone "addition or deletion of users" entre lo de mayor riesgo ([OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)). |
| `Session` (`Features/Auth/Domain/Session.cs`) | No | Es estado técnico. La mitad de sus escrituras son `ExecuteUpdate`/`ExecuteDelete` (sección 2), entonces quedaría auditada a medias. Abrir y cerrar sesión son eventos de seguridad para el log, no cambios de datos. |
| `IdempotencyKey` (`Infrastructure/Persistence/IdempotencyKey.cs`) | No | Fontanería del paso 9, escrita con SQL crudo y `ExecuteUpdate`. |
| `AppIdentityUser` y tablas de Identity | No, en este paso | Cada login fallido escribe `AccessFailedCount` (`OpenSession.cs:48`). Los cambios de rol los hace `UserManager` en tablas de unión. Un paso de seguridad (Fase 4) puede decidirlo. |

**Cómo se marca**

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Interfaz vacía `IAuditable` en `Shared/Domain` | Mismo patrón que `IOwnedResource` (`Shared/Domain/IOwnedResource.cs:3-6`), que ya implementan `TaskItem` y `UserProfile`. `Domain` solo admite BCL y `Shared.Domain` (DUNE0001, `dune/Dune.Analyzers/DuneDiagnostics.cs:10`). El interceptor pregunta `entry.Entity is IAuditable` sin nombrar features. Dune puede leerla por tipo (sección 9). | Un tipo nuevo en `Shared`. |
| B. Atributo `[Auditable]` en `Shared/Domain` | Igual de visible. | Se lee por reflexión; el analyzer tendría que buscar atributos. |
| C. Lista de tipos en el interceptor | Nada que tocar en el dominio. | El interceptor nombraría `TaskItem` y `UserProfile`: choca con `conventions.md:22`. |
| D. Todo lo que tenga ChangeTracker, salvo exclusiones (opt-out, como Audit.NET por defecto) | Una entidad nueva queda auditada sin pensarlo. | Audita Identity y sesiones; la lista de exclusiones nombra features. |

Recomendación: **A**. La interfaz no tiene miembros, entonces el nombre es la decisión: el analyzer DUNE0008 no reserva el sufijo `-able` ni `Interceptor` (`dune/Dune.Analyzers/NamingAnalyzer.cs:78-115`).

## 8. Inmutabilidad y retención

**Respuesta:** la inmutabilidad real pide un trigger y SQL a mano en la migración. El plan no la pide y choca con `conventions.md:90`; recomiendo dejarla pendiente. La tabla no lleva FK al usuario. No se borra nada en este paso.

**Append-only**

- OWASP pide "Build in tamper detection so you know if a record has been modified or deleted" y "Store or copy log data to read-only media as soon as possible" ([OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)).
- **Permisos (`REVOKE`) no alcanzan hoy.** La API se conecta como `postgres` (`compose.yaml:7`, `:44`; `backend/appsettings.json:3`), que es superusuario. "A database superuser bypasses all permission checks, except the right to log in" ([PostgreSQL 18, Role Attributes](https://www.postgresql.org/docs/18/role-attributes.html)). Además el dueño de una tabla puede devolverse cualquier permiso ([PostgreSQL 18, Privileges](https://www.postgresql.org/docs/18/ddl-priv.html)). Experimento E9: después de `REVOKE UPDATE, DELETE, TRUNCATE ... FROM postgres`, un `UPDATE` como `postgres` funcionó.
- **Un trigger sí frena al superusuario.** E9: con un trigger `BEFORE UPDATE OR DELETE ... FOR EACH ROW` y otro `BEFORE TRUNCATE ... FOR EACH STATEMENT` que lanzan excepción, `UPDATE`, `DELETE` y `TRUNCATE` fallaron con `P0001`. `TRUNCATE` necesita su propio trigger, porque solo admite triggers de sentencia ([PostgreSQL 18, CREATE TRIGGER](https://www.postgresql.org/docs/18/sql-createtrigger.html)).
- Pero no es a prueba de todo: el dueño o el superusuario puede hacer `ALTER TABLE ... DISABLE TRIGGER`, y "a disabled trigger [...] is not executed" ([PostgreSQL 18, ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html)). E9 lo hizo dentro de una transacción y el `UPDATE` pasó.
- **El choque con las convenciones.** EF no modela triggers: van con `migrationBuilder.Sql(...)` en una migración, y Microsoft lo documenta para "Triggers" ([Learn, Managing Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing)). Hoy ninguna migración del repo usa `migrationBuilder.Sql` y `docs/conventions.md:90` dice "No se editan a mano".
- **Un paso, un cambio.** El plan pide registro y ausencia de datos personales (`plan.md:310-312`). La inmutabilidad es otro concepto (regla 7).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Sin trigger en este paso; pendiente con nombre | Un concepto por paso; sin tocar la regla de migraciones. | Un `UPDATE` por SQL puede reescribir la historia. Hoy ningún código lo hace, y la regla de la sección 9 frena `ExecuteUpdate` sobre entidades auditadas (no sobre `AuditEntry`). |
| B. Trigger en la migración del paso (excepción en `conventions.md:90` y en `decisions.tsv`) | La tabla queda append-only contra la app y contra un `UPDATE` de consola. | Cambia una convención; el paso enseña dos cosas. |
| C. Rol de base sin `UPDATE`/`DELETE` para la API, otro rol para migrar | Lo que recomienda PostgreSQL ("do most of your work as a role that is not a superuser"). | Toca compose, la imagen, CI, los tests y el arranque (la API migra al arrancar). Es un paso entero de seguridad. |

Recomendación: **A**, con un pendiente que nombre B y C.

**Borrado de la cuenta frente a la trazabilidad**

- Derecho de supresión: el usuario puede pedir que se borren sus datos (GDPR art. 17(1)). El art. 17(3) lo excluye cuando el tratamiento es necesario, entre otros, "for compliance with a legal obligation" o "for the establishment, exercise or defence of legal claims" ([GDPR art. 17](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32016R0679)). Y el art. 5(1)(e) pide guardar los datos identificables "for no longer than is necessary" (misma fuente). Cuál base legal aplica es una decisión de negocio, no técnica.
- **Sin FK.** La tabla de auditoría no referencia `UserProfiles`, a diferencia de `IdempotencyKeys` (`AppDbContext.cs:44-48`). Con FK en cascada, borrar la cuenta borraría la historia. Con FK en cascada y un trigger append-only, borrar la cuenta **falla**: E9 lo probó con una tabla `audit_fk` y el `DELETE` de la cuenta lanzó `P0001`.
- Después del borrado, la fila guarda un GUID que ya no está en `AspNetUsers` ni en `UserProfiles`. En la base, ya no lleva a la persona. Puede seguir llevando por otras fuentes (backups, logs con el mismo id), y entonces sigue siendo dato personal (considerando 26). [INFERENCIA]

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Las filas quedan con el GUID | Trazabilidad completa; simple; compatible con append-only. | Una base legal para guardar el seudónimo (decisión de negocio). |
| B. Borrar las filas del usuario al borrar la cuenta | Supresión total en la base. | La historia desaparece justo cuando más importa; incompatible con append-only. |
| C. Seudonimizar con una clave por usuario y destruir la clave al borrar | Trazabilidad sin poder volver a la persona. | Gestión de claves; mucho más que un paso. |

Recomendación: **A**, y documentarlo.

**Retención**

- OWASP: los logs "must not be destroyed before the duration of the required data retention period, and must not be kept beyond this time" ([OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)).
- El repo no tiene trabajos programados (`plan.md:33`). El paso 9 dejó la limpieza de `IdempotencyKeys` pendiente junto con la de `Sessions` (`docs/decisions.tsv:153`, `progreso.md:111`, `:133`). Recomiendo lo mismo: no borrar en este paso y sumar `AuditEntries` a ese pendiente, con un índice por `OccurredAt` para cuando llegue (como `IdempotencyKeyConfiguration`). Si después entra el trigger append-only, la limpieza va a necesitar un camino privilegiado. [INFERENCIA]

## 9. Contrato HTTP y regla de Dune

**Contrato HTTP: no cambia.**

- El paso no agrega endpoints, headers ni campos: la tabla no se expone. Entonces no rige la regla 3 (`AGENTS.md:37`) y no cambia el frontend.
- Las e2e quedan `NOT_RUN` (`AGENTS.md`, sección "Verificación"). Corresponde correr `dotnet test` y `smoke-api.mjs` como regresión, porque toda escritura pasa ahora por el interceptor.
- Si Tobi quiere ver la auditoría desde la API (un `GET /audit`), es un paso aparte con su contrato.

**Regla de Dune: recomiendo DUNE0017.**

- La regla 4 pide hacer ejecutable toda regla nueva (`AGENTS.md:38`). El paso trae una: "un cambio de una entidad auditada pasa por `SaveChanges`", porque si no, no deja fila (sección 2).
- **Qué afirmaría:** en el backend (sin tests ni migraciones), una llamada a `ExecuteUpdate`, `ExecuteUpdateAsync`, `ExecuteDelete` o `ExecuteDeleteAsync` cuyo tipo de entidad implementa `TemplateApp.Shared.Domain.IAuditable` es un error. El analyzer mira el argumento de tipo `TSource` del método de `RelationalQueryableExtensions`, entonces `db.Set<TaskItem>()` o un `var` no lo esquivan.
- **Fixture válida:** `db.Sessions.Where(...).ExecuteDeleteAsync(ct)` (como `Sessions.cs:55`): `Session` no es auditable.
- **Violación deliberada:** `db.TaskItems.Where(t => t.Id == id).ExecuteDeleteAsync(ct)` en una slice de Tasks.
- **Rojo:** la regla no puede tener rojo sobre el código de antes del paso, porque `IAuditable` no existe y hoy nadie hace `Execute*` sobre `TaskItem` ni `UserProfile`. El rojo es la violación deliberada.
- **Lo que no cubre:** SQL crudo (`ExecuteSqlAsync`) sobre `"TaskItems"`, porque el nombre de la tabla va en un string. `BannedSymbols.txt` ya prohíbe las variantes `Raw` sin parámetros. Lo anoto como límite.
- Precedente: en el paso 9, Tobi metió DUNE0016 dentro del paso (`docs/decisions.tsv:155`).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| DUNE0017 dentro del paso | La regla nace con el mecanismo, como en el paso 9. | Más trabajo en el paso. |
| Paso 10.1 | Un paso, un cambio. | La regla queda solo como texto hasta 10.1. |
| Sin regla, solo una línea en `conventions.md` | Nada que hacer. | Va contra la regla 4. |

## 10. Rojo real: qué test falla hoy y por qué

| Test (`backend/Tests/Auditing/` o en la slice) | Hoy | Por qué |
|---|---|---|
| 1. Actualizar una tarea con `PUT` deja una fila `Modified` de `TaskItem` con el id de la tarea, `ActorKind = user`, `ActorId` = id de Alice, `ChangedProperties = {IsCompleted, Title, UpdatedAt}` y `OccurredAt` = el reloj fijo | Falla | La tabla `AuditEntries` no existe: si el helper consulta por SQL (como `LastSeenAsync`, `TestAccounts.cs:105-112`) da `42P01 relation does not exist`. Con un `DbSet` ni compila. |
| 2. Con valores centinela, ninguna fila de Alice contiene nombre visible, zona horaria, nombre de login, email ni título | Falla solo si exige antes la fila del test 1 | Sin esa precondición pasa con una tabla vacía: es un test que no prueba nada. |
| 3. Un `PUT` con versión vieja (409) no deja fila | Falla hoy por la tabla | Después del paso pasa; prueba la atomicidad con `DbUpdateConcurrencyException`. |
| 4. `POST /tasks` con `Idempotency-Key` y un error de validación no deja fila; con 201, una sola | Falla hoy por la tabla | Prueba la transacción del filtro (sección 3). |
| 5. Borrar la cuenta deja `Deleted UserProfile` con el actor y ninguna fila de sus tareas | Falla hoy por la tabla | Documenta el hueco de la cascada en un test (sección 2). |
| 6. Un cambio por scope de test, sin pedido HTTP, queda con `ActorKind = system` | Falla hoy por la tabla | Cubre el actor del sistema. |

- El orden TDD útil: primero la entidad, el mapping y la migración (el test 1 pasa de `42P01` a "0 filas"), después el interceptor (el test 1 pasa a verde). Ese "0 filas" es el rojo que enseña el concepto. [INFERENCIA]
- `conventions.md:106` ubica las pruebas de `Infrastructure/` en `backend/Tests/<Área>/`. El interceptor es Infrastructure; los tests cruzan HTTP contra PostgreSQL real como pide `conventions.md:104`.

## 11. Choques con los docs y con el plan

1. **"Sin datos personales" contra "quién".** El plan pide las dos cosas (`plan.md:310`). Para el GDPR, el id seudónimo sigue siendo dato personal (considerando 26). La salida que propongo usa la definición de `CONTEXT.md:8`: nada de nombre visible, zona horaria, credenciales ni texto libre; solo ids opacos. Decide Tobi; si acepta, conviene escribirlo en `CONTEXT.md` o en `conventions.md`.
2. **Trigger append-only contra `conventions.md:90`** ("Las migraciones se generan por CLI [...] No se editan a mano"). Solo choca si Tobi elige la opción B de la sección 8.
3. **Tercera lectura del claim.** `GetCallerId` (Auth), `IdempotencyFilter.OwnerOf` (Infrastructure) y el interceptor leen `ClaimTypes.NameIdentifier` por separado. No choca con ningún doc, pero repite conocimiento de Auth. Lo dejo como pendiente.

No encontré choques con ADR-0004 ni con `architecture.md`: el interceptor y la entidad `AuditEntry` viven en `Infrastructure/Persistence` (como `IdempotencyKey`) y la marca en `Shared/Domain` no nombra features.

## Decisiones abiertas para Tobi

1. **Mecanismo** (regla 5 para la librería).
   - Interceptor propio: ganás lo que pide el plan, atomicidad y reuso en el paso 22; perdés ver `Execute*` y cascadas.
   - Override de `SaveChangesAsync`: ganás simplicidad; perdés un `AppDbContext` limpio y el reuso en el paso 22.
   - Trigger de PostgreSQL: ganás ver todo; perdés el actor sin `set_config`, y la migración se edita a mano.
   - Audit.NET 33 (MIT): ganás código probado; perdés control de la atomicidad y suma un paquete.
   - Recomiendo **interceptor propio**.
2. **Qué se guarda.**
   - Solo nombres de propiedades: ganás un test de datos personales simple; perdés ver los valores.
   - Lista de valores permitidos: ganás "completó la tarea"; perdés simplicidad y alguien decide qué es seguro.
   - Recomiendo **solo nombres**.
3. **Qué cuenta como dato personal** (sección 11.1).
   - Ids opacos sí, nombre visible, zona horaria, credenciales y texto libre no.
   - Recomiendo **esa lectura**, escrita en `CONTEXT.md`.
4. **Alcance de entidades.**
   - `TaskItem` y `UserProfile`: ganás el borrado de la cuenta en la auditoría; perdés nada relevante.
   - Solo `TaskItem`: ganás menos filas; perdés el borrado de la cuenta.
   - También Identity: ganás cambios de cuenta; perdés claridad (cada login fallido escribe).
   - Recomiendo **`TaskItem` y `UserProfile`**, marcadas con `IAuditable` en `Shared/Domain`.
5. **Actor.**
   - `IHttpContextAccessor` en el interceptor: ganás cero cambios en slices y cubre Identity; perdés estado ambiental.
   - Servicio scoped con middleware: ganás un dato explícito; perdés una pieza más para lo mismo.
   - Recomiendo **`IHttpContextAccessor`**, con `user`, `anonymous` y `system`.
6. **Inmutabilidad.**
   - Pendiente: ganás un concepto por paso; perdés protección contra SQL a mano.
   - Trigger en este paso: ganás append-only real contra la app; perdés la convención de migraciones intacta.
   - Rol de base sin permisos: ganás lo que recomienda PostgreSQL; perdés un paso entero.
   - Recomiendo **pendiente**.
7. **Borrado de la cuenta.**
   - Las filas quedan con el GUID, sin FK: ganás trazabilidad; perdés supresión total (pide base legal).
   - Borrar las filas: ganás supresión; perdés la historia.
   - Recomiendo **quedan, sin FK**.
8. **Retención.**
   - No borrar en este paso, pendiente con `Sessions` e `IdempotencyKeys`: ganás un concepto por paso; la tabla crece.
   - Recomiendo **no borrar**.
9. **Regla de Dune.**
   - DUNE0017 dentro del paso; paso 10.1; o sin regla.
   - Recomiendo **dentro del paso**, como DUNE0016.

## Pendientes fuera de alcance (para `progreso.md`)

- Tabla de auditoría append-only: trigger en una migración (choca con `conventions.md:90`) o rol de base sin `UPDATE`/`DELETE`. Hoy la API se conecta como superusuario `postgres`, y eso también afecta a cualquier permiso futuro.
- Limpieza periódica de `AuditEntries`, junto con la de `Sessions` e `IdempotencyKeys`.
- El borrado en cascada de las tareas al borrar la cuenta no deja una fila por tarea.
- SQL crudo sobre una tabla auditada no deja fila, y DUNE0017 no lo ve.
- Auditar cambios de cuenta en Identity (roles, bloqueo, contraseña), probablemente en la Fase 4.
- Tres lecturas separadas de `ClaimTypes.NameIdentifier` (`GetCallerId`, `IdempotencyFilter`, interceptor).
- Leer la auditoría desde la API o un tablero.

## Qué quedó sin comprobar

- No corrí nada del repo: ni tests, ni smoke, ni `dotnet ef` sobre el proyecto real. El experimento usó un modelo mínimo con las mismas piezas.
- Que `UserManager` y `DeleteUser` compartan el mismo `AppDbContext` en el repo, y que entonces el perfil borrado quede `Deleted` en el mismo `SaveChanges`. Lo infiero de la DI y lo imité en E7 sin Identity.
- `IHttpContextAccessor` dentro de `WebApplicationFactory`/`TestServer`: en el experimento asigné `HttpContext` a mano, sin pipeline HTTP.
- Que `Activity.Current` exista en cada pedido del repo, también en el entorno `Testing`. El experimento creó la actividad con un `ActivityListener` propio.
- El interceptor registrado como singleton: E10 lo probó como scoped. Como sus dependencias (`IHttpContextAccessor`, `TimeProvider`) son singleton y no guarda estado, debería funcionar igual. [INFERENCIA]
- El analyzer DUNE0017: no escribí código de Roslyn.
- La comparación de `OccurredAt` con `FakeTimeProvider` y la precisión de microsegundos.
- Audit.NET: leí su README y su paquete; no lo ejecuté. No verifiqué si su interceptor de comandos audita `ExecuteUpdate`.
- La base legal para guardar el seudónimo después del borrado: es una decisión de negocio, no algo que se compruebe.

## Fuentes

- https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors
- https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/events
- https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete
- https://learn.microsoft.com/en-us/ef/core/saving/transactions
- https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete
- https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/use-http-context?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/UserStore.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Stores/src/IdentityUser.cs
- https://www.postgresql.org/docs/18/plpgsql-trigger.html
- https://www.postgresql.org/docs/18/sql-createtrigger.html
- https://www.postgresql.org/docs/18/sql-altertable.html
- https://www.postgresql.org/docs/18/functions-admin.html
- https://www.postgresql.org/docs/18/role-attributes.html
- https://www.postgresql.org/docs/18/ddl-priv.html
- https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html
- https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32016R0679
- https://github.com/thepirat000/Audit.NET/blob/master/src/Audit.EntityFramework/README.md
- https://github.com/thepirat000/Audit.NET/blob/master/LICENSE
- https://api.nuget.org/v3/catalog0/data/2026.09.15.06.53.34/audit.entityframework.core.33.0.0.json
