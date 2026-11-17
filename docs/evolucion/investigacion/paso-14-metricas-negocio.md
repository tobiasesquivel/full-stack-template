# Paso 14: métricas de negocio reales (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 14 del [plan](../plan.md) (`plan.md:333-336`). Hoy la API exporta solo métricas del framework: `ObservabilityExtensions.cs:38` llama a `AddMetrics()` y `:52-58` configura OpenTelemetry con instrumentación de ASP.NET Core, HttpClient y runtime, más el exporter OTLP cada 10 s. No hay ningún `Meter` propio ni ningún `AddMeter`. El paso agrega un `Meter` por feature (`TemplateApp.Tasks`, `TemplateApp.Auth`), instrumenta las slices y suma tests. Verifica con un test que observa la medición con `MeterListener` y con `smoke-api.mjs --compose`, que tiene que ver el export OTLP con la métrica nueva.
- Decisiones que ya están tomadas y que este informe no reabre:
  - Un `Meter` por feature: `Features/Auth/AuthMetrics.cs` (Meter `TemplateApp.Auth`) y `Features/Tasks/TaskMetrics.cs` (Meter `TemplateApp.Tasks`). El setup de OpenTelemetry queda en Infrastructure con `.AddMeter("TemplateApp.*")`, porque Infrastructure no puede nombrar features (`docs/evolucion/informe-vsa-deep-modules.md:80`).
  - "Ids y títulos no se usan como etiquetas de métricas" (`docs/conventions.md:101`).
  - `ITemplateMetrics` se borró por código muerto; "las métricas vuelven conectadas en el paso 14" (`docs/decisions.tsv:15`).
  - Las pruebas de una slice cruzan HTTP con `WebApplicationFactory` y PostgreSQL real; una app por clase de test (`IClassFixture<ApiFixture>`) y las clases corren en paralelo en el mismo proceso (`docs/conventions.md`, sección Pruebas; `backend/Tests/Support/ApiFixture.cs`).
  - Un paquete NuGet nuevo lo elige Tobi (regla 5). Por eso la recomendación por defecto usa solo lo que trae el framework.
- Método: solo fuentes primarias. Son Microsoft Learn (.NET 10 y ASP.NET Core 10), el código de `dotnet/runtime` (`release/10.0`) y de `dotnet/extensions`, la documentación y el código de `opentelemetry-dotnet` y `opentelemetry-dotnet-contrib`, las especificaciones de OpenTelemetry (SDK de métricas, OTLP, convenciones semánticas), el `.proto` de métricas de OTLP, el README del `fileexporter` del collector y la documentación de Prometheus. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA].
- Evidencia propia:
  - "Experimento": una app .NET 10 de un solo archivo (`dotnet run m.cs`, SDK 10.0.401, runtime `Microsoft.AspNetCore.App` 10.0.12, el mismo que usa el repo) con dos hosts `WebApplication` sobre `TestServer` (`Microsoft.AspNetCore.TestHost` 10.0.12) en el mismo proceso, cada uno con OpenTelemetry 1.19.1 (la versión del repo) y el exporter en memoria. Corre 6 casos (E1 a E6), descriptos en la sección 8. Vive fuera del repo, en el scratchpad de la sesión.
  - "Export real": `artifacts/verify-template/fase1-03-ci/otel-export/metrics.json` del checkout principal. Es lo que el collector de este repo escribió en una corrida de `verify-template` anterior. Lo leí con Node para ver la forma exacta del JSON (sección 9).
  - No se tocó código del repo.

## Glosario corto

- **Log**: un registro por cada evento ("el usuario 42 abrió sesión a las 10:03"). Guarda el detalle de ese evento.
- **Métrica**: un número agregado en el tiempo ("hubo 120 logins exitosos en los últimos 5 minutos"). No guarda cada evento: los suma o los reparte en rangos.
- **Instrumento**: el objeto de .NET que recibe las mediciones: `Counter<T>`, `UpDownCounter<T>`, `Histogram<T>`, `Gauge<T>` y sus versiones `Observable*`.
- **`Meter`**: el grupo con nombre que crea los instrumentos. Su nombre es lo que OpenTelemetry habilita con `AddMeter`. En el JSON de OTLP aparece como **scope** (`scope.name`).
- **Etiqueta** (*tag* en .NET, *attribute* en OpenTelemetry, *label* en Prometheus): un par clave-valor que acompaña a una medición, por ejemplo `outcome=success`.
- **Serie temporal**: cada combinación distinta de nombre de métrica + valores de etiquetas. Cada serie ocupa memoria en la app y espacio en el backend.
- **Cardinalidad**: cuántas series distintas puede producir una métrica. Una etiqueta con 3 valores posibles da 3 series; una etiqueta con el id de usuario da tantas series como usuarios.
- **Histograma y *buckets***: un histograma reparte las mediciones en rangos (*buckets*, baldes) y cuenta cuántas caen en cada uno. Sirve para percentiles ("el 90 % de las tareas se completa en menos de 1 día").
- **`IMeterFactory`**: el servicio de DI que crea `Meter`s. Cada contenedor de DI tiene el suyo, y cada `Meter` que crea lleva a esa factory en su propiedad `Scope`.
- **`MeterListener`**: la API de .NET para escuchar mediciones dentro del mismo proceso. OpenTelemetry la usa por dentro; un test también puede usarla.
- **OTLP**: el protocolo de OpenTelemetry. El collector del repo lo recibe y lo escribe como JSON en `/otel-export/metrics.json`.
- **Temporalidad acumulativa** (*cumulative*): cada export manda el total desde que arrancó el proceso, no solo lo nuevo desde el export anterior.

## Conclusión

- **Qué se mide como métrica y qué queda en log.** Métrica: cuántas tareas se crean, cuánto tardan en completarse y cómo terminan los intentos de login. Son preguntas de "cuántos" y "cuánto tarda" sobre muchos eventos. Log: quién hizo qué (id de usuario, id de tarea), una cuenta bloqueada puntual, un perfil faltante. Son preguntas sobre un evento concreto y llevan datos de cardinalidad alta.
- **Tres instrumentos, dos Meters:**

| Meter | Instrumento | Tipo | Unidad | Etiquetas | Dónde se mide |
|---|---|---|---|---|---|
| `TemplateApp.Tasks` | `templateapp.tasks.created` | `Counter<long>` | `{task}` | ninguna | `CreateTask`, después de `SaveChangesAsync` |
| `TemplateApp.Tasks` | `templateapp.tasks.completion.duration` | `Histogram<double>` | `s` | ninguna | `UpdateTask`, después de `SaveChangesAsync`, solo si la tarea pasa de pendiente a completa |
| `TemplateApp.Auth` | `templateapp.auth.login.attempts` | `Counter<long>` | `{attempt}` | `templateapp.auth.login.outcome` = `success` \| `invalid_credentials` \| `locked_out` | `OpenSession`, en cada salida |

- **Regla de nombres:** el nombre del instrumento empieza con el nombre del Meter en minúsculas (`TemplateApp.Tasks` → `templateapp.tasks.`). Así cada nombre es único por sí solo, como pide Microsoft, y la regla es fácil de chequear [INFERENCIA]. Sin `_total`, unidades UCUM.
- **Buckets del histograma:** por *advice* en el código (`InstrumentAdvice<double>`, viene con .NET 9+), en segundos: `[60, 300, 900, 3600, 14400, 86400, 259200, 604800, 2592000]` (1 min, 5 min, 15 min, 1 h, 4 h, 1 día, 3 días, 7 días, 30 días). Los buckets por defecto de OpenTelemetry terminan en 10.000 y meterían en el último balde a toda tarea de más de 2,8 horas.
- **Clases de métricas:** `TaskMetrics` y `AuthMetrics`, `sealed`, registradas como **singleton** en `AddTasksFeature` y `AddAuthFeature`, que reciben `IMeterFactory` en el constructor. Sin `static Meter` y sin `Dispose`: la factory libera los Meters cuando se dispone el contenedor (Learn y E4, E6).
- **Etiqueta como enum cerrado:** `AuthMetrics.LoginAttempted(LoginOutcome outcome)` recibe un `enum` de la feature y lo traduce con un `switch` a constantes `string`. El llamador no puede pasar un texto libre, así que la cardinalidad máxima es 3. El límite por defecto de OpenTelemetry (2000 series por métrica) queda lejos y no hace falta configurarlo.
- **Cuándo se mide:** después del `SaveChangesAsync` que confirma el hecho. Un replay de idempotencia de `POST /tasks` no llega a la slice (`IdempotencyFilter.cs:53-54`), así que no cuenta. Un replay de `PUT` (misma versión vieja, mismo estado) y un conflicto 409 salen antes del guardado (`UpdateTask.cs:40-42`, `:54-57`), así que tampoco cuentan.
- **Setup OTel:** una línea en `ObservabilityExtensions.cs`: `.AddMeter("TemplateApp.*")` dentro de `WithMetrics`. opentelemetry-dotnet acepta comodines (docs y código).
- **Patrón de test:** un helper en `backend/Tests/Support/` que crea un `MeterListener` y habilita solo los instrumentos cuyo `Meter.Scope` es el `IMeterFactory` del host del test (y cuyo Meter e instrumento tienen el nombre buscado). Es exactamente el filtro de `MetricCollector<T>` de Microsoft, sin el paquete. E2 y E3 lo prueban con dos hosts en paralelo: sin el filtro de scope, el listener ve las mediciones del otro host.
- **No asertar sobre el exporter de OpenTelemetry en tests:** E5 muestra que el `MeterProvider` de un host agrega también las mediciones del otro host, porque OpenTelemetry filtra por nombre de Meter y no por scope. En tests paralelos, la única fuente confiable es el listener filtrado por scope.
- **Smoke con Compose:** buscar en `metrics.json` un `scopeMetrics` con `scope.name === 'TemplateApp.Tasks'`, la métrica `templateapp.tasks.created` con `sum.isMonotonic` y un `dataPoints[].asInt` ≥ 1 (es un string: hay que pasarlo por `Number`), el histograma con `histogram.dataPoints[].count` ≥ 1, y el login con el atributo `templateapp.auth.login.outcome = success`.
- **Paquetes:** ninguno. `System.Diagnostics.Metrics` (`Meter`, `IMeterFactory`, `MeterListener`, `InstrumentAdvice`) viene con .NET 10, y OpenTelemetry ya está en el repo. `Microsoft.Extensions.Diagnostics.Testing` (`MetricCollector<T>`, MIT) es opcional; si Tobi lo quiere, es decisión suya por regla 5 (sección 8).
- **Hallazgo:** OpenTelemetry ya exporta métricas de Identity (`aspnetcore.identity.user.create.duration`, `aspnetcore.identity.user.check_password_attempts`) porque `AddAspNetCoreInstrumentation()` habilita el Meter `Microsoft.AspNetCore.Identity`. Por eso no propongo un contador propio de registros: duplicaría `aspnetcore.identity.user.create.duration`, cuyo `count` ya es la cantidad de altas con su resultado.

## 1. Métricas contra logs

**Respuesta:** métrica para contar y medir muchos eventos con pocas dimensiones; log para guardar el detalle de un evento.

**Qué dicen las fuentes**

- Microsoft: los logs "record individual operations, such as an incoming request, a failure in a specific component, or an order being placed"; las métricas son "measuring counters and gauges such as number of completed requests, active requests, widgets that have been sold; or a histogram of the request latency" ([Learn, .NET Observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel#what-is-observability)).
- OpenTelemetry, sobre el costo: "Unlike logs, this cost scales with the number of distinct attribute combinations rather than with request volume" ([OTel, Metrics, Cardinality limits](https://opentelemetry.io/docs/concepts/signals/metrics/)). Es decir: un log cuesta por cada evento; una métrica cuesta por cada combinación de etiquetas, sin importar cuántos eventos haya.
- OpenTelemetry, sobre la agregación: "An aggregation is a technique whereby a large number of measurements are combined into either exact or estimated statistics" ([misma página](https://opentelemetry.io/docs/concepts/signals/metrics/)).
- Microsoft, sobre el límite de las métricas: "If you anticipate a large number of unique tag combinations, then logs, transactional databases, or big data processing systems may be more appropriate" ([Learn, Creating Metrics, Multi-dimensional metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#multi-dimensional-metrics)).
- Prometheus las ve como complementarias: "As a general rule, for every line of logging code you should also have a counter that is incremented" ([Prometheus, Instrumentation, Logging](https://prometheus.io/docs/practices/instrumentation/#logging)).
- El costo de grabar es bajo: `Add` y `Record` "typically run in <10 ns when no data is being collected, or tens to hundreds of nanoseconds" con un colector ([Learn, Creating Metrics, Best practices](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#best-practices)).

| | Métrica | Log |
|---|---|---|
| Qué guarda | Un número agregado por ventana de tiempo | Cada evento con su detalle |
| Costo | Crece con las combinaciones de etiquetas | Crece con la cantidad de eventos |
| Sirve para | Tableros, alertas, tendencias ("¿cuántos?", "¿cuánto tarda?") | Investigar un caso ("¿qué le pasó a este usuario?") |
| Datos de cardinalidad alta (ids, nombres) | Prohibidos | Permitidos (salvo secretos, `conventions.md:101`) |

**Qué conviene medir en este dominio** [INFERENCIA, aplicando las fuentes de arriba al código]

| Hecho | ¿Métrica? | Por qué |
|---|---|---|
| Tarea creada | Sí, contador | Pregunta de negocio directa: ¿se usa la app? Pocas dimensiones. |
| Tarea completada y cuánto tardó desde su creación | Sí, histograma | Pregunta de negocio: ¿las tareas se cierran? ¿en horas o en semanas? El `count` del histograma ya es la cantidad de tareas completadas: no hace falta otro contador. |
| Intento de login y cómo terminó | Sí, contador con `outcome` | Una suba de `invalid_credentials` o `locked_out` es una alerta de seguridad (fuerza bruta). |
| Registro de usuario | No: ya existe | `aspnetcore.identity.user.create.duration` ya cuenta altas con `aspnetcore.identity.result` (Export real; [Learn, ASP.NET Core Identity metrics](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/metrics?view=aspnetcore-10.0#aspnet-core-identity-metrics)). |
| Tarea borrada, título editado, logout | No, por ahora | Ninguna pregunta de negocio del plan las pide. Agregar instrumentos sin pregunta es ruido. |
| Qué usuario falló el login, qué tarea se completó | Log | Ids: cardinalidad sin límite. Prohibido como etiqueta (`conventions.md:101`). |
| Cuenta bloqueada puntual, perfil faltante (`AuthErrors.ProfileMissing`) | Log | Es un caso a investigar, no una tendencia. El perfil faltante es una inconsistencia de datos. |
| Conflicto 409, replay de idempotencia | Ya está en `http.server.request.duration` | Esa métrica lleva `http.response.status_code` y `http.route` (Export real). |

## 2. `IMeterFactory` contra `static Meter`

**Respuesta:** en una app con DI, `IMeterFactory` y una clase de métricas singleton. Es lo que Microsoft recomienda y lo que hace posible el test en paralelo.

- "For code that is not designed for use in a Dependency Injection (DI) container, create the Meter once and store it in a static variable. For usage in DI-aware libraries static variables are considered an anti-pattern" ([Learn, Creating Metrics, Best practices](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#best-practices)).
- "In code that uses DI, such as ASP.NET Core or apps with Generic Host, create the Meter object using IMeterFactory." Y el porqué: "The meter factory integrates metrics with DI, keeping Meters in different service collections isolated from each other even if they use an identical name. This is especially useful for testing so that multiple tests running in parallel only observe measurements produced from within the same test case" ([Learn, Get a Meter via dependency injection](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#get-a-meter-via-dependency-injection)).
- El patrón oficial es una clase que recibe `IMeterFactory`, crea el Meter y los instrumentos en el constructor y expone métodos con nombre de negocio (`HatsSold(int quantity)`). Se registra con `builder.Services.AddSingleton<HatCoMetrics>()` ([misma sección](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#get-a-meter-via-dependency-injection); la versión ASP.NET Core con `ContosoMetrics` está en [Learn, ASP.NET Core metrics, Creating metrics with IMeterFactory](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/metrics?view=aspnetcore-10.0#creating-metrics-in-aspnet-core-apps-with-imeterfactory)).
- "ASP.NET Core registers IMeterFactory in dependency injection (DI) by default" ([Learn, ASP.NET Core metrics](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/metrics?view=aspnetcore-10.0#creating-metrics-in-aspnet-core-apps-with-imeterfactory)). El `services.AddMetrics()` de `ObservabilityExtensions.cs:38` registra lo mismo de forma explícita; no molesta.
- Ciclo de vida: "IMeterFactory automatically manages the lifetime of any Meter objects it creates, disposing them when the DI container is disposed. It's unnecessary to add extra code to invoke Dispose() on the Meter, and it won't have any effect" ([Learn, Best practices](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#best-practices-1)).
- El código lo confirma ([`DefaultMeterFactory.cs`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/Microsoft.Extensions.Diagnostics/src/Metrics/DefaultMeterFactory.cs)):
  - `Create` guarda los Meters en un diccionario por nombre y devuelve el mismo si coinciden nombre, versión y tags (líneas 37-45). Dos clases que piden `"TemplateApp.Tasks"` a la misma factory comparten el Meter (E4).
  - Antes de construir el Meter pone `options.Scope = this` (líneas 53-56). Ese `Scope` es lo que usa el test (sección 8).
  - `FactoryMeter.Dispose(bool)` es un no-op (líneas 95-98): un `Dispose()` manual no hace nada (E4b). `DefaultMeterFactory.Dispose()` libera todos (líneas 63-84; E6: al disponer el host, el listener recibió `MeasurementsCompleted` de sus 3 instrumentos).
- opentelemetry-dotnet dice lo mismo en otras palabras: el Meter es "fairly expensive and meant to be reused throughout the application", como campo estático o como singleton de DI ([opentelemetry-dotnet, docs/metrics, Meter](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md#meter)).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. `TaskMetrics`/`AuthMetrics` singleton con `IMeterFactory` (recomendada) | Lo que recomienda Microsoft para DI. Cada host de test tiene sus Meters, así que el test filtra por scope y corre en paralelo. Sin `Dispose` manual. | Una clase y un registro por feature. |
| B. `static readonly Meter` en cada feature | Menos código. | Microsoft lo llama *anti-pattern* con DI. Todos los hosts del proceso comparten el Meter: el test no puede separar sus mediciones de las de otra clase de test en paralelo ([Learn, Test without dependency injection](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#test-without-dependency-injection): "make sure such tests are configured not to run in parallel"). |
| C. Inyectar `IMeterFactory` en cada slice y crear el instrumento ahí | Nada en limpio. | Cada request crea o busca instrumentos; los nombres quedan repartidos por las slices. |

Forma sugerida [INFERENCIA, siguiendo el ejemplo oficial y las convenciones del repo]:

```csharp
public sealed class TaskMetrics
{
    public const string MeterName = "TemplateApp.Tasks";
    private readonly Counter<long> _created;
    private readonly Histogram<double> _completionDuration;

    public TaskMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("templateapp.tasks.created", "{task}", "Tasks created.");
        _completionDuration = meter.CreateHistogram<double>(
            "templateapp.tasks.completion.duration", "s", "Time from creation to completion.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [60, 300, 900, 3600, 14400, 86400, 259200, 604800, 2592000] });
    }

    public void TaskCreated() => _created.Add(1);
    public void TaskCompleted(TimeSpan sinceCreation) => _completionDuration.Record(sinceCreation.TotalSeconds);
}
```

Las slices son scoped (`TasksFeature.cs`, `AuthFeature.cs:29-31`) y reciben el singleton por constructor, igual que reciben `TimeProvider`.

## 3. Nombres y unidades

**Respuesta:** Meter en `UpperCamelCase` con puntos (`TemplateApp.Tasks`); instrumento en minúsculas con puntos y `snake_case` dentro de cada parte, con el nombre del Meter como prefijo; sin `_total`; unidad UCUM.

- Nombre del Meter: "We recommend OpenTelemetry naming guidelines, which use dotted hierarchical names. Assembly names or namespace names for code being instrumented are usually a good choice" ([Learn, Creating Metrics, Best practices](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#best-practices)). opentelemetry-dotnet pide "dot-separated UpperCamelCase" para `Meter.Name` ([docs/metrics, Meter](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md#meter)). `TemplateApp.Tasks` y `TemplateApp.Auth` cumplen las dos.
- Nombre del instrumento: Microsoft recomienda "lowercase dotted hierarchical names and an underscore ('_') as the separator between multiple words in the same element", y agrega: "Not all metric tools preserve the Meter name as part of the final metric name, so it's beneficial to make the instrument name globally unique on its own" ([Learn, Best practices](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#best-practices)). OpenTelemetry: "Names SHOULD be lowercase", "Delimit the namespaces using a dot character" y "use snake_case" dentro de cada parte ([OTel semconv, Naming](https://opentelemetry.io/docs/specs/semconv/general/naming/)).
- Prefijo propio: para nombres de una aplicación, "prefix the attribute name by your application name"; los nombres que empiezan con `otel.` están reservados ([OTel semconv, Naming](https://opentelemetry.io/docs/specs/semconv/general/naming/)). De ahí `templateapp.`.
- `_total`: "Counters SHOULD NOT append `_total` either because then their meaning will be confusing in delta backends" ([OTel semconv, Naming](https://opentelemetry.io/docs/specs/semconv/general/naming/)). Prometheus sí usa `_total` en sus nombres ([Prometheus, Metric and label naming](https://prometheus.io/docs/practices/naming/)), pero eso lo agrega el exporter de Prometheus al traducir; en OTLP no va [INFERENCIA: el repo exporta OTLP, no Prometheus].
- Plural: "Metric names SHOULD NOT be pluralized, unless the value being recorded represents discrete instances of a countable quantity" ([OTel semconv, Naming](https://opentelemetry.io/docs/specs/semconv/general/naming/)). Tareas e intentos son cosas contables: `tasks`, `attempts` en plural. Los nombres del framework siguen esa regla: `dotnet.gc.collections`, `aspnetcore.identity.user.check_password_attempts` (Export real).
- Unidades: "Units should follow the Unified Code for Units of Measure"; las anotaciones entre llaves como `{request}` son para conteos y van en singular ([OTel semconv, Metrics, Instrument units](https://opentelemetry.io/docs/specs/semconv/general/metrics/#instrument-units)). Microsoft: "The curly braces around "{hat}" is part of the UCUM standard, indicating that it is a descriptive annotation rather than a unit name" y "When recording measurements of time, prefer units of seconds recorded as a floating point or double value" ([Learn, Descriptions and units](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#descriptions-and-units)). La unidad no va en el nombre: "SHOULD NOT include the units in the metric name" ([OTel semconv, Metrics](https://opentelemetry.io/docs/specs/semconv/general/metrics/)).
- Duraciones: el framework usa el sufijo `.duration` en segundos (`http.server.request.duration`, unidad `s`, Export real).

**Nombres propuestos**

| Meter (scope) | Instrumento | Unidad | Por qué este nombre |
|---|---|---|---|
| `TemplateApp.Tasks` | `templateapp.tasks.created` | `{task}` | Prefijo = Meter en minúsculas. Plural porque cuenta tareas. |
| `TemplateApp.Tasks` | `templateapp.tasks.completion.duration` | `s` | `.duration` como el framework; segundos en `double`. |
| `TemplateApp.Auth` | `templateapp.auth.login.attempts` | `{attempt}` | Mismo patrón que `check_password_attempts` de Identity. |
| — (etiqueta) | `templateapp.auth.login.outcome` | — | Atributo con prefijo de la app, como pide la guía de nombres. |

| Opción para el prefijo | Qué ganás | Qué perdés |
|---|---|---|
| A. Prefijo = Meter en minúsculas (`templateapp.tasks.*`) (recomendada) | Una regla mecánica: del nombre del instrumento sale el Meter y al revés. Un check de Dune puede verificarla. | `tasks` en plural en el prefijo, distinto del estilo de semconv (`aspnetcore.identity.user.*`, singular). |
| B. Namespace en singular (`templateapp.task.created`) | Más parecido a semconv. | El prefijo deja de coincidir con el Meter; la regla pasa a ser una lista. |
| C. Etiqueta corta `outcome` en vez de `templateapp.auth.login.outcome` | Más corta en tableros. | Contradice "prefix the attribute name by your application name"; si mañana semconv define `outcome`, choca. |

## 4. Qué instrumentos

**Respuesta:** dos `Counter<long>` y un `Histogram<double>`. Ningún `UpDownCounter` ni `Observable*`.

- Microsoft: "For counting things, or any other value that solely increases over time, use Counter or ObservableCounter"; "For timing things, Histogram is usually preferred. Often it's useful to understand the tail of these distributions (90th, 95th, 99th percentile) rather than averages or totals"; tamaños de colas o cachés van con `UpDownCounter` ([Learn, Best practices when selecting an instrument type](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#best-practices-when-selecting-an-instrument-type)).
- OpenTelemetry: el Counter "only ever goes up"; el UpDownCounter "can also go down again"; el Histogram es "A client-side aggregation of values" ([OTel, Metrics, Metric instruments](https://opentelemetry.io/docs/concepts/signals/metrics/)). Prometheus: "if the value can go down, it is a gauge" ([Prometheus, Instrumentation, Counter vs. gauge](https://prometheus.io/docs/practices/instrumentation/#counter-vs-gauge-summary-vs-histogram)).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| `Counter` para tareas creadas e intentos de login (recomendada) | Una llamada por evento, en el mismo lugar donde pasa. | Nada para este caso. |
| `UpDownCounter` "tareas pendientes" (+1 al crear, −1 al completar o borrar) | Muestra el stock actual de pendientes. | Arranca en 0 en cada proceso: después de un reinicio miente, porque las tareas viven en la base [INFERENCIA]. Con varias réplicas, cada una tiene su número. |
| `ObservableGauge` "tareas pendientes" leyendo la base | Número real. | El callback corre en el hilo del export y Microsoft pide no hacer operaciones largas ahí: "Favor quickly reading a cached value" ([Learn, Types of instruments](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#types-of-instruments)). Una consulta SQL cada 10 s está fuera del alcance del paso. |
| `Histogram` del tiempo de creación a completado (recomendada) | Percentiles reales del ciclo de vida de una tarea; su `count` ya cuenta las completadas. | Necesita buckets propios (abajo). |

**Buckets del histograma**

- Default de OpenTelemetry: `[ 0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000 ]` ([Learn, Using Advice to customize Histogram instruments](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#using-advice-to-customize-histogram-instruments)). En segundos, todo lo que dura más de 10.000 s (2,8 horas) cae en el último balde.
- `InstrumentAdvice<T>` llegó en `System.Diagnostics.DiagnosticSource` 9.0.0 y "is supported in the OpenTelemetry .NET SDK as of version 1.10.0" ([misma sección](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#using-advice-to-customize-histogram-instruments)). El repo usa .NET 10 y OpenTelemetry 1.19.1 (`TemplateApp.csproj`). E5 confirmó que el export usa los buckets del advice.
- Vista contra advice: "When both the View API and Advice API are used, the View API takes precedence" ([opentelemetry-dotnet, Customizing the SDK](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md)); la especificación dice lo mismo ([OTel SDK spec](https://opentelemetry.io/docs/specs/otel/metrics/sdk/)).
- Costo: "more buckets will lead to more precise data [...] but each bucket requires memory" ([Learn, Using Advice](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#using-advice-to-customize-histogram-instruments)).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Advice en `TaskMetrics` (recomendada) | Los buckets viven junto al instrumento, en la feature. Infrastructure no nombra la métrica. | Ninguna para este caso. |
| B. `AddView("templateapp.tasks.completion.duration", new ExplicitBucketHistogramConfiguration { ... })` en `ObservabilityExtensions` | La forma del ejemplo de ASP.NET Core ([Learn, ASP.NET Core metrics](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/metrics?view=aspnetcore-10.0#create-the-starter-app)). | Infrastructure pasa a nombrar una métrica de Tasks: choca con `informe-vsa-deep-modules.md:80`. |

Buckets propuestos [INFERENCIA, por el uso esperado de una app de tareas]: `[60, 300, 900, 3600, 14400, 86400, 259200, 604800, 2592000]` = 1 min, 5 min, 15 min, 1 h, 4 h, 1 día, 3 días, 7 días, 30 días. Diez baldes en total contando el de "más de 30 días".

## 5. Cardinalidad y etiquetas acotadas

**Respuesta:** una sola etiqueta en todo el paso, `templateapp.auth.login.outcome`, con 3 valores fijos que salen de un `enum`. Nunca ids, títulos, nombres de usuario ni emails.

**Qué dicen las fuentes**

- Prometheus: "Remember that every unique combination of key-value label pairs represents a new time series [...] Do not use labels to store dimensions with high cardinality (many different label values), such as user IDs, email addresses, or other unbounded sets of values" ([Prometheus, Metric and label naming, Labels](https://prometheus.io/docs/practices/naming/#labels)). Y "try to keep the cardinality of your metrics below 10" ([Prometheus, Instrumentation, Do not overuse labels](https://prometheus.io/docs/practices/instrumentation/#do-not-overuse-labels)).
- Microsoft: "Beware of having very large or unbounded combinations of tag values"; "likely less than 1000 combinations for one instrument is safe"; para histogramas "safe limits could be 10-100 times lower" ([Learn, Multi-dimensional metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#multi-dimensional-metrics)).
- Nombres de etiquetas: "If tag names are reused in different metrics or other telemetry records, then they should have the same meaning and set of legal values everywhere they are used" ([misma sección](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#multi-dimensional-metrics)).
- opentelemetry-dotnet: evitar "`TraceId` and `SpanId` as attributes in metrics" y mantener el mismo orden de tags en cada llamada ([docs/metrics, Metrics Enrichment / tags](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md)).

**El límite por defecto de opentelemetry-dotnet**

- "OpenTelemetry has a default cardinality limit of `2000` per metric." Al llegar, las combinaciones nuevas se suman en un punto sintético con `otel.metric.overflow=true`, y "Once an overflow exists, queries that filter on attributes (dimensions) become unreliable" ([opentelemetry-dotnet, docs/metrics, Cardinality Limits](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md#cardinality-limits)). Desde 1.10.0 eso es automático.
- La especificación fija el mismo número: si nada lo configura, "the default value of 2000 SHOULD be used", y el límite es "the maximum number of data points allowed to be emitted in a collection cycle by a single instrument" ([OTel SDK spec, Cardinality limits](https://opentelemetry.io/docs/specs/otel/metrics/sdk/#cardinality-limits)).
- Cómo se cambia: por métrica con una vista y `MetricStreamConfiguration.CardinalityLimit`. `SetMaxMetricPointsPerMetricStream` "is marked `Obsolete` in stable builds since 1.10.0" ([opentelemetry-dotnet, Customizing the SDK](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md)).
- Para este paso no hace falta cambiarlo: la cardinalidad máxima es 1 + 1 + 3 = 5 series. El límite es una red de seguridad; la protección real es el `enum` [INFERENCIA].

**Etiquetas permitidas y prohibidas**

| Dato | ¿Etiqueta? | Motivo |
|---|---|---|
| Resultado del login (`success`, `invalid_credentials`, `locked_out`) | Sí | Conjunto cerrado de 3 valores. |
| Id de usuario, nombre de usuario, email | Nunca | Prometheus los nombra como ejemplo de lo prohibido; uno es dato personal. |
| Id o título de tarea | Nunca | `conventions.md:101`. |
| `TraceId`, `SpanId` | Nunca | opentelemetry-dotnet lo prohíbe. |
| Ruta HTTP, código de estado | Ya están | Los pone `http.server.request.duration`. No se repiten en las métricas de negocio. |

**Cómo hacer que la etiqueta sea un enum cerrado en C#** [INFERENCIA, sobre el patrón de Microsoft de métodos con nombre de negocio]

```csharp
public enum LoginOutcome { Success, InvalidCredentials, LockedOut }

public sealed class AuthMetrics
{
    public const string MeterName = "TemplateApp.Auth";
    private readonly Counter<long> _loginAttempts;

    public AuthMetrics(IMeterFactory meterFactory) =>
        _loginAttempts = meterFactory.Create(MeterName)
            .CreateCounter<long>("templateapp.auth.login.attempts", "{attempt}", "Login attempts by outcome.");

    public void LoginAttempted(LoginOutcome outcome) =>
        _loginAttempts.Add(1, new KeyValuePair<string, object?>("templateapp.auth.login.outcome", TagOf(outcome)));

    private static string TagOf(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.Success => "success",
        LoginOutcome.InvalidCredentials => "invalid_credentials",
        LoginOutcome.LockedOut => "locked_out",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}
```

- El método público solo acepta el `enum`: ninguna slice puede pasar un `string` armado con datos del usuario.
- El `switch` devuelve constantes: no usa `ToString()`, así que renombrar un miembro del enum no cambia la serie en silencio.
- Una etiqueta pasada de forma individual no aloca: "The .NET API is optimized to be allocation-free for Add and Record calls with three or fewer tags specified individually" ([Learn, Multi-dimensional metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#multi-dimensional-metrics)).
- E2 lo mostró: con una entrada inválida (`"hacker-input"`) la etiqueta salió `invalid_credentials`, nunca el texto recibido.

## 6. Dónde se mide dentro de la slice

**Respuesta:** después del `SaveChangesAsync` que confirma el hecho de negocio, y solo en el camino que realmente cambió algo. Así se cuentan hechos, no intentos de escritura.

Microsoft pone la medición después de la lógica de negocio: `// ... business logic such as saving the sale to a database ...` y recién después `metrics.HatsSold(...)` ([Learn, Get a Meter via dependency injection](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#get-a-meter-via-dependency-injection)). El resto es [INFERENCIA] sobre el código del repo:

| Slice | Dónde | Qué pasa con los casos raros |
|---|---|---|
| `CreateTask` | Después de `await _db.SaveChangesAsync(ct)` (`CreateTask.cs:44`), antes del `return`. | Validación fallida o sin permiso: sale antes, no cuenta. **Replay de idempotencia:** `IdempotencyFilter` devuelve la respuesta guardada sin llamar al handler (`IdempotencyFilter.cs:53-54`), así que la slice no corre y no cuenta. Bien. |
| `UpdateTask` | Guardar `bool completes = !item.IsCompleted && request.IsCompleted` antes de mutar (`UpdateTask.cs:46`); después del `SaveChangesAsync` exitoso (`:52`), si `completes`, medir `now - item.CreatedAt`, con `now` del mismo `TimeProvider` que puso `UpdatedAt`. | **Replay** (versión vieja con el mismo estado): devuelve la entidad sin guardar (`:40-42`), no cuenta. **Conflicto** 409: sale por `:40-42` o por `DbUpdateConcurrencyException` (`:54-57`), no cuenta. Editar solo el título o reabrir (`true → false`) no cuenta. Completar, reabrir y completar de nuevo cuenta dos veces, cada una desde `CreatedAt`. |
| `OpenSession` | `LockedOut` en la salida de `IsLockedOutAsync` (`OpenSession.cs:42-43`); `InvalidCredentials` en las salidas de entrada inválida, usuario inexistente y contraseña incorrecta (después de `AccessFailedAsync`, `:48`); `Success` después del `SaveChangesAsync` de la sesión (`:59`). | La respuesta HTTP sigue siendo la misma para los tres fallos (`AuthErrors.InvalidCredentials`): la métrica distingue `locked_out` por dentro, sin dar pistas al cliente. `ProfileMissing` (`:56`) no se cuenta: es una inconsistencia de datos y va a un log de error. |

**Un borde que el paso tiene que aceptar o resolver.** Con `Idempotency-Key`, la slice guarda dentro de la transacción que abrió el filtro (`IdempotencyFilter.cs:44`), y el `CommitAsync` llega después (`:76`). Si el commit falla, o el cliente corta la conexión y `RequestAborted` lo cancela, la tarea no queda guardada pero el contador ya sumó 1 [INFERENCIA]. Opciones:

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Aceptarlo y anotarlo (recomendada) | Cero código extra. El error es de +1 en un caso muy raro, y una métrica es una tendencia, no un libro contable. | El contador puede quedar 1 arriba del número de filas. |
| B. Medir en el filtro, después del commit | Exacto. | Infrastructure tendría que nombrar a Tasks: prohibido (`informe-vsa-deep-modules.md:80`). |
| C. Un `IDbTransactionInterceptor` que mida en `TransactionCommitted` | Exacto y genérico. | Mucha maquinaria para un borde; fuera del alcance del paso. |

## 7. `AddMeter("TemplateApp.*")` en opentelemetry-dotnet

**Respuesta:** sí, funciona. Está documentado y en el código, y E5 lo confirmó con la versión del repo.

- Documentación: `AddMeter` "supports a wildcard subscription model", donde `*` es cero o más caracteres y `?` es uno; el ejemplo es `.AddMeter("AbcCompany.XyzProduct.*")` ([opentelemetry-dotnet, Customizing the SDK, Meter](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md)).
- Código: `MeterProviderSdk.CreateFilterPredicate` separa cada fuente en nombre exacto, prefijo (`WildcardHelper.TryGetWildcardPrefix`) o patrón, y arma un `Predicate<Instrument>` que compara `instrument.Meter.Name` ([`MeterProviderSdk.cs`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/Metrics/MeterProviderSdk.cs)). Ese predicado mira **solo el nombre**, no el `Scope`: de ahí sale el resultado de E5.
- E5: con `.AddMeter("TemplateApp.*")`, el export trajo `scope=TemplateApp.Tasks` y `scope=TemplateApp.Auth`.
- Cuidado: `TemplateApp.*` también habilita el Meter `TemplateApp.Api` si alguien lo crea. Hoy ese nombre solo aparece como `ActivitySource` de trazas (`ObservabilityExtensions.cs`, `.AddSource("Npgsql", "TemplateApp.Api")`), que es otra API [INFERENCIA].
- Dónde: en `WithMetrics(...)`, junto a las instrumentaciones (`ObservabilityExtensions.cs:52-58`).

## 8. Tests con `MeterListener` y varias `WebApplicationFactory` en paralelo

**Respuesta:** un `MeterListener` que habilita un instrumento solo si `instrument.Meter.Scope` es el `IMeterFactory` del host del test. Es el mismo filtro que usa `MetricCollector<T>`.

**El patrón oficial**

- Microsoft prueba métricas con `MetricCollector<T>`, construido con la `IMeterFactory` del host: "The MetricCollector<T> is created using the web app's IMeterFactory. This allows the collector to only report metrics values recorded by test" ([Learn, Test metrics in ASP.NET Core apps](https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/metrics?view=aspnetcore-10.0#test-metrics-in-aspnet-core-apps)). Necesita el paquete `Microsoft.Extensions.Diagnostics.Testing`.
- Por dentro, `MetricCollector<T>` es un `MeterListener` con este filtro: `Equals(instrument.Meter.Scope, meterScope) && instrument.Meter.Name == meterName && instrument.Name == instrumentName` ([`MetricCollector.cs`, dotnet/extensions](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Diagnostics.Testing/Metrics/MetricCollector.cs)).
- `MeterListener.Start()` llama a `InstrumentPublished` "for every pre-existing Instrument in the process", y los instrumentos nuevos también lo disparan ([Learn, Collect metrics, MeterListener](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-collection#create-a-custom-collection-tool-using-the-net-meterlistener-api)). No importa si `TaskMetrics` ya existía cuando arrancó el listener.
- Los callbacks corren en el hilo que graba la medición, así que el helper tiene que guardar en una colección segura entre hilos; y después de `Dispose` "there may still be callbacks in progress" ([misma página](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-collection#create-a-custom-collection-tool-using-the-net-meterlistener-api)).

**El experimento**

Dos hosts `WebApplication` con `TestServer` en el mismo proceso, cada uno con `AddSingleton<TaskMetrics>()` (con los tres instrumentos de la Conclusión) y OpenTelemetry con `.AddMeter("TemplateApp.*")` y el exporter en memoria. Se mandan en paralelo: 3 `POST /create` al host A y 5 al host B, un "completar" de 90 s en A y uno de 7200 s en B, y tres logins en A (`success`, `locked_out` y un texto cualquiera).

| Caso | Qué probé | Resultado |
|---|---|---|
| E1 | ¿Cada host tiene su `IMeterFactory`? | Sí: dos instancias distintas. |
| E2 | Listener filtrado por `ReferenceEquals(instrument.Meter.Scope, factoryA)` | Vio solo lo de A: 3 `templateapp.tasks.created`, la duración de 90 s y los 3 logins. El texto cualquiera salió como `invalid_credentials`. |
| E3 | Listener filtrado solo por nombre de Meter | Vio A=7 y B=6 mediciones: mezcla los dos hosts. Es lo que pasaría en el repo con clases de test en paralelo. |
| E4 | `factoryA.Create("TemplateApp.Tasks")` dos veces | El mismo objeto `Meter`, con `Scope == factoryA`. `Dispose()` sobre él no hace nada (E4b). |
| E5 | Qué exporta el `MeterProvider` de OpenTelemetry del host A | `templateapp.tasks.created` sum=**8** y el histograma count=**2**: sumó también lo de B. Los buckets fueron los del advice. El login salió en 3 series, una por `outcome`. |
| E6 | Disponer el host A | El listener recibió `MeasurementsCompleted` de los 3 instrumentos de A: la factory liberó sus Meters. |

E5 es el hallazgo práctico: en tests paralelos no se puede asertar sobre lo que exporta OpenTelemetry, porque su filtro es por nombre (sección 7). El listener filtrado por scope sí aísla.

**Helper propuesto** [INFERENCIA: el filtro es el de `MetricCollector<T>`; la forma sigue `backend/Tests/Support/`]

```csharp
internal sealed class RecordedMeasurements<T> : IDisposable where T : struct
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(T Value, Dictionary<string, object?> Tags)> _values = new();

    public RecordedMeasurements(IServiceProvider services, string meterName, string instrumentName)
    {
        object scope = services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, scope)
                && instrument.Meter.Name == meterName
                && instrument.Name == instrumentName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<T>((_, value, tags, _) =>
            _values.Enqueue((value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _listener.Start();
    }

    public IReadOnlyList<(T Value, Dictionary<string, object?> Tags)> Snapshot() => _values.ToArray();
    public void Dispose() => _listener.Dispose();
}
```

**Cómo se usa en las pruebas de cada slice** [INFERENCIA]

- `CreateTaskTests`: crear una tarea suma una medición de 1 en `templateapp.tasks.created`; repetir la misma `Idempotency-Key` no suma otra.
- `UpdateTaskTests`: con `FakeTimeProvider` (ya está en el repo, `ListTasksTests.cs:63-64`) se crea la tarea, se avanza el reloj 90 minutos y se completa: el histograma recibe exactamente 5400. El replay y el 409 no graban nada. Editar solo el título no graba nada.
- `OpenSessionTests`: contraseña mala → `invalid_credentials`; cuenta bloqueada → `locked_out`; login bueno → `success`.
- El listener se crea con el `IServiceProvider` del host del test (`_fixture.Services` o el `out services` de `CreateIsolatedClient`). Dentro de una clase, xUnit corre los tests uno por vez, así que un listener creado al empezar el test ve solo ese test.
- La medición se graba dentro de la slice, antes de que el endpoint arme la respuesta. Cuando `await client.PostAsync(...)` vuelve, ya está grabada y el test puede leer el snapshot sin esperar [INFERENCIA, por el orden del código]. El ejemplo de Microsoft sí espera con `WaitForMeasurementsAsync`, pero porque mide `http.server.request.duration`, que se graba al terminar la request.

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Helper propio con `MeterListener` (recomendada) | Sin paquete nuevo. Unas 25 líneas en `Tests/Support/`. Enseña cómo funciona la escucha. | Código propio que mantener. |
| B. `MetricCollector<T>` de `Microsoft.Extensions.Diagnostics.Testing` | El patrón exacto de la documentación; trae `WaitForMeasurementsAsync` y `GetMeasurementSnapshot`. Licencia MIT ([NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Testing)). | Paquete nuevo: lo decide Tobi (regla 5). La última estable (11.0.0) pide `Microsoft.Extensions.Logging >= 10.0.15`, más nuevo que el 10.0.12 del repo; habría que elegir una 10.x o aceptar la suba. |
| C. Asertar sobre el exporter en memoria de OpenTelemetry | Prueba el pipeline entero. | Paquete nuevo y, por E5, mezcla los hosts paralelos. |

## 9. Cómo se ve la métrica en el JSON que escribe el collector

**Respuesta:** cada línea de `metrics.json` es un objeto `{ resourceMetrics: [...] }`. Adentro, `scopeMetrics[].scope.name` es el nombre del Meter, `metrics[].name` el del instrumento, un contador trae `sum` y un histograma trae `histogram`. Los enteros de 64 bits llegan como strings.

- El `fileexporter` del collector con `format: json` escribe "Newline-delimited OTLP JSON, one line per batch" ([fileexporter README](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/exporter/fileexporter/README.md)). El repo lo usa con `append: true` (`deploy/otel-collector-config.yaml`): el archivo acumula todas las corridas.
- Reglas de OTLP/JSON: "The keys of JSON objects are field names converted to lowerCamelCase"; "Values of enum fields MUST be encoded as integer values"; los enteros de 64 bits "are encoded as decimal strings" ([OTLP spec, JSON Protobuf Encoding](https://opentelemetry.io/docs/specs/otlp/#json-protobuf-encoding)).
- Los campos salen de [`metrics.proto`](https://github.com/open-telemetry/opentelemetry-proto/blob/main/opentelemetry/proto/metrics/v1/metrics.proto): `Sum` tiene `data_points`, `aggregation_temporality` e `is_monotonic`; `NumberDataPoint` tiene `as_int` (`sfixed64`) o `as_double`; `HistogramDataPoint` tiene `count` (`fixed64`), `sum`, `bucket_counts`, `explicit_bounds`, `min`, `max`. `AggregationTemporality`: 1 = delta, 2 = acumulativa.

Forma real (Export real, recortada):

```json
{"resourceMetrics":[{"resource":{...},"scopeMetrics":[
  {"scope":{"name":"Microsoft.AspNetCore.MemoryPool"},
   "metrics":[{"name":"aspnetcore.memory_pool.allocated","unit":"By",
     "sum":{"dataPoints":[{"attributes":[{"key":"aspnetcore.memory_pool.owner","value":{"stringValue":"kestrel"}}],
                           "startTimeUnixNano":"1792292250271199100","timeUnixNano":"1792292254629411200","asInt":"16384"}],
            "aggregationTemporality":2,"isMonotonic":true}}]},
  {"scope":{"name":"Microsoft.AspNetCore.Hosting"},
   "metrics":[{"name":"http.server.request.duration","unit":"s",
     "histogram":{"dataPoints":[{"attributes":[...],"count":"1","sum":0.1231155,
        "bucketCounts":["0","0","0","0","0","0","1","0","0","0","0","0","0","0","0"],
        "explicitBounds":[0.005,0.01,0.025,0.05,0.075,0.1,0.25,0.5,0.75,1,2.5,5,7.5,10],
        "min":0.1231155,"max":0.1231155}],"aggregationTemporality":2}}]}
]}]}
```

Qué tiene que buscar `smoke-api.mjs` [INFERENCIA, sobre esa forma y sobre el script actual, `smoke-api.mjs:285-315`]:

```js
const scopes = metrics.flatMap(resource => resource.scopeMetrics ?? []);
const of = (scopeName, metricName) => scopes
  .filter(scope => scope.scope?.name === scopeName)
  .flatMap(scope => scope.metrics ?? [])
  .filter(metric => metric.name === metricName);
const points = (list, kind) => list.flatMap(metric => metric[kind]?.dataPoints ?? []);

assert.ok(of('TemplateApp.Tasks', 'templateapp.tasks.created').some(m => m.sum?.isMonotonic)
  && points(of('TemplateApp.Tasks', 'templateapp.tasks.created'), 'sum').some(p => Number(p.asInt) >= 1));
assert.ok(points(of('TemplateApp.Tasks', 'templateapp.tasks.completion.duration'), 'histogram').some(p => Number(p.count) >= 1));
assert.ok(points(of('TemplateApp.Auth', 'templateapp.auth.login.attempts'), 'sum')
  .some(p => p.attributes?.some(a => a.key === 'templateapp.auth.login.outcome' && a.value?.stringValue === 'success')));
```

- `>= 1` y no un número exacto: el export es acumulativo (`aggregationTemporality: 2`), el archivo junta corridas (`append: true`) y `--compose` recrea el contenedor de la API (`smoke-api.mjs:267`). El smoke actual crea 5 tareas (más un replay que no cuenta), completa una y abre varias sesiones, así que las tres series existen.
- Un contador con etiquetas aparece en el export recién después de su primera medición. Por eso el chequeo va al final del smoke, donde ya está el bucle de 30 s (`smoke-api.mjs:286`).
- Tiempos: export cada 10 s (`ObservabilityExtensions.cs:58`), batch del collector hasta 10 s y flush del archivo cada 5 s (`deploy/otel-collector-config.yaml`). Suman hasta unos 25 s, dentro de los 30 s del bucle. Es el mismo margen con el que hoy pasa el chequeo de `http.server.request.duration` [INFERENCIA].

## Cosas que el paso tiene que decidir y que este informe deja abiertas

- **Dune (regla 4):** una regla ejecutable mínima podría ser prohibir `new Meter(...)` en `backend/` con `dune/BannedSymbols.txt` (obliga a pasar por `IMeterFactory`), con fixture válida y violación deliberada. Chequear que el nombre del instrumento empiece con el Meter en minúsculas es posible pero más caro. [INFERENCIA]
- **Paquete de test:** opción A (helper propio, sin paquete) por defecto; B solo si Tobi elige el paquete.
- **Contrato HTTP:** el paso no lo cambia (las respuestas son las mismas), así que no toca el frontend. Las e2e quedan `NOT_RUN` según `AGENTS.md`, sección Verificación [INFERENCIA].

## Verificación y límites

- **Leído:** Microsoft Learn: "Creating Metrics" (.NET), "Collect metrics" (.NET), ".NET Observability with OpenTelemetry" y "ASP.NET Core metrics" (vista 10.0). Código: `DefaultMeterFactory.cs` en `dotnet/runtime` `release/10.0`; `MetricCollector.cs` en `dotnet/extensions` `main`; `MeterProviderSdk.cs` en `opentelemetry-dotnet` `main`; `AspNetCoreInstrumentationMeterProviderBuilderExtensions.cs` en `opentelemetry-dotnet-contrib` `main`. Documentación de opentelemetry-dotnet (`docs/metrics/README.md`, `customizing-the-sdk/README.md`). Especificaciones de OpenTelemetry: SDK de métricas, OTLP, convenciones de nombres y de métricas, concepto de métricas. `metrics.proto`. README del `fileexporter`. Prometheus: naming e instrumentation. Página de NuGet de `Microsoft.Extensions.Diagnostics.Testing`.
- **Comprobado con ejecución:** E1 a E6 (filtro por scope, mezcla entre hosts en el export de OpenTelemetry, caché y `Dispose` de la factory, comodín de `AddMeter`, buckets por advice, etiqueta desde enum) con SDK 10.0.401, runtime 10.0.12 y OpenTelemetry 1.19.1. La forma del JSON, con un `metrics.json` real que escribió el collector de este repo.
- **No comprobado:**
  - El código de opentelemetry-dotnet y de opentelemetry-dotnet-contrib lo leí en `main`, no en la etiqueta `1.19.x`. E5 y el Export real coinciden con lo leído, pero no comparé línea por línea con 1.19.
  - El Export real es de una corrida vieja (`fase1-03-ci`), con un contador del framework y no con uno propio. Las métricas nuevas no pasaron por el collector real: E5 usó el exporter en memoria. La forma JSON de una métrica propia debería ser la misma, porque OTLP no distingue métricas del framework de las propias [INFERENCIA].
  - No corrí nada contra PostgreSQL ni contra el repo: el orden "medición antes de que vuelva el `PostAsync`" y el borde del commit de idempotencia salen de leer el código.
  - No miré dotnet/eShop: las fuentes de Microsoft y OpenTelemetry alcanzaron para cada pregunta.
  - No verifiqué si `Microsoft.Extensions.Diagnostics.Testing` ya llega de forma transitiva al proyecto de tests (no hay `obj/` restaurado en este worktree).
