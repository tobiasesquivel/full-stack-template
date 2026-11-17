# Paso 15: correlación log↔traza automatizada (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 15 del [plan](../plan.md) (`plan.md:338-341`). Concepto: verificar las señales de telemetría en CI, no a ojo. Cambia: scripts de `verify-template`, el job `compose` de `ci.yml` y `artifacts/`. Verifica: el job falla si un log de request no lleva trace/span o si el collector recibe métricas vacías. Hoy el único control es `smoke-api.mjs:285-327`: espera hasta 30 s mientras el stack corre y pide que **algún** log tenga un `traceId` presente en los spans (`smoke-api.mjs:301-302`), más nombres de métricas. Un solo log correlacionado alcanza para pasar.
- Decisiones que ya están tomadas y que este informe no reabre:
  - Serilog (Serilog.AspNetCore 10.0.0, Serilog.Sinks.OpenTelemetry 4.2.0) exporta los logs por OTLP; OpenTelemetry .NET 1.19.x exporta trazas y métricas (`ObservabilityExtensions.cs:32-59`, `TemplateApp.csproj:16-22`).
  - El collector es `otel/opentelemetry-collector-contrib:0.162.0` y escribe `logs.json`, `metrics.json` y `traces.json` con el `file` exporter (`compose.yaml:20-29`, `deploy/otel-collector-config.yaml:17-34`).
  - El job `compose` levanta el stack, corre `smoke-api.mjs --compose`, hace `docker compose down --volumes` y sube `otel-export/` como artifact (`ci.yml:132-174`).
  - Una herramienta nueva la elige Tobi (regla 5). Por eso la recomendación usa solo Node, que el repo ya usa para sus gates (`dune/check-*.mjs`).
  - Este informe no propone tocar el backend: el paso cambia scripts y CI.
- Método: solo fuentes primarias. Son el código de Serilog (`serilog` v4.3.0, `serilog-extensions-logging` v10.0.0, `serilog-extensions-hosting` v10.0.0, `serilog-aspnetcore` v10.0.0, `serilog-sinks-opentelemetry` v4.2.0), el código de `dotnet/aspnetcore` y `dotnet/runtime` (`release/10.0`), el código de `opentelemetry-dotnet` (`core-1.19.1`) y `opentelemetry-dotnet-contrib` (`Instrumentation.AspNetCore-1.19.0`), la especificación OTLP y los `.proto` de `opentelemetry-proto` v1.11.1, la especificación de OpenTelemetry v1.62.0, el código del collector (`opentelemetry-collector` y `opentelemetry-collector-contrib` v0.162.0), Microsoft Learn, la documentación de Docker y de protobuf. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA].
- Evidencia propia:
  - "Export real del paso 14": `artifacts/verify-template/paso14-compose/otel-export/` del worktree del paso 14 (`logs.json`, `traces.json`, `metrics.json`). Lo leí con un script de Node fuera del repo (scratchpad de la sesión). Los números de las secciones 1, 4, 5 y 8 salen de ahí.
  - No corrí la API ni el stack. No se tocó código del repo.

## Glosario corto

- **Traza**: el recorrido completo de un pedido. Tiene un `traceId` de 16 bytes.
- **Span**: un tramo de la traza (el pedido HTTP entero, una consulta SQL). Tiene un `spanId` de 8 bytes. El span del pedido HTTP es de tipo **SERVER** (`kind: 2` en el JSON).
- **`Activity`**: el objeto de .NET que representa un span. `Activity.Current` es el span en curso del hilo lógico.
- **Grabado** (*recorded*, *sampled*): un span que OpenTelemetry va a exportar. Un `Activity` puede existir, con ids, y no estar grabado.
- **Log de request**: un log que se escribió mientras ASP.NET Core atendía un pedido HTTP. Lo reconocemos por el atributo `RequestId` (sección 2).
- **Correlación log↔traza**: que el log lleve `traceId` y `spanId` y que esos ids apunten a un span que de verdad se exportó.
- **Referencia colgada**: un log con `traceId`/`spanId` válidos que no corresponden a ningún span exportado. El log "parece" correlacionado, pero en el backend no se puede navegar a la traza.
- **Scope de log**: un grupo de propiedades que MEL (`Microsoft.Extensions.Logging`) agrega a todos los logs escritos mientras el scope está abierto (`ILogger.BeginScope`).
- **LogContext**: el equivalente de Serilog: una pila de enriquecedores en un `AsyncLocal` que `Enrich.FromLogContext()` aplica a cada evento.
- **Punto de dato** (*data point*): cada valor de una métrica para una combinación de etiquetas. Una métrica sin puntos no dice nada.

## Conclusión

- **Qué es un log de request.** ASP.NET Core abre un scope con `RequestId` y `RequestPath` al empezar cada pedido, y Kestrel abre otro con `ConnectionId` por conexión. Serilog convierte esos scopes en propiedades y el sink OTLP las exporta como atributos. El criterio más sólido es **el atributo `RequestId`**: sale del pedido, no de la conexión, y no depende de la traza.
- **No elegir los logs de request por su `traceId`.** Sería circular: un log que perdió su `traceId` dejaría de contar como log de request y el control nunca fallaría por la falla que tiene que detectar.
- **De dónde salen los ids del LogRecord.** Serilog 4 guarda `TraceId` y `SpanId` en el `LogEvent` a partir de `Activity.Current`. El sink los copia a los campos `traceId`/`spanId` del LogRecord con `IncludedData.TraceIdField | SpanIdField` (`ObservabilityExtensions.cs:82-85`). El `ActivityTraceEnricher` del repo no participa en esos campos: solo agrega atributos `TraceId`/`SpanId` duplicados. Es redundante para OTLP (no se propone quitarlo en este paso).
- **El log de cierre de `UseSerilogRequestLogging` cae dentro del span SERVER.** El middleware escribe el log al terminar el pipeline, y ASP.NET Core detiene el `Activity` del pedido recién después. En el export real, los 63 logs con `RequestId` apuntan a un span SERVER con el mismo `traceId`, y los 60 spans SERVER tienen al menos un log.
- **Un log con `traceId` no prueba nada por sí solo.** ASP.NET Core crea el `Activity` del pedido aunque OpenTelemetry no lo grabe, porque el logging está prendido. Con un `Filter` que devuelve `false`, con un sampler que descarta, o sin la instrumentación de ASP.NET Core, los logs siguen llevando ids válidos, pero no hay span exportado. El control tiene que **cruzar** el log con el span exportado.
- **Formato del export.** Cada línea del archivo es un objeto JSON (`{"resourceLogs": [...]}` y equivalentes). `traceId` y `spanId` van en hex (no base64); el `kind` va como entero (`2` = SERVER); los enteros de 64 bits van como string. Un log sin traza **no trae** los campos `traceId`/`spanId` (export real); la especificación también trata como inválidos el string vacío y los ids todo en cero.
- **Métrica vacía** = una métrica sin `sum`/`gauge`/`histogram`/`exponentialHistogram`/`summary`, o con `dataPoints` vacío, o un `metrics.json` sin ninguna métrica. En el export real: 183 métricas, ninguna sin puntos.
- **Cuándo leer el export: después de `docker compose down`.** Compose detiene `api` antes que `otel` (orden de dependencias). La API vacía sus buffers al recibir SIGTERM (OpenTelemetry y Serilog al disponerse) y el collector vacía el batch processor y cierra el archivo al apagarse. Leer mientras el stack corre da falsos rojos: el log de cierre se escribe **antes** de que termine su span.
- **Cómo lo hace la industria.** El demo oficial de OpenTelemetry corre tests de "presencia de señales" en CI contra los backends; Weaver `live-check` valida convenciones semánticas; Tracetest hace pruebas sobre trazas; el collector-contrib compara salidas con archivos "golden". Ninguno trae un control listo de "cada log de request apunta a un span exportado". Un script de Node sobre la salida del `file` exporter es razonable para un repo sin backend de telemetría [INFERENCIA].
- **Recomendación** (sección final): un `check-otel-export.mjs` con seis reglas, su `check-otel-export.test.mjs` con una fixture válida y una violación por regla, y un paso nuevo del job `compose` que lo corre después de `docker compose down`.

## 1. Qué hace que un log sea "de request"

**Respuesta:** un log es de request si se escribió dentro del scope que ASP.NET Core abre para cada pedido. Ese scope trae `RequestId` y `RequestPath`. `ConnectionId` viene de otro scope, el de la conexión de Kestrel.

**El scope de hosting (ASP.NET Core 10)**

- Al empezar cada pedido, `HostingApplicationDiagnostics.BeginRequest` calcula `loggingEnabled = _logger.IsEnabled(LogLevel.Critical)` y, si es `true`, abre el scope con `Log.RequestScope(_logger, httpContext)` ([`HostingApplicationDiagnostics.cs:105`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs#L105) y [`:132-137`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs#L132-L137)). El comentario del código dice "Scope may be relevant for a different level of logging, so we always create it".
- El scope es `HostingLogScope`, con dos pares: `RequestId` (el `HttpContext.TraceIdentifier`) y `RequestPath` (`PathBase + Path`) ([`HostingApplicationDiagnostics.cs:555-584`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs#L555-L584)).
- Se cierra al final de `RequestEnd`, después de detener el `Activity` ([`:236`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs#L236) y [`:255`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs#L255)). Entre esas dos líneas el código no escribe logs.
- Kestrel abre `ConnectionLogScope` con `ConnectionId` por cada conexión, con la misma condición `IsEnabled(LogLevel.Critical)` ([`KestrelConnection.cs:174-176`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Servers/Kestrel/Core/src/Internal/Infrastructure/KestrelConnection.cs#L174-L176); [`ConnectionLogScope.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Servers/Kestrel/Core/src/Internal/ConnectionLogScope.cs)). Una conexión vive más que un pedido: un log de la conexión puede tener `ConnectionId` sin pedido [INFERENCIA, del alcance de cada scope].
- En el repo, el logger de hosting tiene nivel mínimo `Warning` (`ObservabilityExtensions.cs:92`). `Critical` sigue prendido, así que el scope se abre en cada pedido [INFERENCIA, del código de arriba].
- Microsoft Learn explica qué es un scope ("every log created as part of processing a transaction can include the transaction ID"), pero no documenta las claves del scope de hosting; para eso, la fuente es el código ([Learn, Logging, Log scopes](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0#log-scopes)).

**Cómo llega el scope de MEL a Serilog**

1. `AddSerilog(..., preserveStaticLogger: true)` (`ObservabilityExtensions.cs:32-36`) **reemplaza** el `ILoggerFactory` de MEL por un `SerilogLoggerFactory` ([`SerilogServiceCollectionExtensions.cs:186-211`](https://github.com/serilog/serilog-extensions-hosting/blob/v10.0.0/src/Serilog.Extensions.Hosting/SerilogServiceCollectionExtensions.cs#L186-L211)).
2. Ese factory crea un único `SerilogLoggerProvider` y nunca llama a `SetScopeProvider` ([`SerilogLoggerFactory.cs`](https://github.com/serilog/serilog-extensions-logging/blob/v10.0.0/src/Serilog.Extensions.Logging/Extensions/Logging/SerilogLoggerFactory.cs#L38)). Por eso el `ActivityTrackingOptions` de MEL (que agregaría `SpanId`/`TraceId`/`ParentId` como scope, [Learn](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0#automatically-log-scope-with-spanid-traceid-and-parentid)) **no aplica** en este repo [INFERENCIA, del código; coincide con el export real, donde no aparece ningún atributo `ParentId`].
3. `ILogger.BeginScope` de MEL llega a `SerilogLoggerProvider.BeginScope`. El primer scope hace `LogContext.Push(this)`: empuja al propio provider como enriquecedor en el `LogContext` de Serilog ([`SerilogLoggerProvider.cs:79-89`](https://github.com/serilog/serilog-extensions-logging/blob/v10.0.0/src/Serilog.Extensions.Logging/Extensions/Logging/SerilogLoggerProvider.cs#L79-L89)). `LogContext` vive en un `AsyncLocal` ([`LogContext.cs:40`](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Context/LogContext.cs#L40)).
4. Cuando un evento pasa por el enriquecedor, el provider recorre los scopes abiertos y agrega cada par clave-valor como propiedad ([`SerilogLoggerProvider.cs:92-127`](https://github.com/serilog/serilog-extensions-logging/blob/v10.0.0/src/Serilog.Extensions.Logging/Extensions/Logging/SerilogLoggerProvider.cs#L92-L127); [`SerilogLoggerScope.cs:52-85`](https://github.com/serilog/serilog-extensions-logging/blob/v10.0.0/src/Serilog.Extensions.Logging/Extensions/Logging/SerilogLoggerScope.cs#L52-L85)). Un `IEnumerable<KeyValuePair<string, object>>` como `HostingLogScope` se vuelve propiedades sueltas, sin la propiedad `Scope` ([README de Serilog.Extensions.Logging, Notes on Log Scopes](https://github.com/serilog/serilog-extensions-logging/blob/v10.0.0/README.md#notes-on-log-scopes)).
5. `Enrich.FromLogContext()` es lo que aplica la pila del `LogContext` a cada evento ([`LoggerEnrichmentConfiguration.cs:86`](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Configuration/LoggerEnrichmentConfiguration.cs#L86)). El repo lo llama en `ObservabilityExtensions.cs:94`.
6. Hay dos caminos y conviene distinguirlos [INFERENCIA, del código de los pasos 2 a 5]:
   - Un log escrito con `ILogger<T>` de MEL pasa por el logger del provider, que ya tiene al provider como enriquecedor (`logger.ForContext([this])`, [`SerilogLoggerProvider.cs:40-41`](https://github.com/serilog/serilog-extensions-logging/blob/v10.0.0/src/Serilog.Extensions.Logging/Extensions/Logging/SerilogLoggerProvider.cs#L40-L41)). Recibe `RequestId` aunque falte `FromLogContext`.
   - Un log escrito directo con el `Serilog.ILogger`, como el de `UseSerilogRequestLogging` (`Program.cs:34`), recibe `RequestId` **solo** por `FromLogContext`.
7. El sink OTLP mapea cada propiedad a un atributo con el mismo nombre ([README del sink, mapping](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/README.md#serilog-logevent-to-opentelemetry-log-record-mapping); [`OtlpEventBuilder.cs:100-126`](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/Sinks/OpenTelemetry/OtlpEventBuilder.cs#L100-L126)).

**Qué hay en el export real del paso 14**

| Grupo | Cantidad | `RequestId` | `traceId`/`spanId` |
|---|---|---|---|
| Logs de cierre de `UseSerilogRequestLogging` (`HTTP {RequestMethod} {RequestPath} responded ...`) | 60 | sí | sí |
| Otros logs dentro de un pedido (2 de antiforgery, 1 `Failed executing DbCommand`) | 3 | sí | sí |
| Arranque y apagado (`Now listening on`, `Application started`, `Application is shutting down...`, Data Protection, una falla de SQL al migrar) | 14 | no | no |
| Total | 77 | 63 | 63 |

Ningún log tiene `traceId` sin `RequestId`, y ninguno tiene `RequestId` sin `traceId`.

## 2. Criterio para elegir los logs de request

**Respuesta:** `RequestId` presente como atributo. `RequestPath` sirve igual (sale del mismo scope). `ConnectionId` no, porque es de la conexión. Elegir por `traceId` es circular.

| Criterio | Qué gana | Qué pierde |
|---|---|---|
| A. Atributo `RequestId` (recomendado) | Sale del scope de hosting, que existe por cada pedido (sección 1). No depende de la traza. Es la clave que usa la documentación de Serilog.AspNetCore en su ejemplo de log de cierre ([README](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/README.md#request-logging)). | Depende de `FromLogContext` para el log de cierre (sección 1, paso 6). Si alguien lo quita, ese log deja de contar como de request; la regla 5 de la recomendación lo detecta. |
| B. Atributo `RequestPath` | Mismo scope. | El log de cierre también lo escribe en su plantilla (`RequestLoggingMiddleware`, [`:91`](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/src/Serilog.AspNetCore/AspNetCore/RequestLoggingMiddleware.cs#L91)), así que sobrevive a la falta de `FromLogContext` y tapa justo ese caso [INFERENCIA]. |
| C. Atributo `ConnectionId` | Ninguna ventaja. | Scope de la conexión: un log de Kestrel sobre la conexión no es de un pedido (sección 1). |
| D. Scope `Serilog.AspNetCore.RequestLoggingMiddleware` | Selecciona exactamente los logs de cierre (es el `scope.name` en el export real). | Deja afuera los logs de las slices y de EF dentro del pedido. |
| E. Tener `traceId` | Ninguna. | **Circular.** El paso quiere fallar "si un log de request no lleva trace/span". Si "de request" se define como "tiene trace", un log que perdió el trace sale de la muestra y el control pasa. Es una tautología. |

Para que A no pase en vacío, la recomendación agrega dos reglas de completitud: que haya al menos un log de request, y que cada span SERVER tenga al menos un log de request (sección final).

## 3. Cómo llegan `TraceId` y `SpanId` al LogRecord

**Respuesta:** por el `LogEvent` de Serilog, no por el enricher del repo. El sink copia `LogEvent.TraceId`/`SpanId` a los campos de primer nivel del LogRecord.

- Desde Serilog 3.1, `LogEvent` tiene `TraceId` y `SpanId`. En v4.3.0 (la que trae Serilog.AspNetCore 10.0.0, [csproj](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/src/Serilog.AspNetCore/Serilog.AspNetCore.csproj)), `Logger.Write` los toma de `Activity.Current` al crear el evento ([`Logger.cs:427-428`](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Core/Logger.cs#L427-L428)). Si el valor es el por defecto (todo ceros), las propiedades devuelven `null` ([`LogEvent.cs:105`](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Events/LogEvent.cs#L105), [`:111`](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Events/LogEvent.cs#L111)).
- Los logs de MEL hacen lo mismo en `SerilogLogger` ([`SerilogLogger.cs:160-162`](https://github.com/serilog/serilog-extensions-logging/blob/v10.0.0/src/Serilog.Extensions.Logging/Extensions/Logging/SerilogLogger.cs#L160-L162)).
- El sink escribe `logRecord.TraceId` y `logRecord.SpanId` solo si el flag está prendido y el valor no es `null` ([`OtlpEventBuilder.cs:178-188`](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/Sinks/OpenTelemetry/OtlpEventBuilder.cs#L178-L188)). Convierte el hex a bytes y descarta un id que no mida 16 u 8 bytes ([`PrimitiveConversions.cs:71-81`](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/Sinks/OpenTelemetry/ProtocolHelpers/PrimitiveConversions.cs#L71-L81)).
- `TraceIdField` y `SpanIdField` toman el valor "from `Activity.Current` as a top-level field" ([`IncludedData.cs:44-54`](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/Sinks/OpenTelemetry/IncludedData.cs#L44-L54)). Están en el default del sink ([`OpenTelemetrySinkOptions.cs:30-32`](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/Sinks/OpenTelemetry/OpenTelemetrySinkOptions.cs#L30-L32)); el repo los pone a mano (`ObservabilityExtensions.cs:82-85`), así que sacar uno de la lista los apaga.
- **El sink no escribe `flags` (TraceFlags).** No hay `Flags` en `OtlpEventBuilder.cs` ni en la tabla de mapeo del README ([README](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/README.md#serilog-logevent-to-opentelemetry-log-record-mapping)). En el export real, ningún log trae `flags`; los spans traen `flags: 257` (bit SAMPLED más el bit "se sabe si es remoto", [`trace.proto:356-365`](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/trace/v1/trace.proto#L356-L365)). Consecuencia: desde el log no se puede saber si su traza se grabó; hay que buscar el span [INFERENCIA].

**¿El `ActivityTraceEnricher` es redundante para OTLP?** Sí [INFERENCIA, del código de arriba]. `ActivityTraceEnricher.cs:11-18` agrega **propiedades** `TraceId` y `SpanId` como texto. El sink las exporta como **atributos** con ese nombre (en el export real, los 63 logs de request tienen los dos atributos). Los campos de primer nivel `traceId`/`spanId` salen de `LogEvent.TraceId`/`SpanId`, sin pasar por el enricher. Quitarlo dejaría los campos iguales y borraría los atributos duplicados. Este paso no lo toca (regla 7); va como pendiente.

Una consecuencia para el control: tiene que leer **los campos de primer nivel** `traceId`/`spanId`, no los atributos. Si leyera los atributos, probaría el enricher y no lo que un backend OTLP usa para correlacionar ([logs.proto, `trace_id`](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/logs/v1/logs.proto#L188-L199)).

## 4. El log de cierre corre dentro del span SERVER

**Respuesta:** sí. El middleware escribe el log con `Activity.Current`, que en ese momento es el `Activity` del pedido; ASP.NET Core lo detiene después de que el pipeline termina.

- `HostingApplication.ProcessRequestAsync` corre el pipeline; `DisposeContext` llama a `RequestEnd` después ([`HostingApplication.cs:94-122`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplication.cs#L94-L122)).
- El `Activity` se crea en `BeginRequest`, antes del pipeline ([`HostingApplicationDiagnostics.cs:107-109`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs#L107-L109)), y se detiene en `RequestEnd` ([`:236`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs#L236)). Es `ActivityKind.Server` ([`:427-428`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingApplicationDiagnostics.cs#L427-L428)).
- `RequestLoggingMiddleware.Invoke` hace `await _next(httpContext)` y después `LogCompletion` ([`RequestLoggingMiddleware.cs:52-76`](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/src/Serilog.AspNetCore/AspNetCore/RequestLoggingMiddleware.cs#L52-L76)). `LogCompletion` arma el `LogEvent` con `Activity.Current.TraceId` y `SpanId` de forma explícita ([`:93-104`](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/src/Serilog.AspNetCore/AspNetCore/RequestLoggingMiddleware.cs#L93-L104)). Con una excepción, el log sale desde el filtro `when` del `catch`, todavía dentro del pedido ([`:66-71`](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/src/Serilog.AspNetCore/AspNetCore/RequestLoggingMiddleware.cs#L66-L71)).
- El middleware solo escribe si el nivel está habilitado (`if (!logger.IsEnabled(level)) return false;`, [`:83`](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/src/Serilog.AspNetCore/AspNetCore/RequestLoggingMiddleware.cs#L83)). Y no mide lo que está antes que él en el pipeline ([README](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/README.md#request-logging)). En el repo solo `UseApiSecurityHeaders()` va antes (`Program.cs:33-34`).
- Export real: los 60 logs de cierre (y los otros 3 de request) tienen el `spanId` de un span `Microsoft.AspNetCore` con `kind: 2` y el mismo `traceId`. Hay 60 spans SERVER, 60 `RequestId` distintos y 60 `traceId` distintos: uno a uno. Los 311 spans de Npgsql (`kind: 3`, CLIENT) no tienen logs, y está bien.
- Un log escrito dentro de un span hijo (por ejemplo, durante un `HttpClient`) llevaría el `spanId` del hijo, no el del SERVER [INFERENCIA, porque `Activity.Current` sería el hijo]. Por eso la regla de correlación pide "un span exportado con el mismo `traceId`", no "el span SERVER".

## 5. Forma del JSON que escribe el collector

**Respuesta:** una línea por export; ids en hex minúscula; `kind` entero; enteros de 64 bits como string; un log sin traza no trae los campos.

- `file` exporter v0.162.0: "When `format` is json and `compression` is none, telemetry data is written to file in JSON format. Each line in the file is a JSON object" ([README](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/fileexporter/README.md#file-format)). El código escribe el mensaje y después `"\n"` ([`file_writer.go:30-41`](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/fileexporter/file_writer.go#L30-L41)). Cada línea es un `{"resourceLogs": [...]}`, `{"resourceSpans": [...]}` o `{"resourceMetrics": [...]}` (export real).
- `append: true` (`deploy/otel-collector-config.yaml:20`): el archivo acumula. En CI la carpeta es nueva en cada corrida (`ci.yml:143-144`).
- OTLP/JSON se aparta del mapeo estándar de protobuf en tres puntos ([OTLP, JSON Protobuf Encoding](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/docs/specification.md#json-protobuf-encoding)):
  - "The `traceId` and `spanId` byte arrays are represented as case-insensitive hex-encoded strings; they are not base64-encoded". El collector los escribe en minúscula (export real: 32 y 16 caracteres, ninguno en mayúscula), pero la especificación los acepta en cualquier caja: el control tiene que comparar en minúscula [INFERENCIA].
  - "Values of enum fields MUST be encoded as integer values", con el ejemplo `{ "kind": 2, ... }` para `SPAN_KIND_SERVER` ([`trace.proto:166`](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/trace/v1/trace.proto#L166)).
  - Las claves van en lowerCamelCase, y los enteros de 64 bits "are encoded as decimal strings" (por eso `"asInt":"5"` y `"count":"1"`).
- **Trace ausente o inválido.** Para `LogRecord.trace_id`: "An ID with all zeroes OR of length other than 16 bytes is considered invalid (empty string in OTLP/JSON is zero-length and thus is also invalid)", y el receptor debe asumir que el log no tiene traza "if the field is not present" o "contains an invalid value". Lo mismo para `span_id` con 8 bytes; además, "If the sender specifies a valid span_id then it SHOULD also specify a valid trace_id" ([`logs.proto:188-213`](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/logs/v1/logs.proto#L188-L213)).
- Cómo aparece en la práctica: protobuf omite en JSON un campo sin presencia que tiene el valor por defecto ("serializers should omit it from the output", [protobuf, JSON Mapping, Presence and default-values](https://protobuf.dev/programming-guides/json/)). En el export real, los 14 logs sin traza no traen ni `traceId` ni `spanId`. El control igual debe tratar `""` y ceros como inválidos, porque la especificación lo permite y otro emisor podría mandarlos.

Forma real (export real, recortada):

```json
{"resourceLogs":[{"resource":{...},"scopeLogs":[{"scope":{"name":"Serilog.AspNetCore.RequestLoggingMiddleware"},
  "logRecords":[{"timeUnixNano":"1794888603086341500","severityNumber":9,
    "body":{"stringValue":"HTTP GET /api/v1/health/ready responded 200 in 54.6624 ms"},
    "attributes":[{"key":"RequestId","value":{"stringValue":"0HNQ4C2JPKRO1:00000001"}},
                  {"key":"TraceId","value":{"stringValue":"04f4ef2d5ae9e7313a01eabed358276d"}}, ...],
    "traceId":"04f4ef2d5ae9e7313a01eabed358276d","spanId":"e246b23fd4abf8e9"}]}]}]}

{"resourceSpans":[{"resource":{...},"scopeSpans":[{"scope":{"name":"Microsoft.AspNetCore"},
  "spans":[{"traceId":"04f4ef2d5ae9e7313a01eabed358276d","spanId":"e246b23fd4abf8e9","flags":257,
    "name":"GET /api/v{version:apiVersion}/health/ready","kind":2, ...}]}]}]}
```

## 6. Qué dice la especificación sobre la correlación

- El modelo de datos de logs define tres campos de contexto: `TraceId` ("Can be set for logs that are part of request processing and have an assigned trace ID"), `SpanId` ("If SpanId is present TraceId SHOULD be also present") y `TraceFlags`. Los tres son opcionales ([Logs Data Model, Trace Context Fields](https://github.com/open-telemetry/opentelemetry-specification/blob/v1.62.0/specification/logs/data-model.md#trace-context-fields)).
- La correlación por contexto de ejecución: "OpenTelemetry extends this practice to logs where possible by including TraceId and SpanId in the LogRecords. This allows to directly correlate logs and traces that correspond to the same execution context" ([Logs, Log Correlation](https://github.com/open-telemetry/opentelemetry-specification/blob/v1.62.0/specification/logs/README.md#log-correlation)).
- Para librerías de logging existentes (como Serilog), la especificación propone *log appenders* o *bridges* que agregan el contexto de traza sin tocar cada línea de log ([Logs, Legacy First-Party Applications Logs](https://github.com/open-telemetry/opentelemetry-specification/blob/v1.62.0/specification/logs/README.md#legacy-first-party-applications-logs)). Con el SDK de logs de OpenTelemetry, "The trace context fields MUST be populated from the resolved Context" ([Logs SDK](https://github.com/open-telemetry/opentelemetry-specification/blob/v1.62.0/specification/logs/sdk.md#readablelogrecord)). El repo no usa ese SDK para logs: usa el sink de Serilog, que hace lo equivalente con `Activity.Current` (sección 3).
- Formatos que no son OTLP (texto, JSON propio): "trace_id" y "span_id" en hex minúscula y "trace_flags" ([Trace Context in non-OTLP Log Formats](https://github.com/open-telemetry/opentelemetry-specification/blob/v1.62.0/specification/compatibility/logging_trace_context.md)). Esto aplica a la consola del repo, no al export OTLP, donde mandan los campos de primer nivel.
- El SDK de logs tiene una opción `trace_based` que descarta logs de trazas no grabadas, y por defecto está apagada ([Logs SDK, LoggerConfig](https://github.com/open-telemetry/opentelemetry-specification/blob/v1.62.0/specification/logs/sdk.md#loggerconfig)). Es decir: la especificación acepta que existan logs con ids de una traza que no se exportó. Que eso sea un error depende del sistema; en este repo, donde todo se graba (sección 7), sí lo es [INFERENCIA].

## 7. Cómo se rompe la correlación sin que nadie se entere

**Respuesta:** casi todas las roturas reales dejan el `traceId` en el log y quitan el span. Por eso el control tiene que cruzar logs con spans.

**El punto de partida: el `Activity` existe aunque OpenTelemetry no lo grabe.**

- `ActivityCreator.IsActivityCreated` devuelve `true` si la fuente tiene listeners **o** si el logging está prendido ([`ActivityCreator.cs:10-13`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Shared/Diagnostics/ActivityCreator.cs#L10-L13)). Si OpenTelemetry no crea el `Activity` y el logging está prendido, ASP.NET Core crea uno con `new Activity(activityName)` ([`:56-99`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Shared/Diagnostics/ActivityCreator.cs#L56-L99)). Ese `Activity` tiene `TraceId` y `SpanId`, pero nadie lo exporta.
- OpenTelemetry .NET solo pasa al procesador un `Activity` con `IsAllDataRequested` ([`TracerProviderSdk.cs:205-220`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L205-L220)), y el procesador batch descarta lo que no está `Recorded` ([`BatchActivityExportProcessor.cs:59`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/Processor/BatchActivityExportProcessor.cs#L59)).

| Rotura | Qué pasa en el código | Qué ve el control |
|---|---|---|
| `Filter` de `AddAspNetCoreInstrumentation` que devuelve `false` (por ejemplo, para no trazar `/health`) | El listener pone `activity.IsAllDataRequested = false` y borra `Recorded` de `ActivityTraceFlags` ([`HttpInListener.cs:184-208`](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/Instrumentation.AspNetCore-1.19.0/src/OpenTelemetry.Instrumentation.AspNetCore/Implementation/HttpInListener.cs#L184-L208)). El `Activity` sigue siendo `Activity.Current` con sus ids. El README dice que el filtro "does not collect telemetry about the request" ([README, Filter](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/Instrumentation.AspNetCore-1.19.0/src/OpenTelemetry.Instrumentation.AspNetCore/README.md#filter)). | Logs de request con ids válidos, sin span: **referencia colgada**. |
| Sampler que descarta. El default es `ParentBased(AlwaysOn)` ([`TracerProviderSdk.cs:452`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L452)) y se puede cambiar con la variable `OTEL_TRACES_SAMPLER` ([`:18`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L18), [`:420-450`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L420-L450)) | Con `Drop`, si el span es raíz o el padre es remoto, el SDK devuelve `PropagationData`: crea el `Activity` sin grabarlo "so the trace ID is preserved" ([`:496-515`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L496-L515), [`:553-561`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L553-L561)). | Referencia colgada. Pasa también con el default si el cliente manda `traceparent` con el flag de no grabado (`...-00`), porque `ParentBased` respeta al padre [INFERENCIA, del mismo código]. |
| Quitar `.AddAspNetCoreInstrumentation()` de `WithTracing` (`ObservabilityExtensions.cs:48`) | La fuente `Microsoft.AspNetCore` queda sin listener y ASP.NET Core crea el `Activity` igual, por el logging ([`ActivityCreator.cs:56-99`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Shared/Diagnostics/ActivityCreator.cs#L56-L99)). | Referencias colgadas y cero spans SERVER. |
| Sacar `TraceIdField` o `SpanIdField` de `IncludedData` (`ObservabilityExtensions.cs:82-85`) | El sink no escribe el campo ([`OtlpEventBuilder.cs:180-188`](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/Sinks/OpenTelemetry/OtlpEventBuilder.cs#L180-L188)). Los atributos `TraceId`/`SpanId` del enricher **siguen** ahí. | Log de request sin `traceId` o sin `spanId`. Un control que mirara los atributos no lo vería (sección 3). |
| Quitar `Enrich.FromLogContext()` (`ObservabilityExtensions.cs:94`) | El log de cierre pierde `RequestId`, `RequestPath` y `ConnectionId`; los logs de MEL los conservan (sección 1, paso 6). El trace no cambia. | Menos logs de request; los spans SERVER de pedidos sin logs de MEL quedan sin log de request. Solo lo detecta la regla "cada span SERVER tiene un log de request". |
| Subir el nivel mínimo de Serilog o del middleware (`GetLevel`) | `LogCompletion` no escribe (`RequestLoggingMiddleware.cs:83`). | Spans SERVER sin log de request. |
| Log escrito fuera del pedido (un `IHostedService`, el arranque) | Sin `Activity.Current` y sin scope de hosting. | Nada: no es log de request y no tiene trace. Correcto: no es una rotura. |
| Apagado brusco (SIGKILL antes del flush) | Se pierden spans o logs en buffer (sección 9). | Referencias colgadas o spans sin log. Es un falso rojo: por eso el control corre después de un apagado ordenado. |

## 8. Qué es una métrica vacía

**Respuesta:** tres casos: no llegó ninguna métrica; una métrica no trae ningún tipo de dato; o trae un tipo con `dataPoints` vacío.

- Cada `Metric` tiene un `oneof data` con `gauge`, `sum`, `histogram`, `exponential_histogram` o `summary` ([`metrics.proto:200-210`](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/metrics/v1/metrics.proto#L200-L210)). Cada uno guarda sus valores en `repeated ... data_points` ([`:237-291`](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/metrics/v1/metrics.proto#L237-L291)). En JSON: `metric.sum.dataPoints`, `metric.histogram.dataPoints`, etc. Un `repeated` vacío se omite en JSON ([protobuf, JSON Mapping](https://protobuf.dev/programming-guides/json/)), así que "vacío" aparece como `dataPoints` ausente o `[]`.
- La especificación llama "envoltorio vacío" a un `ResourceMetrics` sin `ScopeMetrics` o sin `Metrics`, y dice que "senders SHOULD NOT create empty envelopes (OTLP payloads that contain zero spans, zero metric points or zero log records)" y que los receptores "MAY ignore" o "MAY drop" esos envoltorios ([OTLP, Empty Telemetry Envelopes](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/docs/specification.md#empty-telemetry-envelopes)). Como el collector puede descartarlos, el control no debería depender de verlos; tiene que fallar por lo que **falta** [INFERENCIA].
- Un punto puede llevar el flag `DATA_POINT_FLAGS_NO_RECORDED_VALUE_MASK = 1`: "This DataPoint is valid but has no recorded value", equivalente al marcador de "staleness" de Prometheus ([`metrics.proto:367-380`](https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/metrics/v1/metrics.proto#L367-L380); [Metrics Data Model, No recorded value](https://github.com/open-telemetry/opentelemetry-specification/blob/v1.62.0/specification/metrics/data-model.md#no-recorded-value)). Un punto así no es "vacío"; el control no debería fallar por él [INFERENCIA].
- Export real: 3 líneas en `metrics.json`, 183 métricas (146 `sum`, 37 `histogram`), ninguna sin puntos. Los nombres que pide el paso 14 siguen en `smoke-api.mjs:303-322`; este paso agrega la regla general "ninguna métrica sin puntos".

## 9. Cuándo leer el export: después de `docker compose down`

**Respuesta:** sí, conviene leerlo después del apagado. Es determinista: cada pieza vacía sus buffers al recibir SIGTERM, y Compose apaga la API antes que el collector.

**Por qué no alcanza con esperar mientras corre**

- El log de cierre se escribe dentro del pipeline (sección 4); el span se termina después y recién entonces entra al procesador batch de OpenTelemetry. Los dos viajan por caminos distintos (sink de Serilog y exporter de OpenTelemetry) con sus propios lotes. En un momento dado, el archivo puede tener el log y todavía no el span [INFERENCIA, del orden del código]. Un control estricto ("cada log apunta a un span") leído en caliente daría rojos al azar.

**La cadena de apagado**

1. `docker compose down` (paso `Apagar stack`, `ci.yml:166-168`). Compose "always starts and stops containers in dependency order" ([Docker, Control startup and shutdown order](https://docs.docker.com/compose/how-tos/startup-order/)). `api` depende de `otel` (`compose.yaml:36-40`), así que se detiene antes.
2. Docker manda SIGTERM y espera 10 segundos en Linux antes de SIGKILL ([Docker, container stop](https://docs.docker.com/reference/cli/docker/container/stop/)). `docker compose down` acepta `--timeout` ([Docker, compose down](https://docs.docker.com/reference/cli/docker/compose/down/)).
3. La API corre como `dotnet TemplateApp.dll` (`backend/Dockerfile:16`). Desde .NET 6, `ConsoleLifetime` maneja SIGTERM "(sent by other apps, such as `docker stop`)" y detiene el host de forma ordenada ([Learn, Generic Host, Host shutdown](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host#host-shutdown)).
4. `RunAsync` (`Program.cs:48`) dispone el host en un `finally` ([`HostingAbstractionsHostExtensions.cs:63-82`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/HostingAbstractionsHostExtensions.cs#L63-L82)), y con él el contenedor de DI.
5. OpenTelemetry .NET: al disponerse, el `TracerProvider` llama a `Processor.Shutdown(5_000)` ("Wait for up to 5 seconds grace period", [`TracerProviderSdk.cs:361-381`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L361-L381)); el procesador batch vacía la cola y apaga el exporter ([`BatchExportProcessor.cs:167-197`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/BatchExportProcessor.cs#L167-L197)). El lector de métricas hace un último `Collect` al apagarse ([`MetricReader.cs:299-300`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Metrics/Reader/MetricReader.cs#L299-L300)). La guía oficial: con DI, el provider "is disposed of at application shutdown", y si no se dispone "activities might get dropped due to the lack of proper flush" ([docs/trace, TracerProvider Management](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/docs/trace/README.md#tracerprovider-management)).
6. Serilog: con `preserveStaticLogger: true`, el `SerilogLoggerFactory` se crea con `dispose = true` sobre el logger final ([`SerilogServiceCollectionExtensions.cs:140`](https://github.com/serilog/serilog-extensions-hosting/blob/v10.0.0/src/Serilog.Extensions.Hosting/SerilogServiceCollectionExtensions.cs#L140), [`:202`](https://github.com/serilog/serilog-extensions-hosting/blob/v10.0.0/src/Serilog.Extensions.Hosting/SerilogServiceCollectionExtensions.cs#L202); [`SerilogLoggerProvider.cs:43-47`](https://github.com/serilog/serilog-extensions-logging/blob/v10.0.0/src/Serilog.Extensions.Logging/Extensions/Logging/SerilogLoggerProvider.cs#L43-L47)). Disponer el contenedor dispone el logger. El sink OTLP va envuelto en el `BatchingSink` de Serilog ([`OpenTelemetryLoggerConfigurationExtensions.cs:108`](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/OpenTelemetryLoggerConfigurationExtensions.cs#L108)), que promete que "any events whose Emit() method has completed at the time of sink disposal will be flushed" y en `Dispose` vacía la cola ([`BatchingSink.cs:93-96`](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Core/Sinks/Batching/BatchingSink.cs#L93-L96), [`:170-199`](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Core/Sinks/Batching/BatchingSink.cs#L170-L199), [`:263-279`](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Core/Sinks/Batching/BatchingSink.cs#L263-L279)). El `Log.CloseAndFlushAsync()` de `Program.cs:58` vacía solo el logger de arranque (consola), no el de OTLP [INFERENCIA, por `preserveStaticLogger: true`].
7. Collector: maneja SIGTERM con apagado ordenado salvo que se lo desactive ([`collector.go:454-457`](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.162.0/otelcol/collector.go#L454-L457)). Apaga los componentes "in topological order so that upstream components are stopped before downstream components. This ensures that each component has a chance to drain to its consumer" ([`graph.go:467-476`](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.162.0/service/internal/graph/graph.go#L467-L476)): primero el receptor, después el batch, al final el exporter.
8. El batch processor, al apagarse, procesa lo que queda en su canal y manda el lote pendiente ([`batch_processor.go:175-218`](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.162.0/processor/batchprocessor/batch_processor.go#L175-L218)).
9. El `file` exporter, al apagarse, para el ticker y cierra el archivo ([`file_writer.go:97-107`](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/fileexporter/file_writer.go#L97-L107)); el `Close` del writer con buffer hace `Flush` antes de cerrar ([`buffered_writer.go:32-36`](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/fileexporter/buffered_writer.go#L32-L36)).

**Evidencia en el export real.** La última línea de `logs.json` trae un solo log: `Application is shutting down...` a las 04:10:31, 22 s después del último span (04:10:09). Ese log salió en el apagado final y llegó al archivo: la cadena 5-9 funcionó al menos una vez con este stack [INFERENCIA: no tengo el comando exacto con el que se apagó esa corrida].

**Riesgos que quedan**

- El margen de 10 s de Docker es menor que el `ShutdownTimeout` de 30 s del host ([`HostOptions.cs:24`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/Microsoft.Extensions.Hosting/src/HostOptions.cs#L24)). Hoy la API apaga en mucho menos, pero `docker compose down --timeout 30` alinea los dos números [INFERENCIA].
- El smoke recrea la API a mitad de camino (`smoke-api.mjs:267`, `up --force-recreate`): ese contenedor también recibe SIGTERM y vacía sus buffers. El export real lo confirma: el primer `Application is shutting down...` (04:10:06) está en el archivo.
- Fuera de CI, con `append: true` y una carpeta reutilizada, una corrida cortada con SIGKILL deja referencias colgadas que harían fallar la siguiente. El control asume una carpeta nueva por corrida [INFERENCIA].

## 10. Cómo lo verifican otros proyectos

**Respuesta:** los proyectos de OpenTelemetry verifican presencia de señales y convenciones; nadie publica un control listo de correlación log↔span. Un script propio sobre la salida del `file` exporter es razonable y usa piezas del propio collector [INFERENCIA].

| Enfoque | Qué hace | Relación con este paso |
|---|---|---|
| Demo oficial (`opentelemetry-demo` 3.1.0, `test/telemetry`) | Pytest en Docker que corre "on every PR in CI" y consulta Jaeger, Prometheus y OpenSearch para ver que cada servicio manda trazas, métricas y logs. Dice explícitamente que "Does NOT validate semantic conventions or attributes (weaver's job)" ([README](https://github.com/open-telemetry/opentelemetry-demo/blob/3.1.0/test/telemetry/README.md)). | Mismo objetivo (señales en CI), pero solo presencia, y contra backends que el repo no tiene. |
| OTel Weaver `live-check` | Recibe telemetría (OTLP, archivo o stdin) y la compara contra un registro de convenciones semánticas ([README](https://github.com/open-telemetry/weaver/blob/v0.28.0/crates/weaver_live_check/README.md)). | Valida nombres y atributos, no la correlación. Sería herramienta nueva (regla 5). |
| Tracetest | "the easiest way to test microservices and distributed apps with OpenTelemetry distributed traces"; tiene versión open source para uso hobby y versiones comerciales ([docs](https://docs.tracetest.io/getting-started/overview)). | Pruebas sobre trazas, no sobre logs. Herramienta nueva, con licencia a evaluar. |
| `pdatatest` y `golden` del collector-contrib | Comparan métricas, logs y trazas reales contra archivos esperados en los tests del propio collector ([pdatatest](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/pkg/pdatatest/README.md); [golden](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/pkg/golden/README.md)). | La misma idea (asertar sobre la salida OTLP), pero en Go. |
| `file` exporter + script | El exporter existe para escribir OTLP JSON leíble; el collector incluso trae un receptor para leerlo de vuelta ([README del file exporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/fileexporter/README.md)). | Es lo que el repo ya hace desde la Fase 1 (`smoke-api.mjs:285-327`). Este paso lo vuelve estricto. |

No hay herramienta nueva en la recomendación: Node con `node:test`, como los gates de `dune/` (`ci.yml:48-50`, `:123-125`).

## Recomendación para este repo

**Reglas del control** (un `check-otel-export.mjs` que recibe la carpeta del export) [INFERENCIA, aplicando las secciones 1 a 9]:

1. **Hay logs de request.** Al menos un LogRecord con atributo `RequestId`. Evita que las demás reglas pasen en vacío.
2. **Cada log de request tiene `traceId` y `spanId` válidos.** Presentes, hex de 32 y 16 caracteres, no todo ceros (`logs.proto:188-213`). Se leen los campos de primer nivel, no los atributos (sección 3).
3. **Cada log de request apunta a un span exportado con el mismo `traceId`.** Se arma un índice `spanId → traceId` con todos los spans de `traces.json` (cualquier `kind`) y se compara en minúscula. Detecta referencias colgadas (sección 7).
4. **Hay spans SERVER** (`kind === 2`). Sin esto, la regla 5 pasa en vacío.
5. **Cada span SERVER tiene al menos un log de request** con su `spanId`. Detecta logs perdidos: `FromLogContext` quitado, nivel subido, middleware movido (sección 7).
6. **Métricas no vacías.** `metrics.json` trae al menos una métrica, y cada métrica trae un tipo de dato con al menos un `dataPoint` (sección 8).

Los logs sin `RequestId` (arranque, apagado) y los spans que no son SERVER (Npgsql) no se controlan. El export real del paso 14 cumple las seis reglas: 63 logs de request, 0 inválidos, 0 colgados, 60 spans SERVER todos con log, 183 métricas sin ninguna vacía.

La salida del control conviene que sea un resumen JSON (cantidades por regla y los primeros casos que fallan), escrito en la carpeta del export para que viaje en el artifact `otel-export` (`ci.yml:169-174`). Fuera de CI, `verify-template` guarda su evidencia en `artifacts/verify-template/<runId>/` (`AGENTS.md`, sección Verificación); eso explica el `artifacts/` del plan [INFERENCIA].

**Dónde corre en CI** (job `compose`):

- Un paso nuevo después de `Apagar stack` (`docker compose down`) y antes de `Publicar evidencia`: `node .agents/skills/verify-template/scripts/check-otel-export.mjs "$OTEL_EXPORT_DIR"`. Sin `if: always()`: si el smoke falló, el control no agrega información.
- Opcional: `docker compose down --timeout 30 --volumes` (sección 9, riesgos).
- Las pruebas del control (`node --test .agents/skills/verify-template/scripts/check-otel-export.test.mjs`) corren al principio del job, antes de levantar el stack: son rápidas y no necesitan Docker.

**Violaciones deliberadas que tienen que cubrir las pruebas** (fixtures JSON chicas, armadas a partir del export real):

| Fixture | Debe |
|---|---|
| Válida: logs de request correlacionados, logs de arranque sin trace, spans CLIENT sin log, ids en mayúscula en un caso | Pasar |
| Log de request sin `traceId` ni `spanId` (campos omitidos), como al sacar `TraceIdField` | Fallar regla 2 |
| Log de request con `traceId: ""` o todo ceros | Fallar regla 2 |
| Log de request cuyo `spanId` no existe en `traces.json` (caso `Filter` o sampler) | Fallar regla 3 |
| Log de request con `spanId` existente pero otro `traceId` | Fallar regla 3 |
| Span SERVER sin ningún log de request (caso `FromLogContext` o nivel) | Fallar regla 5 |
| Ningún log con `RequestId` | Fallar regla 1 |
| Ningún span SERVER | Fallar regla 4 |
| `metrics.json` sin métricas | Fallar regla 6 |
| Métrica con `sum.dataPoints: []`, y otra sin ningún tipo de dato | Fallar regla 6 |

Una violación real, de punta a punta, para la evidencia del cierre: correr el stack con `OTEL_TRACES_SAMPLER=always_off` en la API. Los logs siguen con ids y no hay spans; el control tiene que fallar por las reglas 3 y 4. No toca código: el SDK lee esa variable de la configuración ([`TracerProviderSdk.cs:18`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L18), [`:64`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Trace/TracerProviderSdk.cs#L64)) [INFERENCIA: no lo ejecuté].

**Qué choca o hay que actualizar en los docs del repo**

- `AGENTS.md:38` (regla 4) pide que "toda regla nueva se hace ejecutable en Dune, con una fixture válida y una violación deliberada". El plan pone el control en los scripts de `verify-template` (`plan.md:339`). No es una contradicción de fondo: el control es ejecutable y trae su fixture válida y sus violaciones, con el mismo patrón que `dune/check-*.mjs` + `*.test.mjs`. Pero el lugar difiere de `dune/`. Recomiendo seguir el plan y anotar la decisión en `docs/decisions.tsv`.
- `README.md:131` describe el control viejo ("correlaciona traceId de un log con un span recibido", "el flush de batch puede requerir hasta 30 segundos"). Hay que actualizarlo.
- `docs/architecture.md:125` ("Los logs de request llevan trace y span, sin credenciales") coincide con las reglas 2 y 3; puede sumar que el job `compose` lo verifica.
- ADR-0004 y `docs/conventions.md` no hablan de este control; no hay choque.

## Cosas que el paso tiene que decidir y que este informe deja abiertas

- **El chequeo débil del smoke** (`smoke-api.mjs:301-302`, "algún log correlacionado"): queda redundante con el control nuevo. Recomiendo quitarlo y dejar en el smoke los nombres de métricas del paso 14, que son de otro concepto.
- **`--timeout 30` en `docker compose down`:** barato y alinea los márgenes; no es imprescindible hoy.
- **Pendientes para `progreso.md`** (regla 7, fuera de este paso):
  - `ActivityTraceEnricher` es redundante para OTLP y duplica `TraceId`/`SpanId` como atributos (sección 3).
  - El sink de Serilog no exporta `TraceFlags` (sección 3).

## Verificación y límites

- **Leído:** el código citado en cada link, en las etiquetas fijadas arriba; Microsoft Learn (Generic Host, Logging en ASP.NET Core 10); la especificación OTLP y los `.proto` de logs, trazas y métricas (v1.11.1); la especificación de logs y métricas de OpenTelemetry (v1.62.0); los README del batch processor, del `file` exporter, de `pdatatest` y `golden` (v0.162.0); el README de los tests de telemetría del demo (3.1.0) y de Weaver `live-check` (v0.28.0); las páginas de Docker sobre `stop`, `compose down` y orden de arranque; la guía de JSON de protobuf; la página de Tracetest.
- **Comprobado con datos:** el export real del paso 14 (tablas de las secciones 1 y 4, forma del JSON de la sección 5, métricas de la sección 8, el log de apagado de la sección 9). Las seis reglas de la recomendación las corrí a mano con Node sobre ese export y pasan.
- **No comprobado:**
  - No corrí la API ni el stack. Los casos de la sección 7 (`Filter`, sampler, quitar `FromLogContext`, `OTEL_TRACES_SAMPLER=always_off`) salen de leer el código, no de ejecutarlos.
  - Que un log de MEL conserve `RequestId` sin `FromLogContext` sale de leer `SerilogLoggerProvider`; no lo probé.
  - No sé con qué comando se apagó la corrida del paso 14; la deducción "llegó en el apagado final" sale de los horarios.
  - El código de `HostingApplication.cs` y `ActivityCreator.cs` lo leí en `release/10.0`, no en la etiqueta exacta del runtime 10.0.12 de la imagen.
  - No medí cuánto tarda el apagado de la API dentro del margen de 10 s de Docker.
