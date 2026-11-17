# Paso 13: cabeceras de seguridad HTTP (investigación)

- Fecha: 2026-11-17.
- Alcance: la mitad "cabeceras" del paso 13 del [plan](../plan.md) (`plan.md:326-329`). El concepto es "superficie HTTP y cadena de dependencias como parte del diseño". Cambia "middlewares, `Program.cs`, `ci.yml`, `docs/`". Verifica con "cabeceras observables en una respuesta real". El escaneo de dependencias y de secretos en `ci.yml` lo investiga otro agente y no está acá.
- Decisiones que ya están tomadas y que este informe no reabre:
  - La API solo devuelve JSON y ProblemDetails. Todo error sale por `UseStatusCodePages` o por `UseExceptionHandler` con `GlobalExceptionHandler` (`docs/conventions.md:74-77`, `backend/Program.cs:33-34`).
  - La sesión es la cookie `__Host-session` (`HttpOnly`, `Secure`, `SameSite=Strict`), y la única cookie legible es `XSRF-TOKEN` (`docs/architecture.md:74`, `docs/conventions.md:98`).
  - El antiforgery se valida en un middleware de Auth y no se agrega una regla de Dune que exija registrarlo, porque los tests ya lo detectan (`docs/decisions.tsv:92`, `docs/decisions.tsv:104`).
  - `Infrastructure/` es fontanería que no nombra features; lo que nombra una feature va a esa feature (`docs/architecture.md:12`, `docs/conventions.md:22`, ADR-0004 punto 6).
  - La configuración entra solo por options validadas (`docs/conventions.md:67`, DUNE0013).
  - ForwardedHeaders en Compose quedó como pendiente del paso 12 (`docs/evolucion/progreso.md:98`).
- Método: solo fuentes primarias. Son OWASP (REST Security Cheat Sheet, HTTP Headers Cheat Sheet y los JSON del OWASP Secure Headers Project), MDN, Microsoft Learn (ASP.NET Core 10), el código de `dotnet/aspnetcore` en la rama `release/10.0` (HSTS, antiforgery, exception handler, Kestrel, TestHost, Mvc.Testing), el repo, el paquete NuGet y un PR de NetEscapades.AspNetCore.SecurityHeaders, la guía de seguridad de Angular y la documentación de nginx. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA].
- Evidencia propia: un experimento descartable fuera del repo.
  - "Experimento": una app .NET 10 de un solo archivo (`dotnet run sh.cs`, SDK 10.0.401, runtime `Microsoft.AspNetCore.App` 10.0.12, el mismo de `backend/Dockerfile`). Corre en **Kestrel real** sobre `http://127.0.0.1`, con `AddProblemDetails`, `UseExceptionHandler`, `UseStatusCodePages`, un limiter global de 1 request por hora en `/limited` y un endpoint que llama a `IAntiforgery.GetAndStoreTokens`, igual que `SessionHttpPolicy.IssueCsrf` (`backend/Features/Auth/SessionHttpPolicy.cs:78`). Tiene cinco modos (E1 a E5, abajo). Después borré el archivo.
  - No se tocó código del repo.

## Glosario corto

- **Cabecera de respuesta** (*response header*): un par nombre-valor que el servidor manda antes del cuerpo. Las de seguridad son instrucciones para el navegador: "no adivines el tipo", "no me muestres dentro de un iframe", "no guardes esto en caché".
- **MIME sniffing**: el navegador ignora el `Content-Type` y adivina el tipo mirando el contenido. Un JSON con HTML adentro podría terminar interpretado como página. `X-Content-Type-Options: nosniff` lo apaga.
- **CSP** (*Content Security Policy*): la cabecera `Content-Security-Policy`. Le dice al navegador de dónde puede cargar scripts, estilos e imágenes un documento, y quién puede enmarcarlo (`frame-ancestors`).
- **Clickjacking**: otra página carga la tuya en un iframe invisible y engaña al usuario para que haga clic. `frame-ancestors 'none'` y `X-Frame-Options: DENY` lo impiden.
- **HSTS** (*HTTP Strict Transport Security*): la cabecera `Strict-Transport-Security`. Le dice al navegador "a este host entrá solo por HTTPS durante N segundos".
- **Terminación TLS**: el punto donde se descifra HTTPS. Si lo hace un proxy (nginx, un balanceador), la app detrás recibe HTTP plano.
- **`OnStarting`**: un callback de `HttpResponse` que corre justo antes de mandar las cabeceras. Sirve para poner cabeceras "a último momento", sin importar qué middleware generó la respuesta.
- **Respuesta de error generada por el framework**: el 404 que arma `UseStatusCodePages`, el 500 de `UseExceptionHandler`, el 429 del limiter, el 401 del esquema de cookie. No las escribe ningún endpoint del repo.

## Conclusión

- **Qué cabeceras:** la API manda seis cabeceras en **toda** respuesta, sin depender del entorno, y deja de mandar `Server`:

| Cabecera | Valor | Por qué en esta API |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | OWASP la pide en toda respuesta de API: evita que un JSON se interprete como HTML |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | `frame-ancestors 'none'` es la de la tabla principal de OWASP REST; `default-src 'none'` neutraliza cualquier contenido si alguna vez se renderizara. Es el preset de API de NetEscapades |
| `X-Frame-Options` | `DENY` | Cabecera vieja que OWASP REST todavía lista. Además pisa el `SAMEORIGIN` que el antiforgery agrega en `/auth/csrf`, login, registro y logout (E5) |
| `Referrer-Policy` | `no-referrer` | El valor de OWASP REST para APIs. Casi sin efecto en JSON; es defensa en profundidad |
| `Cross-Origin-Resource-Policy` | `same-origin` | La SPA llama a `/api/v1` desde el mismo origen (proxy de `ng serve` y nginx); bloquea que otro sitio cargue un JSON en modo `no-cors` |
| `Cache-Control` | `no-store` | OWASP REST: ninguna caché guarda la respuesta. La API devuelve datos de una sesión por cookie |
| `Server` | (sin cabecera) | OWASP pide quitarla. Kestrel la manda por defecto (`Server: Kestrel`, E1); se apaga con `AddServerHeader = false` |

- **Qué no se manda:** `Strict-Transport-Security` (la app no recibe HTTPS en ningún entorno del repo: el navegador la ignoraría y `UseHsts` nunca la escribe; es trabajo de quien termine TLS). `X-XSS-Protection` (OWASP: no ponerla; MDN: puede crear XSS). `Expect-CT` y `Public-Key-Pins` (obsoletas). `Cross-Origin-Opener-Policy`, `Cross-Origin-Embedder-Policy` y `Permissions-Policy` (actúan sobre documentos; la API no sirve documentos; van con el pendiente de nginx).
- **Cómo:** un middleware propio de unas 30 líneas en `Infrastructure/Middlewares/SecurityHeaders.cs`, con dos extensiones: `AddSecurityHeaders()` (apaga `Server` en Kestrel) y `UseSecurityHeaders()` (registra un `OnStarting` que escribe las seis cabeceras). Escribir las cabeceras **directo** antes de `next` no alcanza: `UseExceptionHandler` limpia todas las cabeceras antes de armar el 500 (E2, código de `ExceptionHandlerMiddlewareImpl`). `OnStarting` sobrevive a esa limpieza (E1).
- **Dónde:** `app.UseSecurityHeaders()` como **primer** middleware de `Program.cs`, antes de `UseSerilogRequestLogging`. Así su callback queda registrado antes de que cualquier middleware corte la request (429, 401, 400 de antiforgery, 404, 500). Como los callbacks `OnStarting` corren en orden inverso (pila), el del primer middleware corre **último**: sus valores ganan sobre los que pongan el antiforgery o el exception handler.
- **Cookie de antiforgery:** el pendiente de `progreso.md:110` tiene dos errores. El valor por defecto es `CookieSecurePolicy.None` (nunca `Secure`, tampoco por HTTPS), no `SameAsRequest`. Y `Always` **rompe la app**: el antiforgery lanza `InvalidOperationException` en toda request que no sea HTTPS (E4: `/csrf` pasa a 500), y en este repo todas lo son (dev, tests y Compose). Lo correcto es `SameAsRequest`: por HTTP no cambia nada hoy, y con HTTPS la cookie sale `Secure`. Entra en este paso porque el pendiente ya lo asignó al 13.
- **Tests:** una clase nueva `backend/Tests/SecurityHeaders/SecurityHeadersTests.cs` que comprueba las seis cabeceras con su valor exacto en siete tipos de respuesta (200, 200 de `/auth/csrf`, 400 de antiforgery, 401, 404, 429 y 500). `TestServer` no pasa por Kestrel, así que la ausencia de `Server` se prueba leyendo `KestrelServerOptions` y, sobre todo, en `smoke-api.mjs` contra la API real.
- **Dune:** sin regla nueva, con el mismo criterio que `docs/decisions.tsv:104`. El test de siete respuestas falla si alguien borra `UseSecurityHeaders()` o lo mueve detrás del limiter o del antiforgery. Como el callback del middleware corre último y pisa los valores, ningún endpoint puede debilitar las cabeceras: prohibir escribirlas en otros archivos no protege nada.
- **nginx:** fuera de alcance, como pendiente. La CSP del documento de Angular es un problema de diseño aparte (estilos inline de Angular, CSS crítico inline, sin nonces en un nginx estático), y nginx 1.28 tiene trampas de herencia de `add_header`.
- **Herramienta:** elegir entre el middleware propio y **NetEscapades.AspNetCore.SecurityHeaders** (MIT, 1.3.1 del 2025-12-20, un solo dueño en NuGet) es elegir una librería. Por la sección "Modo autónomo" de `AGENTS.md`, el hilo frena y le pregunta a Tobi. Recomiendo el middleware propio (sección 3).
- **Choque con los docs:** solo el texto del pendiente de `progreso.md:110` (valor por defecto y "una línea" con `Always`). No encontré choque con ADR-0004, `architecture.md` ni `conventions.md`.

## 1. Qué cabeceras recomienda OWASP hoy, para una API JSON y para una SPA

**Respuesta:** para una API, OWASP REST pide `Cache-Control: no-store`, `Content-Security-Policy: frame-ancestors 'none'`, un `Content-Type` correcto, HSTS si hay HTTPS, `X-Content-Type-Options: nosniff` y `X-Frame-Options: DENY`. Las de documento (CSP `default-src 'none'`, `Permissions-Policy`, `Referrer-Policy: no-referrer`) son opcionales cuando la API nunca devuelve HTML.

**API JSON, según OWASP REST Security Cheat Sheet** ([Security headers](https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html)):

- "The following headers should be included in all API responses that may be consumed by browser clients."
- Tabla principal: `Cache-Control: no-store` ("Instructs compliant private and shared HTTP caches not to store the response or reuse it for another request"); `Content-Security-Policy: frame-ancestors 'none'` ("prevents any domain from framing the response returned by the API call"); `Content-Type` "as per the type of content returned"; `Strict-Transport-Security` "for hosts covered by an active HSTS policy"; `X-Content-Type-Options: nosniff`; `X-Frame-Options: DENY`, "Legacy header superseded by `Content-Security-Policy: frame-ancestors 'none'`".
- Las de documento: "The headers below are only intended to provide additional security when responses are rendered as HTML". Si la API "never" devuelve HTML "may not be necessary", pero las recomienda como defensa en profundidad si hay duda. Son `Content-Security-Policy: default-src 'none'`, `Permissions-Policy` y `Referrer-Policy: no-referrer`.

**Lista general del OWASP Secure Headers Project** ([`headers_add.json`](https://raw.githubusercontent.com/OWASP/secure-headers-project/master/ci/headers_add.json), `last_update_utc: 2026-10-18`). El proyecto dice que su repo de GitHub es "the sole source of truth" ([página del proyecto](https://owasp.org/www-project-secure-headers/)). Es una lista para sitios, no solo APIs:

| Cabecera | Valor en la lista |
|---|---|
| `Cache-Control` | `no-store, max-age=0` |
| `Clear-Site-Data` | `"cache","cookies","storage"` |
| `Content-Security-Policy` | `default-src 'self'; form-action 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; upgrade-insecure-requests` |
| `Cross-Origin-Embedder-Policy` | `require-corp` |
| `Cross-Origin-Opener-Policy` | `same-origin` |
| `Cross-Origin-Resource-Policy` | `same-origin` |
| `Permissions-Policy` | una lista larga de funciones en `()` (cámara, micrófono, geolocalización, etc.) |
| `Referrer-Policy` | `no-referrer` |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains` |
| `X-Content-Type-Options` | `nosniff` |
| `X-DNS-Prefetch-Control` | `off` |
| `X-Frame-Options` | `deny` |
| `X-Permitted-Cross-Domain-Policies` | `none` |

- `Clear-Site-Data` borra datos del navegador: es para el logout, no para toda respuesta [INFERENCIA, por lo que hace el valor].

**SPA (documento HTML), según OWASP HTTP Headers Cheat Sheet** ([HTTP Headers Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)): `Referrer-Policy: strict-origin-when-cross-origin`, `Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Embedder-Policy: require-corp`, `Cross-Origin-Resource-Policy: same-site`, `Permissions-Policy: geolocation=(), camera=(), microphone=()`, y una CSP: "Content Security Policy is complex to configure and maintain".

**Obsoletas o dañinas:**

- `X-XSS-Protection`: OWASP dice "Do not set this header or explicitly turn it off" (`X-XSS-Protection: 0`) ([HTTP Headers Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)). MDN: "**`X-XSS-Protection` can create XSS vulnerabilities** in otherwise safe websites" y "It is recommended that you use `Content-Security-Policy` instead" ([MDN X-XSS-Protection](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/X-XSS-Protection)).
- `Expect-CT`: OWASP dice "Do not use it". MDN: "Chromium has deprecated the header from version 107, because Chromium now enforces CT by default" ([MDN Expect-CT](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Expect-CT)).
- `Public-Key-Pins`: OWASP dice "Do not use. Remove any `Public-Key-Pins` or `Public-Key-Pins-Report-Only` headers from production" ([HTTP Headers Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)).

**Las que hay que quitar:** `Server` ("Remove this header or set non-informative values") y `X-Powered-By` ("Remove all `X-Powered-By` headers") ([HTTP Headers Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)). Las dos están en [`headers_remove.json`](https://raw.githubusercontent.com/OWASP/secure-headers-project/master/ci/headers_remove.json), junto con `X-AspNet-Version` y `X-AspNetMvc-Version`.

- Kestrel manda `Server` por defecto: `AddServerHeader` "Defaults to true" ([Learn, KestrelServerOptions.AddServerHeader](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserveroptions.addserverheader?view=aspnetcore-10.0)). Kestrel lo escribe si `ServerOptions.AddServerHeader && !responseHeaders.HasServer` ([`HttpProtocol.cs:1257`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs)). E1 lo vio: `Server: Kestrel` en las seis respuestas.
- `X-Powered-By`, `X-AspNet-Version` y `X-AspNetMvc-Version` son de IIS y de ASP.NET clásico: OWASP los apaga con `web.config` y `Global.asax` ([HTTP Headers Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)). En E1, Kestrel no mandó ninguna.

**Por qué la API deja afuera COOP, COEP y Permissions-Policy:**

- COEP configura "the current document's policy for loading and embedding cross-origin resources" ([MDN COEP](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cross-Origin-Embedder-Policy)). Puesta en un JSON no hace nada sobre ese JSON. COOP y `Permissions-Policy` también son del documento. [INFERENCIA, por la misma lógica: un JSON pedido con `fetch()` no crea un documento.] NetEscapades las incluye en su preset de API "for safety" (comentario en el código, sección 3).
- CORP sí sirve en una API: "the browser should block `no-cors` cross-origin or cross-site requests to the given resource", con `same-origin` = "Resources can only be loaded from the same origin" ([MDN CORP](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cross-Origin-Resource-Policy)).
- `frame-ancestors` **no** hereda de `default-src`: "A policy that declares `default-src 'none'` still allows the resource to be embedded by anyone" ([MDN frame-ancestors](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/frame-ancestors)). Por eso la CSP de la API nombra las dos directivas.

**Contraste con el repo:**

- Hoy la API no manda ninguna cabecera de seguridad propia. El único código que escribe cabeceras es el limiter (`Retry-After`, `backend/Infrastructure/Http/ApiRateLimiting.cs:62`), la idempotencia (`Location` e `Idempotent-Replayed`, `backend/Infrastructure/Http/IdempotencyFilter.cs:109-110`) y el versionado (`api-supported-versions`). El antiforgery sí manda dos sin que el repo lo pida: `X-Frame-Options: SAMEORIGIN` y `Cache-Control: no-cache, no-store`, solo en las respuestas que emiten el token (sección 2).
- La SPA y la API comparten origen: `ng serve` hace de proxy de `/api/v1` (`frontend/proxy.conf.json`) y nginx también (`frontend/nginx.conf:8-14`). Por eso `CORP: same-origin` no bloquea a la SPA [INFERENCIA: lo deduzco de las configuraciones; no lo probé en un navegador].

## 2. HSTS y redirección a HTTPS en este repo

**Respuesta:** hoy no. La app no recibe HTTPS en ningún entorno. `UseHsts` solo escribe la cabecera si la request es HTTPS, y el navegador la ignora si llega por HTTP. HSTS va donde se termine TLS, cuando exista.

**Cómo funciona en ASP.NET Core 10:**

- `HstsMiddleware.Invoke` sale sin escribir nada si `!context.Request.IsHttps`, y también si el host está excluido. Si no, escribe `Strict-Transport-Security` antes de llamar a `next` ([`HstsMiddleware.cs`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Middleware/HttpsPolicy/src/HstsMiddleware.cs)).
- Learn: "`UseHsts` isn't recommended in development because the HSTS settings are highly cacheable by browsers. By default, `UseHsts` excludes the local loopback address": `localhost`, `127.0.0.1` y `[::1]`. `MaxAge` "defaults to 30 days". Para un primer despliegue con HTTPS, empezar con un `MaxAge` de horas y subirlo después ("commonly, one year") ([Learn, HSTS](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0#http-strict-transport-security-hsts-protocol)).
- Para APIs: "Projects that use Web APIs should either: Not listen on HTTP. Close the connection with status code 400 (Bad Request) and not serve the request". Y: "Do **not** use RequireHttpsAttribute on Web APIs that receive sensitive information", porque los clientes de API pueden no seguir redirecciones ([Learn, API projects](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0#api-projects)).
- Detrás de un proxy: "If the proxy also handles HTTPS redirection, there's no need to use HTTPS redirection middleware. If the proxy server also handles writing HSTS headers [...] then the app doesn't require HSTS middleware". Y si la app redirige, ForwardedHeaders va antes, o la app no ve el esquema real ([Learn, Require HTTPS](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0#require-https)).
- MDN: "Browsers ignore the header if sent over HTTP" y "One weakness of HSTS is that it does not take effect until the browser has made at least one secure connection" ([MDN Strict-Transport-Security](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Strict-Transport-Security)).

**Contraste con el repo:**

- Compose: la API escucha `http://+:8080` en `Production` (`compose.yaml:42-43`) y nginx escucha HTTP en 8080 (`frontend/nginx.conf:2`). Nadie termina TLS.
- `verify-template` levanta la API en `http://127.0.0.1:<puerto>` en `Development` (`verify.mjs:1148-1149`). Los tests usan `http://localhost` por defecto ([`WebApplicationFactoryClientOptions.cs:17,40`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Mvc/Mvc.Testing/src/WebApplicationFactoryClientOptions.cs)).
- El perfil `https` de `launchSettings.json` existe, pero ningún script del repo lo usa.
- Conclusión: `UseHsts()` en `Program.cs` sería código que nunca escribe nada en ningún entorno del repo, y no se puede observar "en una respuesta real", que es lo que pide el plan. `UseHttpsRedirection()` va contra la guía de Learn para APIs. Las dos quedan como pendiente: cuando el repo sume TLS (en nginx o en un ingress), HSTS lo escribe ese punto, con `max-age` bajo al principio.

**La cookie de antiforgery (pendiente de `progreso.md:110`):**

- El pendiente dice que la cookie sale sin `Secure` porque "usa el valor por defecto `SameAsRequest`" y que fijarla en `Always` "es un cambio de una línea". Las dos cosas son incorrectas:
  - El valor por defecto es `SecurePolicy = CookieSecurePolicy.None`, con el comentario "we are relaxing the restriction on secure policy on some cookies by setting to 'None'" ([`AntiforgeryOptions.cs:18-30`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Antiforgery/src/AntiforgeryOptions.cs)). `None` = "Secure is not marked true" ([Learn, CookieSecurePolicy](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.cookiesecurepolicy?view=aspnetcore-10.0)): la cookie sale sin `Secure` **también por HTTPS**.
  - Con `Always`, `CheckSSLConfig` lanza `InvalidOperationException` si `!context.Request.IsHttps`. Lo llaman `GetAndStoreTokens`, `GetTokens`, `IsRequestValidAsync`, `ValidateRequestAsync` y `SetCookieTokenAndHeader` ([`DefaultAntiforgery.cs`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Antiforgery/src/Internal/DefaultAntiforgery.cs)). El mensaje es "The antiforgery system has the configuration value {optionName} = {value}, but the current request is not an SSL request" ([`Resources.resx`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Antiforgery/src/Resources.resx)). E4: con `Always`, `GET /csrf` por HTTP da 500.
  - En el repo, toda request es HTTP: dev, tests y Compose (detrás de nginx sin ForwardedHeaders, la API ve `http`). Con `Always`, `/auth/csrf` y toda escritura darían 500.
- `SameAsRequest` = "If the URI that provides the cookie is HTTPS, then the cookie will only be returned to the server on subsequent HTTPS requests [...] also supports HTTP for localhost development" ([Learn, CookieSecurePolicy](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.cookiesecurepolicy?view=aspnetcore-10.0)). Por HTTP no cambia nada; por HTTPS (directo, o con ForwardedHeaders detrás de un proxy) la cookie sale `Secure`. Es el valor que cierra el riesgo real: hoy un despliegue con HTTPS mandaría la cookie sin `Secure`.
- `__Host-session` puede ser `Always` sin romper nada porque la autenticación por cookie solo marca el atributo; no comprueba `IsHttps` como el antiforgery [INFERENCIA: lo deduzco de que la sesión funciona hoy por HTTP con `CookieSecurePolicy.Always` (`backend/Features/Auth/SessionHttpPolicy.cs:27`); no leí el handler de cookies].

**Lo que el antiforgery agrega por su cuenta:**

- `SetDoNotCacheHeaders` pone `Cache-Control: no-cache, no-store` y `Pragma: no-cache`. `SaveCookieTokenAndHeader` pone `X-Frame-Options: SAMEORIGIN` si `SuppressXFrameOptionsHeader` es `false` y la cabecera no existe ([`DefaultAntiforgery.cs`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Antiforgery/src/Internal/DefaultAntiforgery.cs); "By default, the X-Frame-Options header is generated with the value SAMEORIGIN", [`AntiforgeryOptions.cs`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Antiforgery/src/AntiforgeryOptions.cs)).
- En el repo eso pasa en cada `IssueCsrf`: `/auth/csrf`, login, registro y logout (`backend/Features/Auth/SessionHttpPolicy.cs:66,73,78`). Sin cambios, esas respuestas dirían `SAMEORIGIN` y el resto `DENY`. E5: el `OnStarting` del middleware corre después y deja `DENY`. No hace falta tocar `SuppressXFrameOptionsHeader`.

## 3. Cómo implementarlo en ASP.NET Core 10

**Respuesta:** un middleware propio con `OnStarting`. El framework no trae un middleware de cabeceras de seguridad. NetEscapades hace lo mismo con más API y es una dependencia más.

**Lo que trae el framework:**

- HSTS (`UseHsts`) y la redirección (`UseHttpsRedirection`), nada más para cabeceras de seguridad. No encontré en Learn un middleware nativo para CSP, `nosniff` o CORP [INFERENCIA por ausencia: busqué en Learn y en la web; los resultados son paquetes de terceros].
- Learn muestra el patrón exacto: "The following example uses `HttpResponse.OnStarting` to set the headers before the response headers are flushed to the client", porque "Provides the ability to append or override headers just in time" y "Doesn't require knowledge of the next middleware in the pipeline" ([Learn, Best practices](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices?view=aspnetcore-10.0#do-not-modify-the-status-code-or-headers-after-the-response-body-has-started)).

**Por qué `OnStarting` y no escribir las cabeceras directo:**

- Al atrapar una excepción, el exception handler llama a `ClearHttpContext`, que hace `context.Response.Clear()` ([`ExceptionHandlerMiddlewareImpl.cs:163,291-298`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Middleware/Diagnostics/src/ExceptionHandler/ExceptionHandlerMiddlewareImpl.cs)). `Clear()` hace `response.Headers.Clear()` ([`ResponseExtensions.cs`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Http/Http.Extensions/src/ResponseExtensions.cs)). Toda cabecera escrita antes se pierde en el 500.
- El mismo exception handler usa `OnStarting` para sus cabeceras de caché (`ExceptionHandlerMiddlewareImpl.cs:168,300-308`): es el patrón del propio framework.
- E2 (escritura directa, middleware primero): `nosniff` aparece en 200, 404 y 429, pero **falta en el 500**. E1 (`OnStarting`): aparece en las seis respuestas, también en el 500.

**Orden de los callbacks:**

- Kestrel guarda los callbacks en una pila: `_onStarting.Push(...)` ([`HttpProtocol.cs:43,794-798`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs)). TestServer los encadena igual: el nuevo corre antes que el anterior ([`TestHost/ResponseFeature.cs:74-86`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Hosting/TestHost/src/ResponseFeature.cs)). El callback del primer middleware corre **último**, y lo que escribe gana. E5 lo confirma: el `DENY` pisa el `SAMEORIGIN` del antiforgery.

**Apagar `Server`:** `ConfigureKestrel(options)` es `services.Configure(options)` sobre `KestrelServerOptions` ([`WebHostBuilderKestrelExtensions.cs:132-139`](https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Servers/Kestrel/Kestrel/src/WebHostBuilderKestrelExtensions.cs)). Entonces `services.Configure<KestrelServerOptions>(o => o.AddServerHeader = false)` va bien dentro de una extensión de `IServiceCollection`, como las demás de `Infrastructure`. E3: con esa línea, `Server` desaparece de las seis respuestas.

**Opción A: middleware propio (recomendada).**

```csharp
// backend/Infrastructure/Middlewares/SecurityHeaders.cs (boceto, no compilado en el repo)
public static class SecurityHeaders
{
    public static IServiceCollection AddSecurityHeaders(this IServiceCollection services) =>
        services.Configure<KestrelServerOptions>(kestrel => kestrel.AddServerHeader = false);

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((http, next) =>
        {
            http.Response.OnStarting(Write, http.Response);
            return next(http);
        });

    private static Task Write(object state)
    {
        IHeaderDictionary headers = ((HttpResponse)state).Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }
}
```

- Gana: cero dependencias; seis valores fijos que se leen en un archivo; el mismo patrón que Learn y que el exception handler; cada cabecera tiene su porqué en este informe.
- Pierde: si mañana una ruta necesita otra política (por ejemplo, una página HTML con CSP propia), hay que agregar ese caso a mano.
- Sin options ni `appsettings`: los valores no cambian por entorno, así que no hay nada que validar al arrancar. El precedente de `ApiRateLimiting` tiene options porque sus números sí cambian (tests y entornos).
- `Cache-Control` se **pisa** con `no-store`, no se completa. El antiforgery y el exception handler ponen `no-cache, no-store`, que también prohíbe guardar; un solo valor deja una sola aserción en el test. Una ruta cacheable en el futuro sería una decisión aparte.
- Carpeta: `Infrastructure/Middlewares/`, porque el plan dice "middlewares" y ahí vive `GlobalExceptionHandler`. `Infrastructure/Http/` (como `ApiRateLimiting`) también encaja; es una elección de orden, no de diseño.

**Opción B: NetEscapades.AspNetCore.SecurityHeaders.** Ver "Decisión de herramienta", abajo.

## 4. Dónde va en el pipeline

**Respuesta:** primero, antes de `UseSerilogRequestLogging`. Así cubre toda respuesta que pase por el pipeline, también las que corta otro middleware.

- NetEscapades lo dice igual: "The security headers middleware can only add headers to all requests if it is early in the middleware pipeline" ([README](https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders)). Learn: "the order of middleware can be critical for security, performance, and functionality" ([Learn, Middleware order](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0#middleware-order)).
- Quién corta la request hoy, en el orden de `Program.cs:32-38`:

| Respuesta | Quién la genera | ¿La cubre un middleware puesto primero? |
|---|---|---|
| 500 | `UseExceptionHandler` + `GlobalExceptionHandler` | Sí, con `OnStarting` (E1). Con escritura directa, no (E2) |
| 404 sin ruta, versión inexistente | `UseStatusCodePages` | Sí (E1) |
| 401 sin sesión | evento `OnRedirectToLogin` del esquema de cookie (`SessionHttpPolicy.cs:34`) | Sí [INFERENCIA: corre dentro del pipeline, después del middleware] |
| 429 | `UseRateLimiter` | Sí (E1). Si el middleware fuera después del limiter, no |
| 400 sin antiforgery | `UseAuthAntiforgery` | Sí [INFERENCIA, mismo motivo]. Si fuera después del antiforgery, no |
| 200 de health | endpoints de Health | Sí |

- Lo que **no** cubre: los errores que Kestrel arma antes de llamar a la app (request mal formada, cabeceras demasiado grandes) [INFERENCIA: no pasan por middlewares; no lo probé].
- `MapOpenApi()` en Development (`Program.cs:29`) es un endpoint: también lleva las cabeceras.

## 5. El lado de la SPA: nginx

**Respuesta:** fuera de este paso, como pendiente con investigación propia. El plan nombra "middlewares, `Program.cs`", y la CSP de Angular es un problema de diseño aparte.

**Qué pide Angular para una CSP** ([Angular, Security](https://angular.dev/best-practices/security#content-security-policy)):

- Política mínima: `default-src 'self'; style-src 'self' 'nonce-randomNonceGoesHere'; script-src 'self' 'nonce-randomNonceGoesHere';`. Los nonces tienen que ser "**unique per request**".
- En hosting estático sin nonces: `security.autoCsp` hashea los scripts inline y agrega una CSP en un `<meta>`, pero "covers only scripts", y los navegadores ignoran `frame-ancestors` en un `<meta>`. Con `autoCsp`, la cabecera manda solo `style-src 'self' 'unsafe-inline'`.
- "Neither `autoCsp` nor avoiding inline scripts covers Angular's runtime component `<style>` elements or inlined critical CSS". Sin nonces: `style-src 'self' 'unsafe-inline'`.
- Sin scripts inline: `optimization.styles.inlineCritical: false`; entonces alcanza `script-src 'self'`.

**Qué tiene nginx:**

- `add_header` solo actúa en 200, 201, 204, 206, 301, 302, 303, 304, 307 y 308, salvo con `always`. Y se hereda del nivel anterior "if and only if there are no `add_header` directives defined on the current level". `add_header_inherit` arregla eso, pero "appeared in version 1.29.3" ([nginx, headers module](https://nginx.org/en/docs/http/ngx_http_headers_module.html)). El repo usa `nginx:1.28-alpine` (`frontend/Dockerfile:8`), que no la tiene [INFERENCIA: 1.28 < 1.29.3].
- nginx no pasa `Server` ni `Date` del upstream: "By default, nginx does not pass the header fields “Date”, “Server”, “X-Pad”, and “X-Accel-...”" ([nginx, proxy module](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_hide_header)). En Compose, el navegador ve el `Server` de nginx, con versión, porque `server_tokens` es `on` por defecto ([nginx, core](https://nginx.org/en/docs/http/ngx_http_core_module.html#server_tokens)).
- Las demás cabeceras de la API sí pasan por el proxy [INFERENCIA por la misma cita: solo esas se ocultan]. Un `add_header` a nivel `server` también se sumaría a las respuestas de `/api/v1/`: la misma cabecera saldría dos veces [INFERENCIA: `add_header` "Adds the specified field"; no lo probé].

**Contraste con el repo:**

- `frontend/src/index.html` no tiene scripts inline. Pero el build de producción usa la optimización por defecto (`angular.json` solo apaga `optimization` en desarrollo, línea 50), y eso incluye CSS crítico inline [INFERENCIA: es el valor por defecto de producción que nombra Angular; no inspeccioné `dist/`].
- Tailwind se compila a un archivo CSS (`@tailwindcss/postcss`, `package.json:31`), así que no suma estilos inline propios [INFERENCIA].
- Una CSP de documento mal puesta rompe la SPA entera, y solo se ve en Compose (`ng serve` no usa `nginx.conf`). Necesita elegir entre `autoCsp`, `inlineCritical: false` o `'unsafe-inline'` en estilos, y una prueba en navegador. Es un paso propio.

**Recomendación:** pendiente "cabeceras del documento de la SPA en nginx": CSP de Angular, `frame-ancestors`, COOP, `Permissions-Policy`, `Referrer-Policy`, `nosniff`, `server_tokens off`, con `always` y sin duplicar las de `/api/v1/`.

## 6. Cómo probarlo

**Respuesta:** un test de integración sobre siete tipos de respuesta, aserciones nuevas en `smoke-api.mjs` contra Kestrel real, y un `curl` guardado como evidencia. Sin regla de Dune nueva.

**Test de integración** (`backend/Tests/SecurityHeaders/SecurityHeadersTests.cs`; las pruebas de `Infrastructure/` van en `backend/Tests/<Área>/`, `docs/conventions.md:108`):

- Un helper `AssertSecurityHeaders(SessionResponse)` que compara el valor **exacto** de las seis cabeceras y que haya un solo valor por cabecera.
- Casos, todos con piezas que el repo ya tiene:

| Caso | Cómo se arma |
|---|---|
| 200 | `GET api/v1/health/live` |
| 200 con antiforgery | `GET api/v1/auth/csrf`: `X-Frame-Options` es `DENY`, no `SAMEORIGIN` |
| 400 | `POST api/v1/auth/login` sin token, como `ErrorContractTests.MissingAntiforgeryToken_...` |
| 401 | `GET api/v1/tasks` sin sesión, como `ErrorContractTests.NoSession_...` |
| 404 | `GET api/v9/health/live`, como el smoke (`smoke-api.mjs:144`) |
| 429 | cliente aislado con `RateLimiting:AnonymousRequests = 1` y `ClientAddressProbe`, como `RateLimitingTests.Isolated` |
| 500 | `ErrorProbe.ThrowPath` (`backend/Tests/Support/ErrorProbe.cs:17`): su `app.Map` va después del pipeline de `Program.cs`, así que pasa por el middleware |

- `Server`: `TestServer` no es Kestrel y nunca escribe `Server` [INFERENCIA: `Server` lo escribe `HttpProtocol` de Kestrel], así que una aserción "no hay `Server`" pasaría siempre. En su lugar: resolver `IOptions<KestrelServerOptions>` del `IServiceProvider` del factory y comprobar `AddServerHeader == false` [INFERENCIA: el `Configure` queda registrado aunque el servidor sea TestServer; no lo probé].
- Cookie de antiforgery: dos casos. Con `BaseAddress = https://localhost`, el `Set-Cookie` de `.AspNetCore.Antiforgery.*` lleva `secure`. Con `http://localhost`, no lo lleva y la respuesta es 200 (no 500) [INFERENCIA: TestServer toma el esquema de la URI del request; no lo probé].
- Rojo primero: los casos fallan con el código de hoy (no hay cabeceras). Después, mover `UseSecurityHeaders()` detrás de `UseRateLimiter()` tiene que hacer fallar el caso 429, como el paso 12 comprobó su orden.

**Respuesta real:**

- `smoke-api.mjs` ya hace `GET health/live` (200), la versión inexistente (404), `tasks` sin sesión (401) y `login` sin antiforgery (400) (`smoke-api.mjs:142-151`), y `request()` devuelve `headers` (`smoke-api.mjs:74`). Sumar una función que compare las seis cabeceras en esas cuatro respuestas y `assert.equal(headers.get('server'), null)`. Corre contra Kestrel real (`verify.mjs:1148-1149`): es la única prueba que ve `Server`.
- `curl -si http://127.0.0.1:<puerto>/api/v1/health/live` guardado en `artifacts/verify-template/<runId>/` como la "respuesta real" que pide el plan.
- E2E: cambian cabeceras que el navegador lee (CORP), aunque no el contrato JSON. Conviene una corrida de e2e al cerrar, para mostrar que el recorrido del navegador no se rompió.

**Dune (regla 4):**

- Opción A, sin regla nueva (recomendada): el mismo criterio que `docs/decisions.tsv:104` para `UseAuthAntiforgery`. Si alguien borra `UseSecurityHeaders()`, fallan los siete casos y el smoke; si lo mueve detrás del limiter o del antiforgery, fallan el 429 o el 400. Una regla estática repetiría el test.
- Opción B, `check-structure.mjs` exige `app.UseSecurityHeaders()` como primer `app.Use*` de `Program.cs`. El mecanismo existe: `checkRateLimitExemptionOnlyInHealth` lee los fuentes con una regex (`dune/check-structure.mjs:220-230`) y sus tests usan un repo temporal con fixture válida y violaciones (`dune/check-structure.test.mjs:201-236`). Gana: avisa sin Docker. Pierde: duplica el test y ata la regla a la forma exacta de `Program.cs`.
- Opción C, prohibir escribir esas cabeceras fuera de `SecurityHeaders.cs`: no protege nada, porque el callback del middleware corre último y pisa cualquier valor (E5).
- Si la opción A es la elegida, va una fila en `docs/decisions.tsv` que lo diga, como la 104.

## Contraste con el repo y con los docs

- **Pendiente de `progreso.md:110`**: corregir el texto. El valor por defecto es `None`, no `SameAsRequest`, y `Always` lanza excepción por HTTP. El paso aplica `SameAsRequest` en `AddAntiforgery` (`backend/Features/Auth/AuthFeature.cs:75-78`); vive en Auth porque es la configuración del antiforgery de Auth (`docs/conventions.md:22`).
- **ADR-0004, `architecture.md`, `conventions.md`**: no encontré choque. El middleware no nombra features, así que va en `Infrastructure`. `docs/architecture.md:32` suma el middleware de cabeceras a la lista de Infrastructure. `docs/conventions.md` gana una línea en la sección de seguridad (`:98-101`): "toda respuesta lleva las cabeceras de `SecurityHeaders`; `UseSecurityHeaders()` va primero en `Program.cs`".
- **Plan**: "Verifica: cabeceras observables en una respuesta real". El smoke contra Kestrel y el `curl` lo cumplen. HSTS no se puede observar en ningún entorno del repo: por eso queda afuera.
- **Pendiente de Data Protection** (`progreso.md:106`, "encaja con el paso 13 o el 35"): no tiene que ver con cabeceras. No lo toco.

## Decisiones que toma el hilo (modo autónomo)

1. Las seis cabeceras y sus valores, en toda respuesta y en todo entorno (Conclusión, tabla).
2. Sin `Server` (`AddServerHeader = false`).
3. Sin HSTS ni redirección a HTTPS en la app; van con TLS, como pendiente (sección 2).
4. Sin `X-XSS-Protection`, `Expect-CT`, `Public-Key-Pins`, COOP, COEP ni `Permissions-Policy` en la API (sección 1).
5. `OnStarting` en el primer middleware de `Program.cs`, pisando valores (secciones 3 y 4).
6. `Cache-Control: no-store` pisado en toda respuesta de la API (sección 3).
7. Cookie de antiforgery con `SameAsRequest`, no `Always` (sección 2).
8. Sin regla de Dune nueva, con el criterio de `decisions.tsv:104` (sección 6).
9. nginx fuera del paso, como pendiente (sección 5).

Las de diseño (1, 3, 5, 6, 7, 8 y 9) van en `docs/decisions.tsv`.

## Decisión de herramienta (regla 5 y "Modo autónomo")

Elegir entre un middleware propio y una librería es elegir una herramienta: el hilo frena y le pregunta a Tobi. Los datos:

| | Middleware propio | NetEscapades.AspNetCore.SecurityHeaders |
|---|---|---|
| Licencia | La del repo | MIT ([LICENSE en el repo](https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders); [NuGet](https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders)) |
| Versión | No aplica | 1.3.1, publicada el 2025-12-20. Antes: 1.3.0 (2025-11-21), 1.2.0 (2025-09-21), 1.1.0 (2025-05-07), 1.0.0 (2025-04-01) ([NuGet](https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders); [índice de versiones](https://api.nuget.org/v3-flatcontainer/netescapades.aspnetcore.securityheaders/index.json)) |
| Mantenimiento | El repo | Un solo dueño en NuGet (`andrewlock`). Último commit en `main`: 2026-07-22, el PR #286 que corrige el formato de `Permissions-Policy` (`fullscreen=self` → `fullscreen=(self)`); no salió en NuGet todavía ([PR #286](https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders/pull/286)) |
| Uso | No aplica | 21,0 M descargas; 3,0 M de la versión actual ([NuGet](https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders)) |
| Target y dependencias | No aplica | `netcoreapp3.1`, sin dependencias; NuGet la marca compatible hasta net10.0 ([NuGet](https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders)) |
| Mecanismo | `OnStarting` | `OnStarting` también: `context.Response.OnStarting(OnResponseStarting, ...)` ([`SecurityHeadersMiddleware.cs`](https://raw.githubusercontent.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders/main/src/NetEscapades.AspNetCore.SecurityHeaders/SecurityHeadersMiddleware.cs)) |
| Preset de API | Los seis valores de este informe | `AddDefaultApiSecurityHeaders()`: `X-Frame-Options: DENY`, `nosniff`, HSTS (solo por HTTPS), `RemoveServerHeader`, CSP `default-src 'none'; frame-ancestors 'none'`, `Referrer-Policy: no-referrer`, `Permissions-Policy` con 19 funciones apagadas, COOP `same-origin`, COEP `require-corp`, CORP `same-site` ([`HeaderPolicyCollectionExtensions.cs:71-92`](https://raw.githubusercontent.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders/main/src/NetEscapades.AspNetCore.SecurityHeaders/HeaderPolicyCollectionExtensions.cs)). No pone `Cache-Control` |
| `Server` | `AddServerHeader = false` | Su README dice que `RemoveServerHeader` "will rarely (ever?) be sufficient to remove the Server header" y recomienda la opción de Kestrel ([README](https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders)): hace falta la misma línea igual |
| Extras | Ninguno | Políticas por endpoint, constructores de CSP, nonces y hashes con un paquete de TagHelpers aparte (Razor) |

**Qué ahorra NetEscapades:** unas 25 líneas y los constructores de CSP. En una API sin Razor, sin políticas por endpoint y con valores fijos, eso es casi todo lo que hay que escribir.

**Qué cuesta:** una dependencia más en la cadena de suministro, justo en el paso que la pone como parte del diseño. Un mantenedor principal. Un preset que no trae `Cache-Control` y suma COOP, COEP y `Permissions-Policy`, que no actúan sobre JSON.

**Recomendación: el middleware propio.** Porque el conjunto es chico y fijo, el mecanismo es el mismo `OnStarting`, y la librería no evita la línea de Kestrel. Además, el repo ya eligió código propio sin librería en casos parecidos: el filtro de idempotencia del paso 9 y el interceptor de auditoría del paso 10 (Tobi lo eligió el 2026-11-16, `progreso.md:7`). La industria usa las dos cosas: NetEscapades es el paquete más usado para esto en .NET (21 M descargas), y Learn muestra el `OnStarting` a mano.

## Pendientes fuera de alcance (para `progreso.md`)

- Cabeceras del documento de la SPA en nginx: CSP de Angular (`autoCsp`, `inlineCritical: false` o `'unsafe-inline'` en estilos), `frame-ancestors`, COOP, `Permissions-Policy`, `Referrer-Policy`, `nosniff` y `server_tokens off`; con `always` y sin duplicar las de `/api/v1/`; nginx 1.28 no tiene `add_header_inherit`.
- HSTS donde se termine TLS, cuando el repo sume HTTPS: `max-age` bajo al principio; ForwardedHeaders para que la API vea `https` (la cookie de antiforgery con `SameAsRequest` lo necesita para salir `Secure`).
- `Clear-Site-Data: "cookies"` en el logout (`CloseSession`): está en la lista de OWASP; necesita revisar qué borra en la SPA.
- Errores que Kestrel arma antes del pipeline (400 de request mal formada, 431) salen sin cabeceras.

## Qué quedó sin comprobar

- No corrí nada del repo: ni tests, ni smoke, ni e2e. El experimento no tiene la cookie de sesión, Identity, el grupo versionado, `GlobalExceptionHandler` ni el middleware de antiforgery de Auth.
- Los casos 401 y 400 del repo: deduzco que llevan las cabeceras porque los generan middlewares que corren después del primero; el experimento no los tuvo.
- Que `CORP: same-origin` y la CSP de la API no rompan la SPA en el navegador: lo deduzco del proxy del mismo origen; falta la corrida de e2e.
- Que TestServer marque `IsHttps` con un `BaseAddress` `https://`, y que `IOptions<KestrelServerOptions>` se pueda leer en los tests.
- Que `__Host-session` con `Always` funcione por HTTP porque el handler de cookies no comprueba `IsHttps`: lo deduzco de que hoy funciona; no leí ese handler.
- Que el build de producción de Angular meta CSS crítico inline: no inspeccioné `dist/`.
- Que un `add_header` en nginx duplique cabeceras de `/api/v1/`: deducción de la documentación.
- Que no exista un middleware nativo de cabeceras de seguridad: lo deduzco de no encontrarlo.

## Fuentes

- https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html
- https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html
- https://owasp.org/www-project-secure-headers/
- https://raw.githubusercontent.com/OWASP/secure-headers-project/master/ci/headers_add.json
- https://raw.githubusercontent.com/OWASP/secure-headers-project/master/ci/headers_remove.json
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/X-XSS-Protection
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Expect-CT
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Strict-Transport-Security
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cross-Origin-Resource-Policy
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cross-Origin-Embedder-Policy
- https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/frame-ancestors
- https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserveroptions.addserverheader?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.cookiesecurepolicy?view=aspnetcore-10.0
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Middleware/HttpsPolicy/src/HstsMiddleware.cs
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Antiforgery/src/Internal/DefaultAntiforgery.cs
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Antiforgery/src/AntiforgeryOptions.cs
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Antiforgery/src/Resources.resx
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Middleware/Diagnostics/src/ExceptionHandler/ExceptionHandlerMiddlewareImpl.cs
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Http/Http.Extensions/src/ResponseExtensions.cs
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Servers/Kestrel/Kestrel/src/WebHostBuilderKestrelExtensions.cs
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Hosting/TestHost/src/ResponseFeature.cs
- https://raw.githubusercontent.com/dotnet/aspnetcore/release/10.0/src/Mvc/Mvc.Testing/src/WebApplicationFactoryClientOptions.cs
- https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders
- https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders/pull/286
- https://raw.githubusercontent.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders/main/src/NetEscapades.AspNetCore.SecurityHeaders/HeaderPolicyCollectionExtensions.cs
- https://raw.githubusercontent.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders/main/src/NetEscapades.AspNetCore.SecurityHeaders/SecurityHeadersMiddleware.cs
- https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders
- https://api.nuget.org/v3-flatcontainer/netescapades.aspnetcore.securityheaders/index.json
- https://angular.dev/best-practices/security
- https://nginx.org/en/docs/http/ngx_http_headers_module.html
- https://nginx.org/en/docs/http/ngx_http_proxy_module.html
- https://nginx.org/en/docs/http/ngx_http_core_module.html#server_tokens

## Apéndice: el experimento

Cinco modos sobre la misma app, seis requests cada uno (`/ok`, `/boom` que lanza, `/missing`, `/limited` dos veces, `/csrf`):

| Modo | Qué cambia | Resultado |
|---|---|---|
| E1 | Middleware primero con `OnStarting` | `nosniff` y `Cache-Control` en 200, 500, 404, 200, 429 y 204. `Server: Kestrel` en todas. `/csrf` con `X-Frame-Options: SAMEORIGIN` y `Cache-Control: no-store, no-cache` |
| E2 | Middleware primero, escritura directa antes de `next` | `nosniff` en 200, 404 y 429; **falta en el 500** |
| E3 | E1 + `Configure<KestrelServerOptions>(o => o.AddServerHeader = false)` | Sin `Server` en las seis |
| E4 | E1 + antiforgery con `Cookie.SecurePolicy = Always` | `/csrf` por HTTP da **500**; las demás iguales |
| E5 | E1 + `X-Frame-Options = "DENY"` en el callback | `DENY` en las seis, también en `/csrf` (pisa el `SAMEORIGIN`) |

Con la política por defecto (E1), la cookie de antiforgery salió `path=/; samesite=strict; httponly`, sin `secure`.

En E1 el callback completaba `Cache-Control` solo si faltaba; la recomendación lo pisa con `no-store`. Pisar `Cache-Control` no se probó por separado; usa el mismo mecanismo que pisar `X-Frame-Options` en E5.
