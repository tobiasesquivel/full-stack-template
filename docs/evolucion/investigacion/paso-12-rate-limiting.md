# Paso 12: rate limiting por usuario y por tipo de endpoint (investigación)

- Fecha: 2026-11-16.
- Alcance: el paso 12 del [plan](../plan.md) (`plan.md:321-324`). Hoy hay una sola política fija, `"auth"`, sin partición: una ventana de 20 requests por minuto que comparte toda la API. El paso reparte el límite por usuario (o por IP si no hay sesión) y por tipo de endpoint. El plan pide cambiar la configuración del limiter, los endpoints y los tests. Verifica con un test que observa 429 con `Retry-After`, y `smoke-api.mjs` tiene que seguir pasando.
- Decisiones que ya están tomadas y que este informe no reabre:
  - El rechazo pasa de 503 a 429 en este paso, y su `code` cambia ahí (`docs/decisions.tsv:130`).
  - Una app de test por clase (`IClassFixture`) y un solo contenedor PostgreSQL, justamente porque una app compartida agotaba el limiter `auth` (`docs/decisions.tsv:33`).
  - Las e2e esperan la ventana del limiter con `waitForAuthRoom()` y `check-all.mjs` espera 65 s antes de las e2e (`docs/decisions.tsv:86`, `docs/conventions.md:111`).
  - Todo error sale como ProblemDetails por `UseStatusCodePages`, también el rechazo del limiter (`docs/conventions.md:77`).
  - La configuración entra solo por options validadas (`docs/conventions.md:67`, DUNE0013).
  - La política `"auth"` vive en `AddAuthFeature()` porque nombra a la feature (`docs/evolucion/informe-vsa-deep-modules.md:80`).
  - El limiter nativo de ASP.NET Core, sin dependencia externa (`docs/evolucion/informe-dotnet-empresarial.md:111`).
- Método: solo fuentes primarias. Son Microsoft Learn (ASP.NET Core 10), el código de `dotnet/aspnetcore` en la rama `release/10.0` (middleware de rate limiting, TestHost, StatusCodePages, `WebApplicationBuilder`, ForwardedHeaders), el código de `dotnet/runtime` en `release/10.0` (`System.Threading.RateLimiting`), la muestra oficial `dotnet/AspNetCore.Docs.Samples`, RFC 6585, RFC 9110, el borrador IETF `ratelimit-headers`, OWASP y la documentación de nginx y de GitHub. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA].
- Evidencia propia: un experimento descartable fuera del repo.
  - "Experimento": una app .NET 10 de un solo archivo (`dotnet run rl.cs`, SDK 10.0.401, runtime `Microsoft.AspNetCore.App` 10.0.12, el mismo que usa el repo) con `TestServer` (`Microsoft.AspNetCore.TestHost` 10.0.12). Tiene `AddProblemDetails` + `UseStatusCodePages` como el repo y un esquema de autenticación de prueba que toma el usuario de un header. Corre 6 casos (E1 a E6). Después borré el archivo.
  - No se tocó código del repo.

## Glosario corto

- **Rate limiting** (limitar la tasa): rechazar requests de un cliente cuando pasa de N en un período. Protege recursos (CPU, base) y reparte el servicio entre clientes (*fairness*).
- **Limiter**: el objeto que cuenta y decide. Entrega un **lease** (permiso) por request; si no hay permiso, el lease sale "no adquirido" y la request se rechaza.
- **Partición**: un balde de conteo propio. La **clave de partición** dice a qué balde va cada request (`user:abc`, `ip:10.0.0.7`). Sin partición, todos comparten un balde: es el caso de hoy.
- **Global limiter**: un limiter que el middleware aplica a **toda** request. **Política con nombre**: un limiter que se aplica solo a los endpoints que la piden con `RequireRateLimiting("nombre")`.
- **Ventana fija** (*fixed window*): N permisos por ventana; al terminar la ventana el contador vuelve a N. **Ventana deslizante** (*sliding window*): la ventana se divide en segmentos y avanza de a uno. **Token bucket** (balde de fichas): se reponen K fichas cada período, hasta un tope. **Concurrencia**: N requests a la vez, sin mirar el tiempo.
- **`Retry-After`**: header HTTP que dice cuántos segundos esperar antes de reintentar.
- **Proxy inverso**: un servidor que recibe la request del navegador y la reenvía a la API, como el nginx de `frontend/nginx.conf`. La API ve la IP del proxy, no la del navegador, salvo que lea `X-Forwarded-For` con el middleware **ForwardedHeaders**.

## Conclusión

- **Diseño:** un **global limiter** en `Infrastructure/Http` que cubre toda request, con clave = quién llama + tipo de request. Quién llama: el id de usuario (claim `NameIdentifier`) si hay sesión; si no, la IP. Tipo: lectura (`GET`, `HEAD`, `OPTIONS`) o escritura (el resto). Más la política `"auth"` que ya existe, ahora **partida por IP**, en `AuthFeature`, para login, registro y logout. Health queda exento con `DisableRateLimiting()` en su grupo. Con el global no hay que acordarse de nada al crear un endpoint: es la misma idea que el antiforgery por middleware (`docs/conventions.md:99`).
- **Algoritmo:** ventana fija en las tres clases. Da `Retry-After` y tests deterministas. La ventana deslizante **no** da `Retry-After` en .NET 10 (E2, código de `SlidingWindowRateLimiter`). El token bucket sirve, pero suma dos parámetros sin ganancia para este paso.
- **Números por defecto** [INFERENCIA, apoyada en la proporción de GitHub]: auth 20 por minuto por IP (igual que hoy, ahora por IP); usuario 300 lecturas y 60 escrituras por minuto; anónimo 120 requests por minuto por IP. Salen de `appsettings.json` por options validadas.
- **Orden de middlewares:** `UseRateLimiter()` tiene que ir **después** de `UseAuthentication()` y antes de `UseAuthorization()`. Hoy está antes (`Program.cs:34-35`) y con ese orden la partición por usuario no funciona: E1 mostró que todas las requests caen en `ip:unknown`. La muestra oficial pone el mismo orden, con el comentario "important to add after UseAuthentication because the limiter uses auth info".
- **Rechazo:** `RejectionStatusCode = 429` y un `OnRejected` que solo escribe `Retry-After` (segundos, redondeo hacia arriba). El cuerpo lo sigue poniendo `UseStatusCodePages` como ProblemDetails (E2). `HttpErrors` suma `[429] = "Http.TooManyRequests"` a su tabla.
- **Dos rarezas de .NET 10 que el test tiene que tolerar:** el `Retry-After` de la ventana fija es **la ventana entera**, no lo que falta (E2: 10 s de ventana, 3 s después sigue diciendo 10); y una request que rechaza la política `"auth"` gasta **dos** permisos del global (E5, código del middleware).
- **Tests:** `TestServer` deja `RemoteIpAddress` en `null` (E3): todas las requests anónimas de una clase comparten un balde. Los limiters no aceptan `TimeProvider`: el test usa límites chicos y ventanas largas en `CreateIsolatedClient`. Prueba 429 + `Retry-After`, usuario A agotado y B con 200, y lecturas y escrituras separadas.
- **Frontend:** un `case 429` en `http-error.ts` con su spec (regla 3: cambia el contrato).
- **Dune:** una regla mínima. `DisableRateLimiting` (método o atributo) solo en `backend/Features/Health/`. Va en `check-structure.mjs`, con fixture válida y violación deliberada.
- **Herramientas:** ninguna. `Microsoft.AspNetCore.RateLimiting` y `System.Threading.RateLimiting` vienen con el framework; el repo ya los usa sin `PackageReference`. No hay decisión de Tobi por regla 5.
- **Choque con lo esperado:** el pendiente "un recorrido de navegador seguido del smoke da 503" **no** se resuelve del todo. En `verify-template` el proxy de `ng serve` y el smoke llegan a la API desde `127.0.0.1`: la misma IP, el mismo balde de `"auth"`. La espera de 65 s y `waitForAuthRoom()` siguen haciendo falta. En Compose, todo el navegador llega con la IP de nginx: ForwardedHeaders queda como pendiente.

## 1. Algoritmo y números por tipo de endpoint

**Respuesta:** ventana fija para las tres clases (auth por IP, usuario, anónimo). Health exento.

**Qué ofrece el framework**

- "The fixed, sliding, and token limiters all limit the maximum number of requests in a time period. The concurrency limiter limits only the number of concurrent requests" y "Consider the cost of an endpoint when you select a limiter" ([Learn, Rate limiter algorithms](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#rate-limiter-algorithms)).
- Qué limiter da el dato para `Retry-After` en .NET 10:

| Algoritmo | ¿Da `RetryAfter`? | Qué valor | Fuente |
|---|---|---|---|
| Ventana fija | Sí | `Window × ventanas necesarias`: la ventana **entera**, aunque falte menos | [`FixedWindowRateLimiter.cs:209-215`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/FixedWindowRateLimiter.cs); E2 |
| Ventana deslizante | **No** | El lease fallido es estático y sin metadata: `new SlidingWindowLease(false, null)` | [`SlidingWindowRateLimiter.cs:36`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/SlidingWindowRateLimiter.cs); E2 |
| Token bucket | Sí | `ReplenishmentPeriod × períodos necesarios` | [`TokenBucketRateLimiter.cs:214-223`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/TokenBucketRateLimiter.cs); E2 |
| Concurrencia | **No** | Solo `ReasonPhrase` | [`ConcurrencyLimiter.cs:364`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/ConcurrencyLimiter.cs); E2 |

- La documentación de las versiones 11 y 12 agrega: "FixedWindowRateLimiter reports `MetadataName.RetryAfter` based on the next fixed-window boundary" ([Learn, misma página, moniker 11.0/12.0](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-11.0)). Esa nota no está en la vista de 10.0: confirma que en .NET 10 el valor es la ventana completa. El repo está en .NET 10 (`backend/TemplateApp.csproj:4`).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Ventana fija en todo | Un solo concepto. `Retry-After` gratis. Test determinista: la ventana de cada partición empieza con su primera request ([`FixedWindowRateLimiter.cs:74`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/FixedWindowRateLimiter.cs), y solo se repone cuando pasó la ventana entera, [`:290`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/FixedWindowRateLimiter.cs)). | Ráfaga doble en el borde de dos ventanas. `Retry-After` pesimista (hasta una ventana de más) en .NET 10. |
| B. Token bucket para usuario, ventana fija para auth | Absorbe la ráfaga de una página que carga y repone de a poco. | Tres parámetros por clase (`TokenLimit`, `TokensPerPeriod`, `ReplenishmentPeriod`) y dos algoritmos en un paso. |
| C. Ventana deslizante | Sin ráfaga doble en el borde. | Sin `Retry-After`: habría que calcularlo a mano, y el plan lo exige. |
| D. Concurrencia | Protege endpoints largos. | Ningún endpoint del repo es largo; no da `Retry-After`. |

Recomendación: **A**. El paso pide observar 429 con `Retry-After`; la ventana fija lo da sin código extra. B queda como mejora si una ráfaga legítima choca con el límite.

**Números por defecto** [INFERENCIA]

- Referencia de la industria: GitHub da 60 requests por hora sin autenticar y 5.000 por hora autenticado; su límite secundario cobra 1 punto por `GET` y 5 por `POST`/`PUT`/`DELETE`, con 900 puntos por minuto, y "no more than 80 content-generating requests per minute" ([GitHub Docs, Rate limits for the REST API](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api)). Es decir: escrituras 5 veces más caras que las lecturas.
- OWASP pide "Implement a limit on how often a client can interact with the API within a defined timeframe", "Rate limiting should be fine tuned based on the business needs" y "Limit/throttle how many times or how often a single API client/user can execute a single operation" ([OWASP API4:2023](https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/)).

| Clase | Clave | Límite por defecto | Por qué |
|---|---|---|---|
| Auth: `POST /auth/login`, `/auth/register`, `/auth/logout` | IP | 20 por minuto | El mismo número de hoy, ahora por IP. `waitForAuthRoom()` (`frontend/e2e/support/app.ts:9-10`) y la espera de 65 s calculan con 20: no cambian. |
| Usuario, lectura | id de usuario | 300 por minuto | 5 por segundo sostenido: una SPA de un usuario no llega ni cerca. |
| Usuario, escritura | id de usuario | 60 por minuto | 1 por segundo; 5 veces menos que las lecturas, como GitHub. |
| Anónimo (todo lo demás sin sesión) | IP | 120 por minuto | Cubre `csrf`, `/users/me` sin sesión (401), rutas inexistentes y las POST de auth (que también cuentan acá). Un `TestSession` hace `csrf` + `register` antes de tener sesión (`Tests/Support/TestAccounts.cs:28-36`), y la clase con más llamadas de auth (`CreateTaskTests`) hace unas 11: entra con margen. |
| Health: `/health/live`, `/health/ready` | — | Exento | Sección 3. |

- Por qué auth sigue con su propia política: Identity ya bloquea una **cuenta** después de 5 fallos por 5 minutos (`AuthFeature.cs:43-45`). El límite por IP frena otra cosa: probar muchas cuentas desde un mismo lugar. Si auth usara solo el global, alguien con sesión abierta haría logins contra otras cuentas con su balde de escrituras de usuario (60 por minuto), no con el de auth. [INFERENCIA] OWASP aclara que la IP sola no alcanza: los ataques distribuidos "may defeat both IP block-lists and rate limiting" ([OWASP Credential Stuffing Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Credential_Stuffing_Prevention_Cheat_Sheet.html)). Por eso es una capa más, junto al lockout.
- `QueueLimit = 0` en todas: sin cola, la request rechazada sale enseguida. Con cola, la request espera ocupando una conexión ([Learn, Request queuing](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#request-queuing)).

## 2. Clave de partición y orden de middlewares

**Respuesta:** id de usuario si hay sesión; si no, IP; `"unknown"` si la IP es `null`. Para que el limiter vea al usuario, `UseRateLimiter()` va entre `UseAuthentication()` y `UseAuthorization()`.

**El orden importa**

- El middleware lee la partición **cuando corre**: llama al partitioner con el `HttpContext` en `CombinedAcquire` ([`RateLimitingMiddleware.cs:179-208`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimitingMiddleware.cs)). `HttpContext.User` lo llena `UseAuthentication()`. Si el limiter corre antes, `User` está vacío.
- E1, con `UseRateLimiter()` antes de `UseAuthentication()` (el orden de hoy, `Program.cs:34-35`): las requests de `alice` y de `bob` cayeron en la misma clave `ip:unknown`. `alice` dio 200, 200, 429 y `bob` dio 429, 429 sin haber usado nada. Con el limiter después de la autenticación: claves `user:alice` y `user:bob`; `alice` 200, 200, 429 y `bob` 200, 200.
- La muestra oficial de Microsoft usa este orden: `app.UseAuthentication(); app.UseRateLimiter(); // important to add after UseAuthentication because the limiter uses auth info` y después `app.UseAuthorization();` ([AspNetCore.Docs.Samples, WebRateLimitAuth/Program.cs:281-283](https://github.com/dotnet/AspNetCore.Docs.Samples/blob/main/fundamentals/middleware/rate-limit/WebRateLimitAuth/Program.cs)).
- Antes de `UseAuthorization()`: así una request anónima a un endpoint protegido también cuenta antes de recibir su 401. [INFERENCIA]
- `UseRouting`: Learn dice "`UseRateLimiter` must be called after `UseRouting` when rate limiting endpoint specific APIs are used" ([Learn, Fixed window limiter](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#fixed-window-limiter)). El repo no llama a `UseRouting()`: `WebApplication` lo agrega solo al principio del pipeline cuando hay endpoints ([`WebApplicationBuilder.cs:407-423`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/DefaultBuilder/src/WebApplicationBuilder.cs)). E5 lo confirma: la política con nombre y `DisableRateLimiting()` funcionan sin `UseRouting()` explícito.
- Costo de ponerlo después de la autenticación: con una cookie válida, `OnValidatePrincipal` consulta la tabla `Sessions` (`SessionHttpPolicy.cs:89-98`) **antes** de que el limiter rechace. Una request rechazada de un usuario con sesión cuesta esa lectura. Sin cookie o con cookie inválida no hay consulta. Es el precio de partir por usuario. [INFERENCIA]
- Si la sesión fue revocada, la autenticación la rechaza y la request sigue anónima (`docs/architecture.md`, "Autorización y sesión"): cae en el balde de su IP. [INFERENCIA]

**De dónde sale el id**

- `Infrastructure` ya lee el claim `NameIdentifier` sin nombrar a Auth: lo hacen el filtro de idempotencia (`Infrastructure/Http/IdempotencyFilter.cs:143`) y la auditoría (`Infrastructure/Persistence/AuditInterceptor.cs:79`; `docs/architecture.md:59`, "Infrastructure no nombra a Auth"). El limiter global hace lo mismo con `http.User.FindFirstValue(ClaimTypes.NameIdentifier)`.
- Memoria: Learn avisa que "partitioning on unbounded user-controlled input can exhaust memory (a DoS risk)" ([Learn, Chain limiters in a named policy](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#chain-limiters-in-a-named-policy)). El id viene de una cookie cifrada que emite el servidor, no del cliente. La IP es la del socket TCP. Además, el limiter particionado borra las particiones inactivas por más de 10 s ([`DefaultPartitionedRateLimiter.cs:16`, `:251-257`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/DefaultPartitionedRateLimiter.cs)).

**IP `null` y `TestServer`**

- E3: con `TestServer`, `HttpContext.Connection.RemoteIpAddress` es `null`. El código de TestHost no lo asigna (`HttpContextBuilder.cs` y `ClientHandler.cs` de [`src/Hosting/TestHost/src`](https://github.com/dotnet/aspnetcore/tree/release/10.0/src/Hosting/TestHost/src) no lo nombran).
- Entonces la clave anónima necesita un valor fijo: `ip:unknown`, como el ejemplo de Learn (`RemoteIpAddress?.ToString() ?? "unknown"`, [Learn, By IP address](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#by-ip-address)). En los tests, toda request anónima de una clase cae en ese balde; es lo mismo que pasa hoy con el balde único.
- Para probar la partición por IP, un `IStartupFilter` de test que asigne `RemoteIpAddress` desde un header de prueba, antes del resto del pipeline (como `ErrorProbe`, `Tests/Support/ErrorProbe.cs:8-19`, pero con `app.Use(...)` **antes** de `next(app)`). [INFERENCIA: no lo escribí]

**Detrás de nginx (Compose)**

- `frontend/nginx.conf` reenvía `/api/v1/` a `api:8080` con `X-Forwarded-For $proxy_add_x_forwarded_for`. Esa variable es "the “X-Forwarded-For” client request header field with the $remote_addr variable appended to it" ([nginx, ngx_http_proxy_module](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#var_proxy_add_x_forwarded_for)): el cliente puede mandar su propio `X-Forwarded-For` y nginx agrega la IP real **a la derecha**.
- La API no usa ForwardedHeaders, entonces `RemoteIpAddress` es la IP del contenedor nginx para todo el navegador. Hoy el balde es uno solo; con este paso, todo el tráfico anónimo del navegador en Compose sigue compartiendo un balde (el de la IP de nginx). Los baldes de usuario sí se reparten bien, porque usan el id. No hay regresión: es igual que hoy para lo anónimo. [INFERENCIA]
- Si se configura: ForwardedHeaders "should run before other middleware" ([Learn, Proxy and load balancer](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)); "The request's original remote IP must match an entry in the KnownProxies or KnownNetworks lists before forwarded headers are processed"; "Only allow trusted proxies and networks to forward headers. Otherwise, IP spoofing attacks are possible" (misma página). Los valores por defecto confían solo en loopback: `KnownProxies` = `IPv6Loopback`; en .NET 10 `KnownNetworks` está obsoleto (`ASPDEPR005`) y se usa `KnownIPNetworks` ([`ForwardedHeadersOptions.cs:83-100`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HttpOverrides/src/ForwardedHeadersOptions.cs)). `ForwardLimit` es 1 por defecto: toma solo el valor de la derecha, el que agregó nginx (misma página de Learn).
- Riesgo propio de este Compose: la API también publica su puerto (`compose.yaml`, `api.ports`). Un cliente que entra directo llega con la IP del gateway de la red de Docker, que está en la misma subred que nginx. Si se confía en toda la red (`KnownIPNetworks`), ese cliente puede falsificar `X-Forwarded-For`. Hay que confiar solo en la IP exacta de nginx (`KnownProxies`), y para eso fijarla en `compose.yaml`. [INFERENCIA] Es un cambio de Compose y de despliegue con su propio test de spoofing. Recomiendo dejarlo como **pendiente** (regla 7).

## 3. Global limiter, políticas con nombre o grupos

**Respuesta:** global limiter en `Infrastructure/Http` + la política `"auth"` en Auth + `DisableRateLimiting()` en el grupo de Health. Así ningún endpoint queda sin límite por olvido.

**Cómo se combinan en .NET 10** (código de [`RateLimitingMiddleware.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimitingMiddleware.cs))

- `DisableRateLimiting` gana a todo: si el endpoint tiene esa metadata, el middleware pasa sin tocar ni el global ni la política (`:67-72`). Learn: "regardless of named rate limiters or global limiters applied" ([Learn, EnableRateLimiting and DisableRateLimiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#enableratelimiting-and-disableratelimiting-attributes)).
- Sin política y sin global, no limita (`:73-78`).
- Con los dos, primero el global y después la política del endpoint; la request necesita los dos permisos (`:179-208`).
- `RequireRateLimiting` agrega metadata `EnableRateLimitingAttribute` al endpoint ([`RateLimiterEndpointConventionBuilderExtensions.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterEndpointConventionBuilderExtensions.cs)); un nombre que no existe lanza `InvalidOperationException` en la primera request (`RateLimitingMiddleware.cs:271-278`).
- El global corre también en rutas sin endpoint: E5/E6, un `GET /nope` (404) gastó un permiso del global.
- **Hallazgo:** si la política del endpoint rechaza, el global pierde **dos** permisos. `TryAcquireAsync` prueba `CombinedAcquire` y, si falla, vuelve a pedir todo con `CombinedWaitAsync`, que adquiere de nuevo el global (`:151-163`, `:210-230`). Un permiso de ventana fija no se devuelve al liberar el lease. E5: global de 5; `strict` 200 (1), `strict` 429 por la política (3), `plain` 200 (4), `/nope` 404 (5), `plain` 429 por el global. Los tests que cuentan permisos tienen que sumarlo.
- `AddFixedWindowLimiter("auth", ...)` crea **una sola partición** para todos: la clave es el nombre de la política ([`RateLimiterOptionsExtensions.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterOptionsExtensions.cs), `AddFixedWindowLimiter`). Por eso hoy es un balde para toda la API. Para partir por IP hay que usar `AddPolicy(nombre, http => RateLimitPartition.GetFixedWindowLimiter(clave, ...))` ([`RateLimiterOptions.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterOptions.cs), `AddPolicy`).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Global limiter (clave = quién + tipo) | Un endpoint nuevo queda limitado sin pedirlo, como el antiforgery por middleware (`docs/conventions.md:99`). Vive en `Infrastructure/Http` sin nombrar features. Cubre 404 y rutas de prueba. | El tipo sale del método HTTP, no de lo que hace el endpoint. Una excepción pide `DisableRateLimiting` (regla de Dune, sección 8). |
| B. Política con nombre por endpoint | Límite a medida por slice. | Cada `Map*` tiene que acordarse; haría falta un analyzer como DUNE0016 para cada endpoint. Los 404 no cuentan. |
| C. Política por grupo de feature (`/tasks`, `/users`) | Un renglón por feature. | Cada feature nueva tiene que acordarse; el límite no es "por usuario" entre features sino por feature. |

Recomendación: **A**, más la política `"auth"` por IP en Auth. Encaja con ADR-0004: "Lo que nombra una feature va a esa feature. `Infrastructure/` queda para la fontanería técnica del host" ([ADR-0004, punto 6](../../adr/0004-vertical-slices-deep-modules.md)). El global no nombra features; `"auth"` sí, y queda en `AuthFeature` (`informe-vsa-deep-modules.md:80`).

**Dónde vive cada pieza** [INFERENCIA, siguiendo `docs/architecture.md:32` y `docs/conventions.md`]

| Pieza | Lugar |
|---|---|
| `AddApiRateLimiting()`: `RejectionStatusCode`, `OnRejected`, `GlobalLimiter` | `backend/Infrastructure/Http/ApiRateLimiting.cs` (junto a `VersionedApi.cs` e `Idempotency.cs`) |
| Options del global y su validador | `Infrastructure/Http/ApiRateLimitOptions.cs` y `ApiRateLimitOptionsValidator.cs` |
| Política `"auth"` por IP, su constante y sus options | `Features/Auth/AuthFeature.cs` (`AddAuthHttpPolicy`, hoy `:76-84`) y un `*Options` propio en `Features/Auth/` |
| Uso de la política | `RegisterUserEndpoint`, `OpenSessionEndpoint`, `CloseSessionEndpoint`, con la constante en lugar del texto `"auth"` |
| Exención | `Features/Health/HealthFeature.cs`: `api.MapGroup("/health").AllowAnonymous().DisableRateLimiting()` |
| Registro y orden | `Program.cs`: `AddApiRateLimiting()` y `UseRateLimiter()` entre `UseAuthentication()` y `UseAuthorization()` |
| Código del rechazo | `Shared/Http/HttpErrors.cs`: `[429] = "Http.TooManyRequests"` |

Esbozo del global (sin comentarios, como pide `docs/conventions.md`; los nombres son propuesta):

```csharp
services.AddRateLimiter();
services.AddOptions<RateLimiterOptions>()
    .Configure<IOptions<ApiRateLimitOptions>>((limiter, limits) =>
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = WriteRetryAfter;
        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => Partition(http, limits.Value));
    });

private static RateLimitPartition<string> Partition(HttpContext http, ApiRateLimitOptions limits)
{
    string? userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    (string key, int permits) = userId is null
        ? ($"anonymous:{http.Connection.RemoteIpAddress?.ToString() ?? "unknown"}", limits.AnonymousRequests)
        : IsRead(http.Request.Method) ? ($"read:{userId}", limits.UserReads) : ($"write:{userId}", limits.UserWrites);
    return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permits,
        Window = limits.Window,
        QueueLimit = 0,
    });
}
```

- `AddRateLimiter()` sin parámetros registra solo los servicios; `AddRateLimiter(Action)` hace eso más `services.Configure(...)` ([`RateLimiterServiceCollectionExtensions.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterServiceCollectionExtensions.cs)). Varias configuraciones se suman, así Infrastructure y Auth configuran el mismo `RateLimiterOptions` cada uno por su lado.
- `Configure<IOptions<T>>` es el mismo patrón que usa hoy la cookie (`AuthFeature.cs:62-63`).
- El tipo va en la clave (`read:`, `write:`, `anonymous:`) porque la fábrica corre una sola vez por clave: si dos tipos compartieran clave, el primer límite quedaría para los dos. [INFERENCIA]
- `RateLimitPartition.GetFixedWindowLimiter` apaga el timer propio de cada limiter y los repone todos desde un timer común cada 100 ms ([`RateLimitPartition.cs:132-150`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/RateLimitPartition.cs), [`DefaultPartitionedRateLimiter.cs:37`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/DefaultPartitionedRateLimiter.cs)).

**Health: ¿exento?**

- `smoke-api.mjs` pide `health/ready` cada 500 ms hasta 60 s (`smoke-api.mjs:120-129`), y `verify.mjs` cada 1 s (`verify.mjs:1169-1183`). Con el balde anónimo de 120 por minuto, esa espera sola puede agotarlo. Además, un 429 en una sonda de vida haría que un orquestador reinicie un contenedor sano. [INFERENCIA]
- Costo de exentar: `/health/ready` consulta la base (`GetReadiness`) y nginx lo publica, porque cuelga de `/api/v1/`. Queda un endpoint anónimo sin límite que toca la base. Recomiendo anotarlo como pendiente para el paso 13 o de operación. [INFERENCIA]

Recomendación: **exento**, con `DisableRateLimiting()` en el grupo de Health. E5 confirmó que en el grupo saltea también el global.

## 4. El rechazo: 429, `Retry-After` y el cuerpo

**Respuesta:** `RejectionStatusCode = 429`; `OnRejected` escribe solo `Retry-After` con `Math.Ceiling`; el cuerpo lo pone `UseStatusCodePages`; `HttpErrors` suma el 429 a su tabla.

**Status**

- "The 429 status code indicates that the user has sent too many requests in a given amount of time ("rate limiting")" y "The response representations SHOULD include details explaining the condition, and MAY include a Retry-After header" ([RFC 6585 §4](https://www.rfc-editor.org/rfc/rfc6585#section-4)). El RFC no fija cómo identificar al usuario: "it might identify the user by its authentication credentials, or a stateful cookie" (misma sección).
- 503 es otra cosa: "the server is currently unable to handle the request due to a temporary overload or scheduled maintenance" ([RFC 9110 §15.6.4](https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4)). El default del framework es 503 (`RejectionStatusCode = StatusCodes.Status503ServiceUnavailable`, [`RateLimiterOptions.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterOptions.cs)).
- El middleware fija el status y **después** llama a `OnRejected` (`RateLimitingMiddleware.cs:121-147`): con `RejectionStatusCode = 429`, `OnRejected` no necesita tocar el status.

**`Retry-After`**

- "Retry-After = HTTP-date / delay-seconds" y "A delay-seconds value is a non-negative decimal integer, representing time in seconds" ([RFC 9110 §10.2.3](https://www.rfc-editor.org/rfc/rfc9110#section-10.2.3)).
- Learn muestra dos formas: la de `CreateChained` usa `(int) retryAfter.TotalSeconds` (trunca) y la de "Choosing what happens when a request is rate limited" usa `(long)Math.Ceiling(retryAfter.TotalSeconds)` con `NumberFormatInfo.InvariantInfo` ([Learn](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#choosing-what-happens-when-a-request-is-rate-limited)). Recomiendo `Math.Ceiling`: truncar puede decir `0` para una espera de menos de un segundo. E2: ventana de 0,4 s dio `Retry-After: 1`.
- Si el lease no trae `RetryAfter` (no pasa con ventana fija), no se escribe el header. [INFERENCIA]
- El lease que llega a `OnRejected` es el del limiter que rechazó: el global o la política (`RateLimitingMiddleware.cs:146`, `:191`, `:198`). El `Retry-After` sale del que rechazó.
- Caché: "Responses with the 429 status code MUST NOT be stored by a cache" ([RFC 6585 §4](https://www.rfc-editor.org/rfc/rfc6585#section-4)). 429 no está en la lista de status "heuristically cacheable" ([RFC 9110 §15.1](https://www.rfc-editor.org/rfc/rfc9110#section-15.1)), así que sin `Cache-Control` explícito ninguna caché lo guarda. No hace falta agregar headers. [INFERENCIA]

**El cuerpo**

- `UseStatusCodePages` (`Program.cs:33`) envuelve al limiter. Escribe un cuerpo solo si la respuesta no empezó, el status está entre 400 y 599 y no hay `Content-Length` ni `Content-Type` ([`StatusCodePagesMiddleware.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/Diagnostics/src/StatusCodePage/StatusCodePagesMiddleware.cs), "Do nothing if a response body has already been provided"). Un header `Retry-After` no cambia nada de eso. Su manejador por defecto usa `IProblemDetailsService` ([`StatusCodePagesOptions.cs:23-29`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/Diagnostics/src/StatusCodePage/StatusCodePagesOptions.cs)), y ahí entra el `CustomizeProblemDetails` del repo (`BackendInfrastructure.cs:16-19`).
- E2: `OnRejected` que solo escribe el header dio `429`, `Retry-After: 10` y `application/problem+json` con `title: "Too Many Requests"`, `status`, `traceId` y `code`. Es lo que pide `ProblemAssert` (`Tests/Support/ProblemAssert.cs:9-19`).
- Escribir el cuerpo dentro de `OnRejected` con `IProblemDetailsService` daría lo mismo con un camino más. Recomiendo no hacerlo: `docs/conventions.md:77` ya dice que el rechazo del limiter sale por `UseStatusCodePages`.

**El `code`**

- Hoy `CodeFor(429)` cae a la frase estándar: `ReasonPhrases` da "Too Many Requests" → `Http.TooManyRequests` (`HttpErrors.cs:35-42`; el `title` de E2 confirma la frase).
- Recomiendo escribirlo en la tabla (`[429] = "Http.TooManyRequests"`, junto a `HttpErrors.cs:26-33`). "Un código publicado no cambia" (`docs/conventions.md:75`): con la entrada explícita, el código no depende de la tabla de frases del framework. El `503 → Http.ServiceUnavailable` queda: lo sigue usando cualquier 503 vacío.
- El paso cambia el `code` del rechazo de `Http.ServiceUnavailable` a `Http.TooManyRequests`. Es la excepción que ya anticipó `docs/decisions.tsv:130`; va con su línea en `decisions.tsv`.

**Log**

- El middleware ya registra cada rechazo en nivel `Debug` (`RateLimitingMiddleware.cs:290-291`). No recomiendo un log propio con la IP o el id: `docs/conventions.md:100` limita lo que se loguea. Las métricas nativas (`aspnetcore.rate_limiting.requests` y otras, [`RateLimitingMetrics.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimitingMetrics.cs)) son del paso 14.

## 5. Límites desde configuración, tests, smoke y e2e

**Respuesta:** options validadas, con los valores por defecto en `appsettings.json`. Los tests bajan los límites solo en `CreateIsolatedClient`. La espera de 65 s y `waitForAuthRoom()` se quedan.

- Learn: "Apps should use Configuration to set limiter options" ([Learn, Fixed window limiter](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#fixed-window-limiter)). El repo pide options con `[OptionsValidator]` y `AddOptionsWithValidateOnStart` (`docs/conventions.md:67`), como `SessionOptions` (`Features/Auth/SessionOptions.cs`, con `[Range]`).
- Propuesta [INFERENCIA]:
  - `ApiRateLimitOptions` (sección `RateLimiting`): `WindowSeconds` (60), `UserReads` (300), `UserWrites` (60), `AnonymousRequests` (120), cada uno con `[Range]`.
  - Un `*Options` de Auth (por ejemplo sección `AuthRateLimit`): `PermitLimit` (20) y `WindowSeconds` (60). Una sección propia, porque Infrastructure no puede nombrar a Auth.
  - Los valores van en `appsettings.json` (base de todos los entornos, ya tiene `Session`) y en `appsettings.example.json`.
- Constantes en el código obligarían a los tests a mandar 300 requests para ver un 429 de lectura. Con options, el test usa `CreateIsolatedClient(settings, ...)` (`Tests/Support/ApiFixture.cs:30-49`) con, por ejemplo, `RateLimiting:UserWrites = 2`. Cada cliente aislado es una app nueva, con limiters nuevos.
- La app compartida de cada clase usa los valores por defecto. Con el global, los usuarios de cada test tienen baldes propios; lo anónimo de una clase comparte `anonymous:unknown` (120) y `"auth"` (20), como hoy comparte el balde único de 20. El hilo tiene que correr la suite completa para confirmarlo. [INFERENCIA]

**Smoke y e2e**

- `verify-template` levanta la API en `127.0.0.1` (`verify.mjs:1137`, `:1148`) y `ng serve` con un proxy a `http://127.0.0.1:<puerto>` (`verify.mjs:1253-1256`). El smoke habla directo con la API (`verify.mjs:1566`). Todo llega a la API desde loopback, con la misma IP. [INFERENCIA: no medí la IP que ve la API detrás del proxy de `ng serve`]
- Entonces el balde `"auth"` de `127.0.0.1` lo comparten el smoke y el navegador: la espera de 65 s (`check-all.mjs:14`, `:242-244`) y `waitForAuthRoom()` (`frontend/e2e/support/app.ts:9-10`, `:52-59`) siguen haciendo falta. Recomiendo no tocarlas.
- El global no debería frenar el smoke ni las e2e: los usuarios tienen baldes propios, health queda exento y lo anónimo de una corrida está muy por debajo de 120 por minuto. El hilo lo comprueba con `check-all.mjs`. [INFERENCIA]
- Para sacar la espera haría falta otra cosa: que `verify.mjs` levante la API con un límite de auth alto por variable de entorno (`AuthRateLimit__PermitLimit`), o partir por algo distinto de la IP. Es un cambio del arnés de pruebas y del pendiente de e2e en CI (`progreso.md`, Pendientes). Lo dejo como pendiente.

## 6. Cómo probarlo sin tests que fallan al azar

**Respuesta:** límites chicos y ventanas largas en un cliente aislado. No hay reloj falso para los limiters.

- Los limiters de .NET 10 miden el tiempo con `Stopwatch.GetTimestamp()` ("Use Stopwatch instead of DateTime.UtcNow to avoid issues on systems where the clock can change", [`FixedWindowRateLimiter.cs:272-273`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/FixedWindowRateLimiter.cs)). Ninguna clase de options tiene un `TimeProvider` (`FixedWindowRateLimiterOptions.cs`, `TokenBucketRateLimiterOptions.cs`, `SlidingWindowRateLimiterOptions.cs` y `ConcurrencyLimiterOptions.cs` en [`release/10.0`](https://github.com/dotnet/runtime/tree/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting)). El `FakeTimeProvider` que ya usa el repo no sirve acá.
- Cómo queda determinista: con `PermitLimit = 2` y `WindowSeconds = 3600` en un cliente aislado, la tercera request de la misma partición da 429, salvo que pase una hora. La ventana de cada partición nace con su primera request (sección 1), así que no depende de cuándo arrancó la app. [INFERENCIA]
- `Retry-After`: comprobar que es un entero entre 1 y la ventana, no un valor exacto. En .NET 10 vale la ventana entera (E2); en .NET 11 cambia a lo que falta (nota de Learn 11.0). Con un rango, el test no se rompe al actualizar.
- Contar el doble gasto del global (sección 3) cuando un test agota la política `"auth"` y después mira el global.

Tests propuestos (en `backend/Tests/RateLimiting/`, porque el global es de `Infrastructure/`, `docs/conventions.md:107`) [INFERENCIA]:

| Test | Qué hace | Qué prueba |
|---|---|---|
| Escrituras de un usuario | Cliente aislado con `UserWrites = 2`. `alice` hace 3 `POST /tasks` | 201, 201, 429 con `Retry-After` y ProblemDetails `Http.TooManyRequests` |
| Partición por usuario | Después del anterior, `bob` hace `POST /tasks` | 201: el balde de `alice` no es el de `bob`. Falla si `UseRateLimiter()` vuelve a quedar antes de `UseAuthentication()` (E1) |
| Lectura y escritura separadas | `alice`, ya agotada en escrituras, hace `GET /tasks` | 200 |
| Auth por IP | Cliente aislado con `AuthRateLimit:PermitLimit = 2` y el `IStartupFilter` de IP. IP A agota `login`; IP B intenta | A: 429; B: 401 o 200, no 429 |
| Health exento | Cliente aislado con `AnonymousRequests = 1`. Varios `GET /health/live` | Siempre 200 |
| Contrato (reemplaza `ErrorContractTests.AuthLimiterRejection_Returns503_HttpServiceUnavailable`, `ErrorContractTests.cs:204-217`) | El mismo recorrido con `logout` | 429, `Http.TooManyRequests`, `Retry-After` |

- Registrar usuarios en un cliente con límites bajos gasta auth y anónimo: el test baja solo el límite que mide y deja los otros altos.

## 7. Frontend

**Respuesta:** sí. Un `case 429` en `describeHttpError` con su test, en el mismo PR.

- El paso cambia el contrato HTTP: el status (503 → 429), el `code`, el header nuevo y rechazos nuevos fuera de `/auth`. AGENTS.md regla 3 y `docs/conventions.md:116` ("Si cambia un contrato HTTP, el PR incluye backend, frontend y tests") piden los tres juntos.
- Hoy un 429 cae en `default` (`frontend/src/app/core/http-error.ts:48-49`) y muestra "Error inesperado del servidor". Lo usan login, registro, tareas y el logout (`login.ts:43`, `register.ts:53`, `tasks.ts:71`, `:105`, `app.ts:24`).
- Propuesta: "Demasiadas solicitudes. Espera un momento y vuelve a intentarlo." Si viene `Retry-After`, sumar los segundos (`error.headers.get('Retry-After')`). El frontend y la API comparten origen (`docs/architecture.md`, "Frontend"), así que el header es legible sin CORS. [INFERENCIA]
- Test en `http-error.spec.ts`, junto a los que ya tiene (`:38-58`).
- `smoke-api.mjs` no necesita cambios: solo espera un 503 de readiness con la base caída (`smoke-api.mjs:242`), y health queda exento.

## 8. Regla de Dune

**Respuesta:** una regla chica. Con el global, "todo endpoint tiene límite" ya es estructural. La única salida es `DisableRateLimiting`, y esa salida se limita a Health.

- La regla nueva que el paso escribe en los docs sería: "Toda request pasa por el limiter global; solo Health se exime". Por regla 4 tiene que ser ejecutable.
- `BannedSymbols.txt` ya prohíbe las salidas del antiforgery (`DisableAntiforgery`, `[RequireAntiforgeryToken(false)]`). Pero no admite excepciones por carpeta, y Health necesita la salida.
- Forma mínima: en `dune/check-structure.mjs`, rechazar el texto `DisableRateLimiting` (método `DisableRateLimiting()`, atributo `[DisableRateLimiting]` o `DisableRateLimitingAttribute`) en cualquier `.cs` del backend fuera de `backend/Features/Health/`. Es el mismo estilo que la regla de caché del paso 11 (`check-structure.mjs:197-218`). Fixture válida: el `HealthFeature` actual. Violación deliberada: un `.DisableRateLimiting()` en un endpoint de Tasks, en `check-structure.test.mjs`.
- No recomiendo un analyzer para "`RequireRateLimiting` solo con constantes": un nombre que no existe ya lanza `InvalidOperationException` en la primera request (`RateLimitingMiddleware.cs:271-278`), y los tests de auth lo atraparían.
- El orden `UseAuthentication` → `UseRateLimiter` → `UseAuthorization` lo cuida el test de partición por usuario (sección 6). Hay un pendiente parecido para `UseAuthAntiforgery` en `progreso.md`.

## 9. Herramientas, librerías y paquetes

**Respuesta:** ninguna. No hay decisión de Tobi por regla 5.

- `Microsoft.AspNetCore.RateLimiting` es parte del framework: vive en `src/Middleware/RateLimiting` de `dotnet/aspnetcore` y el repo ya lo usa (`AuthFeature.cs:3`, `:76`) sin `PackageReference` (`backend/TemplateApp.csproj:12-35`). `System.Threading.RateLimiting` llega con él. ForwardedHeaders, si se hace después, también es del framework.
- Alternativas que **sí** serían decisión de herramienta, y que este paso no necesita:
  - `AspNetCoreRateLimit` ([stefanprodan/AspNetCoreRateLimit](https://github.com/stefanprodan/AspNetCoreRateLimit)): librería de terceros; su último push es de julio de 2024.
  - Limiters con Redis para varias instancias ([cristipufu/aspnetcore-redis-rate-limiting](https://github.com/cristipufu/aspnetcore-redis-rate-limiting)), o Redis/Valkey en general (`informe-dotnet-empresarial.md:423`). Learn: "The rate limiting middleware keeps its counters in memory in each app instance [...] The effective limit is the configured limit multiplied by the number of instances" ([Learn, Rate limiting with multiple app instances](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#rate-limiting-with-multiple-app-instances)). El repo corre una sola instancia.
  - Limitar en un gateway: `limit_req` de nginx ("leaky bucket", [nginx, ngx_http_limit_req_module](https://nginx.org/en/docs/http/ngx_http_limit_req_module.html)) o un API gateway. Solo cubriría el tráfico que pasa por nginx, no el de `verify-template` ni el puerto publicado de la API.
  - Protección DDoS comercial: Learn dice que el limiter "it's not a comprehensive solution for Distributed Denial of Service (DDoS) attacks" ([Learn, Prevent DDoS attacks](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0#prevent-ddos-attacks)).
- Headers `RateLimit` y `RateLimit-Policy`: siguen en borrador ([draft-ietf-httpapi-ratelimit-headers-11](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/), sin número de RFC, vence el 2026-11-24). No son una herramienta, pero tampoco los pide el plan. Pendiente.

## 10. Choques con los docs y con el plan

- **El pendiente del navegador seguido del smoke** (`progreso.md`, Pendientes: "Un recorrido de navegador seguido del smoke HTTP da 503 (`fase0-06-runtime`). Paso 12") no queda resuelto: localmente los dos salen de `127.0.0.1` y comparten el balde de auth (sección 5). El paso resuelve la partición y el 429; el pendiente pasa a decir 429 y a nombrar ForwardedHeaders o el límite del arnés como salida. No es un choque entre docs, es una expectativa del pendiente; lo anoto para que el cierre lo diga.
- **"Un código publicado no cambia"** (`docs/conventions.md:75`) contra el cambio de `Http.ServiceUnavailable` a `Http.TooManyRequests`: ya decidido en `docs/decisions.tsv:130`. El paso lo registra.
- **Docs que dicen "20 por minuto para toda la API"** y hay que actualizar: `README.md:113`, `docs/conventions.md:111` y la entrada de `progreso.md` del paso 0.24. `docs/conventions.md:77` sigue valiendo (el rechazo sale por `UseStatusCodePages`). `docs/architecture.md:32` suma el limiter global a la lista de Infrastructure.
- **ADR-0004, `architecture.md` y `conventions.md`**: no encontré choque. El global en `Infrastructure/Http` no nombra features; la política `"auth"` queda en Auth; Health declara su exención en su `HealthFeature`.
- **Plan**: "Cambia: configuración del limiter, endpoints, tests". Además entran `HttpErrors` (catálogo), el frontend (regla 3), `check-structure.mjs` (regla 4) y docs. Es lo mismo que pasó en el paso 9.

## Decisiones que toma el hilo (modo autónomo)

1. Diseño: global limiter en `Infrastructure/Http` (quién llama + lectura/escritura) más la política `"auth"` por IP en Auth (sección 3, opción A).
2. Algoritmo: ventana fija en todo, sin cola (sección 1, opción A).
3. Números por defecto: auth 20/min por IP; usuario 300 lecturas y 60 escrituras por minuto; anónimo 120/min por IP (sección 1).
4. Clave: `NameIdentifier` si hay sesión; si no, IP; `unknown` si es `null` (sección 2).
5. Orden: `UseRateLimiter()` entre `UseAuthentication()` y `UseAuthorization()` (sección 2).
6. Health exento con `DisableRateLimiting()` en su grupo (sección 3).
7. Rechazo: `RejectionStatusCode = 429`, `OnRejected` con `Retry-After` redondeado hacia arriba, cuerpo por `UseStatusCodePages`, `[429] = "Http.TooManyRequests"` en `HttpErrors` (sección 4).
8. Límites por options validadas, con secciones separadas para Infrastructure y Auth (sección 5).
9. Espera de 65 s y `waitForAuthRoom()` sin cambios (sección 5).
10. Tests en `backend/Tests/RateLimiting/` con clientes aislados y un `IStartupFilter` de IP; `Retry-After` comprobado por rango (sección 6).
11. Frontend: `case 429` con los segundos de `Retry-After` si vienen, y su spec (sección 7).
12. Dune: `DisableRateLimiting` solo en `backend/Features/Health/`, en `check-structure.mjs` (sección 8).

Las de diseño (1, 2, 4, 5, 6, 7, 8 y 12) van en `docs/decisions.tsv`.

## Pendientes fuera de alcance (para `progreso.md`)

- ForwardedHeaders en Compose: hoy todo el tráfico anónimo del navegador comparte el balde de la IP de nginx. Necesita `KnownProxies` con la IP exacta de nginx fijada en `compose.yaml` (no toda la red: el puerto publicado de la API entra por el gateway de la misma red) y un test de spoofing.
- El recorrido de navegador seguido del smoke sigue compartiendo el balde de auth en `127.0.0.1`. Opciones: un límite de auth alto para `verify-template` por variable de entorno, o ForwardedHeaders. Liga con el pendiente de e2e en CI.
- `/health/ready` queda anónimo, sin límite y consultando la base, y nginx lo publica bajo `/api/v1/`.
- Headers `RateLimit`/`RateLimit-Policy` del borrador IETF, cuando sea RFC.
- Métricas del limiter (`aspnetcore.rate_limiting.*`) en el export OTLP: paso 14.
- IPv6: una clave por dirección deja rotar direcciones dentro de un mismo /64. Partir por prefijo /64.
- Varias instancias multiplican el límite (Learn): un store compartido o un gateway, con decisión de herramienta.
- `Retry-After` pesimista de la ventana fija en .NET 10: se corrige solo al pasar a .NET 11.

## Qué quedó sin comprobar

- No corrí nada del repo: ni tests, ni smoke, ni e2e. El experimento usó un esquema de autenticación de prueba, sin cookie, sin `Sessions`, sin Identity y sin el grupo versionado.
- Que el proxy de `ng serve` llegue a la API como `127.0.0.1`: lo deduzco de `proxy.verify.json` (`verify.mjs:1253-1256`); no medí `RemoteIpAddress` en una corrida real.
- Que los números por defecto no frenen la suite, el smoke ni las e2e: es una estimación por conteo de llamadas, no una medición.
- El `IStartupFilter` que asigna la IP en los tests: no lo escribí.
- El riesgo de spoofing por el puerto publicado de la API en Compose: deducción sobre cómo Docker publica puertos; no lo probé.
- La regla de `check-structure.mjs`: no la escribí ni la corrí.
- Que `FakeTimeProvider` no tenga ningún efecto en los limiters: lo deduzco del código (no reciben `TimeProvider`); no lo probé.

## Fuentes

- https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-11.0
- https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimitingMiddleware.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterOptions.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterOptionsExtensions.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterEndpointConventionBuilderExtensions.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimiterServiceCollectionExtensions.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/RateLimiting/src/RateLimitingMetrics.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/Diagnostics/src/StatusCodePage/StatusCodePagesMiddleware.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/Diagnostics/src/StatusCodePage/StatusCodePagesOptions.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/DefaultBuilder/src/WebApplicationBuilder.cs
- https://github.com/dotnet/aspnetcore/tree/release/10.0/src/Hosting/TestHost/src
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HttpOverrides/src/ForwardedHeadersOptions.cs
- https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/FixedWindowRateLimiter.cs
- https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/SlidingWindowRateLimiter.cs
- https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/TokenBucketRateLimiter.cs
- https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/ConcurrencyLimiter.cs
- https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/RateLimitPartition.cs
- https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/DefaultPartitionedRateLimiter.cs
- https://github.com/dotnet/AspNetCore.Docs.Samples/blob/main/fundamentals/middleware/rate-limit/WebRateLimitAuth/Program.cs
- https://www.rfc-editor.org/rfc/rfc6585#section-4
- https://www.rfc-editor.org/rfc/rfc9110#section-10.2.3
- https://www.rfc-editor.org/rfc/rfc9110#section-15.1
- https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4
- https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/
- https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/
- https://cheatsheetseries.owasp.org/cheatsheets/Credential_Stuffing_Prevention_Cheat_Sheet.html
- https://nginx.org/en/docs/http/ngx_http_proxy_module.html#var_proxy_add_x_forwarded_for
- https://nginx.org/en/docs/http/ngx_http_limit_req_module.html
- https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api
- https://github.com/stefanprodan/AspNetCoreRateLimit
- https://github.com/cristipufu/aspnetcore-redis-rate-limiting
