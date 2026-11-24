# Paso 29: resiliencia en los clientes HTTP salientes (investigación)

- Fecha: 2026-11-20.
- Estado: **Decidido.** Tobi eligió A + W1 el 2026-11-20 (regla 5): `Microsoft.Extensions.Http.Resilience` 10.11.1 y `WireMock.Net.Minimal` 2.32.0. Las demás decisiones van con la recomendación de la sección "Decisiones", con una desviación medida durante el paso: la decisión 10 (timeout de ruta del gateway con `AddRequestTimeouts`) respondió 400 y no 504, porque YARP 2.3 toma esa cancelación como un cliente que se fue; el paso usa el `HttpRequest:ActivityTimeout` de cada cluster, que responde 504 (`docs/decisions.tsv`).
- Alcance: el paso 29 del [plan](../plan.md) ("29. Resiliencia en los clientes HTTP salientes"). Concepto: timeouts en cascada, retry con backoff, circuit breaker y bulkhead, contra un servidor falso. Cambia: el cliente en la feature que lo usa, la política en `Infrastructure/`, `Microsoft.Extensions.Http.Resilience` y tests con WireMock. Verifica: un fallo transitorio que termina en éxito, un fallo persistente que abre el circuito y la prueba de que el timeout interno es menor que el externo (`[CÓDIGO docs/evolucion/plan.md:464-467]`).
- Base: `main` en `66d5200` (merge del PR #89), rama `fase9/29-resiliencia-http`.
- Límites con los pasos vecinos: la pasarela de pago (paso 30) es el primer cliente hacia un tercero y usará esta misma política; la caché (paso 33) no entra, así que no hay *fallback* (respuesta de reemplazo) con datos guardados. Los chequeos pasivos de YARP quedan como pendiente (sección 1.1).

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `66d5200`.
- `[FUENTE]`: lo leí en un documento primario (Microsoft Learn, código fuente y releases de los mantenedores, NuGet, archivos de licencia, RFC). Cada uno tiene su link.
- `[MEDIDO]`: lo corrí en un programa de prueba fuera del repo (en el scratchpad, sin commit), con .NET 10.0.12, `Microsoft.Extensions.Http.Resilience` 10.11.1, `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 y `WireMock.Net` / `WireMock.Net.Minimal` 2.32.0. También restauré una copia de `TemplateApp.Platform.csproj` con el paquete nuevo.
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Respuesta corta

1. **Qué falla hoy.** Cada request de Tasks pregunta al monolito si la sesión sigue activa (`POST /internal/v1/session-checks`). Si el monolito está caído, el usuario ve "Error inesperado del servidor. Inténtalo más tarde." al instante. Si el monolito está lento, lo ve a los 2 s. Si el monolito tuvo un error suelto (un 503 o una conexión cortada una sola vez), lo ve igual, aunque repetir la llamada habría andado. Hoy el único freno es `HttpClient.Timeout` de 2 s y no hay reintentos (sección 1.1).
2. **Las cuatro piezas, en orden de afuera hacia adentro.** Limitador de concurrencia (bulkhead) → timeout total → reintento con backoff exponencial y jitter → circuit breaker → timeout por intento. Es el orden de `AddStandardResilienceHandler` ([FUENTE](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#standard-resilience-handler-defaults)), y cada pieza está donde está por una razón (sección 1.3). Hedging (pedidos en paralelo) no aplica: cada servicio tiene una sola dirección.
3. **Timeouts en cascada.** Cada nivel espera menos que el de arriba: intento 2 s < total 5 s < ruta del gateway 10 s < nginx 60 s. Hoy el gateway no tiene timeout de ruta (solo el `ActivityTimeout` de 100 s de YARP), así que el nivel "externo" del Verifica no existe todavía (sección 3.4).
4. **Qué se reintenta.** Los dos `GET` y también el `POST` de session-checks: es una lectura con efectos idempotentes (marca `LastSeenAt` con un `UPDATE` condicional o borra una sesión vencida), y la RFC permite reintentar un método no idempotente cuando el cliente sabe que la operación sí lo es. Por defecto la librería reintenta todos los métodos; `DisableForUnsafeHttpMethods` es una API experimental (EXTEXP0001). Recomiendo que cada cliente declare si se reintenta al registrarse (sección 2).
5. **Política propia, no la estándar.** Recomiendo `AddResilienceHandler` con las cinco piezas del estándar escritas una por una. Tres motivos medidos: la estándar pone `HttpClient.Timeout` en infinito sin avisar, toma el reloj del contenedor (y los tests de `IsolatedApi` registran un `FakeTimeProvider` que congela reintentos y timeouts), y no deja elegir qué se reintenta sin una API experimental (sección 3).
6. **Circuito abierto y bulkhead lleno → 503.** La librería tira `BrokenCircuitException`, `TimeoutRejectedException` o `RateLimiterRejectedException`. Las tres heredan de `ExecutionRejectedException` de Polly. Hoy `SendRemoteAsync` no las atrapa y terminarían en 500. Hay que traducirlas a `RemoteServiceUnavailableException`, que ya responde 503 `Http.ServiceUnavailable` (sección 5).
7. **Telemetría.** Polly ya emite métricas (`resilience.polly.strategy.events` con `OnRetry` y `OnCircuitOpened`) y logs con la categoría `Polly`. Los logs llegan solos por Serilog; las métricas necesitan `AddMeter("Polly")`, porque hoy el repo solo exporta `TemplateApp.*` y `Wolverine*` (sección 6).
8. **Tests del Verifica.** (a) Transitorio: el servidor falso responde 503 y después 200, y el cliente devuelve el 200 con 2 llamadas. (b) Persistente: siempre 503; a partir de cierto punto el cliente falla sin llegar al servidor (el contador del servidor deja de subir). (c) Interno < externo: un test lee la configuración efectiva de los dos procesos y la del gateway y compara los cuatro niveles, más un test medido con un servidor mudo. Medí que `FakeTimeProvider` sí controla el pipeline cuando está en el contenedor (sección 7).
9. **Herramientas (decide Tobi).** Librería: **A** `Microsoft.Extensions.Http.Resilience` 10.11.1 (MIT, de Microsoft, trae Polly v8 como dependencia), **B** Polly v8 directo (`Polly.Core` 8.9.1, BSD-3-Clause), **C** un `DelegatingHandler` propio sin librería. Servidor falso: **W1** `WireMock.Net.Minimal` 2.32.0 (Apache-2.0), **W2** uno propio (el `RemoteFault` que ya existe, o un Kestrel mínimo).
10. **Mi recomendación: A + W1.** A es lo que nombran el plan y el informe, es lo que usan eShop y la plantilla de Aspire, y el Open Source Maintenance Fee de Polly no la alcanza: rige solo para la próxima versión mayor de Polly y solo para referencias directas. W1 es el servidor falso que nombra el plan, corre dentro del proceso del test y trae escenarios con estado y demoras. La elección es de Tobi.
11. **Regla nueva de Dune (regla 4).** DUNE0027: ningún archivo del producto fuera de `backend/Infrastructure/Http/` arma un cliente HTTP (`new HttpClient`, `AddHttpClient`, handlers) ni nombra Polly o la librería de resiliencia. Y un gate de `check-structure.mjs`: el paquete de resiliencia solo en `TemplateApp.Platform.csproj`, y ninguna referencia directa a `Polly*` ni al paquete deprecado `Microsoft.Extensions.Http.Polly` (sección 9).
12. **Choques con los docs.** Siete (sección "Choques"). El más importante: `progreso.md:7` dice que el modo autónomo terminó en el paso 28, y `AGENTS.md` (PR #89) dice que llega hasta el 29.

## Glosario corto

- **Cliente saliente** (= el código de un proceso que llama por HTTP a otro proceso). Acá son tres: `AuthSessionClient`, `ProjectsClient` y `TaskCountsSnapshotClient` `[CÓDIGO docs/architecture.md:62-66]`.
- **Fallo transitorio** (= un error que se arregla solo en poco tiempo): "the momentary loss of network connectivity to components and services, the temporary unavailability of a service, or timeouts that occur when a service is busy" ([FUENTE: Azure, Retry pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry)).
- **Timeout por intento** (*attempt timeout* = cuánto espera cada llamada individual) y **timeout total** (*total request timeout* = cuánto espera la operación completa, con todos sus reintentos y esperas). En la librería, el total "ensuring that the request, including retry attempts, doesn't exceed the configured limit" ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#standard-resilience-handler-defaults)).
- **Timeouts en cascada** (= en una cadena de llamadas, cada nivel tiene un límite menor que el nivel que lo llama, así el de abajo se rinde primero y el de arriba recibe una respuesta en vez de cortar a ciegas). [INFERENCIA, del concepto del plan].
- **Reintento** (*retry* = repetir la llamada que falló): "Enable an application to handle transient failures [...] by transparently retrying a failed operation" ([FUENTE: Azure, Retry pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry)).
- **Backoff exponencial** (= cada espera entre reintentos es el doble de la anterior) y **jitter** (= un poco de azar en esa espera): "Jitter adds some amount of randomness to the backoff to spread the retries around in time" ([FUENTE: AWS Builders' Library](https://builder.aws.com/content/3EumjoZascWd1oZiEgL8ORlv3qE/timeouts-retries-and-backoff-with-jitter)).
- **Idempotente** (= repetir el pedido deja el mismo efecto que hacerlo una vez): "the intended effect on the server of multiple identical requests with that method is the same as the effect for a single such request" ([FUENTE: RFC 9110 §9.2.2](https://www.rfc-editor.org/rfc/rfc9110#section-9.2.2)).
- **Circuit breaker** (cortacircuitos = un contador de fallas que, pasado un umbral, deja de llamar al otro servicio por un rato y falla al instante). Tres estados: cerrado (llama y cuenta), abierto (falla sin llamar) y semiabierto (deja pasar una prueba) ([FUENTE: Azure, Circuit Breaker pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)).
- **Bulkhead** (mamparo = un límite de recursos por dependencia, para que una dependencia lenta no se lleve todos los recursos del proceso): "Isolate the elements of an application into pools so that if one element fails, the others continue to function" ([FUENTE: Azure, Bulkhead pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/bulkhead)). En la librería = un limitador de concurrencia: `AddConcurrencyLimiter` "Limit how many requests you make, which enables you to control outbound load" ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/#resilience-pipeline-builder-extensions)).
- **Hedging** (= mandar un segundo pedido en paralelo si el primero tarda): "Hedging retries slow requests in parallel" ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#add-standard-hedging-handler)).
- **Pipeline de resiliencia** (= la cadena de estrategias que envuelve cada llamada; "Each configured strategy executes in order of configuration. In other words, order is important", [FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/#build-a-resilience-pipeline)).
- **Polly** (= la librería .NET de resiliencia sobre la que se apoya el paquete de Microsoft): "These two NuGet packages are built on top of Polly" ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/)).
- **Servidor falso** (*stub server* = un servidor HTTP de test que responde lo que el test le programa). WireMock.Net "mimics functionality from the original Java based WireMock" ([FUENTE: README](https://github.com/wiremock/WireMock.Net)).
- **Open Source Maintenance Fee** (OSMF = una cuota mensual que algunos proyectos open source cobran a empresas que usan sus binarios para ganar dinero). Sección "Herramientas".

---

## 1. El concepto, en el caso real del repo

### 1.1 Qué pasa hoy

Los tres clientes se registran con `AddRemoteClient<TClient>(servicio)` `[CÓDIGO backend/Features/Tasks/TasksFeature.cs:28-29]`, `[CÓDIGO backend/Features/Projects/ProjectsFeature.cs:28]`. Ese método crea un cliente tipado con `IHttpClientFactory`, pone `BaseAddress` y `HttpClient.Timeout` desde `Services:<Servicio>` y suma el handler `X-On-Behalf-Of` `[CÓDIGO backend/Infrastructure/Http/RemoteServices.cs:9-23]`. El timeout por defecto es 2 s `[CÓDIGO backend/Infrastructure/Http/RemoteServiceOptions.cs:9]` y el validador acepta hasta 30 s `[CÓDIGO backend/Infrastructure/Http/RemoteServiceOptionsValidator.cs:7,19-20]`. `SendRemoteAsync` convierte "no conecta", "no contestó a tiempo" y "respondió 5xx" en `RemoteServiceUnavailableException` `[CÓDIGO RemoteServices.cs:36-52]`, y `GlobalExceptionHandler` la responde como 503 `Http.ServiceUnavailable` `[CÓDIGO backend/Infrastructure/Middlewares/GlobalExceptionHandler.cs:14-16]`. Ese problema lleva el texto en `title` y no en `detail` `[CÓDIGO backend/Shared/Http/ApiResults.cs:10-11]`, así que el frontend muestra su texto genérico `[CÓDIGO frontend/src/app/core/http-error.ts:39,57-58]`.

| Si pasa esto | Qué ve el usuario hoy | Por qué |
|---|---|---|
| El monolito está caído (conexión rechazada) | En la pantalla de tareas, "Error inesperado del servidor. Inténtalo más tarde." al instante, en cada request | Cada request de Tasks llama a `AuthSessionClient.CheckAsync` desde `OnValidatePrincipal` `[CÓDIGO backend/Features/Tasks/Access/RemoteSession.cs:9-16]`; la falla llega como `HttpRequestException` `[CÓDIGO RemoteServices.cs:36-38]` |
| El monolito no contesta | El mismo mensaje, a los 2 s | `HttpClient.Timeout` corta y `SendRemoteAsync` lo traduce `[CÓDIGO RemoteServices.cs:20,40-42]`. Lo prueba `ProjectsSilent_TheCallerGivesUpAtItsTimeout_With503` `[CÓDIGO backend/Tests/Remote/RemoteBoundaryTests.cs:151-171]` |
| El monolito falla una sola vez (un 503 suelto, una conexión cortada) | El mismo mensaje, aunque repetir habría andado | No hay reintentos: los tests lo fijan con `Assert.Equal(1, auth.Calls)` y `Assert.Equal(1, projects.Calls)` `[CÓDIGO RemoteBoundaryTests.cs:55,118]` |
| Tasks está caído | Proyectos sigue andando (lee su proyección) | Ninguna lectura de proyectos llama a Tasks (paso 24.1, `[CÓDIGO RemoteBoundaryTests.cs:123-149]`). Solo la reconstrucción de contadores llama, en segundo plano, y reintenta cada 30 s `[CÓDIGO backend/Features/Projects/RebuildTaskCounters/RebuildTaskCountersOnStartup.cs:5,30-35]` |
| Una API está parada detrás del gateway | La request espera hasta que YARP la marca caída | El gateway tiene chequeo activo cada 5 s con umbral 2 `[CÓDIGO gateway/appsettings.json:20-31]`, sin timeout de ruta ni chequeo pasivo. Anotado en `[CÓDIGO docs/evolucion/progreso.md:230]` |

Dos costos que hoy no se ven:

- **La espera ocupa recursos.** Con el monolito mudo, cada request de Tasks retiene su conexión y su hilo lógico 2 s. Con `Idempotency-Key`, `POST /api/v1/tasks` llama a Projects *dentro* de la transacción del filtro de idempotencia, que ya insertó la fila de la clave `[CÓDIGO backend/Infrastructure/Http/IdempotencyFilter.cs:44-56]`, `[CÓDIGO backend/Features/Tasks/CreateTask/CreateTask.cs:41]`. Más tiempo de llamada = más tiempo con esa transacción abierta [INFERENCIA].
- **Nada limita cuántas llamadas salen a la vez.** Si el monolito se pone lento, Tasks acumula llamadas abiertas sin tope: es el problema que describe el patrón bulkhead, donde "the client's connection pool might be exhausted" ([FUENTE: Azure, Bulkhead](https://learn.microsoft.com/en-us/azure/architecture/patterns/bulkhead)).

### 1.2 Las piezas, una por una

| Pieza | Qué hace | Qué cambia para el usuario en este repo |
|---|---|---|
| Timeout por intento | Corta una llamada que no contesta | Igual que hoy: el intento muere a los 2 s |
| Timeout total | Corta la operación completa, con reintentos y esperas | El usuario nunca espera más de 5 s por la llamada, aunque haya reintentos |
| Reintento con backoff y jitter | Repite la llamada tras una espera creciente con azar | Un 503 suelto o una conexión cortada ya no llegan a la pantalla: el usuario ve la respuesta normal, unos cientos de ms más tarde |
| Circuit breaker | Tras muchas fallas, deja de llamar por un rato | Con el monolito caído, Tasks responde 503 en milisegundos en vez de esperar, y el monolito no recibe una ola de reintentos cuando vuelve |
| Bulkhead (limitador de concurrencia) | Pone un tope de llamadas simultáneas por cliente | Si el monolito se pone lento, Tasks rechaza al instante las llamadas que pasan el tope (503) en vez de acumularlas |
| Hedging | Manda un segundo pedido en paralelo | **No aplica.** Cada servicio tiene una sola dirección (`Services:<Servicio>:BaseAddress`) y no hay réplicas a las que mandar el segundo pedido [INFERENCIA]. Duplicaría la carga del monolito en el peor momento |

Por qué reintentar en un solo lugar: si cada nivel reintenta por su cuenta, la carga se multiplica: "If each layer retries independently, the load on the database will increase 243x [...] our best practice is to retry at a single point in the stack" ([FUENTE: AWS](https://builder.aws.com/content/3EumjoZascWd1oZiEgL8ORlv3qE/timeouts-retries-and-backoff-with-jitter)). En el camino de una request de usuario, hoy nadie reintenta: el frontend no tiene operador de reintento (busqué `retry` en `frontend/src`, solo aparece el texto del 429 `[CÓDIGO frontend/src/app/core/http-error.ts:24-29]`) y el gateway no tiene reintentos configurados `[CÓDIGO gateway/appsettings.json:1-61]`. El cliente saliente es el único punto. La excepción es la reconstrucción de contadores, que ya reintenta cada 30 s por encima del cliente; es trabajo en segundo plano, no una request de usuario [INFERENCIA].

### 1.3 El orden del pipeline y por qué

Orden de `AddStandardResilienceHandler`, de afuera hacia adentro: limitador, timeout total, reintento, circuit breaker, timeout por intento ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#standard-resilience-handler-defaults); en el código, [FUENTE: StandardResilience.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/ResilienceHttpClientBuilderExtensions.StandardResilience.cs)).

```
SendRemoteAsync
  └─ limitador de concurrencia     ← rechaza antes de gastar tiempo
      └─ timeout total (5 s)       ← corta todo, reintentos incluidos
          └─ reintento (2 más)     ← repite lo transitorio
              └─ circuit breaker   ← cuenta cada intento; abierto = falla sin llamar
                  └─ timeout por intento (2 s)
                      └─ X-On-Behalf-Of → red → otro proceso
```

1. **Limitador afuera de todo.** Una request toma un permiso y lo tiene durante todos sus reintentos; si no hay permiso, se rechaza sin gastar presupuesto de tiempo [INFERENCIA, por el orden].
2. **Timeout total afuera del reintento.** Polly documenta este caso: "the outer is a timeout which is overarching all retry attempts" ([FUENTE: Polly, pipelines](https://www.pollydocs.org/pipelines/index.html)).
3. **Circuit breaker adentro del reintento.** Así cada intento cuenta para el breaker, y cuando el circuito se abre en medio de una operación el reintento no sigue: `BrokenCircuitException` no es transitoria para la librería, que solo reintenta `HttpRequestException`, `TimeoutRejectedException`, 5xx, 408 y 429 ([FUENTE: HttpClientResiliencePredicates.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpClientResiliencePredicates.cs)). Es lo que pide Azure: "the retry logic should be sensitive to any exceptions that the circuit breaker returns and stop retry attempts if the circuit breaker indicates that a fault isn't transient" ([FUENTE](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)). Lo medí: con mínimo 2 y 50 %, la primera operación hizo 2 intentos, el circuito se abrió y el tercer intento salió como `BrokenCircuitException` sin llegar al servidor `[MEDIDO]`.
4. **Timeout por intento adentro de todo.** Su `TimeoutRejectedException` es transitoria: se reintenta y la cuenta el breaker (misma fuente). Por eso el validador de Microsoft exige que la ventana del breaker sea al menos el doble del timeout por intento ([FUENTE: HttpStandardResilienceOptionsCustomValidator.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/Internal/Validators/HttpStandardResilienceOptionsCustomValidator.cs)).

### 1.4 Cómo queda, con los valores recomendados (sección 3.3)

| Si pasa esto | Hoy | Con el paso |
|---|---|---|
| Un 503 suelto o una conexión cortada | Error en pantalla | Respuesta normal, ~200 ms más tarde (un reintento) |
| Monolito caído | Error al instante | Error en ~0,6 s las primeras requests (3 intentos con esperas de ~200 y ~400 ms); con el circuito abierto, error al instante sin llamar durante 5 s; después una prueba |
| Monolito mudo | Error a los 2 s | Error a los 5 s las primeras requests (2 intentos de 2 s y parte de un tercero); con el circuito abierto, al instante |
| Monolito lento con muchas requests a la vez | Se acumulan llamadas sin tope | Las que pasan el tope responden 503 al instante |
| Respuesta que tarda más que el gateway | El navegador espera hasta 60 s (nginx) o 100 s (YARP) | El gateway corta a los 10 s (decisión 10) |

Los tiempos de la tabla salen de los valores, no de una medición [INFERENCIA].

---

## 2. Qué es seguro reintentar acá

**Regla general.** "A client SHOULD NOT automatically retry a request with a non-idempotent method unless it has some means to know that the request semantics are actually idempotent, regardless of the method" ([FUENTE: RFC 9110 §9.2.2](https://www.rfc-editor.org/rfc/rfc9110#section-9.2.2)). Azure: "Consider whether the operation is idempotent. If so, it's inherently safe to retry" ([FUENTE](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry)).

| Llamada | Método | ¿Idempotente? | ¿Se reintenta? |
|---|---|---|---|
| `GET /internal/v1/owners/{ownerId}/projects/{projectId}` `[CÓDIGO backend/Features/Tasks/ProjectsClient.cs:19-22]` | GET | Sí (método seguro) | Sí |
| `GET /internal/v1/task-count-snapshot` `[CÓDIGO backend/Features/Projects/RebuildTaskCounters/TaskCountsSnapshotClient.cs:17-18]` | GET | Sí | Sí |
| `POST /internal/v1/session-checks` `[CÓDIGO backend/Features/Tasks/Access/AuthSessionClient.cs:19-23]` | POST | **Sí por diseño**: lee la sesión; si venció, la borra (`ExecuteDeleteAsync`, repetirlo no cambia nada); si hace falta, mueve `LastSeenAt` con un `UPDATE ... WHERE LastSeenAt = visto` `[CÓDIGO backend/Features/Auth/Sessions.cs:28-55]`. Dos llamadas iguales dejan el mismo estado | Sí, declarado a mano |

**Cómo lo maneja la librería.** "By default, the standard resilience handler is configured to make retries for all HTTP methods" ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#disable-retries-for-a-given-list-of-http-methods)). `DisableForUnsafeHttpMethods()` corta los reintentos de `POST`, `PATCH`, `PUT`, `DELETE` y `CONNECT` (misma fuente). Lo medí: un `POST` que falla con `HttpRequestException` se intentó 4 veces por defecto y 1 vez con `DisableForUnsafeHttpMethods()` `[MEDIDO]`. Pero `DisableFor`, `DisableForUnsafeHttpMethods` y `HttpResilienceContextExtensions.GetRequestMessage` llevan `[Experimental("EXTEXP0001")]` ([FUENTE: HttpRetryStrategyOptionsExtensions.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpRetryStrategyOptionsExtensions.cs), [FUENTE: DiagnosticIds.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Shared/DiagnosticIds/DiagnosticIds.cs)): usarlas pide un `#pragma warning disable` o un `NoWarn`, y el repo solo tiene `#pragma` en migraciones generadas (busqué `pragma warning` en `backend/`, `dune/` y `gateway/`).

**Recomendación (decisión 4).** Cada pipeline es por cliente tipado (sección 5.3), así que la decisión puede ir en el registro: `AddRemoteClient<TClient>(servicio, RemoteRetry.Idempotent)` o `RemoteRetry.Never`, sin valor por defecto, para que cada cliente nuevo tenga que pensarlo. Los tres de hoy van con `Idempotent`. Con `Never`, el pipeline simplemente no lleva la estrategia de reintento. No hace falta mirar el método de cada request ni usar APIs experimentales [INFERENCIA].

**El cuerpo del POST se puede mandar dos veces.** `JsonContent.Create` serializa en cada envío: medí un `POST` real por sockets contra WireMock con un 503 y después un 200; llegaron 2 pedidos con el mismo cuerpo `[MEDIDO]`.

---

## 3. Política estándar o propia, valores y la cascada completa

### 3.1 Qué trae `AddStandardResilienceHandler`

Valores por defecto ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#standard-resilience-handler-defaults)):

| Orden | Estrategia | Por defecto |
|---|---|---|
| 1 | Limitador | 1000 permisos, cola 0 |
| 2 | Timeout total | 30 s |
| 3 | Reintento | 3 reintentos, exponencial, jitter, 2 s de base |
| 4 | Circuit breaker | 10 % de fallas, mínimo 100 llamadas, ventana 30 s, abierto 5 s |
| 5 | Timeout por intento | 10 s |

Además, el reintento respeta `Retry-After` (`ShouldRetryAfterHeader = true`) ([FUENTE: HttpRetryStrategyOptions.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpRetryStrategyOptions.cs)), y registra dos validadores con `AddOptionsWithValidateOnStart`: total ≥ intento y ventana del breaker ≥ 2 × intento ([FUENTE: StandardResilience.cs, líneas 70-73](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/ResilienceHttpClientBuilderExtensions.StandardResilience.cs)).

### 3.2 Por qué recomiendo una política propia (`AddResilienceHandler`)

Tres hechos medidos sobre la estándar, en este repo:

1. **Pisa `HttpClient.Timeout`.** El código hace `builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)` con el comentario "Disable the HttpClient timeout to allow the timeout strategies to control the timeout" ([FUENTE: StandardResilience.cs, líneas 90-91](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/ResilienceHttpClientBuilderExtensions.StandardResilience.cs)). Medí que gana aunque el cliente ponga 2 s antes, como hace `AddRemoteClient`: `HttpClient.Timeout` quedó en `-00:00:00.001` (infinito) `[MEDIDO]`. Entonces el `catch (TaskCanceledException)` de `SendRemoteAsync` deja de ver los timeouts y su mensaje "did not answer within {client.Timeout.TotalSeconds} s" `[CÓDIGO RemoteServices.cs:40-42]` diría "Infinity". Con un servidor mudo, la llamada salió como `TimeoutRejectedException` al cumplirse el total `[MEDIDO]`, una excepción que hoy terminaría en 500 `[CÓDIGO GlobalExceptionHandler.cs:14-16]`.
2. **Usa el reloj del contenedor.** Polly toma el `TimeProvider` registrado: `if (serviceProvider.GetService<TimeProvider>() is { } timeProvider) builder.TimeProvider = timeProvider;` ([FUENTE: PollyServiceCollectionExtensions.cs](https://github.com/App-vNext/Polly/blob/main/src/Polly.Extensions/DependencyInjection/PollyServiceCollectionExtensions.cs)). El repo registra `TimeProvider.System` `[CÓDIGO backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs:37]`, pero `IsolatedApi` lo reemplaza por un `FakeTimeProvider` `[CÓDIGO backend/Tests/Support/IsolatedApi.cs:60,71]`, que usan 20 archivos de tests (busqué `IsolatedApi.StartAsync|StartOn`). Medí que con un `FakeTimeProvider` en el contenedor, una llamada con un 503 y después un 200 quedó colgada 1,5 s reales con 1 llamada hecha, y terminó recién al adelantar el reloj falso `[MEDIDO]`. Un test que adelanta 14 días (paso 28) también vencería los timeouts de una llamada en curso [INFERENCIA].
3. **Elegir qué se reintenta pide API experimental** (sección 2).

Con `AddResilienceHandler` el pipeline se arma a mano y se puede fijar el reloj: `builder.TimeProvider` es público y settable ([FUENTE: ResiliencePipelineBuilderBase.cs](https://github.com/App-vNext/Polly/blob/8.4.2/src/Polly.Core/ResiliencePipelineBuilderBase.cs)). Medí que con `builder.TimeProvider = TimeProvider.System` dentro de `AddResilienceHandler` y un `FakeTimeProvider` en el contenedor, la misma llamada terminó en 213 ms con 2 intentos `[MEDIDO]`, y que `AddResilienceHandler` no toca `HttpClient.Timeout` (quedó en 2 s) `[MEDIDO]`: hay que ponerlo en infinito a mano, porque `HttpClient.Timeout` envuelve a todo el pipeline y "only the shorter of the two timeouts will apply" ([FUENTE: HttpClient.Timeout](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0)).

Costo de la política propia: `RemoteServices` nombra `Polly` (`using Polly;` hace falta para `AddRetry`, `AddTimeout`, etc.; sin él, no compila `[MEDIDO]`) y el repo valida sus propias reglas (sección 3.5). Learn avala las dos vías y pide no apilar: "you should only add one resilience handler and avoid stacking handlers" ([FUENTE](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#add-resilience-to-an-http-client)).

### 3.3 Valores recomendados para este repo

| Pieza | Valor | Configurable en `Services:<Servicio>` | Por qué |
|---|---|---|---|
| Timeout por intento | 2 s | `Timeout` (la clave que ya existe) | Es el comportamiento de hoy. La prueba de SLO congela la base 1,5 s justamente porque el intento es 2 s `[CÓDIGO docs/decisions.tsv:313]`; bajarlo rompe esa prueba |
| Timeout total | 5 s | `TotalTimeout` | Entran dos intentos completos y las esperas; queda por debajo de la ruta del gateway (10 s) |
| Reintentos | 2, exponencial con jitter, 200 ms de base, respeta `Retry-After` | `Retries`, `RetryDelay` | Para una app web interactiva, "it's better to fail after a smaller number of retries with only a short delay between retry attempts" ([FUENTE: Azure, Retry](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry)) |
| Circuit breaker | 50 % de fallas, mínimo 10 llamadas, ventana 30 s, abierto 5 s | `BreakDuration`, `BreakerMinimumThroughput` | Con el mínimo de 100 por defecto el circuito no se abre nunca con el tráfico de este laboratorio [INFERENCIA]; 30 s ≥ 2 × 2 s cumple la regla de Microsoft |
| Limitador | 100 a la vez, cola 0 | `MaxConcurrentRequests` | Tope real pero holgado para el tráfico del repo [INFERENCIA]; cola 0 = falla rápido |
| `HttpClient.Timeout` | infinito | no | Una sola fuente de verdad para el tiempo, como hace la estándar |
| Reloj del pipeline | `TimeProvider.System` | no | Separa el reloj de red del reloj de negocio de los tests (decisión 6) |

Los valores se validan al arrancar (sección 3.5) y los tests los bajan por configuración (milisegundos), como ya hace `ProjectsSilent` con `Services:Monolith:Timeout` `[CÓDIGO RemoteBoundaryTests.cs:158]`.

### 3.4 La cascada completa

| Nivel | Timeout hoy | Con el paso | Fuente |
|---|---|---|---|
| Navegador (`fetch` del frontend) | sin límite propio [INFERENCIA] | igual | — |
| nginx → gateway | `proxy_read_timeout 60s` por defecto, entre dos lecturas | igual | `[CÓDIGO frontend/nginx.conf:8-14]`; [FUENTE: nginx](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_read_timeout) |
| Gateway YARP → API | sin timeout de ruta; `ActivityTimeout` de 100 s sin actividad | **10 s por ruta** | "Requests do not have any timeouts by default, other than the ActivityTimeout" ([FUENTE: YARP timeouts](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0)) |
| Tasks/monolito → otro proceso (operación) | 2 s (`HttpClient.Timeout`) | **5 s total** | `[CÓDIGO RemoteServiceOptions.cs:9]` |
| Cada intento | — (es el mismo de arriba) | **2 s** | — |

Regla de la cascada: intento (2) < total (5) < gateway (10) < nginx (60). Hoy el gateway no tiene un nivel propio, así que "el timeout interno es menor que el externo" no tiene contra qué compararse más que con nginx y los 100 s de YARP.

**El timeout del gateway (decisión 10).** YARP toma el `Timeout` por ruta del middleware de request timeouts de ASP.NET Core: hace falta `AddRequestTimeouts()` y `UseRequestTimeouts()` ([FUENTE: YARP timeouts](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/timeouts?view=aspnetcore-10.0)). Al vencer, "The default behavior if the app doesn't handle the exception and produce a response is to return status code 504", y `TimeoutStatusCode` lo cambia ([FUENTE: Request timeouts middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts?view=aspnetcore-10.0)). 504 es el código exacto del caso: "did not receive a timely response from an upstream server" ([FUENTE: RFC 9110 §15.6.5](https://www.rfc-editor.org/rfc/rfc9110#section-15.6.5)). El gateway ya arma ProblemDetails con `code = Http.<Reason>` para cualquier status `[CÓDIGO gateway/GatewayProblemDetails.cs:7-16]`, así que el 504 saldría como `Http.GatewayTimeout` [INFERENCIA]. Es un status nuevo del contrato público: por la regla 3, va con un texto en `http-error.ts` y su test de Vitest. Ojo: el timeout de ruta "does not apply when a debugger is attached" (misma fuente de YARP).

No resuelve el pendiente de `progreso.md:230` entero (la espera mientras YARP no marca caída una API): eso pide chequeos pasivos (`HealthCheck:Passive`, política `TransportFailureRate`, [FUENTE: YARP health checks](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/dests-health-checks?view=aspnetcore-10.0)). Con el timeout de ruta, esa espera queda acotada a 10 s [INFERENCIA]. Los chequeos pasivos van como pendiente (regla 7).

### 3.5 Validación al arrancar

`AddRemoteClient` ya valida con `AddOptionsWithValidateOnStart<RemoteServiceOptions, RemoteServiceOptionsValidator>` `[CÓDIGO RemoteServices.cs:12-13]`, con mensajes que un test de arranque compara `[CÓDIGO backend/Tests/Startup/ConfigurationValidationTests.cs:24-26]`. Recomiendo sumar al validador, con un caso de test cada una:

- `Timeout` > 0 y `TotalTimeout` ≤ 30 s (el tope de hoy pasa al total).
- `TotalTimeout` > `Timeout` (la regla de Microsoft, [FUENTE](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/Internal/Validators/HttpStandardResilienceOptionsCustomValidator.cs)).
- Ventana del breaker ≥ 2 × `Timeout` (misma fuente).
- `Retries` entre 0 y 5; `RetryDelay` > 0; `BreakDuration` entre 0,5 s y 60 s; `BreakerMinimumThroughput` ≥ 2; `MaxConcurrentRequests` ≥ 1. El mínimo 2 del breaker es el de Polly [INFERENCIA: lo usé como mínimo en la medición y aceptó 2; no busqué el validador de Polly].

El timeout del gateway vive en otro proceso: no se puede validar desde la API al arrancar. Lo cubre un test (sección 7.3).

---

## 4. Bulkhead

- **Cómo.** La estrategia de limitador de la librería es, por defecto, un limitador de concurrencia: "PermitLimit set to 1000 and QueueLimit set to 0" ([FUENTE: Polly, rate limiter](https://www.pollydocs.org/strategies/rate-limiter.html)); `HttpRateLimiterStrategyOptions` hereda sin cambios de `RateLimiterStrategyOptions` ([FUENTE](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpRateLimiterStrategyOptions.cs)). En una política propia: `builder.AddConcurrencyLimiter(permisos, cola)` ([FUENTE: Polly, rate limiter](https://www.pollydocs.org/strategies/rate-limiter.html)). Viene de `Polly.RateLimiting`, que trae el paquete de Microsoft (sección "Herramientas").
- **Qué pasa al rebalsar.** Se tira `RateLimiterRejectedException`, "Thrown when a rate limiter rejects an execution" (misma fuente). Medí: con 1 permiso y dos llamadas a la vez, una respondió 200 y la otra salió como `RateLimiterRejectedException` sin llegar al servidor `[MEDIDO]`. El reintento no la reintenta (no es transitoria para la librería, sección 1.3).
- **Qué ve el usuario.** 503 `Http.ServiceUnavailable`, el mismo de "el otro proceso no está" (sección 5). No 429: el 429 dice que el que llama se pasó de su cuota, y acá el límite es nuestro hacia la dependencia [INFERENCIA].
- **Granularidad.** Un limitador por cliente tipado (sección 5.3): `ProjectsClient` saturado no frena a `AuthSessionClient`, aunque los dos van al monolito. Es el bulkhead "assigned a connection pool for each service" de Azure, a nivel de cliente [INFERENCIA].

---

## 5. Circuito abierto, mapeo a 503 y `Retry-After`

### 5.1 Qué tira la librería

| Situación | Excepción | Hereda de |
|---|---|---|
| Circuito abierto | `BrokenCircuitException` | `ExecutionRejectedException` ([FUENTE](https://github.com/App-vNext/Polly/blob/8.4.2/src/Polly.Core/CircuitBreaker/BrokenCircuitException.cs)) |
| Venció el timeout (intento o total) | `TimeoutRejectedException` | `ExecutionRejectedException` ([FUENTE](https://github.com/App-vNext/Polly/blob/8.4.2/src/Polly.Core/Timeout/TimeoutRejectedException.cs)) |
| Bulkhead lleno | `RateLimiterRejectedException` | `ExecutionRejectedException` ([FUENTE](https://github.com/App-vNext/Polly/blob/8.4.2/src/Polly.RateLimiting/RateLimiterRejectedException.cs)) |
| Se agotaron los reintentos con 5xx | ninguna: vuelve la última respuesta (por ejemplo el 503) | — |

Lo medí: un servidor mudo terminó en `TimeoutRejectedException`; un servidor siempre 503 abrió el circuito y dio `BrokenCircuitException`, y las dos llamadas siguientes fallaron igual sin llegar al servidor (el contador del handler quedó en 2) `[MEDIDO]`. Polly avisa: "Polly throws a `TimeoutRejectedException` (which inherits from Exception), not the standard TimeoutException" ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#add-custom-resilience-handlers)).

### 5.2 Mapeo

`SendRemoteAsync` suma un `catch (ExecutionRejectedException ex)` que tira `RemoteServiceUnavailableException` con un mensaje por caso ("circuit open", "did not answer within {total} s", "too many concurrent calls"). El 5xx que vuelve después de los reintentos ya lo traduce el código de hoy `[CÓDIGO RemoteServices.cs:48-52]`. El resto no cambia: `GlobalExceptionHandler` → 503 `Http.ServiceUnavailable` `[CÓDIGO GlobalExceptionHandler.cs:14-16]`, también en `OnValidatePrincipal`, que no devuelve `ErrorOr` `[CÓDIGO docs/decisions.tsv:306]`. Así ninguna slice nombra Polly.

### 5.3 ¿Breaker por cliente o compartido?

- **Hoy cada cliente tipado tiene su propio pipeline.** El nombre del pipeline es `{nombre del cliente}-{nombre del handler}` ([FUENTE: PipelineNameHelper.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/Internal/PipelineNameHelper.cs)), y la clave en el registro de Polly sale de ese nombre ([FUENTE: ResilienceHttpClientBuilderExtensions.Resilience.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/ResilienceHttpClientBuilderExtensions.Resilience.cs)). Medí: con el circuito de un cliente abierto, otro cliente tipado hacia el mismo host respondió 200 `[MEDIDO]`. En el repo: `AuthSessionClient` y `ProjectsClient` tienen circuitos separados aunque los dos van al monolito.
- **`SelectPipelineByAuthority`** crea un pipeline por "scheme + host + port" dentro de un mismo cliente ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience#standard-hedging-handler-defaults)); Polly lo recomienda en vez de un diccionario de breakers a mano ([FUENTE: Polly, circuit breaker](https://www.pollydocs.org/strategies/circuit-breaker.html)). Acá cada cliente llama a una sola dirección, así que no suma nada [INFERENCIA].
- **Recomendación (decisión 8):** un breaker por cliente tipado. Ventaja: si el monolito rompe solo la ruta de proyectos (por ejemplo un release viejo), la validación de sesión sigue andando; es la "resource differentiation" de Azure ([FUENTE](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)). Costo: si el monolito entero cae, cada cliente tiene que juntar sus propias fallas para abrir.

### 5.4 `Retry-After`

- `BrokenCircuitException.RetryAfter` existe desde Polly 8.5.0 ("Add `RetryAfter` to `BrokenCircuitException`", [FUENTE: CHANGELOG](https://github.com/App-vNext/Polly/blob/main/CHANGELOG.md)); en 8.4.2 no está ([FUENTE](https://github.com/App-vNext/Polly/blob/8.4.2/src/Polly.Core/CircuitBreaker/BrokenCircuitException.cs)).
- El paquete de Microsoft pide `Polly.Extensions` ≥ 8.4.2 ([FUENTE: NuGet, Microsoft.Extensions.Resilience](https://www.nuget.org/packages/Microsoft.Extensions.Resilience/11.0.0#dependencies-body-tab)). En el proyecto `Platform` del repo, `Polly.Core` ya entra en 8.6.5 por `JasperFx` 2.102.0 (Wolverine), y al sumar el paquete nuevo queda `Polly.Core` 8.6.5 con `Polly.Extensions` y `Polly.RateLimiting` 8.4.2, sin advertencias de restore `[MEDIDO]`. O sea: hoy `RetryAfter` estaría disponible, pero de casualidad, por una dependencia de Wolverine.
- `RateLimiterRejectedException.RetryAfter` es opcional y un limitador de concurrencia no lo llena [INFERENCIA, por [FUENTE: Polly, rate limiter](https://www.pollydocs.org/strategies/rate-limiter.html): "can optionally contain information about when to retry"].
- **Recomendación (decisión 7):** 503 sin `Retry-After` en este paso. El frontend solo lee `Retry-After` en el 429 `[CÓDIGO frontend/src/app/core/http-error.ts:55-56]`, así que el header no cambiaría nada en pantalla, y depender de 8.6.5 pediría una referencia directa a `Polly.Core` (sección "Herramientas"). Queda como pendiente.

---

## 6. Telemetría

- **Métricas.** Meter `Polly`; instrumentos `resilience.polly.strategy.events` (contador, con `event.name`, `pipeline.name`, `strategy.name`, ...), `resilience.polly.strategy.attempt.duration` y `resilience.polly.pipeline.duration` ([FUENTE: Polly, telemetry](https://www.pollydocs.org/advanced/telemetry.html)). Eventos: `OnRetry`, `OnTimeout`, `OnCircuitOpened`, `OnCircuitHalfOpened`, `OnCircuitClosed`, `OnRateLimiterRejected` (misma fuente). El paquete de Microsoft suma la etiqueta `error.type` con el status HTTP ([FUENTE: HttpResilienceMetricsEnricher.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/Internal/HttpResilienceMetricsEnricher.cs)). Medí con un `MeterListener`, para un 503 y un 200: `resilience.polly.strategy.events` con `event.name=OnRetry, pipeline.name=A-standard, strategy.name=Standard-Retry, error.type=503`, y los dos histogramas `[MEDIDO]`.
- **Logs.** Categoría `Polly`. Medí: un `Warning` "Execution attempt ... Result: '503', Handled: 'True'", un `Warning` "Resilience event occurred. EventName: 'OnRetry'", un `Information` para el intento bueno y `Debug` para el inicio y el fin del pipeline `[MEDIDO]`.
- **Cómo se ve con el OpenTelemetry del repo.** Los logs entran solos: Serilog toma todo desde `Information` `[CÓDIGO backend/Infrastructure/Observability/ObservabilityExtensions.cs:91-96]` y los manda por OTLP con el trace id `[CÓDIGO ObservabilityExtensions.cs:69-89]`. Las métricas **no**: el repo exporta solo `AddMeter("TemplateApp.*", "Wolverine*")` `[CÓDIGO ObservabilityExtensions.cs:58]`. Hay que sumar `"Polly"`. Las trazas ya tienen un span por request HTTP saliente (`AddHttpClientInstrumentation()`, `[CÓDIGO ObservabilityExtensions.cs:51]`); que cada reintento aparezca como span propio es [INFERENCIA, NO PROBADO]: no encontré una fuente primaria que lo diga.
- **Azure recomienda** "log early failures as informational entries and only the failure of the last of the retry attempts as an actual error" ([FUENTE](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry)). Polly loguea cada intento fallido como `Warning`. Lo dejo así y anoto como pendiente una alerta sobre `OnCircuitOpened` en Prometheus.

---

## 7. Tests: el Verifica con un servidor falso

### 7.1 El reloj en los tests

- Con la política propia y `TimeProvider.System` (decisión 6), los tests usan tiempos reales chicos por configuración: `RetryDelay` 10 ms, `BreakDuration` 300-500 ms, `Timeout` de unos cientos de ms. Así los tests de `IsolatedApi`, que tienen un reloj falso de negocio, no se cuelgan si una llamada remota falla.
- Si Tobi prefiere el reloj del contenedor (opción R1 de la decisión 6), el pipeline se puede acelerar con `FakeTimeProvider`: lo medí (sección 3.2). El precio es que cada test con `IsolatedApi` y una falla remota tiene que adelantar el reloj a mano.
- Polly ofrece `Polly.Testing` con `GetPipelineDescriptor()` para inspeccionar las estrategias de un pipeline ([FUENTE: Polly, testing](https://www.pollydocs.org/advanced/testing.html)). No lo recomiendo: suma una referencia directa a Polly y la configuración efectiva ya se lee de las options (sección 7.3).

### 7.2 Transitorio y persistente

**Con WireMock (W1).** `WireMockServer.Start()` levanta un servidor en un puerto libre dentro del proceso del test ([FUENTE: WireMock, stubbing](https://wiremock.org/dotnet/stubbing)). Un escenario (`InScenario`, `WhenStateIs`, `WillSetStateTo`, con un segundo argumento para cuántas coincidencias hacen falta antes de cambiar de estado) programa "503 y después 200" ([FUENTE: scenarios and states](https://wiremock.org/dotnet/scenarios-and-states/)); `WithDelay` simula un servidor lento ([FUENTE: stubbing](https://wiremock.org/dotnet/stubbing)); `WithFault(FaultType.EMPTY_RESPONSE | MALFORMED_RESPONSE_CHUNK, probabilidad)` simula una respuesta rota ([FUENTE: faults](https://wiremock.org/dotnet/faults/)). Medí con `WireMock.Net.Minimal` 2.32.0: escenario 503→200 a través del pipeline = 200 con 2 pedidos en el servidor; `WithDelay(3 s)` con total 1 s = `TimeoutRejectedException` a los 1010 ms `[MEDIDO]`. **Ojo:** el primer pedido a un WireMock recién levantado tardó 387-790 ms `[MEDIDO]`; con un timeout por intento de 400 ms, el test falló por eso. Los tests necesitan un pedido de calentamiento o intentos de 1 s o más.

Forma propuesta (en `backend/Tests/Remote/RemoteResilienceTests.cs`):

1. **V1, transitorio → éxito.** Un `ServiceCollection` con `Services:Monolith:BaseAddress = server.Url` y `AddRemoteClient<ProjectsClient>(TasksFeature.MonolithService, RemoteRetry.Idempotent)`. El servidor responde 503 y después 200 con un `ProjectAssignment`. `FindAssignableAsync` devuelve la asignación y el servidor vio 2 pedidos. Variante para `AuthSessionClient` (el POST).
2. **V2, persistente → circuito abierto.** El servidor responde siempre 503; `BreakerMinimumThroughput = 2`, `BreakDuration = 500 ms`. Se llama hasta recibir `RemoteServiceUnavailableException` con `BrokenCircuitException` adentro; la llamada siguiente falla sin que el contador del servidor suba. Después de `BreakDuration` el servidor pasa a 200 y la llamada de prueba cierra el circuito. Se puede afirmar también la métrica `OnCircuitOpened` con un `MeterListener`, como ya hace el repo con sus métricas (`RecordedMeasurements`, `[CÓDIGO backend/Tests/Support/RecordedMeasurements.cs]`).
3. **De punta a punta (sin servidor falso nuevo).** Con el `RemoteFault` que ya existe se ajustan los tests de `RemoteBoundaryTests`: `ProjectsUnreachable` pasa de `Calls == 1` a `Calls == 3` y `AuthUnreachable` también (decisión 12). El interceptor está en el handler primario (`ConfigureHttpClientDefaults(... ConfigurePrimaryHttpMessageHandler ...)`, `[CÓDIGO backend/Tests/Support/ApiPair.cs:66-73]`), o sea adentro del pipeline: ve cada intento [INFERENCIA, por la posición del handler].

**Sin librería (W2).** `RemoteFault` ya simula "no conecta", "mudo", "ruta que falta" y "respuesta perdida" con un `HttpMessageHandler` en memoria `[CÓDIGO backend/Tests/Support/RemoteFault.cs:15-74]`. Para el Verifica le faltan dos cosas: "falla N veces y después anda" y "responde 5xx". Son unas 15 líneas más [INFERENCIA]. La otra variante es un Kestrel mínimo en el test (`WebApplication` en puerto 0 con `MapGet` programados), sin dependencia nueva, con sockets reales.

### 7.3 "El timeout interno es menor que el externo"

Dos tests, porque prueban cosas distintas:

1. **De configuración (determinista).** Arranca los dos hosts de `ApiPair` con la configuración real de `appsettings.json` y lee las options efectivas: `IOptionsMonitor<RemoteServiceOptions>.Get("Monolith")` en Tasks y `.Get("Tasks")` en el monolito. Lee `gateway/appsettings.json` (el `Timeout` de cada ruta) y compara: intento < total < ruta del gateway, y ruta del gateway < 60 s de nginx (el valor por defecto, porque `nginx.conf` no fija `proxy_read_timeout`, `[CÓDIGO frontend/nginx.conf:8-14]`). Medí que las options efectivas se leen por nombre con `IOptionsMonitor<...>.Get(nombre)` (con las options de la librería estándar: `A-standard` devolvió intento 1 s, total 30 s, 3 reintentos) `[MEDIDO]`.
2. **Medido.** Adaptar `ProjectsSilent_TheCallerGivesUpAtItsTimeout_With503` `[CÓDIGO RemoteBoundaryTests.cs:151-171]`: con `Timeout = 300 ms` y `TotalTimeout = 1 s`, el 503 llega entre 1 s y 2 s (no a los 300 ms ni nunca) y `projects.Calls` ≥ 2. Prueba que el total manda sobre el intento y que hubo reintento.

Probar el gateway de punta a punta (un 504 a los 10 s) pide levantar YARP en el test; hoy `ApiPair` no lo levanta `[CÓDIGO ApiPair.cs:10-52]`. Recomiendo cubrirlo en el smoke de Compose de `verify-template`, que ya prueba el 503 del gateway (`progreso.md`, cierre del paso 23) [NO PROBADO].

---

## 8. Dónde va cada pieza y qué docs cambian

**Según ADR-0004 punto 6** ("`Infrastructure/` queda para la fontanería técnica del host", `[CÓDIGO docs/adr/0004-vertical-slices-deep-modules.md:25]`) y la regla de que `Infrastructure/` no nombra features `[CÓDIGO docs/conventions.md:24]`:

| Pieza | Dónde |
|---|---|
| Política (pipeline, opciones nuevas, validador, mapeo de excepciones, `RemoteRetry`) | `backend/Infrastructure/Http/` (`RemoteServices.cs`, `RemoteServiceOptions.cs`, `RemoteServiceOptionsValidator.cs`, un archivo nuevo por tipo, DUNE0010) |
| Paquete `Microsoft.Extensions.Http.Resilience` | `backend/Platform/TemplateApp.Platform.csproj`, que compila `Infrastructure/` `[CÓDIGO backend/Platform/TemplateApp.Platform.csproj:14-15]` |
| Decisión de reintento de cada cliente | En el registro de la feature: `TasksFeature.cs:28-29`, `ProjectsFeature.cs:28` |
| Los clientes | No cambian: siguen llamando a `SendRemoteAsync` |
| `AddMeter("Polly")` | `backend/Infrastructure/Observability/ObservabilityExtensions.cs:58` |
| Timeout de ruta | `gateway/appsettings.json` y `gateway/Program.cs` (`AddRequestTimeouts`/`UseRequestTimeouts`) |
| Texto del 504 | `frontend/src/app/core/http-error.ts` y su test |
| Tests | `backend/Tests/Remote/` (nuevo `RemoteResilienceTests.cs`, ajustes en `RemoteBoundaryTests.cs`), `backend/Tests/Startup/ConfigurationValidationTests.cs`; WireMock en `backend/Tests/TemplateApp.Tests.csproj` |

**Docs a actualizar:**
- `docs/architecture.md:71` ("Timeouts ... sin reintentos: los reintentos y el circuit breaker son del paso 29") → la política nueva, los valores y la cascada. `:72` (Falla) → circuito abierto y bulkhead también dan 503. `:74` (Qué ve el usuario) → los tiempos de la sección 1.4.
- `docs/conventions.md:103` → los reintentos entraron en el paso 29; la regla de clientes (solo `AddRemoteClient`, cada cliente declara su reintento) y el gate nuevo de paquetes.
- `docs/decisions.tsv` → la herramienta y cada decisión de la sección "Decisiones".
- `docs/evolucion/plan.md:464-467` → nota del paso, como los demás.
- `docs/evolucion/progreso.md:87` → el nombre del paso (hoy "Primer cliente HTTP saliente con resiliencia") y `:7` (choque 1).
- `docs/evolucion/informes/informe-dotnet-empresarial.md:147` → precisar el OSMF (choque 5).
- `CONTEXT.md`: nada. Es vocabulario del dominio (perfiles, tareas, cierres) `[CÓDIGO CONTEXT.md:1-3]`; la resiliencia es técnica.
- `dune/check-structure.mjs`, `dune/Dune.Analyzers/`, `dune/Dune.props` (sección 9).

---

## 9. Dune y gates (regla 4)

Hoy nada impide armar un cliente HTTP fuera de `AddRemoteClient`: el único `AddHttpClient` del producto está en `RemoteServices.cs` `[CÓDIGO backend/Infrastructure/Http/RemoteServices.cs:16]` y no hay ningún `new HttpClient` en `backend/` fuera de los tests ni en `gateway/` (busqué `new HttpClient|HttpMessageInvoker|AddHttpClient`), pero es costumbre, no regla. `check-structure.mjs` tampoco mira paquetes de resiliencia: solo prohíbe buses y cachés `[CÓDIGO dune/check-structure.mjs:206-346]`.

**DUNE0027, "Cliente HTTP fuera de Infrastructure/Http"** (recomendada). En archivos de `backend/` que no son tests (`DunePaths.IsBackendFile` y `IsTestsFile`, `[CÓDIGO dune/Dune.Analyzers/DunePaths.cs:15-24]`), fuera de `backend/Infrastructure/Http/`, es error:
- crear `HttpClient`, `HttpMessageInvoker`, `HttpClientHandler` o `SocketsHttpHandler`;
- llamar a `AddHttpClient`, `ConfigureHttpClientDefaults`, `ConfigurePrimaryHttpMessageHandler`, `AddHttpMessageHandler` o cualquier método de `ResilienceHttpClientBuilderExtensions`;
- nombrar un símbolo de los namespaces `Polly*` o `Microsoft.Extensions.Http.Resilience`.

Fixture válida: `services.AddRemoteClient<ProjectsClient>(MonolithService, RemoteRetry.Idempotent)` en `Features/Tasks/TasksFeature.cs`. Violaciones deliberadas: `services.AddHttpClient<ProjectsClient>()` en `TasksFeature.cs`; `new HttpClient()` en una slice; `catch (BrokenCircuitException)` en un cliente. Mismo patrón que DUNE0025 para Wolverine `[CÓDIGO docs/adr/0004-vertical-slices-deep-modules.md:28]`. El gateway queda afuera porque no está en `backend/` y YARP trae su propio cliente.

**Gate de `check-structure.mjs`** (como el de bus, `[CÓDIGO dune/check-structure.mjs:222-248,295-307]`): `Microsoft.Extensions.Http.Resilience` solo en `TemplateApp.Platform.csproj`; ninguna referencia directa a `Polly`, `Polly.Core`, `Polly.Extensions`, `Polly.RateLimiting` ni `Microsoft.Extensions.Http.Polly` en ningún proyecto (este último está deprecado: "The Microsoft.Extensions.Http.Polly NuGet package is deprecated", [FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/)). Con su caso en `check-structure.test.mjs`. Hace explícito que Polly entra solo de forma transitiva, que es lo que deja al repo fuera del OSMF (sección "Herramientas").

Con la opción B, el gate cambia: `Polly.Core` y compañía se permiten solo en `Platform`.

---

## Choques con los docs

1. **Modo autónomo.** `progreso.md:7` dice "El modo autónomo (pasos 23.1 a 28) terminó con el paso 28: vuelve la regla 11". `AGENTS.md` (PR #89, mergeado en `66d5200`, después del PR #92 del paso 28) dice "Modo autónomo (pasos 23.1 a 29)" y que el orquestador "para después del 29" `[CÓDIGO AGENTS.md, sección "Modo autónomo"]`. Por la regla 10, dos docs que se contradicen frenan el paso: Tobi tiene que confirmar si el 29 va en modo autónomo. Mi lectura: vale `AGENTS.md`, que es la fuente de las reglas y es más nuevo; `progreso.md:7` quedó viejo.
2. **El nombre del paso.** `progreso.md:87` dice "Primer cliente HTTP saliente con resiliencia" y el plan dice "Resiliencia en los clientes HTTP salientes" `[CÓDIGO plan.md:464]`. La investigación del paso 23 ya pidió el cambio `[CÓDIGO docs/evolucion/investigacion/paso-23-servicio-gateway.md:30]`. Se corrige en este paso.
3. **El plan no nombra el gateway, pero el Verifica lo necesita.** "Cambia" lista el cliente, `Infrastructure/`, el paquete y los tests `[CÓDIGO plan.md:466]`, y el Verifica pide "el timeout interno es menor que el externo" `[CÓDIGO plan.md:467]`. Hoy el único nivel externo con timeout es nginx (60 s) o el `ActivityTimeout` de YARP (100 s). Recomiendo sumar el timeout de ruta del gateway (decisión 10); la alternativa sin tocar el gateway está en la decisión 11.
4. **"Tests con WireMock" adelanta una elección de herramienta.** La regla 5 dice que las herramientas las elige Tobi, y la excepción del modo autónomo lo repite `[CÓDIGO AGENTS.md, regla 5 y "Modo autónomo"]`. El plan nombra WireMock y `Microsoft.Extensions.Http.Resilience`; los presento como opciones recomendadas, no como hechos.
5. **El aviso del OSMF en el informe está incompleto.** `informe-dotnet-empresarial.md:147` dice que "desde el 16/11/2026 las empresas con ingresos ≥ US$20.000 por un producto que use Polly pagan US$20/mes". El README y el anuncio agregan dos límites: la cuota "applies only to the next major version of Polly released after November 16, 2026" y "Only direct references to Polly are subject to the fee" (Microsoft.Extensions.Resilience y Microsoft.Extensions.Http.Resilience quedan afuera) ([FUENTE: anuncio](https://www.thepollyproject.org/2026/07/14/polly-osmf-announcement.html), [FUENTE: README](https://github.com/App-vNext/Polly/blob/main/README.md)). Se corrige el informe.
6. **Tests que fijan "sin reintentos".** `RemoteBoundaryTests` afirma `Calls == 1` para Auth y Projects `[CÓDIGO RemoteBoundaryTests.cs:55,118]`. La regla 9 permite cambiarlos porque el reintento es el objetivo declarado del paso, con una línea en `decisions.tsv`.
7. **Prueba de SLO atada al timeout de 2 s.** `decisions.tsv:313` congela la base 1,5 s porque con 3 s "la llamada de Tasks a Auth vence su timeout de 2 s". Con el paso, una base congelada 3 s daría 2 intentos y un 503 a los 5 s en vez de a los 2 s [INFERENCIA]. Mantener el intento en 2 s deja la prueba como está; si Tobi baja el intento, la prueba cambia.

Sin choque con ADR-0004 ni `architecture.md`: la política es fontanería del host y vive en `Infrastructure/Http`, como el resto de lo remoto `[CÓDIGO docs/architecture.md:68-71]`.

## Decisiones (con mi recomendación)

Todas salvo la herramienta. Las de diseño van a `docs/decisions.tsv`.

1. **Herramienta:** decide Tobi (sección siguiente). Recomiendo A + W1.
2. **Política propia con `AddResilienceHandler`**, con las cinco estrategias del estándar en su orden, dentro de `AddRemoteClient`. Descartada la estándar por las tres razones medidas de la sección 3.2.
3. **Valores** de la sección 3.3, en `Services:<Servicio>` con nombres `Timeout` (intento, la clave de hoy), `TotalTimeout`, `Retries`, `RetryDelay`, `BreakDuration`, `BreakerMinimumThroughput`, `MaxConcurrentRequests`; el 50 % del breaker y la ventana de 30 s quedan como constantes.
4. **Reintento declarado por cliente** (`RemoteRetry.Idempotent` o `RemoteRetry.Never`, sin valor por defecto). Los tres clientes de hoy van con `Idempotent`; `AuthSessionClient` con la justificación de la sección 2. Sin APIs experimentales.
5. **`HttpClient.Timeout` infinito**: el tiempo lo controla solo el pipeline.
6. **Reloj del pipeline = `TimeProvider.System`** (R2). Alternativa R1: el reloj del contenedor, que acelera los tests con `FakeTimeProvider` pero cuelga los de `IsolatedApi` ante una falla remota. Recomiendo R2 porque separa el reloj de red del de negocio.
7. **`ExecutionRejectedException` → `RemoteServiceUnavailableException` → 503**, en `SendRemoteAsync`, sin `Retry-After`.
8. **Un circuit breaker y un bulkhead por cliente tipado**, sin `SelectPipelineByAuthority`.
9. **`AddMeter("Polly")`** en la observabilidad; los logs de Polly quedan como vienen.
10. **Timeout de ruta del gateway de 10 s** en las dos rutas, con `AddRequestTimeouts`/`UseRequestTimeouts`, respondiendo 504 `Http.GatewayTimeout`; texto en `http-error.ts` y su test de Vitest (regla 3). Alternativa: `TimeoutStatusCode = 503`, que no cambia el contrato pero dice algo menos exacto.
11. **"Interno < externo" con dos tests** (sección 7.3): uno de configuración que compara intento < total < gateway < nginx, y el de servidor mudo medido. Alternativa sin gateway: comparar solo intento < total; es más chico pero no prueba una cascada entre procesos.
12. **Tests existentes:** `RemoteBoundaryTests` pasa a `Calls == 3` donde hoy afirma 1, y el de servidor mudo pasa a medir el total; anotado en `decisions.tsv` (regla 9).
13. **Regla nueva:** DUNE0027 y el gate de paquetes de la sección 9, cada uno con fixture válida y violación deliberada, y DUNE0027 en `WarningsAsErrors` de `dune/Dune.props` `[CÓDIGO dune/Dune.props:5]`.
14. **Sin hedging ni retry budget.** El retry budget (presupuesto de reintentos con ventana deslizante) llegó en 11.0.0 como API experimental (EXTEXP0001) ([FUENTE: release v11.0.0](https://github.com/dotnet/extensions/releases/tag/v11.0.0)); con 2 reintentos y el breaker, la amplificación queda acotada a 3× [INFERENCIA].
15. **Versión: 10.11.1**, la línea 10.x que acompaña a .NET 10 (sección siguiente).

## Herramientas (regla 5)

### Versiones y licencias verificadas al 2026-11-20

| Paquete | Versión | Fecha | Licencia | Fuente |
|---|---|---|---|---|
| `Microsoft.Extensions.Http.Resilience` | **10.11.1** (servicing de la línea 10.x) | 2026-11-20 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience), [release v10.11.1](https://github.com/dotnet/extensions/releases/tag/v10.11.1) |
| ídem | 10.11.0 | 2026-10-14 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience/10.11.0) |
| ídem | 11.0.0 ("ships alongside .NET 11") | 2026-11-10 | MIT | [release v11.0.0](https://github.com/dotnet/extensions/releases/tag/v11.0.0) |
| `Microsoft.Extensions.Resilience` (dependencia) | 10.11.1 | 2026-11-20 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Resilience) |
| `Polly.Core` | 8.9.1 (última); entra 8.6.5 en el repo | 2026-11-07 | BSD-3-Clause | [NuGet](https://www.nuget.org/packages/Polly.Core), `[MEDIDO]` |
| `Polly.Extensions`, `Polly.RateLimiting` | entran 8.4.2 | 2024-09-26 | BSD-3-Clause | [releases de Polly](https://github.com/App-vNext/Polly/releases), `[MEDIDO]` |
| `WireMock.Net` | 2.32.0 | 2026-11-20 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/WireMock.Net), [LICENSE](https://github.com/wiremock/WireMock.Net/blob/master/LICENSE) |
| `WireMock.Net.Minimal` | 2.32.0 | 2026-11-20 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/WireMock.Net.Minimal) |

Detalles:
- **Compatibilidad con .NET 10.** `Microsoft.Extensions.Http.Resilience` trae builds para net8.0, net9.0, net10.0 y net11.0 ([FUENTE: NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience)). Para net10.0, la 10.11.0 depende de `Microsoft.Extensions.Http.Diagnostics` ≥ 10.11.0, `Microsoft.Extensions.ObjectPool` ≥ 10.0.13 y `Microsoft.Extensions.Resilience` ≥ 10.11.0 ([FUENTE: NuGet 10.11.0](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience/10.11.0#dependencies-body-tab)); la 11.0.0 sube `ObjectPool` a ≥ 10.0.15 ([FUENTE: NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience)). El runtime del repo es 10.0.12 (`mcr.microsoft.com/dotnet/aspnet:10.0.12`, `[CÓDIGO backend/Dockerfile:9]`). Restauré `Platform` con la 10.11.1 sin advertencias y el programa de prueba corrió en el runtime 10.0.12 `[MEDIDO]`. Recomiendo 10.11.1 y no 11.0.0, que es la línea de .NET 11. El repo ya usa la 10.11.0 de `Microsoft.Extensions.TimeProvider.Testing` `[CÓDIGO backend/Tests/TemplateApp.Tests.csproj:15]`, del mismo repo de Microsoft.
- **Dependencia de Polly.** `Microsoft.Extensions.Resilience` depende de `Polly.Extensions` ≥ 8.4.2 y `Polly.RateLimiting` ≥ 8.4.2 ([FUENTE: NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Resilience/11.0.0#dependencies-body-tab)); `Polly.Core` llega por ellos.
- **Cómo fija versiones el repo.** Sin gestión central: `ManagePackageVersionsCentrally` en `false` `[CÓDIGO dune/Dune.props:3]` y versión exacta en cada `PackageReference` de cada `.csproj` `[CÓDIGO backend/Platform/TemplateApp.Platform.csproj:19-47]`.
- **Vulnerabilidades.** `dotnet list package --vulnerable --include-transitive` sobre el programa de prueba (resiliencia + WireMock.Net completo): ninguna `[MEDIDO]`.

### El Open Source Maintenance Fee de Polly, exacto

- **Quién paga:** "companies that earn at least US $20,000 from a product or project that uses Polly", US$20 por mes, desde el 16 de noviembre de 2026 ([FUENTE: README de Polly](https://github.com/App-vNext/Polly/blob/main/README.md)).
- **Qué versiones:** "The fee applies only to the next major version of Polly released after November 16, 2026"; "Existing versions of Polly are not subject to the fee" ([FUENTE: anuncio](https://www.thepollyproject.org/2026/07/14/polly-osmf-announcement.html)). La última es 8.9.1 ([FUENTE: NuGet](https://www.nuget.org/packages/Polly.Core)): hoy ninguna versión publicada está alcanzada.
- **Transitivo:** "Only direct references to Polly are subject to the fee"; si Polly llega por otra librería, como `Microsoft.Extensions.Resilience` o `Microsoft.Extensions.Http.Resilience`, la cuota no aplica (mismas fuentes). La FAQ del OSMF dice lo mismo en general: "You do not pay the Maintenance Fee to those projects because they are not your direct dependencies" ([FUENTE: OSMF FAQ](https://opensourcemaintenancefee.org/consumers/faq/)).
- **Este repo:** "Individuals, hobbyists, students, non-profits, and organizations below the revenue threshold owe nothing" ([FUENTE: README](https://github.com/App-vNext/Polly/blob/main/README.md)). Un laboratorio sin ingresos no paga con ninguna opción. Importa para quien use la plantilla en una empresa.
- **Licencia:** sigue siendo "the New BSD License" y "The source code remains free and open under its existing license" (misma fuente).

### Opción A: `Microsoft.Extensions.Http.Resilience` (lo que nombra el plan)

- **Qué es:** el paquete de Microsoft "specifically for the HttpClient", sobre `Microsoft.Extensions.Resilience` y Polly ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)). Trae los predicados HTTP (qué es transitorio), el handler que aplica el pipeline a cada request, la integración con `IHttpClientFactory` y la telemetría con `error.type`.
- **Quién lo mantiene:** Microsoft, en `dotnet/extensions`, con release mensual (10.2.0 a 10.11.0, de enero a octubre de 2026, [FUENTE: NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience/10.11.0)).
- **Encaje:** un paquete en `Platform`, la política en `Infrastructure/Http`, DUNE0027 y el gate.
- **Gana:** las piezas del Concepto con defaults probados por Microsoft, cero código de máquina de estados propio, telemetría lista, y la vía que el repo ya eligió en el informe `[CÓDIGO docs/evolucion/informes/informe-dotnet-empresarial.md:145-147,219]`. Fuera del OSMF por ser transitivo.
- **Pierde:** una capa más entre el código y Polly (nombres como `HttpRetryStrategyOptions`); algunas APIs útiles son experimentales; la versión de Polly la decide Microsoft (hoy 8.4.2 como mínimo).

### Opción B: Polly v8 directo (`Polly.Core` + `Polly.Extensions` + `Polly.RateLimiting`)

- **Qué es:** el motor sin la capa HTTP de Microsoft. Hay que escribir los predicados ("5xx, 408, 429, `HttpRequestException`") y el `DelegatingHandler` que corre el pipeline en cada request, que es lo que hace `ResilienceHandler` de Microsoft ([FUENTE: ResilienceHandler.cs](https://github.com/dotnet/extensions/blob/v10.11.1/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/ResilienceHandler.cs)).
- **Versión:** 8.9.1, BSD-3-Clause ([FUENTE: NuGet](https://www.nuget.org/packages/Polly.Core)). Con 8.5.0 o más, `BrokenCircuitException.RetryAfter` está garantizado.
- **Gana:** se ve el motor sin capas; control total de la versión de Polly.
- **Pierde:** más código propio (predicados y handler); referencia directa, así que una futura versión 9 quedaría bajo el OSMF para empresas usuarias de la plantilla; el gate de paquetes tiene que permitir Polly en `Platform`.

### Opción C: sin librería (un `DelegatingHandler` propio)

- **Qué es:** retry con backoff y jitter, breaker con sus tres estados, timeouts con `CancellationTokenSource` enlazados y el limitador con un `SemaphoreSlim`, escritos a mano.
- **Gana:** se entiende cada línea; cero dependencias.
- **Pierde:** el breaker tiene que ser seguro con muchos hilos a la vez ("The implementation shouldn't block concurrent requests or add excessive overhead", [FUENTE: Azure](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)), hay que distinguir el timeout propio de la cancelación del usuario, y la telemetría se escribe desde cero. Es lo que el informe llama no recomendado para producción [INFERENCIA, el informe solo recomienda la opción A]. No es lo que hace la industria (abajo).

### Servidor falso

**W1: `WireMock.Net.Minimal` 2.32.0** (Apache-2.0). `WireMock.Net` es `WireMock.Net.Minimal` más extensiones (GraphQL, ProtoBuf, MimePart, OpenTelemetry, JSONPath) ([FUENTE: NuGet, dependencias de WireMock.Net](https://www.nuget.org/packages/WireMock.Net)); la Minimal alcanza para escenarios, demoras y fallas `[MEDIDO]`. Un dueño en NuGet (sheyenrath), 2.30.0 a 2.32.0 en noviembre de 2026 ([FUENTE: NuGet](https://www.nuget.org/packages/WireMock.Net)).
- **Gana:** un servidor HTTP real con sockets, en proceso; escenarios con estado, `WithDelay`, `WithFault`; el nombre que usa el plan y el informe; paquetes para Testcontainers y Aspire si un día hacen falta ([FUENTE: README](https://github.com/wiremock/WireMock.Net)).
- **Pierde:** un árbol de dependencias grande para tests: la Minimal trae NSwag, Scriban, JmesPath, `Microsoft.IdentityModel.Protocols.OpenIdConnect` 6.34.0 y otras ([FUENTE: NuGet](https://www.nuget.org/packages/WireMock.Net.Minimal)); arranque lento en el primer pedido (387-790 ms `[MEDIDO]`); un solo mantenedor visible.

**W2: servidor falso propio.**
- **W2a, extender `RemoteFault`** (handler en memoria) con "falla N veces" y "responde 5xx".
- **W2b, Kestrel mínimo en el test** (`WebApplication` en el puerto 0).
- **Gana:** cero dependencias; W2a reutiliza lo que ya prueba el límite remoto de punta a punta `[CÓDIGO RemoteBoundaryTests.cs]`.
- **Pierde:** W2a no pasa por sockets (no prueba conexiones reales); los dos son código de test propio para mantener; ninguno enseña la herramienta de la industria.

### Qué gana y qué pierde Tobi, en simple

| | A | B | C |
|---|---|---|---|
| Qué aprendés | Las cuatro piezas con la herramienta de Microsoft | Las mismas, más los predicados y el handler | Cómo funciona cada pieza por dentro |
| Qué ves | Un 503 suelto deja de llegar a pantalla; con el monolito caído, 503 instantáneo; `OnRetry` y `OnCircuitOpened` en métricas y logs | Lo mismo | Lo mismo, con la telemetría que escribas |
| Qué se suma al repo | 1 paquete, ~5 archivos en `Infrastructure/Http`, 1 regla de Dune, 1 gate | 3 paquetes y ~2 archivos más | 0 paquetes, ~10 archivos con lógica delicada |
| Riesgo | Bajo | Medio | Alto (concurrencia del breaker) |
| OSMF | Fuera (transitivo) | Fuera hoy; una v9 directa quedaría alcanzada para empresas | No aplica |

| | W1 (WireMock) | W2 (propio) |
|---|---|---|
| Qué aprendés | La herramienta estándar de servidor falso | A escribir un doble de test |
| Qué se suma | 1 paquete de test con ~30 transitivos [INFERENCIA, por el árbol de NuGet] | ~15-40 líneas de test |
| Realismo | Sockets reales | W2a en memoria; W2b sockets reales |

### Qué hace la industria

- **Resiliencia en todos los clientes, con el handler estándar.** eShop, la aplicación de referencia de .NET, hace `builder.Services.ConfigureHttpClientDefaults(http => { http.AddStandardResilienceHandler(); ... })` ([FUENTE: eShop.ServiceDefaults/Extensions.cs, líneas 22-25](https://github.com/dotnet/eShop/blob/main/src/eShop.ServiceDefaults/Extensions.cs)); la plantilla de service defaults de Aspire hace lo mismo ([FUENTE: aspire-servicedefaults/Extensions.cs, líneas 29-32](https://github.com/microsoft/aspire/blob/main/src/Aspire.ProjectTemplates/templates/aspire-servicedefaults/Extensions.cs)).
- **El paquete viejo está deprecado.** `Microsoft.Extensions.Http.Polly` "is deprecated. Use either of the aforementioned packages instead" ([FUENTE: Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/)).
- **Bulkhead con Polly.** Azure nombra Polly y resilience4j para "creating consumer bulkheads" ([FUENTE: Bulkhead pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/bulkhead)).
- **Reintentar en un solo punto y con jitter** (AWS, sección 1.2).
- **Servidores falsos:** WireMock nació en Java y WireMock.Net lo porta a .NET ([FUENTE: README](https://github.com/wiremock/WireMock.Net)); la industria .NET usa también handlers falsos en memoria [INFERENCIA: no encontré una guía primaria de Microsoft que compare las dos].

### Mi recomendación: A + W1

1. **A es lo que piden el plan y el informe** (`plan.md:466`, `informe-dotnet-empresarial.md:145-147`) y lo que usan eShop y Aspire. Enseña las cuatro piezas sin escribir una máquina de estados a mano.
2. **Con la política propia (decisión 2) igual se ven las piezas una por una**: `AddConcurrencyLimiter`, `AddTimeout`, `AddRetry`, `AddCircuitBreaker`, `AddTimeout`, en el orden de la sección 1.3. El costo de A frente a B es casi cero en aprendizaje y bajo en código.
3. **A deja al repo fuera del OSMF** sin depender de cuándo salga la v9 de Polly, y el gate de paquetes lo hace ejecutable.
4. **W1 es el servidor falso que pide el plan**, con escenarios y demoras listos, y prueba el pipeline con sockets reales. Con la Minimal el árbol queda más chico. Los tests de punta a punta siguen con `RemoteFault`, que ya existe.

Si Tobi prefiere no sumar dependencias de test, **W2a** cumple el Verifica con unas líneas más en `RemoteFault`. B la recomiendo solo si el interés es ver Polly sin la capa de Microsoft; C, solo como ejercicio aparte, no en el producto.

## Pendientes para `progreso.md`

**Se abren:**
- Chequeos pasivos de YARP (`TransportFailureRate`) para no mandar tráfico a una API caída entre dos sondas (completa `progreso.md:230`).
- `Retry-After` en el 503 de circuito abierto, cuando el frontend lo use también en el 503.
- Propagar el tiempo que le queda a la request (*deadline propagation*) al servicio llamado, para que no siga trabajando después de que el que llama se rindió.
- Alerta de Prometheus sobre `OnCircuitOpened` y un panel con `resilience.polly.strategy.events`.
- Retry budget cuando deje de ser experimental.
- Texto en español para el 503 (`title` en inglés y el frontend muestra el genérico).
- Hedging, si un servicio llega a tener réplicas.

**Se cierra en parte:** `progreso.md:230` (la espera queda acotada por el timeout de ruta).

## Verificado / No verificado

**Verificado (leído en el repo, en una fuente primaria o medido):**
- Todo `[CÓDIGO]` en `66d5200`: `RemoteServices`, `RemoteServiceOptions`, `RemoteServiceOptionsValidator`, `RemoteServiceUnavailableException`, `OnBehalfOf`, `GlobalExceptionHandler`, `HttpErrors`, `ApiResults`, `IdempotencyFilter`, `BackendInfrastructure`, `ObservabilityExtensions`, los tres clientes, `RemoteSession`, `Sessions`, `CreateTask`, `RebuildTaskCountersOnStartup`, `TasksFeature`, `ProjectsFeature`, `RemoteBoundaryTests`, `RemoteFault`, `ApiPair`, `ApiFixture`, `IsolatedApi`, `ConfigurationValidationTests`, los cuatro `.csproj`, `Dune.props`, `BannedSymbols.txt`, `check-structure.mjs`, `DuneDiagnostics.cs`, `DunePaths.cs`, `MeterConstructionBanTests.cs`, `gateway/appsettings.json`, `gateway/Program.cs`, `GatewayProblemDetails.cs`, `frontend/nginx.conf`, `http-error.ts`, `compose.yaml`, los Dockerfile, `global.json`, y los docs citados (`AGENTS.md`, `plan.md`, `progreso.md`, `architecture.md`, `conventions.md`, ADR-0004, `CONTEXT.md`, `decisions.tsv`, `informe-dotnet-empresarial.md`, `paso-23-servicio-gateway.md`, `paso-23-2-extraer-tasks.md`, `paso-28-durable-execution.md`).
- Fuentes primarias: Learn (http-resilience, resilience, HttpClient.Timeout, request timeouts, YARP timeouts y health checks, networking tracing, patrones Retry, Circuit Breaker y Bulkhead); código de `dotnet/extensions` en `v10.11.1`; releases v10.11.1 y v11.0.0; código de Polly en `8.4.2` y `main`, su CHANGELOG, README y anuncio del OSMF; FAQ del OSMF; docs de Polly (telemetry, testing, DI, rate limiter, circuit breaker, pipelines); NuGet de `Microsoft.Extensions.Http.Resilience` (10.11.0 y última), `Microsoft.Extensions.Resilience` 11.0.0, `Polly.Core`, `WireMock.Net`, `WireMock.Net.Minimal`; LICENSE y README de WireMock.Net; docs de WireMock (stubbing, scenarios, faults); RFC 9110 (§9.2.2, §15.6.4, §15.6.5); nginx (`proxy_read_timeout`, `proxy_next_upstream`); AWS Builders' Library; eShop y la plantilla de Aspire.
- `[MEDIDO]` en un programa fuera del repo: reintento 503→503→200 (3 llamadas); `FakeTimeProvider` del contenedor congela el pipeline y `builder.TimeProvider = TimeProvider.System` lo evita; `AddStandardResilienceHandler` deja `HttpClient.Timeout` infinito y `AddResilienceHandler` no lo toca; servidor mudo → `TimeoutRejectedException` al vencer el total; circuito abierto → `BrokenCircuitException` sin llegar al servidor, y otro cliente tipado no se ve afectado; `POST` reintentado 4 veces por defecto y 1 con `DisableForUnsafeHttpMethods`; bulkhead lleno → `RateLimiterRejectedException`; métricas y logs de Polly con sus etiquetas; WireMock (completo y Minimal) con escenario 503→200, `WithDelay`, y el primer pedido lento; `POST` con `JsonContent` reenviado por sockets con el mismo cuerpo; `Platform` restaurado con el paquete (Polly.Core 8.6.5 por JasperFx, Polly.Extensions 8.4.2); sin paquetes vulnerables.

**No verificado (queda para el paso o para Tobi):**
- Que cada reintento aparezca como span propio en las trazas.
- El tiempo exacto de cada escenario de la sección 1.4 en el repo real (salen de los valores).
- Que el gateway responda 504 como ProblemDetails con `code = Http.GatewayTimeout` (el middleware de request timeouts con `UseStatusCodePages`).
- El mínimo que acepta Polly para `MinimumThroughput` (usé 2 y lo aceptó; no leí su validador).
- Que el interceptor de `ApiPair` vea cada intento en el repo real (lo infiero por la posición del handler primario).
- Que los 20 archivos de tests con `IsolatedApi` sigan verdes con la política nueva: depende de la decisión 6.
- La cantidad exacta de paquetes transitivos de `WireMock.Net.Minimal` en el proyecto de tests del repo.
- Ningún test del repo corrió: todo lo de las secciones 3 a 9 es diseño.
