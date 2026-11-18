# Paso 23: extraer un servicio con gateway adelante (investigación)

- Fecha: 2026-11-18.
- Estado: **Listo** como investigación. Antes de escribir código, Tobi tiene que decidir los 13 puntos de "Decisiones para Tobi". Tres son de herramienta (regla 5). Hay 8 choques con los docs (regla 10) que también esperan su respuesta.
- Alcance: el paso 23 del [plan](../plan.md) (`plan.md:396-399`). Concepto: límite de despliegue y contrato remoto; el primer proceso propio. Cambia: gateway (YARP), `compose.yaml`, Dockerfiles, `ci.yml`. Verifica: `docker compose up --build --wait` + `smoke-api.mjs --compose` contra el nuevo despliegue; navegador completo; health de cada proceso.
- Base: rama `fase7/23-servicio-gateway`, que ya tiene el paso 22.1 (`25aa8ef`). Los nombres son los del código real.

**Cómo leer las marcas:**
- `[FUENTE]`: lo leí en un documento primario. Cada uno tiene su link.
- `[CÓDIGO archivo:línea]`: lo leí en el repo.
- `[EXPERIMENTO EX-x]`: lo medí en esta corrida. Los IDs están en la sección "Experimentos".
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo medí. Queda pendiente.

## Respuesta corta

"Extraer un servicio" quiere decir esto: una feature que hoy vive en el mismo proceso que las demás pasa a correr en su propio proceso, con su propio contenedor. Un gateway (la puerta única que recibe las requests del navegador y las reparte) decide a qué proceso va cada URL. Las llamadas entre features que cruzan ese límite dejan de ser llamadas a un método y pasan a ser requests HTTP.

Recomiendo extraer **Tasks**:

- Solo una llamada de otra feature entra a Tasks: `DeleteOwnedTasksAsync`, desde `DeleteUser`.
- Solo una transacción cruza el límite: la de `DeleteUser`. Users rompería dos (`RegisterUser` y `DeleteUser`) y Auth está en el camino de todas las requests.
- Leer y cambiar las tareas propias no llama a Auth: la regla "el dueño puede" se decide adentro de Tasks (`AccessPolicy.cs:43-44`).

Recomiendo un gateway fino, como el de eShop (la app de referencia de Microsoft): solo reparte requests. Cada proceso sigue autenticando la cookie `__Host-session` por su cuenta, con las mismas claves de Data Protection (el servicio de ASP.NET Core que cifra la cookie). Lo medí: una cookie y un token antiforgery que emite un proceso los acepta el otro, si los dos comparten la carpeta de claves y el nombre de aplicación. Sin el mismo nombre, el otro proceso responde 401 y 400 (EX-AF).

Tres cosas cambian el plan:

- **El paso es mucho más grande que lo que dice "Cambia".** La extracción toca el backend (un proyecto nuevo y una librería compartida), Dune (las reglas leen `backend/Features/<X>/` y un solo proyecto), los tests (`ApiFixture` arranca un solo host), `verify.mjs` y las reglas de SLO. Recomiendo partirlo en dos pasos: primero el gateway delante del monolito, después la extracción. Es lo que pide el patrón Strangler Fig (sección 1.3).
- **El paso necesita clientes HTTP salientes, y el plan los pone en el paso 29.** Hay que escribirlos en el 23 sin reintentos ni circuit breaker (solo con timeout), para no adelantar el 29. El texto del plan dice "Primer cliente HTTP saliente" en el 29 (`plan.md:442`) y "Sin cliente HTTP saliente" en la sección 1 (`plan.md:33`). Hay que corregirlo.
- **`DeleteUser` pierde su transacción única antes del outbox del paso 24.** `architecture.md:67` acepta el acople "hasta el outbox del paso 24". Con dos procesos, un `TransactionScope` no puede abarcar la llamada HTTP. Recomiendo borrar primero las tareas por HTTP, con una operación que se puede repetir sin efecto extra, y después el perfil y la cuenta en una transacción local (sección 4.3).

## Conclusión

1. **Tasks es la mejor candidata.** Recibe una llamada de otra feature y hace una sola llamada hacia afuera (a Auth, para permisos). Una sola transacción cruza el límite (sección 1).
2. **YARP es una librería, no un producto aparte.** El gateway es un proyecto ASP.NET Core nuevo con el paquete `Yarp.ReverseProxy` 2.3.0 (MIT, de Microsoft). Esa versión es la última estable y declara soporte para .NET 8 o superior [FUENTE: Learn, YARP getting started](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0). Compila en `net10.0` y NuGetAudit no le encuentra vulnerabilidades (EX-PKG).
3. **YARP copia todos los headers del navegador al servicio, salvo `Host`.** Un header de identidad falso pasa al servicio si una ruta no lo borra (EX-SPOOF). Por eso no recomiendo que el servicio confíe en un header que pone el gateway (sección 2).
4. **La cookie y el antiforgery funcionan en dos procesos** si comparten carpeta de claves y `SetApplicationName("TemplateApp")`, que el repo ya fija (`BackendInfrastructure.cs:45`) (EX-AF). Revocar una sesión tiene que seguir cortando el acceso en la request siguiente (`architecture.md:79`). Entonces Tasks tiene que preguntarle a Auth si la sesión sigue activa, por HTTP.
5. **El contrato remoto conviene escribirlo del lado del que llama**, con sus propios DTOs (objetos que solo llevan datos) y un cliente HTTP tipado. Así lo hace eShop: `WebApp/Services/OrderingService.cs` define su propio `OrderRecord` (sección 4.1).
6. **Health:** el gateway avisa "listo" por sí mismo. A cada servicio lo vigila el chequeo activo de YARP (EX-HEALTH). Cada contenedor tiene su healthcheck de Compose. La imagen `aspnet:10.0.12` no trae `curl` ni `wget`, pero sí `bash`, y `bash` puede hacer el GET (EX-IMG).
7. **El gateway agrega `Server: Kestrel`** si no se apaga en su propio Kestrel (EX-SERVER). El smoke exige que no exista (`smoke-api.mjs:26`).
8. **Herramientas nuevas:** solo YARP en la opción recomendada. `IHttpClientFactory` y los forwarded headers ya vienen en el framework compartido de ASP.NET Core (EX-PKG).

## Glosario corto

- **Proceso**: un programa corriendo. Hoy el backend es un proceso (`api`). Después del paso son tres: gateway, monolito y Tasks.
- **Límite de despliegue**: la línea entre dos cosas que se pueden publicar por separado. Cruzarla es una llamada de red, no una llamada a un método.
- **Contrato remoto**: lo que un proceso le promete a otro por la red: la URL, el JSON y los códigos HTTP.
- **Gateway** (puerta de entrada): el proceso que recibe todas las requests y las reenvía al proceso que corresponde.
- **YARP** (Yet Another Reverse Proxy): la librería de Microsoft para escribir un reverse proxy (un servidor que reenvía requests a otros) en ASP.NET Core.
- **Ruta y cluster** (en YARP): la ruta dice qué URLs atrapa. El cluster dice a qué direcciones las manda. Cada dirección es un **destino**.
- **Transform**: un cambio que YARP le hace a la request antes de reenviarla, por ejemplo borrar un header.
- **Monolito**: el proceso que queda con Auth, Users y Health.
- **Strangler Fig** (higuera estranguladora): migrar de a partes. Una fachada reparte las requests entre el sistema viejo y el nuevo, y el nuevo crece de a una feature.
- **Idempotente**: repetir la operación no cambia nada después de la primera vez.

---

## 1. Qué quiere decir "extraer un servicio" y qué feature conviene

**Respuesta:** extraer es mover una feature a su propio proceso y cambiar sus llamadas con otras features por HTTP. Conviene Tasks.

### 1.1 Cómo se hablan hoy las features

Todas las llamadas entre features pasan por `Contracts/` y son llamadas a métodos en memoria [CÓDIGO]:

| Quién llama | Contrato | Para qué | Dónde |
|---|---|---|---|
| Tasks (4 slices) | `IAuthModule.HasPermissionAsync` y `CanAccessAsync` | Decidir permisos | `CreateTask.cs:33`, `GetTask.cs:32`, `UpdateTask.cs:38`, `DeleteTask.cs:26` |
| Tasks (5 endpoints) | `ClaimsPrincipalExtensions.GetCallerId` | Leer el id de la sesión | `CreateTaskEndpoint.cs:20` y otros |
| Users | `IAuthModule.CanAccessAsync`, `FindAccountAsync`, `DeleteAccountAsync` | Permisos, datos de la cuenta, borrar la cuenta | `GetUser.cs:29-32`, `UpdateProfile.cs:35`, `DeleteUser.cs:37,45` |
| Users | `ITasksModule.DeleteOwnedTasksAsync` | Borrar las tareas de la cuenta | `DeleteUser.cs:40` |
| Auth | `IUsersModule.CreateProfileAsync`, `FindProfileAsync` | Crear y leer el perfil | `RegisterUser.cs:73`, `OpenSession.cs:16` |
| Auth | `TaskPermissions`, `UserPermissions` (constantes) | Armar los permisos de cada rol | `RolePermissions.cs:15-30` |

Las transacciones que cruzan contextos usan `CrossContextTransaction.Begin()` (`CrossContextTransaction.cs:7-11`): `RegisterUser.cs:61`, `DeleteUser.cs:30` y `IdempotencyFilter.cs:45`.

### 1.2 Comparación de candidatas

| Criterio | Tasks | Users | Auth |
|---|---|---|---|
| Llamadas que entran desde otra feature | 1: `DeleteOwnedTasksAsync` | 2: `CreateProfileAsync` y `FindProfileAsync` (el login y el registro) | Todas: cada request con sesión valida su cookie en Auth |
| Llamadas que salen | A Auth: sesión y permisos | A Auth (3 métodos) y a Tasks | A Users (2 métodos) |
| Transacciones que cruzan el límite | 1: `DeleteUser` | 2: `RegisterUser` (identidad + perfil + sesión) y `DeleteUser` | 2: las mismas |
| Llamadas remotas por request | Validar la sesión. Pedir permisos solo en `CreateTask` o si quien llama no es el dueño (`AccessPolicy.cs:43-46`) | Validar la sesión y pedir la cuenta en cada lectura (`GetUser.cs:32`) | — |
| Datos | Schema `tasks`, ya separado (paso 22.1) | Schema `users` | Schema `auth` |

[INFERENCIA] Tasks es la que menos cruza. Users rompería el registro, que hoy crea identidad, perfil y sesión juntos o nada (`RegisterUser.cs:61-79`). Auth es el corazón de la sesión: sacarla primero haría remota cada request de la app.

La guía de Milan Jovanović (fuente secundaria) da el mismo criterio: extraer primero un módulo con "Low coupling with other modules" y "High cohesion within the module" [FUENTE: Milan, migrate your modular monolith](https://milanjovanovic.tech/blog/breaking-it-down-how-to-migrate-your-modular-monolith-to-microservices).

### 1.3 El patrón: fachada primero, después la extracción

Microsoft describe la migración por partes así: "A façade (proxy) intercepts requests that go to the back-end legacy system. The façade routes these requests either to the legacy application or to the new services". Y el primer paso es "introducing a façade (proxy) between the client app, the legacy system, and the new system". Al principio, "the façade routes most requests to the legacy system" [FUENTE: Azure Architecture Center, Strangler Fig](https://learn.microsoft.com/en-us/azure/architecture/patterns/strangler-fig).

La misma página avisa dos cosas que pegan acá:

- "Consider how to handle services and data stores that both the new system and the legacy system might use." Es el caso de `platform.IdempotencyKeys` y `platform.AuditEntries` (sección 4.4).
- "Make sure that the façade doesn't become a single point of failure or a performance bottleneck."

[INFERENCIA] Esto da dos cambios observables, cada uno con su evidencia:

1. **Gateway delante del monolito.** El navegador y el smoke pasan por YARP, que manda todo al monolito. Nada cambia en el backend. Se prueba que el gateway no rompe nada: cookies, antiforgery, cabeceras de seguridad, health, trazas.
2. **Extracción de Tasks.** El gateway manda `/api/v1/tasks/**` al proceso nuevo.

La regla 1 dice "Un paso = un cambio observable. No juntes dos pasos." (`AGENTS.md`). Por eso lo pongo como decisión (decisión 2).

### 1.4 Qué dice el informe del repo

El informe empresarial pone "Microservicios solo cuando el dolor es medible" (`informe-dotnet-empresarial.md:48`), y en la matriz dice que el API Gateway no conviene con "Un solo backend" (`informe-dotnet-empresarial.md:419`). `AGENTS.md` ya resuelve este choque: el patrón se aplica igual y se anota en `docs/decisions.tsv`.

## 2. Cómo autentica el servicio a quien llama

**Respuesta:** recomiendo que el gateway solo reparta y que Tasks valide la cookie por su cuenta, con las claves compartidas. La validez de la sesión y los permisos se los pide a Auth por HTTP. Así no se confía en ningún header que pueda falsificarse.

### 2.1 Hoy

- Un solo esquema: la cookie de ASP.NET Core (`AuthFeature.cs:66-67`). La cookie es `__Host-session`, `HttpOnly`, `Secure`, `SameSite=Strict` (`SessionHttpPolicy.cs:25-29`).
- En cada request, `ValidatePrincipalAsync` busca la fila de la sesión en `auth.Sessions`. Si no está, rechaza (`SessionHttpPolicy.cs:89-99`). Por eso revocar una sesión corta el acceso en la request siguiente (`architecture.md:79`).
- Toda escritura bajo `/api` exige antiforgery (`SessionHttpPolicy.cs:37-53`). El header es `X-XSRF-TOKEN` (`AuthFeature.cs:80`).
- Data Protection usa el nombre de aplicación `TemplateApp` (`BackendInfrastructure.cs:45`) y guarda las claves en el volumen `dataprotection` (`compose.yaml:69`).

### 2.2 Qué dicen las fuentes

- **YARP:** "No authentication or authorization is performed on requests unless enabled". Y sobre cookies: "These authentication types already pass their values in the request headers and these will flow to the destination server by default. That server will still need to verify and interpret those values, causing some double work." [FUENTE: Learn, YARP authn/authz](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0).
- **Libro de microservicios de .NET:** "If you're using an API Gateway, the gateway is a good place to authenticate [...] make sure that the individual microservices cannot be reached directly (without the API Gateway) unless additional security is in place". Y la alternativa: "Trust decisions are shared between services with security tokens or cookies. (These tokens can be shared between ASP.NET Core applications, if needed, by implementing cookie sharing.)" [FUENTE: Learn, Securing .NET Microservices](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/secure-net-microservices-web-applications/).
- **Compartir la cookie:** hace falta la misma ubicación de claves, `SetApplicationName` igual y el mismo esquema: "The scheme is used when encrypting and decrypting cookies, so a consistent scheme must be used across apps" [FUENTE: Learn, cookie sharing](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing?view=aspnetcore-10.0).
- **eShop:** el gateway (`mobile-bff`) es YARP y solo reparte por ruta. No tiene política de autorización ni transforms de identidad [FUENTE: eShop, `AppHost.cs:62-64`](https://github.com/dotnet/eShop/blob/main/src/eShop.AppHost/AppHost.cs) y [`Extensions.cs`, `ConfigureMobileBffRoutes`](https://github.com/dotnet/eShop/blob/main/src/eShop.AppHost/Extensions.cs). Cada servicio valida el token por su cuenta con `AddJwtBearer` [FUENTE: eShop, `AuthenticationExtensions.cs`](https://github.com/dotnet/eShop/blob/main/src/eShop.ServiceDefaults/AuthenticationExtensions.cs).

### 2.3 Tres opciones

| Opción | Cómo funciona | Gana | Pierde |
|---|---|---|---|
| **A. Cookie compartida y validación remota** (recomendada) | Tasks tiene el mismo manejador de cookie, con las claves compartidas. Su `OnValidatePrincipal` le pregunta a Auth por HTTP si la sesión sigue activa, en vez de leer `auth.Sessions`. Los permisos también los pide a Auth | El gateway no hace nada de seguridad, como en eShop. Si alguien entra directo a Tasks sin pasar por el gateway, igual necesita una cookie válida. El antiforgery funciona igual (EX-AF). `RequireAuthorization` y `GetCallerId` no cambian | Los dos procesos pueden emitir cookies, porque los dos tienen las claves. Hay 1 llamada remota por request, como hoy hay 1 consulta a la base |
| **B. El gateway autentica y reenvía la identidad** | El gateway valida la sesión y pone un header (`X-User-Id`) o un token interno firmado. Tasks confía en eso | Tasks no conoce cookies ni claves | El gateway igual necesita preguntarle a Auth por la sesión. Un header sin firma se puede falsificar (EX-SPOOF). Tasks no puede ser accesible sin pasar por el gateway (la advertencia de Learn). Un token firmado pide `Microsoft.AspNetCore.Authentication.JwtBearer` o firma propia: es una decisión de herramienta |
| **C. Tasks reenvía la cookie a Auth** | Tasks no tiene las claves. En cada request le manda la cookie a un endpoint de Auth que dice quién es | Las claves quedan solo en el monolito | Tasks no puede validar antiforgery sin las claves: habría que mandar eso también a Auth o validarlo en el gateway. Es una autenticación propia, sin ejemplo en las fuentes |

**Seguridad de los headers reenviados.** [EXPERIMENTO EX-SPOOF] YARP copió al destino un `X-User-Id: mallory` enviado por el cliente en la ruta sin transform. En la ruta con `RequestHeaderRemove: X-User-Id`, el header no llegó. Un `X-Gateway-User: mallory` llegó al servicio en una ruta que no lo borraba. En la ruta con un transform de código que primero borra y después pone el usuario autenticado, llegó `alice` con sesión y nada sin sesión. La doc lo confirma: "All incoming request headers are copied to the proxy request by default with the exception of the Host header" [FUENTE: Learn, YARP request transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms-request?view=aspnetcore-10.0). Con la opción B, cada ruta necesita su transform, y una ruta nueva sin él abre el hueco. Si se elige B, ese control tiene que ser una regla de Dune (regla 4).

**Antiforgery en dos procesos.** [EXPERIMENTO EX-AF] Dos copias de la misma app en carpetas distintas compartieron la carpeta de claves con `SetApplicationName("TemplateApp")`. El token antiforgery que emitió una lo validó la otra (200). Sin token, 400. El nombre de la cookie antiforgery salió igual en las dos (`.AspNetCore.Antiforgery.1op6hdoQAgk`), aunque cambiaba la carpeta de la app. Control negativo: sin `SetApplicationName`, la otra app respondió 401 a la cookie de sesión y 400 al token, y su cookie antiforgery tuvo otro nombre (`...U18jwGG1kyQ`).

**[INFERENCIA] Antiforgery con la opción A.** El token antiforgery queda atado a la identidad que lee del usuario (en este repo, el claim `NameIdentifier`). En la opción A, Tasks arma el usuario con el mismo manejador de cookie, así que el claim es el mismo. En EX-AF el usuario venía de la cookie en los dos procesos. [NO PROBADO] con el `OnValidatePrincipal` remoto real.

**Las claves en dos contenedores.** Hoy el volumen `dataprotection` se monta solo en `api` (`compose.yaml:69`). Con la opción A se monta también en `tasks`. La doc del repo ya avisa que "Varias instancias necesitan compartir esas claves; un volumen local no alcanza" (`architecture.md:77`). En Compose, un volumen con nombre sí alcanza para dos contenedores del mismo host [INFERENCIA].

## 3. YARP: paquete, configuración y lo que cambia de lugar

### 3.1 Paquete y proyecto

- `Yarp.ReverseProxy` 2.3.0, del 27 de febrero de 2025. Licencia MIT. Es la última estable: después de 2.3.0 no hay otra versión en NuGet [FUENTE: NuGet, Yarp.ReverseProxy](https://www.nuget.org/packages/Yarp.ReverseProxy). Declara `net8.0` y corre en `net10.0`. La doc: "YARP 2.3.0 supports .NET 8 or later" [FUENTE: Learn, getting started](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0).
- [EXPERIMENTO EX-PKG] `dotnet add package Yarp.ReverseProxy --version 2.3.0` en un proyecto `net10.0` con el SDK 10.0.401: compila sin advertencias, y `dotnet list package --vulnerable --include-transitive` no encuentra vulnerabilidades. Su única dependencia en `net8.0` es `System.IO.Hashing` 8.0.0.
- El gateway es un proyecto ASP.NET Core vacío con `AddReverseProxy().LoadFromConfig(...)` y `MapReverseProxy()` [FUENTE: Learn, getting started](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0). Entonces sí: es un proyecto nuevo en la solución.

### 3.2 Rutas por configuración o por código

- **Por configuración** (`appsettings.json`, sección `ReverseProxy`, con `Routes` y `Clusters`): la doc lo muestra primero, y una ruta se puede recargar sin reiniciar [FUENTE: Learn, authn/authz, "can be modified and reloaded without restarting the proxy"](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0).
- **Por código**: `RouteConfig` y `ClusterConfig`, o transforms de código con `AddTransforms`. Lo usé en EX-SPOOF para el transform de identidad.
- [INFERENCIA] Recomiendo configuración para las rutas y los clusters, porque Compose cambia las direcciones con variables de entorno (`ReverseProxy__Clusters__tasks__Destinations__d1__Address`). Código solo para lo que no se puede decir en JSON.

**Qué rutas.** El gateway no debe responder él mismo a una URL de la API:

- [EXPERIMENTO EX-SERVER] Una URL que no coincide con ninguna ruta dio 404 del gateway, sin body y sin cabeceras de seguridad. Un destino caído sin health check dio `502 Bad Gateway` sin body.
- El smoke pide `GET ../v9/health/live` y espera un ProblemDetails con `code = Http.NotFound` y las seis cabeceras (`smoke-api.mjs:160-161,171`). Si el gateway solo atrapa `/api/v1/**`, ese 404 lo da el gateway y el smoke falla.
- [INFERENCIA] Por eso: una ruta `/api/v1/tasks/{**rest}` hacia Tasks y una ruta comodín `/api/{**rest}` hacia el monolito. YARP elige la ruta más específica, igual que el routing de ASP.NET Core [NO PROBADO con estas dos rutas exactas].

### 3.3 Transforms

[FUENTE: Learn, YARP request transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms-request?view=aspnetcore-10.0):

- `X-Forwarded-*`: "This transform is enabled by default even if not specified in the route config." La acción por defecto es `Set`, y con `Set` "any existing header is still removed to prevent spoofing".
- `RequestHeaderOriginalHost` es `false` por defecto: el `Host` que llega al destino es el del destino.
- `RequestHeaderRemove` borra un header. `RequestHeadersAllowed` copia solo los de una lista.

[EXPERIMENTO EX-SPOOF] El cliente mandó `X-Forwarded-For: 6.6.6.6`. El destino recibió `X-Forwarded-For: ::1` (la IP real de quien llamó al gateway), `X-Forwarded-Proto: http`, `X-Forwarded-Host: localhost:5100` y `Host: localhost:5102`.

**[INFERENCIA] Con nginx adelante, la IP del cliente se pierde.** nginx pone `X-Forwarded-For` con la IP del navegador (`nginx.conf:12`). El gateway lo reemplaza (`Set`) con la IP de nginx. Hoy ya pasa algo parecido: la API no lee `X-Forwarded-For` y ve la IP de nginx (`progreso.md:908`), y el limiter anónimo usa esa IP (`ApiRateLimiting.cs:28-29`). El paso no empeora eso, pero tampoco lo arregla. Arreglarlo pide `UseForwardedHeaders` con `KnownProxies` en el gateway, y Learn avisa: "Only allow trusted proxies and networks to forward headers. Otherwise, IP spoofing attacks are possible" [FUENTE: Learn, proxy and load balancer](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0). Lo dejo fuera del paso: ya es un pendiente sin paso asignado.

### 3.4 Health de los destinos

[FUENTE: Learn, YARP destination health checks](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/dests-health-checks?view=aspnetcore-10.0):

- **Activo:** YARP pide un path cada `Interval` (por defecto 15 s) y marca sano con 2xx. La política `ConsecutiveFailures` marca enfermo después de `Threshold` fallas seguidas (por defecto 2).
- **Pasivo:** mira las respuestas reales (`TransportFailureRate`). "Once a destination gets assigned an unhealthy passive state, it stops receiving all new traffic".
- **Sin destinos sanos:** con `HealthyAndUnknown`, "If no destinations are available then requests will get a 503 error". `HealthyOrPanic`, que es la política por defecto, manda igual a todos.

[EXPERIMENTO EX-HEALTH] Con chequeo activo cada 2 s, `Threshold` 1 y `HealthyAndUnknown`: apagué el destino y el gateway respondió 503 en esa ruta. La otra ruta siguió en 200. Sin chequeo, el destino caído dio 502.

### 3.5 Trazas a través de YARP

[FUENTE: Learn, YARP distributed tracing](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/distributed-tracing?view=aspnetcore-10.0): YARP crea actividades si alguien escucha el `ActivitySource` "Yarp.ReverseProxy", y "the `AddHttpClientInstrumentation()` call is required along with the `AddSource("Yarp.ReverseProxy")` call to make the request spans emit".

- [EXPERIMENTO EX-SPOOF] El cliente mandó `traceparent` con trace id `0af7651916cd43dd8448eb211c80319c`. El destino recibió el mismo trace id con otro span padre: el gateway participa de la traza y no la corta.
- El repo ya usa `AddHttpClientInstrumentation` (`ObservabilityExtensions.cs:49,54`). Al gateway le falta `AddSource("Yarp.ReverseProxy")`.
- **El nombre de servicio está fijo** en `"templateapp-api"` (`ObservabilityExtensions.cs:42,78`). Con tres procesos hay que pasarlo como parámetro. Si no, las trazas de los tres salen con el mismo nombre.
- **El control de correlación exige un log de request por cada span SERVER** (`check-otel-export.mjs:75-79`). El gateway también crea spans SERVER, así que necesita el mismo Serilog con `UseSerilogRequestLogging` (`Program.cs:34`) [INFERENCIA].

### 3.6 Rate limiting y cabeceras de seguridad: dónde quedan

- **Rate limiting.** YARP puede aplicar una política por ruta con `RateLimiterPolicy`, sobre el limiter de ASP.NET Core [FUENTE: Learn, YARP rate limiting](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/rate-limiting?view=aspnetcore-10.0). Pero el limiter de hoy parte por el id de usuario de la sesión (`ApiRateLimiting.cs:40-45`), y en la opción A el gateway no conoce la sesión. [INFERENCIA] Recomiendo dejar el limiter en cada servicio. Consecuencia: el balde de un usuario se parte en dos, uno por proceso, así que su límite total se duplica. Ya está anotado que los contadores son por instancia (`architecture.md:83`, `progreso.md:913`).
- **Cabeceras de seguridad.** Las escribe cada respuesta de la API (`architecture.md:84`) y el smoke las exige en 401, 400 y 404 (`smoke-api.mjs:170-173`). Tasks tiene que escribirlas también. [EXPERIMENTO EX-SERVER] El gateway agrega `Server: Kestrel` a toda respuesta reenviada aunque el destino no lo mande. Con `KestrelServerOptions.AddServerHeader = false` en el gateway, desaparece. Es el mismo ajuste que hace `ApiSecurityHeaders.cs:9`. Las respuestas propias del gateway (502, 503) no llevan las seis cabeceras. [INFERENCIA] Hay que ponerlas también en el gateway, con la misma librería NetEscapades, que ya está aprobada (paso 13).

### 3.7 nginx y el gateway

Hoy nginx sirve el build de Angular y reenvía `/api/v1/` a `api:8080` (`nginx.conf:8-14`). El smoke y CI hablan directo con la API en `localhost:8080` (`ci.yml:179,192`, `smoke-api.mjs:5`).

| Opción | Cómo queda | Gana | Pierde |
|---|---|---|---|
| **N1. nginx → gateway → servicios** (recomendada) | `nginx.conf:9` pasa a `proxy_pass http://gateway:8080`. El gateway publica el puerto 8080. `api` y `tasks` no publican puertos | Un cambio de una línea en nginx. El smoke y CI siguen usando `localhost:8080`, ahora del gateway | Dos proxies seguidos. La IP del cliente se pierde (sección 3.3) |
| N2. El gateway reemplaza el proxy de nginx | El navegador entra al gateway. El gateway reenvía `/` a nginx (solo estáticos) | Un solo proxy de API | Cambia el puerto que abre el navegador (`WEB_PORT`), y con él el e2e y los docs |
| N3. El gateway sirve el SPA | Se borra nginx y el gateway usa `UseStaticFiles` | Un contenedor menos | Cambia el Dockerfile del frontend y mezcla estáticos con el proxy |

## 4. El contrato remoto

### 4.1 Cómo mantener el contrato tipado

| Opción | Gana | Pierde | Quién lo hace |
|---|---|---|---|
| **DTOs propios del que llama + cliente HTTP tipado** (recomendada) | Cada proceso se publica por separado. Un cambio en el servidor no rompe la compilación del cliente | El compilador no ve si los dos lados coinciden: hace falta un test de contrato | eShop: `WebApp/Services/OrderingService.cs` define su propio `OrderRecord` y usa `AddHttpClient<OrderingService>` [FUENTE](https://github.com/dotnet/eShop/blob/main/src/WebApp/Services/OrderingService.cs). Los eventos de integración también se copian en cada servicio: `OrderStatusChangedToPaidIntegrationEvent` está en Catalog.API, Ordering.API, WebApp y Webhooks.API [FUENTE: árbol de eShop](https://github.com/dotnet/eShop/tree/main/src) |
| Ensamblado compartido de contratos | El compilador ve los dos lados | Ata los releases: cambiar un DTO obliga a publicar los dos procesos juntos | Monolitos modulares (los `Contracts` de hoy) |
| Cliente generado desde OpenAPI | Un solo origen de verdad, el documento OpenAPI | Una herramienta nueva de generación (NSwag o Kiota): decisión de Tobi. Hoy `MapOpenApi` solo corre en Development (`Program.cs:28-31`) | Equipos con muchos consumidores |

Un "cliente HTTP tipado" es una clase que recibe un `HttpClient` y expone métodos con nombre de negocio. Learn dice que da "IntelliSense and compiler help when consuming clients" y "a single location to configure and interact with a particular `HttpClient` instance" [FUENTE: Learn, IHttpClientFactory](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0). [EXPERIMENTO EX-PKG] `Microsoft.Extensions.Http.dll` está en `shared/Microsoft.AspNetCore.App/10.0.12`: no hace falta un paquete.

[INFERENCIA] En este repo quedaría así:

- En el monolito, una clase que implementa `ITasksModule` con HTTP: `DELETE /internal/v1/owners/{authId}/tasks`. `DeleteUser` no cambia de dependencia.
- En Tasks, una clase que implementa lo que Tasks usa de `IAuthModule` con HTTP: sesión activa y permisos del usuario. La memoria por request de `AccessPolicy` (`AccessPolicy.cs:13,21-29`) evita pedir lo mismo dos veces.
- Las constantes `TaskPermissions` las usa Auth para armar roles (`RolePermissions.cs:17-30`). Con DTOs propios, Auth tiene su copia de esos textos.

### 4.2 Timeouts y resiliencia: no adelantar el paso 29

- El paso 29 trae `Microsoft.Extensions.Http.Resilience`, con reintentos, circuit breaker y timeouts en cascada (`plan.md:442-445`). `conventions.md:97` dice que los reintentos entran solo con su paso.
- La industria pone resiliencia en todo cliente desde el principio. eShop llama a `http.AddStandardResilienceHandler()` para todos los clientes en `ServiceDefaults` [FUENTE: eShop, `eShop.ServiceDefaults/Extensions.cs:25`](https://github.com/dotnet/eShop/blob/main/src/eShop.ServiceDefaults/Extensions.cs).
- Sin configuración, un `HttpClient` espera 100 segundos: "The default value is 100,000 milliseconds (100 seconds)" [FUENTE: Learn, HttpClient.Timeout](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0). YARP tampoco tiene un timeout total por defecto, solo un `ActivityTimeout` de 100 s sin actividad [FUENTE: Learn, YARP timeouts](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0).
- [INFERENCIA] Recomiendo timeouts explícitos en el paso 23 y nada más: un `HttpClient.Timeout` corto en cada cliente (por ejemplo 2 s, como los health checks, `BackendInfrastructure.cs:13`) y un `Timeout` por ruta en el gateway, mayor que el de los clientes internos. Sin reintentos. El paso 29 suma el resto. El plan tiene que dejar de decir "Primer cliente HTTP saliente" (choque 3).

### 4.3 Qué reemplaza a la transacción entre procesos

**Hoy** `DeleteUser` hace todo en un `TransactionScope`: busca el perfil, chequea el permiso, borra las tareas, las claves de idempotencia, el perfil y la cuenta, y confirma (`DeleteUser.cs:30-49`).

**Por qué no puede seguir así:** `System.Transactions` solo suma conexiones de base que se anotan en la transacción, y una request HTTP no es una de ellas [INFERENCIA]. Las transacciones distribuidas de .NET existen solo en Windows con MSDTC: "Distributed transaction support in System.Transactions was added to .NET 7.0 for Windows only" [FUENTE: Learn, EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions). Compose corre Linux. [INFERENCIA] Si la llamada HTTP queda adentro del scope, nada falla: simplemente queda fuera de la transacción, sin aviso. Ese silencio es el riesgo.

| Opción | Qué pasa si algo falla | Gana | Pierde |
|---|---|---|---|
| **T1. Tareas primero, por HTTP, idempotente; después perfil y cuenta en transacción local** (recomendada) | Falla la llamada: no se borra nada y el usuario puede reintentar. Falla lo local después: quedan la cuenta y el perfil sin tareas, y reintentar termina el borrado | Nunca quedan tareas huérfanas, que es lo que cuidó el paso 22 (`architecture.md:51`) | Si lo local falla, las tareas ya no vuelven |
| T2. Cuenta primero, tareas después | Falla la llamada: quedan tareas de una cuenta que no existe | El usuario desaparece enseguida | Vuelven las huérfanas |
| T3. Evento "cuenta borrada" con outbox | Tasks borra cuando recibe el evento | Es la forma de la industria | Es el paso 24 en adelante: adelantarlo rompe la regla 8 |

Con T1, el endpoint interno de Tasks tiene que ser idempotente (borrar las tareas de un dueño sin tareas da 204), y el chequeo de permiso sigue en el monolito, antes de la llamada.

`RegisterUser` (`RegisterUser.cs:61-79`) y `IdempotencyFilter` (`IdempotencyFilter.cs:45`) no cruzan el límite si se extrae Tasks: siguen iguales.

### 4.4 Datos compartidos: `platform`

- Tasks escribe en `platform.AuditEntries` en el mismo `SaveChanges` que la tarea (`TasksDbContext.cs:21`, `SharedAuditEntries.cs:11`).
- `POST /tasks` usa el filtro de idempotencia (`CreateTaskEndpoint.cs:10`), que escribe en `platform.IdempotencyKeys` con `PlatformDbContext` (`IdempotencyFilter.cs:45-49`).
- `DeleteUser` borra las claves de idempotencia del dueño desde el monolito (`DeleteUser.cs:41`).

[INFERENCIA] Para el paso 23 recomiendo que las dos tablas sigan compartidas: las escriben los dos procesos y las migra solo el monolito. Es lo que Strangler Fig acepta ("Make sure that both systems can access these resources at the same time"). Separarlas en cada servicio es otro paso: cambia el modelo de auditoría, que hoy es "de todo el host" (`architecture.md:47`).

Las migraciones al arrancar no chocan: los dos procesos toman el mismo advisory lock de PostgreSQL (un candado de la base que deja entrar de a uno) antes de migrar (`DatabaseInitializer.cs:23`). Tasks migra solo `TasksDbContext`. [NO PROBADO] con dos procesos reales.

### 4.5 Endpoints internos

`/internal/...` no tiene que pasar por el gateway: el gateway no tiene ruta para ese path. Pero en la red de Compose, cualquier contenedor llega a `tasks:8080`. El informe del repo pide para "servicio a servicio" "Client credentials con `mTLS` o identidad administrada; sin secretos estáticos" (`informe-dotnet-empresarial.md:303`). [INFERENCIA] Eso es otro paso: pide elegir herramientas. Para el 23 recomiendo aislar por red (sin puertos publicados para `api` y `tasks`) y anotar el riesgo como pendiente.

## 5. Mecánica del repo: qué cambia y qué se rompe

### 5.1 Supuestos de "un solo proyecto"

- ADR-0004, punto 7: "**Un solo proyecto .NET.** Dune hace cumplir los límites." (`0004-vertical-slices-deep-modules.md:26`). `architecture.md:7`: "El backend es un solo proyecto ASP.NET Core 10".
- `TemplateApp.slnx:2-3` tiene un producto y sus tests. `TemplateApp.csproj` compila todo `backend/**` salvo `Tests/**` (`TemplateApp.csproj:8`).
- `Dune.props:2,12` activa los analyzers solo en proyectos llamados `TemplateApp` o `TemplateApp.Tests`. Un proyecto nuevo queda sin Dune.
- Los analyzers reconocen una feature por el path `backend/Features/<X>/` (`DunePaths.cs:36`) y por el namespace `TemplateApp.Features.` (`CrossFeatureAnalyzer.cs:16`, `PublicSurfaceAnalyzer.cs:12`, y otros 8). El namespace esperado sale del path después de `backend/` (`DunePaths.cs:91-112`).
- `check-structure.mjs` lee `backend/Features`, `Shared`, `Infrastructure`, `TemplateApp.csproj` y `Program.cs` (`check-structure.mjs:53-58`), y exige `InternalsVisibleTo` solo hacia `TemplateApp.Tests` (`check-structure.mjs:177-195`).
- `progreso.md:272` anota lo que el paso 21 enseñó: "el paso 23 como el día en que el compilador cuida el límite solo". Eso supone un ensamblado aparte.

### 5.2 Disposición de proyectos

| Opción | Cómo queda | Gana | Pierde |
|---|---|---|---|
| **L2. Proyectos separados** (recomendada) | Una librería con `Infrastructure/` y `Shared/` que usan los dos hosts (= `eShop.ServiceDefaults`), el monolito (`TemplateApp.csproj`, sin Tasks), el host de Tasks y el gateway | El límite es real: el compilador no deja usar el interior de Tasks desde el monolito. Cada imagen tiene solo su código | Dune, `check-structure`, los tests y el Dockerfile cambian. Los tipos `internal` de `Infrastructure` que usa una feature pasan a `public` (por ejemplo `IdempotencyKeys`, `DeleteUser.cs:18`) |
| L1. Mismo ensamblado, dos procesos | La misma imagen corre dos veces y una variable elige qué features mapear | Dune y los tests casi no cambian | El compilador no separa nada. No es un límite de despliegue: los dos procesos salen siempre del mismo binario |

[INFERENCIA] Con L2, dejar la librería en `backend/Infrastructure` y `backend/Shared` (con su propio `.csproj`) y Tasks en `backend/Features/Tasks` mantiene los paths y los namespaces que lee Dune. Hay que sacarlos del glob de `TemplateApp.csproj` y extender la condición de `Dune.props` a los proyectos nuevos. `AppDbContext` y la rama del historial viejo de `DatabaseInitializer` (`DatabaseInitializer.cs:18`) se quedan en el monolito.

### 5.3 Archivos que cambian (con L2, opción A, T1 y N1)

| Área | Archivos |
|---|---|
| Solución | `TemplateApp.slnx` (proyectos nuevos), `Directory.Build.props` y `dune/Dune.props` (condición por nombre de proyecto) |
| Gateway (nuevo) | Proyecto, `Program.cs`, `appsettings.json` con rutas y clusters, `Dockerfile` |
| Tasks (nuevo host) | Proyecto, `Program.cs` (lo que hoy hace `Program.cs:14-46` para una feature), `Dockerfile`, cliente HTTP de Auth, endpoint interno de borrado |
| Monolito | `Program.cs` (sin `AddTasksFeature` ni `MapTasksEndpoints`), `TemplateApp.csproj`, cliente HTTP de Tasks, endpoint interno de sesión y permisos en Auth, `DeleteUser.cs` (orden T1) |
| Librería compartida | `Infrastructure/**` y `Shared/**` con su `.csproj`. `ObservabilityExtensions.cs:42,78` recibe el nombre del servicio |
| Compose | `compose.yaml`: servicios `gateway` y `tasks`, puerto 8080 del gateway, `dataprotection` montado en `api` y `tasks`, healthchecks para `--wait` |
| Frontend | `frontend/nginx.conf:9` hacia el gateway |
| CI | `ci.yml:175-188` (`up --build --wait` en vez del bucle de `curl`), `ci.yml:187,198-200` (logs de los servicios nuevos), `ci.yml:35,41` (los tests nuevos) |
| Verify | `smoke-api.mjs:271` (recrear también `tasks`), una comprobación de health por proceso; `verify.mjs:19,1156-1178` arranca hoy un solo `dotnet run` y el modo local con `ng serve` necesita los tres; `check-all.mjs:69-70` |
| Observabilidad | `deploy/prometheus/rules/slo-api.rules.yml` (job `templateapp-api`), sus tests `slo-api.test.yml`, `check-slo-alerts.mjs:112` |
| Dune | `DunePaths.cs`, `check-structure.mjs` y su test (fuentes por proyecto, `InternalsVisibleTo`), reglas nuevas con fixture válida y violación (regla 4): por ejemplo, una llamada HTTP dentro de `CrossContextTransaction` |
| Tests | `ApiFixture` arranca un solo `WebApplicationFactory<Program>` (`ApiFixture.cs:12,58`). Los 6 tests de `Features/Tasks` y 8 archivos de afuera que tocan tareas (`DeleteUserTests`, `AuditTrailTests`, `ErrorContractTests`, `RateLimitingTests`, `SecurityHeadersTests`, `PerFeatureContextsTransitionTests`, `TaskCompletionMigrationTests`, `IssueCsrfTokenTests`) necesitan dos hosts |
| Docs | ADR-0004 (punto 7) o un ADR nuevo, `architecture.md`, `conventions.md`, `decisions.tsv`, `plan.md` (pasos 23 y 29), README |

### 5.4 Gates que se rompen si no se tocan

- `docker compose up --build --wait`: el plan lo pide, pero CI no lo usa (`ci.yml:175`). `--wait` espera "services to be running|healthy" [FUENTE: Docker, compose up](https://docs.docker.com/reference/cli/docker/compose/up/). `api` no tiene healthcheck (`compose.yaml:53-71`). [INFERENCIA] sin healthcheck, `--wait` solo espera que el contenedor corra, no que esté listo.
- Smoke: el 404 de una versión inexistente, las cabeceras y `Server` (sección 3.2 y 3.6). Readiness devuelve `{database, migrations}` del monolito (`smoke-api.mjs:162-164`).
- `check-otel-export.mjs`: los spans SERVER del gateway sin log de request (sección 3.5).
- SLOs: [INFERENCIA] si se miden en el gateway, la etiqueta `http_route` es el patrón de YARP (`/api/{**rest}`), no la ruta del endpoint. El filtro `http_route!~".*/health/.*"` (`slo-api.rules.yml:7`) dejaría pasar el health. Una ruta propia para `/api/v1/health/{**rest}` lo evita. [NO PROBADO].
- `check-structure.mjs:109-129`: busca `backend/TemplateApp.csproj` con sus exclusiones y un solo proyecto de tests.
- Imagen: `backend/Dockerfile:3-6` copia solo `backend/` y restaura un `.csproj`. Con una librería compartida, el contexto de build tiene que incluirla [INFERENCIA].

## 6. Health de cada proceso

**Respuesta:** cada proceso tiene su live y su ready. El ready del gateway no depende de los servicios. Compose vigila a cada uno.

- Sobre dependencias, el paso 16 ya cita: "Exclude optional dependencies from the decision to remove an instance from service", y el riesgo de "Cascading failure from deep probes" [FUENTE: Azure, Health Endpoint Monitoring](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#problems-and-considerations) (`paso-16-health-dependencias.md:165`).
- [INFERENCIA] Si el ready del gateway dependiera de Tasks, una caída de Tasks sacaría el gateway entero, y con él el login. El chequeo activo de YARP ya saca a Tasks de su ruta, y esa ruta responde 503 (EX-HEALTH). El ready del gateway queda superficial: el proceso responde y cargó su configuración.
- El ready de Tasks: su base y sus migraciones (solo `TasksDbContext`). El ready del monolito queda como hoy.
- **Cómo lo ve Compose.** [EXPERIMENTO EX-IMG] `mcr.microsoft.com/dotnet/aspnet:10.0.12` es Ubuntu 24.04 y no trae `curl` ni `wget`. Microsoft los sacó a propósito desde .NET 5 [FUENTE: dotnet-docker #2379](https://github.com/dotnet/dotnet-docker/issues/2379). Sí trae `bash`, y un `GET` por `/dev/tcp` devolvió `HTTP/1.1 200 OK`. [INFERENCIA] Alcanza para un healthcheck de Compose sin agregar paquetes. Alternativa: instalar `curl` en la imagen, que suma superficie de ataque.
- El smoke puede afirmar "health de cada proceso" con `docker compose ps` (cada servicio `healthy`) y pedir el ready de Tasks por la red interna. Publicar el ready de Tasks por el gateway amplía el pendiente de `progreso.md` sobre `/health/ready` anónimo.

## 7. Herramientas, licencias y qué hace la industria

| Pieza | Licencia | ¿Nueva? | Necesaria en la opción recomendada |
|---|---|---|---|
| `Yarp.ReverseProxy` 2.3.0 | MIT ([nuspec](https://www.nuget.org/packages/Yarp.ReverseProxy/2.3.0)) | Sí | Sí. **Decisión de Tobi** (regla 5): el plan la nombra, pero no está registrada como elegida |
| `Microsoft.Extensions.Http` (`IHttpClientFactory`) | MIT, framework compartido | No (EX-PKG) | Sí |
| `Microsoft.AspNetCore.HttpOverrides` (forwarded headers) | MIT, framework compartido | No (EX-PKG) | No en el paso 23 |
| NetEscapades.AspNetCore.SecurityHeaders, OpenTelemetry, Serilog | Ya aprobadas (pasos 13-17) | No | Sí, también en el gateway y en Tasks |
| `Microsoft.Extensions.Http.Resilience` | MIT | Sí | No: es del paso 29 |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | MIT | Sí | Solo con la opción B y token firmado |
| NSwag o Kiota | MIT | Sí | Solo con el cliente generado desde OpenAPI |
| `curl` en la imagen | Paquete del sistema | Sí | No: alcanza con `bash` |

**Alternativas a YARP como gateway:** nginx (ya está en el repo), Ocelot (MIT, de la comunidad), Envoy o Traefik. El informe del repo dice que proyectos grandes migran de Ocelot a YARP (`informe-dotnet-empresarial.md:240-242`). eShop usa YARP desde Aspire (`AppHost.cs:62`). El libro de microservicios de Learn todavía muestra Ocelot, porque es de 2021 [FUENTE](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/direct-client-to-microservice-communication-versus-the-api-gateway-pattern).

**Qué hace la industria, en resumen:**

- La fachada va primero, y las features salen de a una (Strangler Fig, Microsoft).
- El gateway es fino y cada servicio valida la credencial (eShop). Si el gateway autentica, los servicios no tienen que ser accesibles sin él (Learn).
- El cliente define sus DTOs y usa clientes tipados con resiliencia por defecto (eShop).
- Las consecuencias entre servicios van por eventos (eShop usa RabbitMQ con el log de eventos `IntegrationEventLogEF`). En este repo eso es la Fase 8.
- Milan (fuente secundaria) suma lo mismo: reemplazar llamadas directas por HTTP, mensajería para lo asíncrono y "add resilience patterns such as retries and timeouts" [FUENTE](https://milanjovanovic.tech/blog/breaking-it-down-how-to-migrate-your-modular-monolith-to-microservices).

## Choques con los docs

La regla 10 dice que si dos docs se contradicen, el agente frena y le pregunta a Tobi (`AGENTS.md`). No los resuelvo: los dejo para él.

1. **ADR-0004 punto 7** ("Un solo proyecto .NET", `0004-vertical-slices-deep-modules.md:26`) y `architecture.md:7` chocan con un proceso propio en ensamblado propio. Hace falta un ADR nuevo o una revisión del 0004.
2. **`plan.md:396-399`** lista solo gateway, Compose, Dockerfiles y CI. La extracción también cambia backend, Dune, tests y `verify.mjs` (sección 5.3).
3. **`plan.md:442`** ("Primer cliente HTTP saliente") y **`plan.md:33`** ("Sin cliente HTTP saliente") quedan falsos: el paso 23 trae los primeros clientes.
4. **`conventions.md:97`** (reintentos solo con el paso 29) choca con la práctica de eShop (resiliencia en todo cliente). Recomiendo seguir la regla del repo y anotar el riesgo.
5. **`architecture.md:67`** acepta la transacción entre módulos "hasta el outbox del paso 24". El paso 23 la rompe antes, en `DeleteUser`.
6. **`informe-dotnet-empresarial.md:48,419`** desaconseja microservicios y gateway con un solo backend. `AGENTS.md` ya resolvió este tipo de choque: se hace igual y se anota en `decisions.tsv`.
7. **`conventions.md:19`** ("Como todo vive en un solo ensamblado, `internal` no separa una feature de otra") deja de ser cierto para Tasks.
8. **`architecture.md:84`** dice que la API recibe solo HTTP "detrás de nginx en Compose". Con el gateway, la cadena es nginx → gateway → servicio.

## Experimentos

Todos corrieron fuera del repo, en el scratchpad de la sesión, con el SDK 10.0.401 (el de `global.json`) y el runtime ASP.NET Core 10.0.12. Armé dos apps: `Echo` (cookie `__Host-session`, Data Protection con carpeta compartida, antiforgery con `X-XSRF-TOKEN`, y un endpoint que devuelve los headers recibidos) y `Gateway` (YARP 2.3.0 con tres rutas). `Echo` corrió dos veces, desde dos carpetas distintas (`echoA` como monolito, `echoB` como servicio). Un script de Node manejó las cookies igual que `smoke-api.mjs`.

| ID | Qué hice | Resultado | Tipo |
|---|---|---|---|
| EX-PKG | `dotnet add package Yarp.ReverseProxy --version 2.3.0` en `net10.0`; `dotnet list package --vulnerable --include-transitive`; listado del framework compartido | Compila sin advertencias. Sin paquetes vulnerables. `Microsoft.Extensions.Http.dll` y `Microsoft.AspNetCore.HttpOverrides.dll` están en `Microsoft.AspNetCore.App/10.0.12` | EXPERIMENTO |
| EX-AF | Login en `echoA` por el gateway; `/whoami` y `POST /write` con el token en `echoB` por el gateway, los dos con `SetApplicationName("TemplateApp")` | `whoami` 200 con `alice`; escritura con token 200; sin token 400. El nombre de la cookie antiforgery es igual en las dos carpetas | EXPERIMENTO |
| EX-AF (negativo) | `echoB` sin `SetApplicationName` | `whoami` 401, escritura 400, otro nombre de cookie antiforgery | EXPERIMENTO |
| EX-SPOOF | Headers falsos (`X-User-Id`, `X-Gateway-User`, `X-Forwarded-For`) y `traceparent` hacia rutas con y sin transform | La cookie pasa. `X-User-Id` se borra solo donde hay `RequestHeaderRemove`. `X-Gateway-User` falso pasa en la ruta sin transform. El transform de código pone `alice` con sesión y nada sin sesión. `X-Forwarded-For` falso se reemplaza por `::1`. `Host` es el del destino. Mismo trace id con otro span padre | EXPERIMENTO |
| EX-HEALTH | Chequeo activo cada 2 s, `Threshold` 1, `HealthyAndUnknown`; apagar el destino. Otra ruta sin chequeo, con el destino apagado | 503 en la ruta con chequeo, 200 en la otra. Sin chequeo: 502 sin body | EXPERIMENTO |
| EX-SERVER | `AddServerHeader = false` en el destino, con y sin lo mismo en el gateway; URL sin ruta | El gateway agrega `Server: Kestrel` salvo que se apague en él. URL sin ruta: 404 del gateway sin body | EXPERIMENTO |
| EX-IMG | `docker run mcr.microsoft.com/dotnet/aspnet:10.0.12`: buscar `curl` y `wget`; `GET` con `bash` y `/dev/tcp` | Ubuntu 24.04.5, sin `curl` ni `wget`. `bash` devolvió `HTTP/1.1 200 OK` | EXPERIMENTO |

**Qué no probé:**

- [NO PROBADO] El `OnValidatePrincipal` remoto real contra el Auth del repo, ni el antiforgery con ese usuario.
- [NO PROBADO] La precedencia entre `/api/v1/tasks/{**rest}` y `/api/{**rest}` en YARP.
- [NO PROBADO] La etiqueta `http.route` que ve Prometheus en el gateway.
- [NO PROBADO] Dos procesos migrando la misma base con el advisory lock.
- [NO PROBADO] Que una llamada HTTP dentro de un `TransactionScope` quede afuera sin error.
- [NO PROBADO] `docker compose up --wait` con los healthchecks de `bash`.
- [NO PROBADO] Nada del repo: no compilé, no corrí tests ni gates. El experimento no tocó el checkout.

## Decisiones para Tobi

Las marcadas con **(herramienta)** son de la regla 5.

1. **Qué feature extraer.**
   - Tasks: una llamada entra, una transacción se rompe y las lecturas propias no salen del proceso.
   - Users: rompe el registro y el borrado.
   - Auth: hace remota cada request.
   - Recomiendo **Tasks**.
2. **Partir el paso.**
   - Un paso: gateway y extracción juntos.
   - Dos pasos: primero el gateway delante del monolito, después la extracción.
   - Ganás con dos: cada uno tiene su evidencia, y si algo se rompe sabés si fue el proxy o el corte. Perdés: un PR más.
   - Recomiendo **dos pasos**: lo pide la regla 1 y es el orden de Strangler Fig.
3. **Cómo se autentica Tasks.**
   - A: cookie compartida y validación remota de la sesión.
   - B: el gateway autentica y reenvía la identidad.
   - C: Tasks le reenvía la cookie a Auth.
   - Recomiendo **A**: no confía en headers falsificables (EX-SPOOF), es la forma de eShop, el antiforgery anda igual (EX-AF) y la revocación sigue inmediata. Perdés: dos procesos con las claves.
4. **Forma del contrato remoto.**
   - DTOs propios del que llama, con cliente tipado y test de contrato.
   - Ensamblado compartido de contratos.
   - Cliente generado desde OpenAPI **(herramienta: NSwag o Kiota)**.
   - Recomiendo **DTOs propios**: cada proceso se publica por separado, como en eShop. Perdés: el compilador no ve el otro lado, y el test de contrato lo reemplaza.
5. **Borrado de cuenta sin transacción común.**
   - T1: tareas primero, por HTTP e idempotente; lo local después.
   - T2: cuenta primero.
   - T3: esperar al outbox.
   - Recomiendo **T1**: nunca deja huérfanas y no adelanta el paso 24. Perdés: si lo local falla, las tareas ya no vuelven.
6. **Tablas `platform` (idempotencia y auditoría).**
   - Compartidas por los dos procesos y migradas solo por el monolito.
   - Una copia propia en el schema `tasks`.
   - Recomiendo **compartidas** en este paso (Strangler Fig lo acepta) y separarlas en otro.
7. **nginx y el gateway.**
   - N1: nginx → gateway → servicios.
   - N2: el gateway reemplaza el proxy de nginx.
   - N3: el gateway sirve el SPA.
   - Recomiendo **N1**: una línea en nginx, y el smoke y CI siguen en `localhost:8080`.
8. **Rate limiting y cabeceras de seguridad.**
   - En cada servicio, con el gateway sin `Server` y con cabeceras en sus propias respuestas.
   - Todo en el gateway.
   - Recomiendo **en cada servicio**: el limiter necesita la sesión, y el gateway no la conoce. Perdés: el límite de un usuario se reparte entre dos procesos.
9. **Readiness del gateway.**
   - Superficial, con el chequeo activo de YARP por destino.
   - Profunda: el gateway no está listo si un servicio no lo está.
   - Recomiendo **superficial**: evita la falla en cascada (paso 16).
10. **Endpoints internos.**
    - Solo aislamiento de red en este paso, con el riesgo anotado como pendiente.
    - Autenticación servicio a servicio ya **(herramienta: mTLS o client credentials)**.
    - Recomiendo **aislamiento de red** en el 23 y la autenticación en un paso propio.
11. **Disposición de proyectos.**
    - L2: proyectos separados, con una librería compartida.
    - L1: mismo ensamblado, dos procesos.
    - Recomiendo **L2**: es el límite de despliegue real y lo que anotó el paso 21 (`progreso.md:272`). Perdés: más cambios en Dune y en los tests.
12. **Resiliencia de los clientes nuevos.**
    - Solo timeouts explícitos, sin reintentos, y el plan corrige el texto del 29.
    - Adelantar `Microsoft.Extensions.Http.Resilience` **(herramienta)**.
    - Recomiendo **solo timeouts**: respeta la regla 8 y `conventions.md:97`.
13. **Gateway: confirmar YARP (herramienta).**
    - YARP 2.3.0 (MIT, Microsoft).
    - nginx como gateway (ya está, pero sin health activo ni transforms en C#).
    - Ocelot (MIT, comunidad).
    - Recomiendo **YARP**: es lo que nombra el plan, lo que usa eShop y lo que medí.

Además, Tobi tiene que responder los 8 choques de la sección "Choques con los docs" (regla 10). El más grande es el 1 (ADR-0004, un solo proyecto).

## Cierre del informe

- Git: el checkout del paso solo suma este archivo. Los experimentos corrieron en el scratchpad de la sesión.
- Pendientes que este informe toca: `progreso.md:908` (forwarded headers) y `progreso.md:913` (limiter por instancia). Este paso no los cierra.

## Fuentes

- [Learn, YARP getting started (versión 2.3.0, .NET 8+)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0)
- [NuGet, Yarp.ReverseProxy (versiones, MIT)](https://www.nuget.org/packages/Yarp.ReverseProxy)
- [Learn, YARP request transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms-request?view=aspnetcore-10.0)
- [Learn, YARP authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0)
- [Learn, YARP destination health checks](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/dests-health-checks?view=aspnetcore-10.0)
- [Learn, YARP distributed tracing](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/distributed-tracing?view=aspnetcore-10.0)
- [Learn, YARP rate limiting](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/rate-limiting?view=aspnetcore-10.0)
- [Learn, YARP request timeouts](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0)
- [Learn, Share authentication cookies among ASP.NET apps](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing?view=aspnetcore-10.0)
- [Learn, Securing .NET Microservices and Web Applications](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/secure-net-microservices-web-applications/)
- [Learn, API gateway pattern versus direct client-to-microservice communication](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/direct-client-to-microservice-communication-versus-the-api-gateway-pattern)
- [Azure Architecture Center, Strangler Fig pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/strangler-fig)
- [Azure Architecture Center, Health Endpoint Monitoring](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring)
- [Learn, Configure ASP.NET Core to work with proxy servers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- [Learn, IHttpClientFactory](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0)
- [Learn, HttpClient.Timeout (100 s por defecto)](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0)
- [Learn, EF Core transactions (transacciones distribuidas solo en Windows)](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
- [Docker, docker compose up (`--wait`)](https://docs.docker.com/reference/cli/docker/compose/up/)
- [dotnet-docker #2379, curl fuera de las imágenes de runtime](https://github.com/dotnet/dotnet-docker/issues/2379)
- eShop (`main`, leído el 2026-11-18): [`AppHost.cs`](https://github.com/dotnet/eShop/blob/main/src/eShop.AppHost/AppHost.cs), [`AppHost/Extensions.cs`](https://github.com/dotnet/eShop/blob/main/src/eShop.AppHost/Extensions.cs), [`ServiceDefaults/AuthenticationExtensions.cs`](https://github.com/dotnet/eShop/blob/main/src/eShop.ServiceDefaults/AuthenticationExtensions.cs), [`ServiceDefaults/Extensions.cs`](https://github.com/dotnet/eShop/blob/main/src/eShop.ServiceDefaults/Extensions.cs), [`WebApp/Services/OrderingService.cs`](https://github.com/dotnet/eShop/blob/main/src/WebApp/Services/OrderingService.cs)
- Secundaria: [Milan Jovanović, migrate your modular monolith to microservices](https://milanjovanovic.tech/blog/breaking-it-down-how-to-migrate-your-modular-monolith-to-microservices)
- Citadas en otros informes del repo y no releídas aquí: Kubernetes probes (paso 16), Grzybek (paso 22).
