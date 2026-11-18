# Paso 23.2: extraer `Tasks` a su propio proceso (investigación)

- Fecha: 2026-11-18.
- Estado: **Listo** como investigación. No hace falta ninguna herramienta nueva. Hay 8 choques con los docs y 12 decisiones, cada una con mi recomendación.
- Alcance: el paso 23.2 del [plan](../plan.md) (`plan.md:409-413`). Concepto: contrato remoto; una llamada entre features que cruza el límite pasa a ser HTTP.
- Base: rama `fase7/23-2-extraer-tasks`, igual a `main` (`f2fd5b4`, con el paso 23.1).
- Qué NO se vuelve a investigar: lo decidió Tobi en el paso 23 con [paso-23-servicio-gateway.md](paso-23-servicio-gateway.md) (nota de `plan.md:411`): autenticación opción A (cookie compartida y validación remota, §2.3), DTOs del lado del que llama y cliente HTTP tipado (§4.1), orden T1 en `DeleteUser` (§4.3), solo timeouts (§4.2), tablas de `platform` compartidas y migradas por el monolito (§4.4), endpoints internos protegidos solo por red (§4.5) y disposición L2 (§5.2). Este archivo cubre **lo nuevo**: las llamadas que trajo el paso 23.1 (`Projects`) y cómo se arma cada pieza en este código.

**Cómo leer las marcas:**
- `[FUENTE]`: lo leí en un documento primario. Cada uno tiene su link.
- `[CÓDIGO archivo:línea]`: lo leí en el repo.
- `[EXPERIMENTO EX-x]`: lo corrí en esta sesión, en el scratchpad. Detalle en "Experimentos".
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo medí. Queda pendiente para el paso.

## Respuesta corta

Hoy cinco llamadas cruzan el borde de `Tasks`. Después del paso, cada una es una request HTTP a un endpoint bajo `/internal/v1/`, que el gateway no publica:

| # | Quién llama → quién responde | Hoy (en memoria) | Contrato remoto recomendado |
|---|---|---|---|
| C1 | Tasks → Auth: ¿la sesión sigue activa? y permisos | `SessionHttpPolicy.ValidatePrincipalAsync` + `IAuthModule.HasPermissionAsync`/`CanAccessAsync` | `POST /internal/v1/session-checks` → `200 {permissions}` o `404` |
| C2 | Tasks → Projects: ¿el proyecto es mío y está activo? | `IProjectsModule.FindAssignableProjectAsync` | `GET /internal/v1/owners/{ownerId}/projects/{projectId}` → `200 {isArchived}` o `404` |
| C3 | Projects → Tasks: contadores de tareas por proyecto | `ITasksModule.CountByProjectAsync` | `GET /internal/v1/task-counts?projectId=…&projectId=…` → `200 {counts:[…]}` |
| C4 | Users → Tasks: borrar las tareas del dueño | `ITasksModule.DeleteOwnedTasksAsync` | `DELETE /internal/v1/owners/{ownerId}/tasks` → `204` siempre |
| C5 | Auth arma roles con los permisos de Tasks | `TaskPermissions` (constantes) en `RolePermissions.cs` | No es una llamada: cada lado tiene su copia de los textos y un test compara las dos |

Tres cosas nuevas que el paso tiene que resolver y que el paso 23 no vio:

1. **Si Auth o Tasks no responden, la API tiene que devolver 503, no 500 ni 401.** Lo medí: un timeout de `HttpClient` llega como `TaskCanceledException` con un `TimeoutException` adentro, no como `HttpRequestException`. Un `catch (HttpRequestException)` no lo ve y la request termina en 500 (EX-2H). Y si Auth no responde mientras Tasks valida la cookie, Tasks **no** tiene que rechazar la sesión: hoy `ValidatePrincipalAsync` rechaza y borra la cookie (`SessionHttpPolicy.cs:97-98`), y eso desloguearía al usuario por una caída ajena.
2. **Dune y `check-structure.mjs` chocan con un endpoint interno.** DUNE0011 exige que todo grupo del host empiece con `/api` (`ApiRouteAnalyzer.cs:70-76`), y el limiter global contaría las llamadas internas como anónimas de una sola IP, con 120 por ventana para todos los usuarios juntos (`ApiRateLimiting.cs:41-42`, `ApiRateLimitOptions.cs:19`). Eximirlas choca con la regla "`DisableRateLimiting` solo en `backend/Features/Health`" (`check-structure.mjs:251-261`). Son cambios de regla con su fixture (reglas 4 y 9).
3. **Dos hosts en un mismo proyecto de tests chocan por el nombre `Program`.** En .NET 10, un generador crea `public partial class Program` en cada host. Si el test escribe `Program`, falla con CS0433 (EX-2H). Se arregla sin alias: cada `WebApplicationFactory<T>` usa un tipo marcador del ensamblado de su host, porque `TEntryPoint` es "A type in the entry point assembly of the application" ([FUENTE: Learn, WebApplicationFactory](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1?view=aspnetcore-10.0)). Lo probé: los dos hosts arrancan en un test y el cliente tipado de uno le habla al otro en memoria (EX-2H).

## Glosario corto

- **Endpoint interno**: una URL que solo usa otro proceso de la API, nunca el navegador. Vive bajo `/internal/v1/`. El gateway no tiene ruta para ese prefijo, así que desde afuera no se llega.
- **Cliente HTTP tipado** (= el `I<Feature>Module` de hoy, pero por red): una clase que recibe un `HttpClient` y expone métodos con nombre de negocio.
- **Proceso caído**: el contenedor no acepta la conexión o no responde antes del timeout.
- **503 Service Unavailable**: "the server is currently unable to handle the request due to a temporary overload or scheduled maintenance, which will likely be alleviated after some delay" ([FUENTE: RFC 9110 §15.6.4](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.4)).
- **Tipo marcador**: una clase vacía y pública que solo sirve para que el test encuentre el ensamblado de un host.

---

## 1. Las llamadas que cruzan el borde hoy

Busqué `ITasksModule`, `IProjectsModule`, `IAuthModule`, `IUsersModule`, `TaskPermissions`, `ClaimsPrincipalExtensions`, `CrossContextTransaction`, `IdempotencyKeys` y `SharedAuditEntries` en `backend/` [CÓDIGO]. Este es el mapa. Solo cuenta lo que cruza el borde de `Tasks`; lo que queda dentro del monolito (por ejemplo `Projects` → `IUsersModule.FindDisplayNamesAsync`, `ProjectResponses.cs:28`) no cambia.

| Llamada | Dónde | Qué hace |
|---|---|---|
| `IAuthModule.HasPermissionAsync(callerId, TaskPermissions.Create)` | `CreateTask.cs:36` | Permiso para crear |
| `IAuthModule.CanAccessAsync(callerId, item, TaskPermissions.X)` | `GetTask.cs:32`, `UpdateTask.cs:38`, `DeleteTask.cs:26` | Dueño o permiso |
| `IProjectsModule.FindAssignableProjectAsync(callerId, projectId)` | `CreateTask.cs:41` | El proyecto existe, es de quien llama y dice si está archivado. Devuelve `null` si no existe o no es suyo (`ProjectsModule.cs:16-22`) |
| `ITasksModule.CountByProjectAsync(ids)` | `ProjectResponses.cs:27` | Una sola consulta agrupada para todos los proyectos de la página (`TasksModule.cs:22-33`) |
| `ITasksModule.DeleteOwnedTasksAsync(authId)` | `DeleteUser.cs:43` | Borra las tareas con `SaveChanges`, dentro del `TransactionScope` de `DeleteUser.cs:33` |
| `ClaimsPrincipalExtensions.GetCallerId` | 5 endpoints de Tasks (por ejemplo `CreateTaskEndpoint.cs:20`) | Lee `NameIdentifier` de la cookie |
| `TaskPermissions.*` | `RolePermissions.cs:18-21,29-32,36` | Auth arma los roles con los textos de Tasks |
| Cookie: `SessionHttpPolicy.ValidatePrincipalAsync` | `SessionHttpPolicy.cs:89-99` | Busca la sesión en `auth.Sessions`, la toca o la cierra |
| `IdempotencyFilter` | `CreateTaskEndpoint.cs:10`, `IdempotencyFilter.cs:45-79` | `POST /tasks` corre **dentro** de un `TransactionScope` |
| `MapSharedAuditEntries` | `TasksDbContext.cs:21` | Auditoría en `platform.AuditEntries` |

Dos datos que importan para el diseño:

- `ProjectResponses.ComposeAsync` lo usan las cuatro slices de `Projects`, también `CreateProject` y `UpdateProject`, **después** de guardar (`CreateProject.cs:52`, `UpdateProject.cs` al final). Si Tasks no responde, la escritura ya se hizo (sección 2.3).
- `CreateTask` corre dentro del `TransactionScope` del filtro de idempotencia cuando llega `Idempotency-Key` (`IdempotencyFilter.cs:45,58`). Después del paso, ese scope envuelve dos llamadas HTTP de lectura (C1 no, porque la cookie se valida antes, en `UseAuthentication`; C2 sí). Ver sección 7.

## 2. Los contratos remotos, uno por uno

Reglas comunes, ya decididas en el paso 23: el que llama define sus DTOs y su cliente tipado; el cliente lleva solo `HttpClient.Timeout`; sin reintentos (paso 29). Lo que agrego:

- **Prefijo `/internal/v1/`.** Fuera de `/api`, así el antiforgery no lo cubre (`SessionHttpPolicy.cs:39` solo mira `/api`) y el gateway no lo publica (`gateway/appsettings.json` solo enruta `/api/{**rest}`). El `v1` va en la ruta, igual que la API pública, para poder cambiar el contrato con expand-contract.
- **Sin cookie y sin `RequireAuthorization`.** Lo protege la red (decisión del paso 23). El grupo interno necesita `DisableRateLimiting()`, porque sin cookie el limiter global lo cuenta como anónimo por IP (`ApiRateLimiting.cs:41-42`): todas las requests de todos los usuarios que pasan por Tasks compartirían un balde de 120 por ventana (`ApiRateLimitOptions.cs:19`) [INFERENCIA].
- **JSON en camelCase**, igual que la API pública. Los errores, `ProblemDetails` con `code` (`BackendInfrastructure.cs:17-20`).

### C1. Tasks → Auth: sesión activa y permisos, en una sola llamada

**Respuesta:** `POST /internal/v1/session-checks` con `{ "userId": "...", "sessionId": "..." }`. Si la sesión sigue activa: `200 { "permissions": ["Permissions.Tasks.Create", ...] }`. Si no existe, venció o es de otro usuario: `404`.

Por qué así:

- **Una llamada, no dos.** Hoy cada request con cookie hace una consulta de sesión (`Sessions.cs:28-52`), y la consulta de permisos solo cuando hace falta (`AccessPolicy.cs:43-46`). En remoto, dos llamadas duplican la latencia y la superficie de falla. Los permisos de un usuario son pocos textos (`RolePermissions.cs:11-38`), así que devolverlos siempre cuesta poco [INFERENCIA]. Tasks guarda la respuesta en un servicio *scoped* (vive lo que dura la request), igual que la memoria por request de `AccessPolicy` (`AccessPolicy.cs:13,21-29`). Entonces C1 ocurre una vez por request, y la regla "el dueño puede" se decide adentro de Tasks sin llamar a nadie.
- **`POST` y no `GET`.** Validar la sesión no es solo leer: toca `LastSeenAt` y cierra la sesión vencida por inactividad (`Sessions.cs:37-49`). GET es "safe" cuando "their defined semantics are essentially read-only" ([FUENTE: RFC 9110 §9.2.1](https://www.rfc-editor.org/rfc/rfc9110.html#section-9.2.1)). Además, el repo prohíbe que una slice GET escriba (DUNE0018, "CQRS liviano", `DuneDiagnostics.cs` descripción de "Slice de lectura que escribe"). Y con el id de sesión en el body, no queda escrito en la ruta que loguea `UseSerilogRequestLogging` (`Program.cs:36`) [INFERENCIA]. DUNE0016 (todo POST encadena `WithIdempotency`) deja fuera a Auth ("Auth queda fuera: sus POST no crean recursos de negocio", `DuneDiagnostics.cs`, `IdempotentPost`), así que no choca.
- **Cómo se engancha en la cookie.** Tasks registra el mismo manejador de cookie con el mismo esquema (requisito de cookie sharing, ya citado en el paso 23 §2.2). Su `OnValidatePrincipal` llama a C1. Learn: "The ValidatePrincipal event can be used to intercept and override validation of the cookie identity. Validating the cookie on every request mitigates the risk of revoked users accessing the app" ([FUENTE: Learn, cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0#react-to-back-end-changes)). La misma página avisa: "Validating authentication cookies for all users on every request can result in a large performance penalty for the app". El repo ya acepta ese costo hoy con la consulta a la base (`architecture.md:79`).
- **Dónde guardar los permisos.** Dos formas:

| Opción | Cómo | Gana | Pierde |
|---|---|---|---|
| **P1. Servicio scoped `SessionGrant`** (recomendada) | `OnValidatePrincipal` guarda los permisos en un servicio scoped; las slices lo leen | El principal no cambia. Nada se reescribe en la cookie | Un servicio más |
| P2. Claims en el principal | `context.ReplacePrincipal` con claims de permiso | Las slices leen `User` | Learn dice que para cambiarlo de forma no destructiva hay que "call `context.ReplacePrincipal` and set the `context.ShouldRenew` property to `true`" ([FUENTE](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0#react-to-back-end-changes)): renovar reescribe la cookie, y la sesión del repo usa `AllowRefresh = false` (`SessionHttpPolicy.cs:61`) |

**Las constantes de permisos (C5).** Con dos ensamblados, `TaskPermissions` no puede quedar en `Tasks/Contracts/`: DUNE0015 exige que otra feature **del mismo ensamblado** use cada contrato ("ninguna otra feature lo usa", `ContractsAnalyzer.cs:35-46`). En el ensamblado de Tasks no hay otra feature. Recomiendo:

- Tasks: `TaskPermissions` pasa a ser `internal` en la raíz de la feature (las slices lo usan).
- Auth: una copia `internal` de los cuatro textos en `Auth/Access/`, que `RolePermissions` usa.
- Un test en `backend/Tests/` compara las dos copias. Es el "test de contrato" que el paso 23 §4.1 pide para los DTOs del que llama.

[INFERENCIA] Es lo mismo que hace eShop con los eventos de integración: cada servicio tiene su copia y nadie comparte el tipo (paso 23 §4.1).

### C2. Tasks → Projects: ¿puedo poner la tarea en este proyecto?

**Respuesta:** `GET /internal/v1/owners/{ownerId}/projects/{projectId}` → `200 { "isArchived": false }` si el proyecto existe y es de `ownerId`; `404` si no existe o es de otro.

- La regla "el proyecto es tuyo" queda en Projects, como hoy (`ProjectsModule.cs:19-21`): devolver 404 también para "es de otro" no filtra la existencia de proyectos ajenos [INFERENCIA]. Tasks traduce: `404` → `TaskErrors.ProjectNotAssignable` (400); `isArchived: true` → `TaskErrors.ProjectArchived` (409) (`TaskErrors.cs:19-25`). La respuesta pública de `POST /tasks` no cambia.
- La forma `owners/{ownerId}/...` es la misma de C4, que el paso 23 ya eligió (`paso-23-servicio-gateway.md` §4.1).
- GET es seguro: solo lee (`ProjectsModule.cs:18`).
- **DUNE0015 en el monolito.** `FindAssignableProjectAsync` y `ProjectAssignmentInfo` solo los usa Tasks. Cuando Tasks se va, ninguna feature del monolito los usa y DUNE0015 falla. Se borran; el endpoint interno los reemplaza [INFERENCIA, por `ContractsAnalyzer.cs:35-46`].

### C3. Projects → Tasks: contadores en lote

**Respuesta:** `GET /internal/v1/task-counts?projectId={id}&projectId={id}...` → `200 { "counts": [ { "projectId": "...", "total": 3, "completed": 1 } ] }`. Un proyecto sin tareas no aparece; Projects lo trata como `0/0`, igual que hoy (`ProjectResponses.cs:8,36`).

- **Una sola request por página, no una por proyecto.** Hoy ya es una consulta agrupada (`TasksModule.cs:28-32`). Mantenerla evita el N+1 (una llamada por fila).
- **GET con el parámetro repetido.** Minimal APIs lo enlaza a un arreglo: "GET /tags?q=1&q=2&q=3 … `app.MapGet("/tags", (int[] q) => …)`" ([FUENTE: Learn, parameter binding](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding?view=aspnetcore-10.0)). Lo probé con `Guid[]` (EX-2H).
- **Es lo que hace eShop:** `api.MapGet("/items/by", GetItemsByIds)` con `int[] ids`, "Batch get catalog items" ([FUENTE: eShop, CatalogApi.cs](https://github.com/dotnet/eShop/blob/main/src/Catalog.API/Apis/CatalogApi.cs)).
- **¿Entra en la URL?** La página máxima es 100 (`KeysetCursor.cs:8`). Cada id ocupa unos 47 bytes (`projectId=` + 36 + `&`), unos 4,7 KB en total [INFERENCIA]. Kestrel acepta una línea de request de hasta "8,192 bytes (8 KB)" por defecto ([FUENTE: Learn, MaxRequestLineSize](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserverlimits.maxrequestlinesize?view=aspnetcore-10.0)). Entra, con margen. El endpoint rechaza más de 100 ids con 400.
- **Por qué no `POST` con una lista en el body.** POST no es seguro ni idempotente ([FUENTE: RFC 9110 §9.2.2](https://www.rfc-editor.org/rfc/rfc9110.html#section-9.2.2): "PUT, DELETE, and safe request methods are idempotent"), y en una feature DUNE0016 le exigiría `WithIdempotency` (`DuneDiagnostics.cs`, `IdempotentPost`). Un body en GET tampoco: "content received in a GET request has no generally defined semantics" ([FUENTE: RFC 9110 §9.3.1](https://www.rfc-editor.org/rfc/rfc9110.html#section-9.3.1)).

### C4. Users → Tasks: borrar las tareas del dueño

Ya decidido en el paso 23 (§4.3, T1): `DELETE /internal/v1/owners/{ownerId}/tasks` → `204`, también si no había tareas. DELETE es idempotente ([FUENTE: RFC 9110 §9.2.2](https://www.rfc-editor.org/rfc/rfc9110.html#section-9.2.2)). Lo nuevo del 23.1: `DeleteUser` también borra proyectos (`DeleteUser.cs:44`). `Projects` queda en el monolito, así que entra en la transacción local. El orden queda:

1. Buscar el perfil y chequear el permiso (`DeleteUser.cs:35-41`), **fuera** de la transacción.
2. `DELETE` remoto de las tareas. Si falla: 503 y no se borró nada.
3. `CrossContextTransaction.Begin()`: proyectos, claves de idempotencia, perfil y cuenta (`DeleteUser.cs:44-53`).

[INFERENCIA] Hueco que queda: entre el paso 2 y el 3, la sesión sigue viva y el usuario podría crear una tarea, que quedaría huérfana. Es corto y lo cierra el outbox (paso 24). Lo anoto como pendiente, no lo resuelvo en este paso.

### Tabla de contratos

| # | Método y URL | Request | Respuestas | Idempotente | Timeout del cliente |
|---|---|---|---|---|---|
| C1 | `POST /internal/v1/session-checks` | `{userId, sessionId}` | `200 {permissions: string[]}`; `404` sesión inactiva | Sí en efecto (tocar o cerrar dos veces da lo mismo) [INFERENCIA] | 2 s |
| C2 | `GET /internal/v1/owners/{ownerId}/projects/{projectId}` | — | `200 {isArchived}`; `404` | Sí (safe) | 2 s |
| C3 | `GET /internal/v1/task-counts?projectId=…` (1 a 100) | — | `200 {counts: [{projectId, total, completed}]}`; `400` sin ids o más de 100 | Sí (safe) | 2 s |
| C4 | `DELETE /internal/v1/owners/{ownerId}/tasks` | — | `204` | Sí | 2 s |

Los 2 s salen del timeout de los health checks (`BackendInfrastructure.cs:13`), como propuso el paso 23 §4.2. Cualquier otro status (5xx, 401, 429) el cliente lo trata como "no disponible" (sección 3).

## 3. Qué devuelve la API pública si el otro proceso no responde

### 3.1 Qué ve el cliente HTTP

- Sin conexión: `HttpRequestException`. Sin respuesta antes del timeout: `TaskCanceledException` con `TimeoutException` adentro. [EXPERIMENTO EX-2H] El mensaje fue "The request was canceled due to the configured HttpClient.Timeout of 2 seconds elapsing", con `System.TimeoutException` como excepción interna. Un `catch (HttpRequestException)` no lo atrapó y la respuesta fue 500.
- La doc solo dice el valor por defecto ("The default value is 100,000 milliseconds (100 seconds)") y que, con un token propio, "only the shorter of the two timeouts will apply" ([FUENTE: Learn, HttpClient.Timeout](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0)). El tipo de la excepción lo medí; no lo encontré escrito.
- Hay que separar "venció mi timeout" de "el navegador cortó la request": en el segundo caso `RequestAborted` está cancelado y no corresponde un 503 [INFERENCIA].

### 3.2 Qué status

| Status | Qué dice RFC 9110 | ¿Encaja? |
|---|---|---|
| 502 | "the server, while acting as a gateway or proxy, received an invalid response from an inbound server" ([§15.6.3](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.3)) | No: el monolito y Tasks no son un proxy; arman la respuesta |
| **503** | "currently unable to handle the request due to a temporary overload or scheduled maintenance, which will likely be alleviated after some delay" ([§15.6.4](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.4)) | **Sí**: la falta de la dependencia es temporal y reintentar más tarde puede andar |
| 504 | "while acting as a gateway or proxy, did not receive a timely response from an upstream server" ([§15.6.5](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.6.5)) | No, por la misma razón que 502. El gateway YARP sí puede dar 502/504, y es correcto: él es el proxy |

Recomiendo **503 con `ProblemDetails` y `code = "Http.ServiceUnavailable"`**. El código ya existe en el catálogo (`HttpErrors.cs:41`). Sin `Retry-After`: RFC 9110 lo deja como opcional, y el repo no sabe cuánto va a tardar el otro proceso [INFERENCIA].

### 3.3 Dónde se traduce la falla a 503

| Opción | Cómo | Gana | Pierde |
|---|---|---|---|
| **E1. Excepción propia + `GlobalExceptionHandler`** (recomendada) | Cada cliente convierte `HttpRequestException`, el timeout y un status inesperado en una sola excepción (por ejemplo `RemoteServiceUnavailableException`, en la librería compartida). `GlobalExceptionHandler` la escribe como 503 (`GlobalExceptionHandler.cs:13` hoy escribe siempre 500) | Un solo lugar. Funciona también en `OnValidatePrincipal`, que no devuelve `ErrorOr`. Las slices no cambian de firma. Es como se trata hoy la base caída: excepción y manejador | El 503 no aparece en la firma del método: hay que leer el cliente para saberlo |
| E2. `ErrorOr` en cada cliente | El cliente devuelve `Error.Custom(503, ...)`; `ApiResults` ya mapea 400-599 (`ApiResults.cs:32`) | Se ve en la firma | Cada slice propaga el error a mano. No sirve en `OnValidatePrincipal` |

### 3.4 Caso por caso

| Endpoint público | Proceso caído | Resultado recomendado | Por qué |
|---|---|---|---|
| Cualquiera de `/api/v1/tasks` | Auth (C1) | **503**. La cookie **no** se rechaza ni se borra | Con E1, la excepción sale de `OnValidatePrincipal` antes de `RejectPrincipal` (`SessionHttpPolicy.cs:97-98`). `UseExceptionHandler` está antes de `UseAuthentication` (`Program.cs:37,39`), así que la atrapa. Si Tasks rechazara, el usuario quedaría deslogueado por una caída de Auth |
| `POST /api/v1/tasks` | Projects (C2) | **503**, sin tarea creada | La tarea se guarda después del chequeo (`CreateTask.cs:41-59`). Con `Idempotency-Key`, el filtro no completa su transacción porque el status no es 2xx (`IdempotencyFilter.cs:65-79`): la clave se libera y el cliente puede reintentar con la misma |
| `GET /api/v1/projects` y `GET /projects/{id}` | Tasks (C3) | **503 en toda la lectura** (recomendado) | Ver 3.5 |
| `POST /api/v1/projects` | Tasks (C3) | **201 sin llamar a Tasks** | Un proyecto recién creado no tiene tareas: su id lo generó este request (`CreateProject.cs:41`) y una tarea solo puede apuntar a un proyecto existente del dueño (C2). Hoy `CreateProject` llama a `ComposeAsync` después de guardar (`CreateProject.cs:52`): con Tasks caído y sin `Idempotency-Key`, guardaría el proyecto y respondería 503, y el reintento daría 409 `NameTaken`. Recomiendo componer con `0/0` sin llamar |
| `PUT /api/v1/projects/{id}` | Tasks (C3) | **503 después de guardar**; el reintento con la misma versión responde 200 | `UpdateProject` guarda y después compone. Si el cliente reintenta con la versión vieja y los mismos datos, el código ya responde el estado actual (`UpdateProject.cs`, rama `project.Version != request.Version` con mismo nombre y archivado). Queda aceptable [INFERENCIA, NO PROBADO] |
| `DELETE /api/v1/users/me` y `/{authId}` | Tasks (C4) | **503**, nada borrado | Orden T1 (sección C4) |

### 3.5 `GET /projects` con Tasks caído: fallar o degradar

| Opción | Qué ve el usuario | Gana | Pierde |
|---|---|---|---|
| **D1. Fallar con 503** (recomendada) | La lista de proyectos no carga | Nada miente. El contrato no cambia | Una caída de Tasks apaga también la lista de proyectos |
| D2. Contadores en `0` | La lista carga con `0/0` | Projects sigue andando | Muestra un dato falso como si fuera cierto |
| D3. Contadores `null` | La lista carga y la UI dice "sin dato" | Es la degradación elegante que pide la industria | Cambia el contrato HTTP (`ProjectResponse` tiene `int`): por la regla 3, también el frontend y los tests. Es otro paso |

Qué hace la industria: Azure pide un modo degradado donde el usuario sepa "what functionality remains and what has changed" ([FUENTE: Azure Well-Architected, self-preservation](https://learn.microsoft.com/en-us/azure/well-architected/reliability/self-preservation)). Eso es D3, no D2. Recomiendo D1 porque D3 cambia el contrato (regla 7, no ampliar el paso) y porque el paso 24.1 reemplaza esta lectura por una proyección que ya no llama a Tasks (`plan.md:422-426`). Anoto D3 como pendiente.

## 4. Proyectos y carpetas (L2 en este repo)

### 4.1 Qué lee Dune hoy

- Las reglas reconocen una feature por el path `backend/Features/<X>/` (`DunePaths.cs:33-51`) y el namespace esperado sale del path después de `backend/` (`DunePaths.cs:91-113`). Infrastructure, por `backend/Infrastructure/` (`DunePaths.cs:27-28`).
- `Dune.props` activa los analyzers solo en `TemplateApp`, `TemplateApp.Tests` y `TemplateApp.Gateway` (`dune/Dune.props:2,12`).
- DUNE0005 exime solo `backend/Program.cs` (`NamingAnalyzer.cs:39`). Un `Program.cs` de otro host fallaría: el generador de .NET 10 crea `Program` en el namespace global ([FUENTE: Learn, ASP.NET Core 10 release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0): "In .NET 10, a source generator is used to generate the `public partial class Program` declaration if the programmer didn't declare it explicitly").
- `check-structure.mjs` lee `backend/Features`, `Shared`, `Infrastructure`, `backend/TemplateApp.csproj` y `backend/Program.cs` (`check-structure.mjs:51-62`), y exige `InternalsVisibleTo` solo hacia `TemplateApp.Tests` (`check-structure.mjs:177-196`).

### 4.2 Propuesta concreta

La idea: **ningún archivo `.cs` cambia de carpeta**. Los `.csproj` nuevos viven en carpetas propias y compilan los archivos donde están, así Dune sigue leyendo los mismos paths y namespaces.

```
backend/
  TemplateApp.csproj            ← monolito (Auth, Users, Projects, Health). Igual que hoy, sin Tasks
  Program.cs
  Platform/TemplateApp.Platform.csproj   ← librería: compila ../Infrastructure/** y ../Shared/**
  Hosts/Tasks/TemplateApp.Tasks.csproj   ← host de Tasks: compila ../../Features/Tasks/**
  Hosts/Tasks/Program.cs
  Hosts/Tasks/TasksHost.cs      ← tipo marcador para los tests
  Hosts/Tasks/appsettings.json
  Infrastructure/  Shared/  Features/   ← sin cambios de lugar
  Tests/TemplateApp.Tests.csproj         ← referencia los dos hosts
```

| Proyecto | SDK | Compila | Referencia |
|---|---|---|---|
| `TemplateApp.Platform` | `Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App` | `../Infrastructure/**/*.cs`, `../Shared/**/*.cs` | Paquetes de EF, Serilog, OpenTelemetry, NetEscapades, ErrorOr (los de hoy) |
| `TemplateApp` (monolito) | `Microsoft.NET.Sdk.Web` | Su glob de hoy **menos** `Infrastructure/**`, `Shared/**`, `Features/Tasks/**`, `Platform/**`, `Hosts/**`, `Tests/**` | `TemplateApp.Platform` |
| `TemplateApp.Tasks` | `Microsoft.NET.Sdk.Web` | `Program.cs`, `TasksHost.cs` y `../../Features/Tasks/**/*.cs` menos `*Tests.cs` | `TemplateApp.Platform` |

Por qué así:

- **El glob por defecto del SDK abarca todas las subcarpetas.** Compile incluye `**/*.cs` y solo excluye `bin` y `obj` del propio proyecto ([FUENTE: Learn, project SDK, default includes](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview#default-includes-and-excludes)). Como `TemplateApp.csproj` está en `backend/`, sin `Compile Remove` compilaría también el código de Tasks y los `obj/` de los proyectos hijos. Hoy ya usa `Compile Remove` para los tests (`TemplateApp.csproj:36-39`).
- **Un `.csproj` por carpeta.** Si el host de Tasks viviera en `backend/Features/Tasks/`, su `obj/` quedaría adentro de la feature y dentro del glob del monolito. Por eso `Hosts/Tasks/` [INFERENCIA].
- **`Hosts/` y no `services/` en la raíz.** Dentro de `backend/`, Dune sigue viendo el host (`DunePaths.IsBackendFile`, `DunePaths.cs:15-16`). Fuera, ninguna regla lo miraría.
- **Es la forma de eShop:** cada servicio referencia una librería común, `eShop.ServiceDefaults`. Por ejemplo `Ordering.API.csproj` tiene `<ProjectReference Include="..\eShop.ServiceDefaults\eShop.ServiceDefaults.csproj" />` ([FUENTE: eShop, Ordering.API.csproj](https://github.com/dotnet/eShop/blob/main/src/Ordering.API/Ordering.API.csproj)).
- **Lo que cambia de `internal` a `public`.** `Platform` es otro ensamblado. Un tipo de `Infrastructure/` que usa una feature tiene que ser `public`, porque `InternalsVisibleTo` hacia otro ensamblado de producto está prohibido (`check-structure.mjs:191-195`). Ejemplo: `IdempotencyKeys` (`IdempotencyKeys.cs:5`, usado en `DeleteUser.cs:20`). Ya lo avisó el paso 23 §5.2.
- **`InternalsVisibleTo`.** Monolito y Tasks: `<InternalsVisibleTo Include="TemplateApp.Tests" />` cada uno. `Platform` también, si los tests usan sus internos. La regla de `check-structure` ya lo permite (solo mira el destino) [CÓDIGO `check-structure.mjs:191`].

### 4.3 Qué va dónde, por pieza nueva

| Pieza | Lugar | Por qué |
|---|---|---|
| Cliente de C1 (sesión y permisos) | `Features/Tasks/Access/AuthSessionClient.cs` + `SessionGrant.cs` | Carpeta técnica propia, como `Auth/Access/` (`conventions.md:18`) |
| Cliente de C2 | `Features/Tasks/ProjectsClient.cs` | Raíz de la feature: lo usa una slice |
| Cliente de C3 | `Features/Projects/TaskCountsClient.cs` | Lo usa `ProjectResponses`, en la raíz |
| Cliente de C4 | `Features/Users/OwnedTasksClient.cs` | Lo usa `DeleteUser` |
| Endpoint C1 | `Features/Auth/CheckSession/CheckSessionEndpoint.cs` + `CheckSessionTests.cs` | Es una slice: `check-structure` exige su test (`check-structure.mjs:98-103`) |
| Endpoint C2 | `Features/Projects/FindAssignableProject/...` | Igual |
| Endpoints C3 y C4 | `Features/Tasks/CountTasksByProject/...`, `Features/Tasks/DeleteOwnedTasks/...` | Igual |
| Grupo `/internal/v1` | `Infrastructure/Http/InternalApi.cs` (`MapInternalApi()`), espejo de `VersionedApi.cs` | Un solo lugar declara el prefijo; cada feature recibe el grupo (`Map<Feature>InternalEndpoints(RouteGroupBuilder)`) |
| Opciones de las direcciones (`Services:Tasks:BaseAddress`, etc.) | Options con validador en cada feature que llama | DUNE0013 prohíbe leer `IConfiguration` suelto (`DuneDiagnostics.cs`, `ConfigurationAccess`) |
| Cookie y antiforgery comunes (nombre `__Host-session`, esquema, `X-XSRF-TOKEN`, middleware de antiforgery) | `Infrastructure/Http/SessionCookie.cs` en `Platform` | Los dos hosts los necesitan iguales: "a consistent scheme must be used across apps" (paso 23 §2.2). Hoy viven en `Auth/SessionHttpPolicy.cs:12-53` y `AuthFeature.cs:78-82`. Auth sigue dueño de `SignIn`, `SignOut` y la validación contra su tabla |
| `GetCallerId` | `Shared/Http/` (decisión 6) | Lo usan Tasks, Projects y Users, y Tasks ya no ve `Auth.Contracts`. `IdempotencyFilter.OwnerOf` hace lo mismo (`IdempotencyFilter.cs:140-147`) |

### 4.4 Imagen de Docker

Hoy el Dockerfile restaura un solo `.csproj` y su contexto es `backend/` (`backend/Dockerfile:3-6`). Con dos hosts y una librería:

- **Contexto:** `backend/` para los dos, porque los dos necesitan `Platform/`, `Infrastructure/` y `Shared/`. En Compose, `dockerfile` se resuelve desde el contexto: "A relative path is resolved from the build context" ([FUENTE: Docker, Compose build](https://docs.docker.com/reference/compose-file/build/)). Entonces `tasks` usa `context: ./backend` y `dockerfile: Hosts/Tasks/Dockerfile`.
- **Restore en capas:** copiar primero los tres `.csproj` que hacen falta (`Platform`, el host y `Directory.Build.props`/`dune/` si el build los importa) y después el código [INFERENCIA]. Ojo: `Directory.Build.props` está en la raíz del repo e importa `dune/Dune.props` (`Directory.Build.props:2`), fuera del contexto `backend/`. Hoy eso no pasa en la imagen porque el contexto es `backend/` y el `props` no llega [NO PROBADO: si hoy la imagen compila sin Dune, la nueva también].
- `.dockerignore` en `backend/` para no copiar `bin/`, `obj/` ni `Tests/` [INFERENCIA].

## 5. Tests: dos hosts en un mismo proceso de test

### 5.1 Qué pasa hoy

`ApiFixture` arranca un `WebApplicationFactory<Program>` (`ApiFixture.cs:12,58-59`). El proyecto de tests compila los `*Tests.cs` de las features y referencia el monolito (`TemplateApp.Tests.csproj:27,31`).

### 5.2 El choque de `Program` y cómo evitarlo

[EXPERIMENTO EX-2H] Dos hosts web vacíos, un proyecto de tests que referencia a los dos y escribe `WebApplicationFactory<Program>`: falla con `CS0433: El tipo 'Program' existe en 'HostA' y en 'HostB'`. Cada host tiene su `Program` público generado (fuente citada en 4.1).

| Opción | Cómo | Gana | Pierde |
|---|---|---|---|
| **M1. Tipo marcador por host** (recomendada) | `WebApplicationFactory<TemplateApp.MonolithHost>` y `WebApplicationFactory<TemplateApp.Hosts.Tasks.TasksHost>`. Nunca se escribe `Program` | Sin alias. Los tests de las slices de Tasks usan sus tipos como hoy | Un archivo marcador por host. Hay que cambiar `Program` por el marcador en `ApiFixture` |
| M2. `extern alias` | `<ProjectReference ... Aliases="tasks" />` y `extern alias tasks;` en cada archivo que nombra un tipo de Tasks ([FUENTE: Learn, extern alias](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/extern-alias)) | Se puede seguir usando `Program` del monolito | Cada test de Tasks (6 archivos de slice y los de `Tests/` que tocan tareas) necesita el alias y `tasks::` |
| M3. Dos proyectos de tests | `TemplateApp.Tests` y `TemplateApp.Tasks.Tests`, como eShop (`Ordering.FunctionalTests`, `Catalog.FunctionalTests`, [FUENTE: eShop tests/](https://github.com/dotnet/eShop/tree/main/tests)) | Cada servicio se prueba solo | Casi todos los tests de Tasks necesitan también el monolito (la cookie la emite Auth), así que el segundo proyecto igual referenciaría los dos hosts |

[EXPERIMENTO EX-2H] Con M1 compiló y corrió: los dos hosts arrancaron en el mismo test, sin alias. Con M2 también.

### 5.3 Cómo se habla un host con el otro en memoria

- `WebApplicationFactory.Server` "Gets the TestServer created by this factory" ([FUENTE: Learn, WebApplicationFactory](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1?view=aspnetcore-10.0)), y `TestServer.CreateHandler()` da un `HttpMessageHandler` que entra a ese host sin red.
- El cliente tipado de un host recibe ese handler con `ConfigurePrimaryHttpMessageHandler`: "The delegate is used to create and configure the primary `HttpMessageHandler` used by that client" ([FUENTE: Learn, IHttpClientFactory](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0)). Se registra en `ConfigureTestServices`, que Learn usa para "Services can be overridden in a test" ([FUENTE: Learn, integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)).
- [EXPERIMENTO EX-2H] `HostA` (monolito simulado) llamó a `HostB` por `GET /internal/v1/task-counts?projectId=…&projectId=…` a través de `b.Server.CreateHandler()`. Respuesta `200` con los dos contadores.

**El ciclo.** El monolito llama a Tasks (C3, C4) y Tasks llama al monolito (C1, C2). Los handlers se crean en un delegado, que corre la primera vez que se usa el cliente. Entonces el fixture puede crear las dos factories y cruzarlas: cada delegado lee `otra.Server` recién cuando hace falta [INFERENCIA; EX-2H probó una sola dirección].

**Probar "el otro proceso está caído".** Un handler propio que tira `HttpRequestException` o espera más que el timeout, puesto con el mismo `ConfigurePrimaryHttpMessageHandler`. No hace falta WireMock (eso es del paso 29). EX-2H lo midió con una dirección que no responde: timeout de 2 s y `TaskCanceledException`.

**La cookie entre hosts.** Data Protection usa una carpeta de claves de la config (`BackendInfrastructure.cs:49-54`). En tests, las dos factories tienen que apuntar `DataProtection:KeysDirectory` a la misma carpeta temporal, o la cookie que emite el monolito no la acepta Tasks (paso 23, EX-AF) [NO PROBADO en el fixture real].

## 6. Dos procesos migrando la misma base

### 6.1 Cómo arranca hoy

`DatabaseInitializer.InitializeAsync` (`DatabaseInitializer.cs:13-45`), bajo un advisory lock de PostgreSQL (`:22-24`):

1. Si existe `public.__EFMigrationsHistory` (base heredada), migra `AppDbContext` y, para cada contexto que "adopta tablas heredadas", marca su `InitialCreate` como aplicada (`:27-32`, `BaselineAsync :61-70`).
2. Migra todos los contextos registrados como "migrados" (`:34-35`).
3. Corre los seeders (`:37-38`).

Quién adopta tablas heredadas: Auth, Users, Tasks y Platform (`AuthFeature.cs:20`, `TasksFeature.cs:11`, `BackendInfrastructure.cs:29`). Projects no (`ProjectsFeature.cs:11`). El health de migraciones mira los contextos registrados como migrados (`PendingMigrationsHealthCheck.cs:18`).

### 6.2 Qué se rompe al separar

- **El monolito deja de marcar la línea base de Tasks**, porque `TasksDbContext` ya no está registrado en él. Tasks tiene que hacerlo él mismo [INFERENCIA].
- **El orden importa en una base heredada.** Si Tasks marcara su `InitialCreate` y moviera su tabla (`MoveTasksToSchema`) antes de que `AppDbContext` aplique sus últimas migraciones sobre `public."TaskItems"` (por ejemplo `ContractTaskIsCompleted` o `DropCrossFeatureForeignKeys`), esas migraciones fallarían porque la tabla ya no está en `public` [INFERENCIA].
- **`AddBackendInfrastructure` registra `PlatformDbContext` como migrado** (`BackendInfrastructure.cs:29`). Si Tasks usa el mismo método, migraría `platform`, contra la decisión "las migra el monolito".
- **`DatabaseHealthCheck` usa `PlatformDbContext`** (`DatabaseHealthCheck.cs:38-47`) y el filtro de idempotencia también (`IdempotencyFilter.cs:41`). Tasks lo necesita registrado, pero no migrado.

### 6.3 Recomendación

| Opción | Cómo | Gana | Pierde |
|---|---|---|---|
| **G1. El paso heredado vive en `Platform` y lo corren los dos** (recomendada) | Los dos hosts registran `AppDbContext`. Bajo el mismo lock, el primero que entra migra lo heredado completo. Cada host marca la línea base solo de **sus** contextos. Platform: el monolito la registra como migrada; Tasks, como contexto común (un `AddSharedDbContext` sin la marca `MigratedContext`) | No importa quién arranca primero. `DatabaseInitializer` casi no cambia | La imagen de Tasks lleva `AppDbContext` y sus 11 migraciones viejas (`Infrastructure/Persistence/Migrations/`) |
| G2. Tasks espera al monolito | `depends_on: api: condition: service_healthy` en Compose; Tasks no conoce `AppDbContext` y solo marca su línea base | Tasks queda limpio | El orden queda en Compose, no en el código: en tests o en otro orquestador hay que repetirlo |

Recomiendo G1 **y** además `depends_on` con `service_healthy` (Compose): en una base nueva, Tasks escribe en `platform.IdempotencyKeys` y `platform.AuditEntries` (`TasksDbContext.cs:21`), que crea el monolito. El ready de Tasks puede mirar también "`platform` sin migraciones pendientes" (solo leer, sin migrar), así no se declara listo antes [INFERENCIA].

[NO PROBADO] Dos procesos reales contra la misma base. `PerFeatureContextsTransitionTests` prueba la transición con un host; el paso necesita una versión con los dos.

## 7. Reglas nuevas de Dune (regla 4)

### 7.1 Recomendadas

**R1. "El monolito y Tasks no se compilan uno al otro"** (en `check-structure.mjs`, como la regla "gateway como única entrada", `check-structure.mjs:404-434`).

- Qué mira: `backend/TemplateApp.csproj` y `backend/Hosts/Tasks/TemplateApp.Tasks.csproj` solo tienen `ProjectReference` a `TemplateApp.Platform`; el monolito tiene `Compile Remove="Features/Tasks/**"`; el host de Tasks no incluye otra carpeta de `Features/`.
- Por qué: es el límite de despliegue que el paso enseña. Un `ProjectReference` de más lo borra sin que nada falle [INFERENCIA].
- Fixture válida: los dos `.csproj` del paso. Violación deliberada: el monolito con `<ProjectReference Include="Hosts/Tasks/TemplateApp.Tasks.csproj" />`, y otra con `Compile Include="../../Features/Users/**"` en Tasks. Se prueba en `check-structure.test.mjs`.

**R2. "Ninguna llamada remota dentro de una transacción entre contextos"** (analyzer nuevo, DUNE0022).

- Qué mira: en un método, después de `CrossContextTransaction.Begin()` (declaración `using` o bloque), una invocación sobre `HttpClient` o sobre un tipo con un `HttpClient` en su constructor (un cliente tipado) es error.
- Por qué: una request HTTP no se suma a la transacción, y nada lo avisa (paso 23 §4.3). `DeleteUser` es el caso: hoy borra las tareas dentro del scope (`DeleteUser.cs:33,43`).
- Fixture válida: el `DeleteUser` con orden T1 (llamada antes de `Begin`). Violación deliberada: la llamada dentro del `using`. Test en `Dune.Analyzers.Tests`.
- Límite: no ve a través de un filtro. `IdempotencyFilter` abre el scope y la slice corre adentro (`IdempotencyFilter.cs:45,58`): `CreateTask` hace C2 dentro de esa transacción. Es una lectura, así que no rompe nada, pero mantiene abierta la conexión y el candado de la clave mientras espera (hasta 2 s) [INFERENCIA]. Lo anoto en la descripción de la regla.

### 7.2 Reglas que el paso tiene que cambiar (regla 9, con su fixture)

| Regla | Choque | Cambio |
|---|---|---|
| DUNE0011 (`ApiRouteAnalyzer.cs:70-76`) | "Un grupo del host empieza con /api" | Permitir también el prefijo literal `/internal/v1`, solo creado en `Infrastructure` |
| DUNE0012 (`VersionedApiAnalyzer.cs:86-89`) | Mira solo grupos bajo `/api` | Sin cambio si el grupo interno no empieza con `/api`. Fixture que lo confirme |
| DUNE0005 (`NamingAnalyzer.cs:39`) | Exime solo `backend/Program.cs` | Eximir el `Program.cs` de cada host (`backend/Hosts/*/Program.cs`) |
| DUNE0015 (`ContractsAnalyzer.cs`) | Cuenta usos por ensamblado | Sin cambio de código. `Tasks/Contracts/` desaparece y en el monolito se borran `FindAssignableProjectAsync` y `ProjectAssignmentInfo`. `conventions.md:17` tiene que decir que "otra feature" quiere decir "del mismo ensamblado" |
| `check-structure`: `DisableRateLimiting` solo en Health (`:251-261`) | El grupo interno necesita quedar fuera del limiter | Permitirlo también en `Infrastructure/Http/InternalApi.cs` |
| `check-structure`: fuentes del producto (`:51-62`) y tests (`:108-131`) | Lee un solo `.csproj` | Leer los tres proyectos y los dos `Program.cs` |
| `Dune.props` (`:2,12`) | Condición por nombre | Sumar `TemplateApp.Platform` y `TemplateApp.Tasks` |

## 8. Herramientas (regla 5)

**Ninguna nueva.** Todo lo recomendado viene en el framework compartido o ya está aprobado:

| Pieza | ¿Nueva? | Nota |
|---|---|---|
| `IHttpClientFactory` / clientes tipados | No | En `Microsoft.AspNetCore.App` (paso 23, EX-PKG) |
| `TestServer.CreateHandler`, `ConfigurePrimaryHttpMessageHandler` | No | `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 ya está en `TemplateApp.Tests.csproj:14` |
| `Microsoft.Extensions.Http.Resilience`, WireMock | Sí | **No** se usan: son del paso 29 |
| Generador de clientes (NSwag, Kiota) | Sí | **No** se usa: el contrato lo escribe el que llama |

## Choques con los docs

1. **`conventions.md:19`**: "Como todo vive en un solo ensamblado, `internal` no separa una feature de otra". Deja de ser cierto para Tasks. Ya lo avisó ADR-0005 ("Hasta el paso 23.2").
2. **`conventions.md:17`** y **DUNE0015**: "que otra feature use cada contrato". Con dos ensamblados, Tasks no tiene a quién exponer `Contracts/`. Hay que decir que el contrato entre procesos es HTTP, no `Contracts/`.
3. **`conventions.md:14-15`**: la única puerta entre features es `I<Feature>Module`. Para Tasks, la puerta pasa a ser un endpoint interno y un cliente del lado del que llama.
4. **DUNE0011** ("Cada grupo del host empieza con /api") y la regla de `check-structure` "`DisableRateLimiting` solo en Health" chocan con el grupo `/internal/v1`. Cambio de regla con fixture (sección 7.2).
5. **`architecture.md:79`** ("revocar una sesión corta el acceso en la request siguiente"): sigue siendo cierto con C1, pero ahora depende de que Auth responda. Con Auth caído, Tasks responde 503; no deja pasar.
6. **ADR-0005 punto 4** y la nota de `plan.md:411` dicen "una librería compartida para `Infrastructure/` y `Shared/`". La librería también se lleva `AppDbContext` y sus migraciones viejas si se elige G1 (sección 6.3).
7. **`architecture.md:67`** (transacción entre módulos hasta el outbox del paso 24): `DeleteUser` la rompe para Tasks en este paso, como ya anticipó el paso 23. Queda un hueco corto (sección C4).
8. **`DeleteUser`** sigue siendo de Users, pero ahora también decide el orden de un borrado distribuido. `architecture.md` no tiene una sección para "consecuencias entre procesos": hay que escribirla.

## Decisiones (con mi recomendación)

En modo autónomo cada una la toma el agente con esta recomendación y la anota en `docs/decisions.tsv`.

1. **Sesión y permisos en una sola llamada** (C1, `POST /internal/v1/session-checks`, guardado en un servicio scoped). Recomiendo: sí, opción P1.
2. **Forma de C2:** `GET /internal/v1/owners/{ownerId}/projects/{projectId}`, 404 también para "es de otro". Recomiendo: sí.
3. **Forma de C3:** `GET` con `projectId` repetido, máximo 100. Recomiendo: sí (como eShop `/items/by`).
4. **Falla de una dependencia:** 503 con `code = Http.ServiceUnavailable`, traducido en un solo lugar (E1: excepción propia y `GlobalExceptionHandler`). Recomiendo: E1.
5. **`GET /projects` con Tasks caído:** D1, falla toda la lectura. D3 queda como pendiente.
6. **`GetCallerId` a `Shared/Http`** y la configuración común de cookie y antiforgery a `Infrastructure/Http/SessionCookie.cs`. Recomiendo: sí; Auth sigue dueño de abrir, cerrar y validar sesiones.
7. **`CreateProject` responde `0/0` sin llamar a Tasks.** Recomiendo: sí.
8. **Permisos de Tasks:** copia `internal` en Tasks y en Auth, con un test que compara. Recomiendo: sí.
9. **Carpetas:** `backend/Platform/`, `backend/Hosts/Tasks/`, ningún `.cs` se mueve. Recomiendo: sí.
10. **Tests:** un solo proyecto con tipos marcadores (M1). Recomiendo: M1.
11. **Migraciones:** G1 más `depends_on: service_healthy`. Recomiendo: G1.
12. **Reglas nuevas:** R1 (`check-structure`) y R2 (DUNE0022), más los cambios de 7.2. Recomiendo: las dos.

## Pendientes para `progreso.md`

- Contadores `null` con aviso en la UI cuando Tasks no responde (D3). Lo cubre el paso 24.1 al reemplazar la lectura.
- Hueco de `DeleteUser`: una tarea creada entre el borrado remoto y la transacción local queda huérfana. Lo cierra el outbox (paso 24).
- `CreateTask` hace C2 dentro del `TransactionScope` del filtro de idempotencia: mantiene la conexión abierta durante la llamada.
- Autenticación entre servicios (mTLS o token): sigue pendiente desde el paso 23.

## Experimentos

**EX-2H.** Corrió en el scratchpad con el SDK 10.0.401 y `Microsoft.AspNetCore.Mvc.Testing` 10.0.12. Dos apps `dotnet new web` (`HostA` y `HostB`) y un proyecto de consola que referencia las dos.

1. Con `new WebApplicationFactory<Program>()`: error `CS0433: El tipo 'Program' existe en 'HostA' y en 'HostB'`.
2. Con `Aliases="tasks"` en la referencia a `HostB` y `extern alias tasks;`: compila.
3. Sin alias y con un tipo marcador público en cada host (`HostA.MonolithHost`, `HostB.TasksHost`): compila.
4. `HostB` mapea `GET /internal/v1/task-counts` con `Guid[] projectId`. `HostA` tiene un cliente tipado (`AddHttpClient<TasksClient>`, `Timeout = 2 s`). En el test, `ConfigureTestServices` le pone `b.Server.CreateHandler()` con `ConfigurePrimaryHttpMessageHandler`. Resultado: `200` con `{"counts":[{"projectId":"1111…","total":2,"completed":1},{"projectId":"2222…",…}]}`.
5. Con la dirección `http://127.0.0.1:1/` (nadie responde) y un `catch (HttpRequestException)` en el endpoint: la request tardó 2 s y respondió `500`. El log mostró `TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout of 2 seconds elapsing. ---> System.TimeoutException`.

No probé: el ciclo de dos clientes cruzados en el fixture, la cookie compartida entre dos `WebApplicationFactory` y dos procesos reales migrando la misma base.
