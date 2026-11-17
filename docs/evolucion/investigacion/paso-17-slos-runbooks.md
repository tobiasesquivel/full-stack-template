# Paso 17: SLOs y runbooks (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 17 del [plan](../plan.md) (`plan.md:348-351`). Concepto: operación por objetivos y presupuesto de error. Cambia: documentos de operación y runbooks en `docs/`, y alertas sobre las métricas que ya existen. Verifica: cada SLO con su métrica consultada de verdad en el backend de métricas; cada alerta con su runbook y una ejecución de prueba.
- Cómo está hoy:
  - **No hay backend de métricas.** Compose levanta `db`, `otel`, `api` y `web` (`compose.yaml`). El collector (`otel/opentelemetry-collector-contrib:0.162.0`) recibe OTLP y solo escribe archivos (`file/logs`, `file/metrics`, `file/traces`) y la consola (`debug`) (`deploy/otel-collector-config.yaml`). Nada guarda series de tiempo ni evalúa reglas.
  - La API exporta métricas por OTLP http/protobuf cada 10 s (`ObservabilityExtensions.cs`, `ExportIntervalMilliseconds = 10_000`). Activa `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`, `AddRuntimeInstrumentation()` y `AddMeter("TemplateApp.*")`.
  - Métricas de negocio del paso 14: `templateapp.tasks.created` (`{task}`), `templateapp.tasks.completion.duration` (s) y `templateapp.auth.login.attempts` (`{attempt}`, con `templateapp.auth.login.outcome` = `success`, `invalid_credentials` o `locked_out`) (`TaskMetrics.cs`, `AuthMetrics.cs`).
  - Health del paso 16: `/api/v1/health/live` y `/api/v1/health/ready`, anónimos y sin limiter (`HealthFeature.cs:13`).
  - Un error no controlado termina en `GlobalExceptionHandler`, que responde 500 (`GlobalExceptionHandler.cs`, `ApiResults.cs:33`). La cookie de sesión se valida contra la base en cada request (`SessionHttpPolicy.cs:89-99`): con la base caída, un request autenticado termina en 500.
  - CI: el job `compose` levanta el stack, corre `smoke-api.mjs --compose` (que para la base y pide readiness 503), apaga el stack y corre `check-otel-export.mjs` sobre el export (`ci.yml`, job `compose`).
  - `progreso.md:164` ya anota el pendiente: "Las métricas de negocio no tienen tablero ni alerta. Encaja con el paso 17".
- Decisiones que ya están tomadas y que este informe no reabre:
  - OTLP es el único canal de salida y el collector decide destinos (`informe-dotnet-empresarial.md`, sección 5, regla 1).
  - Alertas por síntoma, no por causa, y SLOs con presupuesto de error por capacidad crítica (`informe-dotnet-empresarial.md`, sección 5, regla 5). Runbooks por alerta (sección 10, fila "Operación").
  - El control de telemetría vive en los scripts de `verify-template` (`decisions.tsv`, paso 15). Las reglas del repo viven en `dune/` con fixture válida y violación deliberada (regla 4).
  - Sin comentarios en YAML (`conventions.md`, "Comentarios"; lo hace cumplir `dune/check-comments.mjs`).
  - Una herramienta, librería o imagen la elige Tobi (regla 5 y excepción del modo autónomo). Por eso la sección "Opciones" deja la elección abierta y solo recomienda.
- Método: solo fuentes primarias. Google SRE (libro y workbook en `sre.google`); la especificación de OpenTelemetry (semconv HTTP y compatibilidad con Prometheus); el código de `dotnet/aspnetcore` (`release/10.0`) y de `opentelemetry-dotnet-contrib` (tag `Instrumentation.AspNetCore-1.19.0`); Microsoft Learn (ASP.NET Core 10); la documentación de Prometheus (versión `latest`, que hoy es 3.16) y su código (tag `v3.13.5`); los READMEs del collector en el tag `v0.162.0`; la documentación de VictoriaMetrics, Sloth (`v0.16.0`), Pyrra (`v0.10.2`) y Aspire; el repo `prometheus-operator/runbooks` y `kubernetes-mixin` (commit fijado). Cada afirmación lleva su link. Marco con [INFERENCIA] lo que deduzco y no está escrito en una fuente.
- Evidencia propia:
  - Leí, sin modificarlo, el export real del paso 16 (`artifacts/verify-template/paso16-compose-2/otel-export/metrics.json`, en el worktree del paso 16). Lo que muestra está en la sección 2.
  - Consulté la API de GitHub para las últimas versiones y las licencias (`gh api repos/<repo>/releases` y `.license.spdx_id`), y la API de Docker Hub para confirmar que existen los tags `prom/prometheus:v3.13.5`, `v3.15.1`, `v3.16.0`, `prom/alertmanager:v0.35.0`, `victoriametrics/victoria-metrics:v1.156.0`, `victoriametrics/vmalert:v1.156.0` y `victoriametrics/vmalert-tool:v1.156.0` (todos respondieron 200).
  - No corrí Docker, ni la API, ni `promtool`. No toqué código del repo.

## Glosario corto

- **SLI** (*service level indicator*, indicador de nivel de servicio): un número medido que dice qué tan bien anda el servicio. Acá siempre es una proporción: eventos buenos / eventos válidos.
- **SLO** (*service level objective*, objetivo de nivel de servicio): la meta para un SLI en una ventana. Ejemplo: "el 99,5 % de los requests no termina en 5xx, medido en 30 días".
- **SLA**: un contrato con consecuencias si no se cumple el SLO. Este repo no tiene SLA.
- **Presupuesto de error** (*error budget*): 100 % menos el SLO. Con 99,5 %, el presupuesto es 0,5 % de los requests de la ventana.
- **Ventana de cumplimiento**: el período en que se mide el SLO (28 o 30 días, móvil).
- **Burn rate** (velocidad de consumo): cuántas veces más rápido que lo "justo" se gasta el presupuesto. Burn rate 1 = se gasta exacto en toda la ventana. Burn rate 14,4 = se gasta el 2 % del presupuesto de 30 días en 1 hora.
- **Alerta multiventana y multi-burn-rate**: una alerta que pide que el burn rate sea alto en una ventana larga (para no saltar por un pico) **y** en una corta (para apagarse rápido cuando se arregla).
- **Página** (*page*): alerta que despierta a alguien ahora. **Ticket**: alerta que se mira en horario laboral.
- **Regla de grabación** (*recording rule*): una consulta que Prometheus calcula periódicamente y guarda como serie nueva. **Regla de alerta** (*alerting rule*): una consulta que, si devuelve algo, crea una alerta.
- **Runbook** (o *playbook*): la página que lee quien recibe la alerta: qué significa, qué impacto tiene, cómo diagnosticar y cómo mitigar.
- **Temporalidad acumulativa** (*cumulative*): cada export manda el total desde que arrancó el proceso. **Delta**: manda solo lo nuevo desde el export anterior.
- **Bucket**: cada tramo de un histograma. El histograma cuenta cuántas mediciones cayeron por debajo de cada límite (`le` = *less or equal*).
- **PromQL**: el lenguaje de consultas de Prometheus. **MetricsQL**: el de VictoriaMetrics, parecido pero no igual.

## Conclusión

- **Dos SLIs alcanzan, y los dos salen de una métrica que ya existe.** `http.server.request.duration` (el histograma que ASP.NET Core emite por cada request) da la disponibilidad (proporción de requests que no terminan en 5xx) y la latencia (proporción de requests más rápidos que un umbral). No hace falta tocar el backend para medirlos.
- **Qué cuenta como malo:** solo los 5xx. Los 4xx, incluido el 429 del limiter, cuentan como buenos: así lo define el ejemplo de Google ("We only count HTTP 5XX status messages as error codes; everything else is counted as success"). Los endpoints de health quedan **fuera** del SLI: los llama un orquestador, no un usuario, y Microsoft recomienda sacarlos de las métricas.
- **El umbral de latencia tiene que caer en un límite de bucket.** ASP.NET Core 10 usa los límites `[0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]` segundos (confirmado en el código y en el export real). 300 ms no es un límite; 250 ms y 500 ms sí. Recomiendo 500 ms, sin tocar código.
- **Falta un backend que guarde series y evalúe reglas.** Hoy el collector solo escribe archivos. La pieza estándar es Prometheus 3, que ya recibe OTLP nativo (`--web.enable-otlp-receiver`). El collector le manda las métricas con el exporter `otlp_http`, que ya viene en la imagen actual. El export a archivos sigue igual, así que `check-otel-export.mjs` no cambia.
- **Las alertas son las de Google:** multiventana y multi-burn-rate. Página si se gasta el 2 % del presupuesto en 1 hora (burn rate 14,4, ventanas 1h y 5m) o el 5 % en 6 horas (6, ventanas 6h y 30m). Ticket si se gasta el 10 % en 1 día (3, ventanas 1d y 2h) o en 3 días (1, ventanas 3d y 6h).
- **Se prueban en dos niveles.** Unitario: `promtool test rules` carga series sintéticas y verifica en segundos que cada alerta salta cuando debe y no salta cuando no debe, aunque las ventanas sean de días. En vivo: en Compose se para la base, se mandan requests autenticados (que dan 500) y se consulta `GET /api/v1/alerts` de Prometheus hasta ver la alerta en `firing`. Con un stack recién levantado salta en menos de un par de minutos [INFERENCIA, sección 6].
- **Runbooks:** un archivo Markdown por alerta en `docs/`, con las cuatro secciones del repo de runbooks de `prometheus-operator` (Significado, Impacto, Diagnóstico, Mitigación). Cada regla de alerta lleva la anotación `runbook_url`. "Toda alerta tiene runbook" se hace ejecutable con un script de Node en `dune/` que lee las reglas con el paquete `yaml` (el mismo que ya usa `dune/check-comments.mjs`), con su fixture válida y sus violaciones deliberadas.
- **Recomendación de herramienta** (sección "Opciones"): opción (a), Prometheus 3.13.5 (la versión LTS, soporte hasta el 2027-07-31), Apache-2.0, imagen `prom/prometheus:v3.13.5`, con las reglas escritas a mano. Sin Alertmanager, sin Grafana y sin generador de reglas en este paso. Elige Tobi.

## 1. SLI, SLO y presupuesto de error para esta API

**Respuesta:** dos SLIs de proporción (disponibilidad y latencia) sobre todos los requests de usuario, con health fuera, solo 5xx como malo, ventana móvil de 30 días y metas flojas para empezar.

**Definiciones**

- SLI: "a carefully defined quantitative measure of some aspect of the level of service that is provided"; SLO: "a target value or range of values for a service level that is measured by an SLI" ([SRE book, cap. 4](https://sre.google/sre-book/service-level-objectives/)).
- El workbook recomienda expresar el SLI como "the number of good events divided by the total number of events", de 0 % a 100 % ([Implementing SLOs](https://sre.google/workbook/implementing-slos/)).
- El presupuesto de error es "100% minus the SLO" ([Implementing SLOs](https://sre.google/workbook/implementing-slos/)). Mientras queda presupuesto se puede liberar; si se agota, "releases are temporarily halted" ([SRE book, cap. 3](https://sre.google/sre-book/embracing-risk/)).

**Qué SLIs sirven para una API de request/respuesta**

- El menú de Google para servicios de request/respuesta: **disponibilidad** ("The proportion of requests that resulted in a successful response"), **latencia** ("The proportion of requests that were faster than some threshold") y calidad (solo si el servicio se degrada a propósito) ([Implementing SLOs](https://sre.google/workbook/implementing-slos/), tabla 2-1).
- Esta API no tiene modo degradado, así que calidad no aplica [INFERENCIA]. Quedan dos.
- "Choose just enough SLOs to provide good coverage" y "It's better to start with a loose target that you tighten" ([SRE book, cap. 4](https://sre.google/sre-book/service-level-objectives/)).

**Qué requests cuentan**

- 5xx = malo; todo lo demás = bueno. El documento de SLO de ejemplo de Google lo dice así: "We only count HTTP 5XX status messages as error codes; everything else is counted as success" ([SLO document](https://sre.google/workbook/slo-document/)). Implementing SLOs repite: "5XX responses count against SLO, while all other requests are considered successful" ([Implementing SLOs](https://sre.google/workbook/implementing-slos/)).
- **4xx**: buenos. Un 400 de validación, un 401 sin sesión o un 409 de concurrencia son respuestas correctas del servicio.
- **429**: Google no lo trata aparte, así que entra en "everything else" (bueno). El ejemplo de Sloth, en cambio, cuenta 5xx **y** 429 como error ([`getting-started.yml`](https://github.com/slok/sloth/blob/v0.16.0/examples/getting-started.yml)). En este repo el limiter es por cliente (IP o usuario, `ApiRateLimiting.cs:38-47`): un 429 dice que ese cliente se pasó, no que el servicio falló. Recomiendo contarlo como bueno [INFERENCIA]. Si un día molesta a usuarios legítimos, se ve con `aspnetcore.rate_limiting.requests` (atributo `aspnetcore.rate_limiting.result`), que ya se exporta ([Learn, HTTP metrics](https://learn.microsoft.com/en-us/aspnet/core/metrics/http?view=aspnetcore-10.0)).
- **Health fuera.** Microsoft: excluir de las métricas "endpoints frequently called by automated systems, such as health checks", porque "can distort results displayed in a telemetry dashboard" ([Learn, metrics overview](https://learn.microsoft.com/en-us/aspnet/core/metrics/overview?view=aspnetcore-10.0)). Además, readiness responde 503 a propósito cuando la base cae (paso 16): contarlo duplicaría el error.
- Cómo se excluye: con un filtro en la consulta (`http_route!~".*/health/.*"`), sin tocar código. La alternativa en código es `.DisableHttpMetrics()` en el grupo `/health` ([Learn, metrics overview](https://learn.microsoft.com/en-us/aspnet/core/metrics/overview?view=aspnetcore-10.0)). Recomiendo el filtro: el plan pide "alertas sobre las métricas ya existentes", y apagar la métrica de health cambiaría lo que ve `check-otel-export.mjs` (paso 15) [INFERENCIA].

**Ventana y metas**

- Ventana: "We have found a four-week rolling window to be a good general-purpose interval" ([Implementing SLOs](https://sre.google/workbook/implementing-slos/)). La tabla de alertas recomendadas de Google está calculada para 30 días ([Alerting on SLOs](https://sre.google/workbook/alerting-on-slos/), tabla 5-8). Sloth trae las dos variantes, `google-30d` y `google-28d`, con los mismos porcentajes de presupuesto ([30d](https://github.com/slok/sloth/blob/v0.16.0/internal/alert/windows/google-30d.yaml), [28d](https://github.com/slok/sloth/blob/v0.16.0/internal/alert/windows/google-28d.yaml)).
- Recomiendo **30 días** en este laboratorio: los factores (14,4, 6, 3, 1) quedan iguales a la tabla de Google y se explican sin recalcular. Con 28 días serían 13,44, 5,6, 2,8 y 0,93 [INFERENCIA: burn rate = % de presupuesto × ventana del SLO / ventana de la alerta].
- Metas: no hay tráfico real para medir. Google sugiere partir del rendimiento actual si no hay otra información ([Implementing SLOs](https://sre.google/workbook/implementing-slos/)), pero también advierte no fijarlo sin pensar ([SRE book, cap. 4](https://sre.google/sre-book/service-level-objectives/)). El ejemplo de Google usa 97 % de disponibilidad para su API y "90% of requests < 400 ms; 99% of requests < 850 ms" ([SLO document](https://sre.google/workbook/slo-document/)).
- Recomiendo, para empezar flojo: **disponibilidad 99,5 %** (presupuesto 0,5 %, unas 3,6 horas de caída total en 30 días) y **latencia 99 % de los requests en menos de 500 ms** (presupuesto 1 %) [INFERENCIA]. Revisarlos al mes, como sugiere Google ("review the SLO frequently—perhaps every month").

**El documento de SLO y la política de presupuesto**

- Un documento de SLO lleva autores, fecha de aprobación y de revisión, una descripción breve del servicio, los objetivos con su implementación de SLI, cómo se calcula el presupuesto y "The rationale behind the numbers" ([Implementing SLOs](https://sre.google/workbook/implementing-slos/); ejemplo completo en [SLO document](https://sre.google/workbook/slo-document/)).
- La política de presupuesto de ejemplo tiene: Service Overview, Goals, Non-Goals, SLO Miss Policy ("we will halt all changes and releases other than P0 issues or security fixes"), Outage Policy (postmortem si un incidente gasta más del 20 % del presupuesto en cuatro semanas) y Escalation Policy ([Error budget policy](https://sre.google/workbook/error-budget-policy/)).

## 2. Qué métrica existente sostiene cada SLI

**Respuesta:** `http.server.request.duration` alcanza para los dos. No necesita paquetes ni código nuevo. Las métricas de negocio no sirven como SLI.

**La métrica**

- Semconv: histograma, unidad `s`, estable. Atributos: `http.request.method` (requerido), `http.response.status_code` (si hubo respuesta), `http.route` (si está disponible), `error.type` (si terminó con error), `url.scheme`. Límites de bucket recomendados: `[ 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10 ]` ([OTel semconv, HTTP metrics](https://opentelemetry.io/docs/specs/semconv/http/http-metrics/)).
- ASP.NET Core 10 la emite desde el meter `Microsoft.AspNetCore.Hosting`, con esos límites como `InstrumentAdvice` ([`HostingMetrics.cs:28-32`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingMetrics.cs#L28-L32); los límites en [`MetricsConstants.cs:11`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Shared/Metrics/MetricsConstants.cs#L11)). Agrega `http.response.status_code`, `http.route`, y `aspnetcore.request.is_unhandled` cuando ningún endpoint atendió el request ([`HostingMetrics.cs:44-94`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingMetrics.cs#L44-L94)).
- `AddAspNetCoreInstrumentation()` en .NET 8+ solo activa los meters integrados: `Microsoft.AspNetCore.Hosting`, `Routing`, `Diagnostics`, `RateLimiting`, `Authentication`, `Authorization`, `Identity` y otros ([`AspNetCoreInstrumentationMeterProviderBuilderExtensions.cs:45-66`](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/Instrumentation.AspNetCore-1.19.0/src/OpenTelemetry.Instrumentation.AspNetCore/AspNetCoreInstrumentationMeterProviderBuilderExtensions.cs#L45-L66)).

**Lo que muestra el export real del paso 16** (evidencia propia, `paso16-compose-2/otel-export/metrics.json`)

- `explicitBounds` = `[0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]`.
- `aggregationTemporality` = 2 (acumulativa) en todos los puntos.
- `http.route` es la plantilla con la versión: `/api/v{version:apiVersion}/tasks/`, `/api/v{version:apiVersion}/tasks/{id:guid}`, `/api/v{version:apiVersion}/health/ready`. Un 404 sin endpoint no trae `http.route` y sí `aspnetcore.request.is_unhandled=true`.
- Status codes vistos: 200, 201, 204, 400, 401, 403, 404, 409, 422 y 503 (solo en readiness). **Ningún 5xx de usuario y ningún 429**: el smoke de hoy nunca produce un 500. La prueba en vivo tiene que provocarlos (sección 6).
- `error.type` no aparece en ningún punto. ASP.NET Core 10 solo lo agrega en esta métrica para una excepción no manejada que llega al hosting; si la maneja `UseExceptionHandler`, ese middleware lo agrega como tag propio ([`HostingMetrics.cs:82-89`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingMetrics.cs#L82-L89)). Conclusión: el SLI se arma con `http.response.status_code`, no con `error.type` [INFERENCIA: no vi un 500 real en el export].
- Recurso: `service.name=templateapp-api`, `service.instance.id=<guid>`, `deployment.environment.name=Production`.

**Umbral de latencia y buckets**

- Un SLI de latencia con histograma clásico se calcula como `bucket{le="<umbral>"} / count`. Solo funciona si el umbral es un límite de bucket [INFERENCIA, aritmética del histograma].
- 300 ms no es límite; 250 ms y 500 ms sí. Con 500 ms no hay que cambiar nada.
- Si un día se quiere otro umbral, OpenTelemetry .NET deja cambiar los límites sin paquetes nuevos con `AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration { Boundaries = ... })`, y "When both the View API and Advice API are used, the View API takes precedence" ([customizing the SDK](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md); el ejemplo de Microsoft en [Learn, metrics overview](https://learn.microsoft.com/en-us/aspnet/core/metrics/overview?view=aspnetcore-10.0)). Recomiendo no hacerlo en este paso.
- Otra razón para preferir 500 ms: Prometheus 3 normaliza `le` a formato decimal y `le="1"` pasa a `le="1.0"` ([migración a v3](https://prometheus.io/docs/prometheus/latest/migration/)). Un umbral no entero (`0.5`) no tiene ese problema [INFERENCIA: la guía habla de scrape y no dice nada de OTLP].

**Las métricas de negocio**

- `templateapp.auth.login.attempts` cuenta resultados de negocio: un `invalid_credentials` es un usuario que se equivocó, no una falla del servicio. Un login que termina en 500 no se cuenta, porque se mide después de decidir el resultado (`decisions.tsv`, paso 14). No sirve como SLI de disponibilidad [INFERENCIA].
- Como alerta de síntoma tampoco cumple las reglas de Google para una página: "Every page should be actionable" y "If a page merely merits a robotic response, it shouldn't be a page" ([SRE book, cap. 6](https://sre.google/sre-book/monitoring-distributed-systems/)). Un pico de `invalid_credentials` es una señal de seguridad (relleno de credenciales), y el limiter de `auth` ya actúa solo. Recomiendo no alertar sobre métricas de negocio en este paso y dejarlo anotado como pendiente.

**Temporalidad**

- El exporter OTLP de .NET usa temporalidad acumulativa por defecto ([README del exporter OTLP, `core-1.19.1`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md)). El repo no lo cambia y el export real lo confirma.
- Prometheus guarda acumulativo sin conversión. Delta solo con la opción experimental `otlp-deltatocumulative` ([guía OpenTelemetry de Prometheus](https://prometheus.io/docs/guides/opentelemetry/); [feature flags](https://prometheus.io/docs/prometheus/latest/feature_flags/)). El exporter `prometheusremotewrite` del collector descarta lo que no es acumulativo ([README, v0.162.0](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/prometheusremotewriteexporter/README.md)). Con acumulativo no hay nada que tocar.

## 3. Backend de métricas y alertas: las piezas

**Respuesta:** Prometheus 3 cubre las tres cosas del paso (guardar, consultar, evaluar alertas) en un solo contenedor, recibe OTLP del collector y trae `promtool` para probar reglas.

**Prometheus 3 con receptor OTLP**

- Se activa con `--web.enable-otlp-receiver` y recibe en `/api/v1/otlp/v1/metrics` ([guía OpenTelemetry](https://prometheus.io/docs/guides/opentelemetry/)).
- Bloque `otlp:` de la configuración: `translation_strategy` con default `UnderscoreEscapingWithSuffixes`; `promote_resource_attributes` vacío por defecto; `keep_identifying_resource_attributes` en `false`; `convert_histograms_to_nhcb` en `false` ([configuración](https://prometheus.io/docs/prometheus/latest/configuration/configuration/)).
- Traducción de nombres con la estrategia por defecto: los puntos pasan a `_`, se agrega la unidad (`s` → `seconds`) y `_total` a los contadores; lo que está entre llaves (`{task}`) se descarta ([compatibilidad OTel-Prometheus](https://opentelemetry.io/docs/specs/otel/compatibility/prometheus_and_openmetrics/); el ejemplo exacto `http.server.request.duration` + `s` → `..._http_server_request_duration_seconds` en [otlptranslator](https://github.com/prometheus/otlptranslator)). Para este repo [INFERENCIA, verificar en el paso con una consulta real]:

| Métrica OTLP | Series en Prometheus | Etiquetas que usa el SLO |
|---|---|---|
| `http.server.request.duration` (histograma, `s`) | `http_server_request_duration_seconds_bucket`, `_count`, `_sum` | `http_route`, `http_response_status_code`, `http_request_method`, `le` |
| `templateapp.tasks.created` (contador, `{task}`) | `templateapp_tasks_created_total` | — |
| `templateapp.auth.login.attempts` (contador, `{attempt}`) | `templateapp_auth_login_attempts_total` | `templateapp_auth_login_outcome` |

- `job` sale de `service.name` (con `service.namespace/` adelante si existe) e `instance` de `service.instance.id`; el resto del recurso va a `target_info` ([guía OpenTelemetry](https://prometheus.io/docs/guides/opentelemetry/)). Acá: `job="templateapp-api"`. El `instance` cambia cada vez que se recrea el contenedor (el smoke lo recrea), así que las reglas suman `by (job)` [INFERENCIA].
- Retención por defecto: "the retention time defaults to `15d`" ([storage](https://prometheus.io/docs/prometheus/latest/storage/)). Un SLO de 30 días necesita `--storage.tsdb.retention.time` de al menos 30 días [INFERENCIA]. En CI no importa (el stack vive minutos), pero el compose tiene que decirlo.
- Evaluación: `global.evaluation_interval` es `1m` por defecto ([configuración](https://prometheus.io/docs/prometheus/latest/configuration/configuration/)). Para que la prueba en vivo sea corta conviene 15 s en este laboratorio [INFERENCIA].
- Salud del propio Prometheus: `/-/ready` "returns 200 when Prometheus is ready to serve traffic" ([management API](https://prometheus.io/docs/prometheus/latest/management_api/)). Sirve para el healthcheck de Compose y para que el script espere.
- La imagen `prom/prometheus` trae `/bin/promtool` junto a `/bin/prometheus`, con `ENTRYPOINT ["/bin/prometheus"]` y usuario `nobody` ([Dockerfile, v3.13.5](https://github.com/prometheus/prometheus/blob/v3.13.5/Dockerfile)). `promtool` se corre con `docker run --entrypoint promtool`.
- Versiones: hay una minor cada 6 semanas; la LTS vigente es la 3.13, con soporte hasta el 2027-07-31 ([release cycle](https://prometheus.io/docs/introduction/release-cycle/)). El último parche de la 3.13 es v3.13.5 (2026-10-21) y la última versión es v3.16.0 (2026-11-05) ([releases](https://github.com/prometheus/prometheus/releases)). Licencia Apache-2.0.

**Cómo llegan las métricas del collector a Prometheus**

| Camino | Cómo | Estado | Comentario |
|---|---|---|---|
| **Collector `otlp_http` → receptor OTLP de Prometheus** | Exporter `otlp_http` con `endpoint: http://prometheus:9090/api/v1/otlp` (le agrega `/v1/metrics`). | `otlp_http`: stable para métricas, en las distribuciones core y contrib ([README, v0.162.0](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.162.0/exporter/otlphttpexporter/README.md)). En 0.162.0 el nombre `otlphttp` es un alias deprecado. | Push, sin conversiones intermedias. Es lo que documenta Prometheus. |
| Collector `prometheus` exporter → scrape de Prometheus | El collector expone `/metrics` y Prometheus lo raspa. | beta ([README, v0.162.0](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/prometheusexporter/README.md)). | Un salto y un puerto más; la traducción la hace el collector. |
| Collector `prometheusremotewrite` → Prometheus con remote write | Remote write. | beta; "Non-cumulative monotonic, histogram, and summary OTLP metrics are dropped" ([README, v0.162.0](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/prometheusremotewriteexporter/README.md)). | Pensado para Cortex, Mimir o Thanos. Nada que ganar acá. |

- El pipeline `metrics` del collector suma el exporter nuevo al lado de `file/metrics` y `debug`. Cada exporter tiene su propia cola, así que si Prometheus no responde, el archivo se sigue escribiendo y `check-otel-export.mjs` no cambia [INFERENCIA: no lo probé].

**Alertmanager**

- "Alerting rules in Prometheus servers send alerts to an Alertmanager. The Alertmanager then manages those alerts, including silencing, inhibition, aggregation and sending out notifications" ([alerting overview](https://prometheus.io/docs/alerting/latest/overview/)).
- Para **ver** que una alerta salta no hace falta: Prometheus la publica en `GET /api/v1/alerts` (con `state: "firing"`, `labels`, `annotations` y `activeAt`) y en `GET /api/v1/rules` ([HTTP API](https://prometheus.io/docs/prometheus/latest/querying/api/)), y guarda la serie sintética `ALERTS{alertstate="pending"|"firing"}` ([alerting rules](https://prometheus.io/docs/prometheus/latest/configuration/alerting_rules/)).
- Este repo no tiene a quién notificar (ni Slack ni mail ni guardia). Recomiendo no sumarlo en este paso [INFERENCIA]. Versión actual: v0.35.0, Apache-2.0, imagen `prom/alertmanager:v0.35.0`.

**VictoriaMetrics + vmalert**

- Recibe OTLP en `/opentelemetry/v1/metrics`. Por defecto guarda los nombres "as is **without any transformations**" (con puntos); `-opentelemetry.usePrometheusNaming` los traduce al estilo Prometheus. "VictoriaMetrics works best with cumulative temporality" ([integración OpenTelemetry](https://docs.victoriametrics.com/victoriametrics/integrations/opentelemetry/)).
- Las alertas las evalúa otro proceso, `vmalert`, que necesita `-datasource.url` y `-notifier.url` si hay reglas de alerta, y guarda `ALERTS` solo si se le configura `-remoteWrite.url`. Expone `/api/v1/alerts`. Su formato de reglas busca ser compatible con el de Prometheus. Leer reglas de S3/GCS y el modo multi-tenant son de la versión Enterprise ([vmalert](https://docs.victoriametrics.com/victoriametrics/vmalert/)).
- `vmalert-tool unittest` acepta el formato de pruebas de Prometheus "except `promql_expr_test` field. Use `metricsql_expr_test` field name instead", porque MetricsQL no siempre es compatible con PromQL ([vmalert-tool](https://docs.victoriametrics.com/victoriametrics/vmalert-tool/)).
- Versión v1.156.0 (2026-11-09), Apache-2.0; imágenes `victoriametrics/victoria-metrics`, `victoriametrics/vmalert` y `victoriametrics/vmalert-tool` con ese tag.

**Grafana, Mimir y Alloy**

- Mimir (almacenamiento de métricas a escala) y Grafana (tableros) son AGPL-3.0; Alloy (el agente de Grafana) es Apache-2.0 (licencias vía la API de GitHub: [mimir](https://github.com/grafana/mimir), [grafana](https://github.com/grafana/grafana), [alloy](https://github.com/grafana/alloy)). Versiones actuales: Mimir 3.2.5, Grafana v13.3.1, Alloy v1.22.0.
- El paso no pide tableros: "consultar la métrica" se cumple con la API de consultas de Prometheus. Mimir resuelve escala y multi-tenant que este repo no tiene. Alloy reemplazaría al collector, que ya está. Recomiendo dejarlos fuera [INFERENCIA]. La fila "tablero de SLOs" del informe empresarial (sección 10) queda como pendiente.

**Aspire dashboard**

- Está "designed for development and short-term diagnostics rather than production telemetry retention", sin persistencia por defecto ([standalone dashboard](https://aspire.dev/dashboard/standalone/)). La página no menciona alertas. No cumple "alertas" ni "presupuesto de 30 días".

## 4. Las reglas: a mano, con Sloth o con Pyrra

**Respuesta:** a mano en este paso. Son unas 20 reglas legibles, y escribirlas es justo el concepto que el paso enseña. Sloth sirve como referencia para comparar.

- **Sloth** (Apache-2.0, v0.16.0, imagen `ghcr.io/slok/sloth:v0.16.0` según su [manifiesto de Kubernetes](https://github.com/slok/sloth/blob/v0.16.0/deploy/kubernetes/raw/sloth.yaml)). Desde un YAML con `error_query` y `total_query` genera reglas de grabación `slo:sli_error:ratio_rate<ventana>` para 5m, 30m, 1h, 2h, 6h, 1d, 3d y 30d, reglas de metadatos (`slo:objective:ratio`, `slo:error_budget:ratio`, `slo:period_error_budget_remaining:ratio`, etc.) y dos alertas multiventana (página y ticket) con el mismo nombre ([introduction](https://sloth.dev/introduction/)). Se usa con `sloth generate -i <spec> -o <reglas>` y tiene `validate` para CI ([CLI](https://sloth.dev/usage/cli/); [README](https://github.com/slok/sloth/blob/v0.16.0/README.md)). Las alertas generadas no usan `for` ([salida de ejemplo](https://github.com/slok/sloth/blob/v0.16.0/examples/_gen/getting-started.yml)).
- **Pyrra** (Apache-2.0, v0.10.2, imagen `ghcr.io/pyrra-dev/pyrra`). Genera reglas de grabación y "4 Multi Burn Rate Alerts", y trae una UI de presupuesto. Fuera de Kubernetes corre como dos procesos (`api` y `filesystem`) que escriben reglas a disco para que Prometheus las cargue ([README, v0.10.2](https://github.com/pyrra-dev/pyrra/blob/v0.10.2/README.md)).
- **A mano.** Ventaja: Tobi ve cada ventana y cada factor; no hay paso de generación que pueda quedar desincronizado; `promtool` prueba lo que se escribió. Desventaja: si un día hay diez SLOs, se repite mucho. En ese caso Sloth vale la pena [INFERENCIA].

## 5. Diseño de alertas: multiventana y multi-burn-rate

**Respuesta:** la tabla 5-8 de Google, sin `for`, una alerta por SLO con dos severidades.

**La tabla de Google** (99,9 % en 30 días; los porcentajes valen para cualquier meta) ([Alerting on SLOs](https://sre.google/workbook/alerting-on-slos/)):

| Severidad | Ventana larga | Ventana corta | Burn rate | Presupuesto gastado |
|---|---|---|---|---|
| Página | 1 hora | 5 minutos | 14,4 | 2 % |
| Página | 6 horas | 30 minutos | 6 | 5 % |
| Ticket | 3 días | 6 horas | 1 | 10 % |

- La expresión de ticket del workbook también usa 1 día con 2 horas y factor 3 ([Alerting on SLOs](https://sre.google/workbook/alerting-on-slos/)); Sloth usa las mismas cuatro combinaciones ([google-30d.yaml](https://github.com/slok/sloth/blob/v0.16.0/internal/alert/windows/google-30d.yaml)).
- "A good guideline is to make the short window 1/12 the duration of the long window" ([Alerting on SLOs](https://sre.google/workbook/alerting-on-slos/)).
- El workbook recorre seis enfoques y recomienda el último, "Multiwindow, Multi-Burn-Rate Alerts" ([Alerting on SLOs](https://sre.google/workbook/alerting-on-slos/)).

**Umbrales para las metas recomendadas** [INFERENCIA: factor × presupuesto]

| SLO | Presupuesto | Página 1h/5m (14,4) | Página 6h/30m (6) | Ticket 1d/2h (3) | Ticket 3d/6h (1) |
|---|---|---|---|---|---|
| Disponibilidad 99,5 % | 0,005 | > 7,2 % de 5xx | > 3 % | > 1,5 % | > 0,5 % |
| Latencia 99 % < 500 ms | 0,01 | > 14,4 % lentos | > 6 % | > 3 % | > 1 % |

**Reglas de grabación** (convención de nombres `level:metric:operations`, y para proporciones "aggregate up the numerator and denominator separately and then divide" ([recording rules](https://prometheus.io/docs/practices/rules/))). Una por SLI y por ventana (5m, 30m, 1h, 2h, 6h, 1d, 3d). Ejemplo para 5m:

```yaml
groups:
  - name: slo-api-sli
    rules:
      - record: job:slo_api_availability_errors:ratio_rate5m
        expr: |
          1 - (
            sum by (job) (rate(http_server_request_duration_seconds_count{job="templateapp-api", http_route!~".*/health/.*", http_response_status_code!~"5.."}[5m]))
            /
            sum by (job) (rate(http_server_request_duration_seconds_count{job="templateapp-api", http_route!~".*/health/.*"}[5m]))
          )
      - record: job:slo_api_latency_errors:ratio_rate5m
        expr: |
          1 - (
            sum by (job) (rate(http_server_request_duration_seconds_bucket{job="templateapp-api", http_route!~".*/health/.*", le="0.5"}[5m]))
            /
            sum by (job) (rate(http_server_request_duration_seconds_count{job="templateapp-api", http_route!~".*/health/.*"}[5m]))
          )
```

- Por qué `1 - buenos/total` y no `malos/total`: si nunca hubo un 5xx, la serie de 5xx no existe y `malos/total` da vacío en vez de 0. Con `1 - buenos/total` el SLI vale 0 cuando hay tráfico sin errores, y la verificación "consultada realmente" encuentra un número [INFERENCIA, semántica de PromQL].
- El filtro `http_route!~".*/health/.*"` también deja pasar los 404 sin ruta, que no tienen `http_route` (un regex negativo acepta la etiqueta vacía) [INFERENCIA].

**Regla de alerta** (forma del workbook; el ticket es igual con 1d/2h × 3 y 3d/6h × 1):

```yaml
  - name: slo-api-alerts
    rules:
      - alert: ApiAvailabilityBudgetBurn
        expr: |
          (job:slo_api_availability_errors:ratio_rate1h > (14.4 * 0.005) and job:slo_api_availability_errors:ratio_rate5m > (14.4 * 0.005))
          or
          (job:slo_api_availability_errors:ratio_rate6h > (6 * 0.005) and job:slo_api_availability_errors:ratio_rate30m > (6 * 0.005))
        labels:
          severity: page
        annotations:
          summary: La API gasta el presupuesto de disponibilidad demasiado rápido
          runbook_url: https://github.com/tobiasesquivel/full-stack-template/blob/main/docs/operacion/runbooks/ApiAvailabilityBudgetBurn.md
```

- Sin `for`: las ventanas ya filtran picos, y una regla sin `for` "will become active on the first evaluation" ([alerting rules](https://prometheus.io/docs/prometheus/latest/configuration/alerting_rules/)). Sloth tampoco lo usa.
- Mismo nombre para página y ticket, distinta `severity`: así cada SLO tiene **un** runbook. Es lo que hace Sloth ([salida de ejemplo](https://github.com/slok/sloth/blob/v0.16.0/examples/_gen/getting-started.yml)).
- Cuatro reglas de alerta, dos nombres: `ApiAvailabilityBudgetBurn` y `ApiLatencyBudgetBurn`.
- Opcional: una regla de grabación del presupuesto restante en 30 días. Sloth la calcula con `sum_over_time` sobre la regla de 5m para no recorrer 30 días de datos crudos ([introduction](https://sloth.dev/introduction/)). Recomiendo sumarla solo si se usa en el documento de SLO.

**Tráfico bajo**

- Con poco tráfico, un solo error dispara la alerta. Google sugiere tráfico artificial, juntar servicios, reintentos del cliente, o bajar el SLO o alargar la ventana ([Alerting on SLOs](https://sre.google/workbook/alerting-on-slos/), "Low-Traffic Services"). Este repo casi no tiene tráfico fuera de las pruebas. Recomiendo anotarlo en el documento de SLO y no resolverlo en este paso [INFERENCIA].

## 6. Cómo se prueba: unitario y en vivo

**Respuesta:** `promtool test rules` prueba cada alerta en los dos sentidos en segundos. En Compose, una prueba real para la base o la congela, manda requests y espera la alerta en `/api/v1/alerts`.

**Unitario con `promtool`**

- `promtool test rules <archivo>`. El archivo de prueba declara `rule_files`, `evaluation_interval` y una lista de `tests` con `input_series` (series sintéticas), `alert_rule_test` (`eval_time`, `alertname`, `exp_alerts` con `exp_labels` y `exp_annotations`; vacío = "no debe saltar") y `promql_expr_test` ([unit testing rules](https://prometheus.io/docs/prometheus/latest/configuration/unit_testing_rules/)).
- La notación `a+bxn` genera `n` muestras más sumando `b`: con `interval: 1m`, `0+60x4320` son tres días de un contador que sube 60 por minuto ([unit testing rules](https://prometheus.io/docs/prometheus/latest/configuration/unit_testing_rules/)). Por eso una ventana de 3 días se prueba sin esperar 3 días.
- `promtool check rules` valida el archivo y por defecto marca reglas duplicadas; `--lint-fatal` hace que el lint falle con código 3 ([promtool](https://prometheus.io/docs/prometheus/latest/command-line/promtool/)). `promtool check config` valida `prometheus.yml`.
- Casos que recomiendo por alerta [INFERENCIA]:

| Caso | Series | Espera |
|---|---|---|
| Sana | 60 requests 200 por minuto durante 2 h | Ninguna alerta. |
| Caída total | 60 min sanos y después todo 500 | Página a los pocos minutos; ticket también (en 1d y 3d la proporción supera el umbral). |
| Pico corto | Un minuto de 500 y después sano | Ninguna página 30 min después: la ventana corta ya se limpió. |
| Health fuera | Solo `/health/ready` con 503 | Ninguna alerta (prueba el filtro). |
| Anotación | Cualquier caso que salte | `exp_annotations` trae `runbook_url` exacto. |

- Esqueleto (sin comentarios, como pide `conventions.md`):

```yaml
rule_files:
  - slo-api.rules.yml
evaluation_interval: 1m
tests:
  - name: caida total dispara la pagina de disponibilidad
    interval: 1m
    input_series:
      - series: 'http_server_request_duration_seconds_count{job="templateapp-api", http_route="/api/v{version:apiVersion}/tasks/", http_response_status_code="200"}'
        values: '0+60x60 3600x30'
      - series: 'http_server_request_duration_seconds_count{job="templateapp-api", http_route="/api/v{version:apiVersion}/tasks/", http_response_status_code="500"}'
        values: '0x60 0+60x30'
    alert_rule_test:
      - eval_time: 70m
        alertname: ApiAvailabilityBudgetBurn
        exp_alerts:
          - exp_labels: { job: templateapp-api, severity: page }
            exp_annotations: { summary: "...", runbook_url: "..." }
          - exp_labels: { job: templateapp-api, severity: ticket }
            exp_annotations: { summary: "...", runbook_url: "..." }
```

- Se corre con la misma imagen: `docker run --rm --entrypoint promtool -v <repo>/deploy/prometheus:/p:ro prom/prometheus:v3.13.5 test rules /p/rules/slo-api.test.yml` ([Dockerfile](https://github.com/prometheus/prometheus/blob/v3.13.5/Dockerfile)). Tiempo: el pull de la imagen más unos segundos [INFERENCIA: no lo corrí].

**En vivo en Compose** (la "ejecución de prueba" del plan)

- **Disponibilidad:** con el stack sano y después del smoke, abrir una sesión (cookie), `docker compose stop db`, mandar requests autenticados a `GET /api/v1/tasks` durante unos 40 s, y consultar `GET /api/v1/alerts` cada 5 s hasta ver `ApiAvailabilityBudgetBurn` con `state: "firing"` y `severity: page`. Después `docker compose start db`.
  - Por qué da 500: la cookie se valida contra la base en cada request (`SessionHttpPolicy.cs:89-99`); con la base caída lanza, y `GlobalExceptionHandler` responde 500. Un request anónimo no sirve: da 401 sin tocar la base.
  - Por qué salta rápido: el stack es nuevo, así que la ventana de 1 hora solo contiene los minutos del smoke. Si el smoke dejó unos 100 requests buenos, alcanzan unos 8 errores para pasar el 7,2 % [INFERENCIA, aritmética].
  - Cuánto tarda: la API exporta cada 10 s, el `batch` del collector espera hasta 10 s, Prometheus evalúa cada 15 s y `rate()` necesita dos muestras de la serie de 500. Estimo entre 30 s y 90 s [INFERENCIA: no lo corrí].
  - El límite global (120 requests por minuto para anónimos, `ApiRateLimitOptions.cs:19`) no molesta con 1 request por segundo [INFERENCIA].
- **Latencia:** `docker compose pause db` congela la base sin cerrar sus conexiones. Los requests autenticados esperan hasta el timeout de Npgsql y terminan en 500 después de 500 ms, así que cuentan como lentos y la alerta de latencia salta (y también la de disponibilidad). Después `docker compose unpause db` [INFERENCIA: no lo probé; es el punto más incierto del paso]. Si resulta inestable, la alternativa es dejar la latencia solo con `promtool` y anotarlo.
- **Consultar cada SLO de verdad:** `GET /api/v1/query?query=job:slo_api_availability_errors:ratio_rate5m` (y la de latencia, y la métrica cruda `http_server_request_duration_seconds_count{job="templateapp-api"}`) devuelve un vector con un valor entre 0 y 1 ([HTTP API](https://prometheus.io/docs/prometheus/latest/querying/api/)). `GET /api/v1/rules` debe mostrar `health: "ok"` en cada regla.
- **Sobre cambiar `for` o usar una regla solo para pruebas:** no hace falta. Las reglas reales saltan rápido en un stack nuevo, así que se prueba lo mismo que corre en producción. Lo único que cambia en el laboratorio es `evaluation_interval: 15s` en `prometheus.yml` [INFERENCIA].
- **El primer valor de una serie nueva se pierde para `rate()`.** Prometheus no inyecta el cero inicial para OTLP: la opción `created-timestamp-zero-ingestion` solo lista PrometheusProto y OpenMetrics ([feature flags](https://prometheus.io/docs/prometheus/latest/feature_flags/)). Los errores del primer export no cuentan. Por eso la prueba manda errores durante al menos dos intervalos de export [INFERENCIA].

## 7. Runbooks y la regla "toda alerta tiene runbook"

**Respuesta:** un `.md` por alerta, con el nombre de la alerta, cuatro secciones fijas y la anotación `runbook_url`. Un script de Node en `dune/` lo hace cumplir.

**Por qué**

- "thinking through and recording the best practices ahead of time in a "playbook" produces roughly a 3x improvement in MTTR as compared to the strategy of "winging it."" ([SRE book, introducción](https://sre.google/sre-book/introduction/)).
- Prometheus: "Alerts should link to relevant consoles and make it easy to figure out which component is at fault" ([alerting practices](https://prometheus.io/docs/practices/alerting/)).

**Qué contiene** (plantilla del repo de runbooks de kube-prometheus, Apache-2.0)

- Un archivo por alerta, con el mismo nombre que la alerta, y el nombre de la alerta arriba ([add-runbook.md](https://github.com/prometheus-operator/runbooks/blob/main/content/docs/add-runbook.md)).
- Secciones: **Meaning**, **Impact**, **Diagnosis**, **Mitigation** ([plantilla](https://github.com/prometheus-operator/runbooks/blob/main/content/docs/add-runbook.md); ejemplo real: [KubePodCrashLooping](https://github.com/prometheus-operator/runbooks/blob/main/content/runbooks/kubernetes/KubePodCrashLooping.md)). Su guía pide escribir para "folks who are novices", sin jerga.
- Para este repo propongo las mismas cuatro en español (Significado, Impacto, Diagnóstico, Mitigación) más una quinta, **Cómo probarla**, que nombra el caso de `promtool` y la prueba en vivo [INFERENCIA]. Ejemplo de diagnóstico para `ApiAvailabilityBudgetBurn`: consultar el SLI por `http_route` y `http_response_status_code`, mirar `GET /api/v1/health/ready` (que nombra la dependencia caída desde el paso 16) y buscar el log `Error` con su excepción por `traceId` (paso 15).

**`runbook_url`**

- `kubernetes-mixin` agrega `runbook_url` a cada alerta que no lo tiene, con el patrón `https://github.com/kubernetes-sigs/kubernetes-mixin/tree/master/runbook.md#alert-name-%s` ([add-runbook-links.libsonnet](https://github.com/kubernetes-monitoring/kubernetes-mixin/blob/2002557eb82c0795cb03f01ba15245786f55c0db/lib/add-runbook-links.libsonnet)). La anotación es una URL absoluta.
- Para este repo: `https://github.com/tobiasesquivel/full-stack-template/blob/main/docs/operacion/runbooks/<Alerta>.md` (el remoto `origin`). Quien copie el template cambia el prefijo en un solo lugar del script.

**Hacerlo ejecutable (regla 4)**

- `dune/check-runbooks.mjs` lee los archivos de reglas con el paquete `yaml`, cargado desde `frontend/node_modules` con `createRequire`, igual que `dune/check-comments.mjs:7-11`. No suma dependencias.
- Falla si: una regla con `alert` no tiene `annotations.runbook_url`; la URL no empieza con el prefijo del repo; el archivo no existe; su primer título no es `# <alerta>`; falta alguna de las secciones; o hay un runbook sin alerta (huérfano).
- `dune/check-runbooks.test.mjs` con `node:test`, al estilo de `check-otel-export.test.mjs`: una fixture válida (una regla y su runbook en una carpeta temporal) y una violación deliberada por cada condición de arriba, observando el código de salida.
- `promtool test rules` cubre además que la anotación llegue a la alerta que salta (`exp_annotations`).

## 8. Encaje en CI y en `check-all`

**Respuesta:** dos gates nuevos de Node y dos de `promtool` en `check-all`, y en el job `compose` un servicio más, un exporter más y un script de prueba en vivo. El control del export no cambia.

- `compose.yaml`: servicio `prometheus` con `prom/prometheus:<tag>`, `--web.enable-otlp-receiver`, `--config.file`, `--storage.tsdb.retention.time=30d` (o más), las reglas montadas de solo lectura y `${PROMETHEUS_PORT:-9090}:9090` (el puerto configurable evita el choque con otros stacks de esta máquina, `progreso.md:119`). Healthcheck contra `/-/ready`; la imagen es busybox y trae `wget` [INFERENCIA].
- `deploy/otel-collector-config.yaml`: exporter `otlp_http/prometheus` en el pipeline `metrics`, al lado de `file/metrics` y `debug`.
- Job `compose`, en este orden: `promtool check config`, `promtool check rules --lint-fatal` y `promtool test rules` (no necesitan el stack); el smoke de siempre; el script nuevo de SLOs (consulta los SLIs, revisa `/api/v1/rules` y fuerza las alertas); después `down` y `check-otel-export.mjs`, como hoy.
- Costo en CI: el pull de la imagen de Prometheus y entre 2 y 3 minutos de la prueba en vivo [INFERENCIA].
- `check-all.mjs`: `check-runbooks` y `check-runbooks-test` (Node), y `promtool-check` y `promtool-test` vía `docker run`. `check-all` ya depende de Docker para `dotnet-test` (Testcontainers). La prueba en vivo queda en el job `compose`, como `check-otel-export.mjs` (`progreso.md:169`).
- Dónde va el script en vivo: `.agents/skills/verify-template/scripts/`, junto a `smoke-api.mjs`, porque es control de telemetría (decisión del paso 15). Recomiendo un script aparte y no más código en `smoke-api.mjs`, para que el smoke siga probando la API [INFERENCIA].

## Opciones (elige Tobi, regla 5)

| Opción | Qué gana Tobi | Qué pierde | Licencia | Imagen |
|---|---|---|---|---|
| **(a) Prometheus 3 LTS con receptor OTLP, reglas a mano, sin Alertmanager.** El collector manda con `otlp_http`. | Un contenedor. PromQL estándar y la documentación de Google en el mismo lenguaje. `promtool` para probar reglas en la misma imagen. `/api/v1/alerts` alcanza para ver alertas. LTS con soporte hasta el 2027-07-31. | Nadie recibe notificaciones (no hay a quién en el repo). Sin tableros. | Apache-2.0 | `prom/prometheus:v3.13.5` (o `v3.16.0`, la última) |
| **(b) (a) + Alertmanager.** | El circuito completo: agrupación, silencios y notificación. La prueba en vivo puede mirar Alertmanager. | Un contenedor más sin receptor real; más configuración para ver lo mismo que muestra Prometheus. | Apache-2.0 | `prom/alertmanager:v0.35.0` |
| **(c) Prometheus que raspa el exporter `prometheus` del collector.** | Modelo pull clásico. | Un puerto y un salto más; exporter beta; la traducción la hace el collector. Nada que ganar frente a (a). | Apache-2.0 | `prom/prometheus` + el collector actual |
| **(d) VictoriaMetrics single + vmalert (+ vmalert-tool).** | Liviano. OTLP nativo. `vmalert-tool` prueba reglas. | Dos o tres contenedores. Los nombres quedan con puntos salvo `-opentelemetry.usePrometheusNaming`. MetricsQL no es PromQL. vmalert necesita notifier y remote write para guardar `ALERTS`. Menos estándar para aprender. | Apache-2.0 (algunas funciones son Enterprise) | `victoriametrics/victoria-metrics:v1.156.0`, `victoriametrics/vmalert:v1.156.0` |
| **(e) Stack de Grafana: Mimir + Grafana (+ Alloy).** | Tableros y escala. | Mucho más para operar; AGPL en Mimir y Grafana; el paso no pide tableros. | AGPL-3.0 (Mimir, Grafana), Apache-2.0 (Alloy) | `grafana/mimir`, `grafana/grafana` (no verifiqué los tags) |
| **(f) Aspire dashboard.** | Ya conocido en .NET. | No tiene alertas ni retención: no cumple el paso. | MIT | `mcr.microsoft.com/aspire/dashboard` |

Generador de reglas (se combina con a, b o c):

| Opción | Qué gana | Qué pierde | Licencia | Imagen |
|---|---|---|---|---|
| **A mano** | Se ve cada ventana y cada factor. Nada que regenerar. | Repetición si crecen los SLOs. | — | — |
| **Sloth** | Reglas uniformes desde un YAML corto; metadatos de presupuesto; `validate` para CI. | Un paso de generación y un archivo generado que hay que mantener sincronizado. Oculta el concepto. | Apache-2.0 | `ghcr.io/slok/sloth:v0.16.0` |
| **Pyrra** | UI de presupuesto. | Dos procesos más fuera de Kubernetes. | Apache-2.0 | `ghcr.io/pyrra-dev/pyrra` (v0.10.2) |

**Recomendación: (a) con reglas a mano, y la LTS `v3.13.5`.** Es el backend que nombra el informe empresarial (sección 5: "Prometheus / Mimir"), recibe OTLP sin intermediarios, y el mismo binario trae la herramienta para probar las reglas. Cumple el plan: guarda y consulta la métrica, evalúa las alertas y deja probarlas en vivo y unitariamente. Alertmanager, tableros y generadores resuelven problemas que este repo todavía no tiene. Para la versión, la LTS da un año de parches sin saltar de minor cada 6 semanas; la `v3.16.0` salió hace 12 días.

## Recomendación para este repo

**Archivos**

| Archivo | Qué tiene |
|---|---|
| `deploy/prometheus/prometheus.yml` | `global.evaluation_interval: 15s`, `rule_files`, bloque `otlp` con `translation_strategy: UnderscoreEscapingWithSuffixes` explícito (para que un cambio de default no rompa las reglas). |
| `deploy/prometheus/rules/slo-api.rules.yml` | Grupo `slo-api-sli` (14 reglas de grabación: 2 SLIs × 7 ventanas) y grupo `slo-api-alerts` (4 reglas, 2 nombres). Sin comentarios. |
| `deploy/prometheus/rules/slo-api.test.yml` | Las pruebas de `promtool` de la sección 6. |
| `docs/operacion/slos.md` | El documento de SLO: servicio, SLIs con su PromQL, metas, ventana, por qué esos números, política de presupuesto corta, fecha de revisión. |
| `docs/operacion/runbooks/ApiAvailabilityBudgetBurn.md`, `ApiLatencyBudgetBurn.md` | Un runbook por alerta. |
| `dune/check-runbooks.mjs` y `.test.mjs` | La regla "toda alerta tiene runbook". |
| `.agents/skills/verify-template/scripts/check-slo-alerts.mjs` | La prueba en vivo. |
| `compose.yaml`, `deploy/otel-collector-config.yaml`, `ci.yml`, `check-all.mjs` | Las piezas de la sección 8. |
| `docs/architecture.md` ("Operación"), `docs/decisions.tsv` | Una línea por Prometheus y SLOs; una fila por cada decisión de abajo. |

**Decisiones de diseño que no son de herramienta** (las toma el agente en modo autónomo y van a `decisions.tsv`):

| Decisión | Recomendación | Por qué |
|---|---|---|
| SLIs | Disponibilidad y latencia sobre `http.server.request.duration`. | Menú de Google para request/respuesta; la métrica ya existe (secciones 1 y 2). |
| Qué es malo | Solo 5xx. 4xx y 429 son buenos. | Ejemplo de SLO de Google; el limiter es por cliente (sección 1). |
| Health | Fuera, con filtro en PromQL. | Microsoft lo recomienda; no se toca el código ni el control del paso 15 (sección 1). |
| Ventana | 30 días móviles. | Los factores coinciden con la tabla de Google (sección 1). |
| Metas | 99,5 % de disponibilidad; 99 % de requests en menos de 500 ms. | Empezar flojo; 500 ms es límite de bucket (secciones 1 y 2). |
| Alertas | Tabla 5-8 + ticket 1d/2h, sin `for`, un nombre por SLO con `severity` page/ticket. | Workbook y Sloth (sección 5). |
| Métricas de negocio | Sin alerta en este paso. | No son SLI ni síntoma accionable (sección 2). |
| Dónde viven los runbooks | `docs/operacion/runbooks/<Alerta>.md`, con `runbook_url` absoluta a `main`. | Convención de kube-prometheus y kubernetes-mixin (sección 7). |
| Cómo salta la alerta en la prueba | Reglas reales; base parada (disponibilidad) y pausada (latencia); `evaluation_interval: 15s`. | Prueba lo mismo que correría en producción (sección 6). |
| Dónde vive cada control | Regla de runbooks en `dune/`; prueba en vivo en `verify-template`. | Regla 4 y decisión del paso 15. |

## Riesgos y sorpresas

- **Los nombres cambian al entrar a Prometheus.** `http.server.request.duration` pasa a `http_server_request_duration_seconds_*` y los atributos pasan a `http_route`, `http_response_status_code`. Si alguien cambia `translation_strategy` a una variante UTF-8, las reglas dejan de encontrar series sin dar error. Por eso conviene fijarla explícita y por eso la prueba en vivo consulta los SLIs de verdad.
- **Umbral fuera de bucket.** Un umbral que no es límite de bucket da un SLI que no se puede calcular con `le`. 500 ms está bien; 300 ms no.
- **`le` en Prometheus 3.** Se normaliza a formato decimal (`1` → `1.0`) ([migración](https://prometheus.io/docs/prometheus/latest/migration/)). Con `0.5` no afecta; verificar con una consulta real.
- **Sin dato no hay alerta.** Con OTLP no existe la serie `up`. Si la API muere o el collector se cae, no llegan muestras, el SLI queda vacío y ninguna alerta de burn rate salta [INFERENCIA]. Una alerta sobre `absent_over_time(target_info{job="templateapp-api"}[5m])` lo cubriría, pero en un template sin tráfico continuo puede ser ruido. Queda como pendiente.
- **Primer valor perdido.** Los errores del primer export de una serie nueva no cuentan en `rate()` (sección 6). La prueba en vivo tiene que sostener la falla.
- **Retención por defecto de 15 días**, menos que la ventana de 30. Hay que fijarla en Compose.
- **Latencia en vivo con `pause`.** No está probado que los requests terminen en un tiempo razonable con la base congelada; depende de los timeouts de Npgsql y de la validación de la cookie [INFERENCIA].
- **El 503 de readiness.** Si el filtro de health se escribe mal, la caída de la base cuenta doble. El caso "Health fuera" de `promtool` lo cubre.
- **Instancia nueva por recreación.** El smoke recrea la API y cambia `instance`. Las reglas tienen que sumar `by (job)`; si sumaran por `instance`, las ventanas largas verían dos series cortadas [INFERENCIA].
- **Puerto 9090.** Puede estar ocupado en la máquina de Tobi; por eso `${PROMETHEUS_PORT:-9090}`.

## Cosas que el paso tiene que decidir y que este informe deja abiertas

- **La opción de backend y de generador.** Es elección de herramienta: la decide Tobi (regla 5).
- **La versión de Prometheus:** LTS `v3.13.5` (recomendada) o última `v3.16.0`.
- **Pendientes para `progreso.md`** (regla 7, fuera de este paso):
  - Alertmanager y un receptor de notificaciones. Sin paso asignado.
  - Tablero de SLOs (informe empresarial, sección 10, "Operación"). Sin paso asignado.
  - Alerta por ausencia de telemetría (`target_info`). Sin paso asignado.
  - Alertas sobre métricas de negocio (por ejemplo, picos de `invalid_credentials`). Sin paso asignado.
  - El collector arranca en `service_started` (`progreso.md:171`): con Prometheus detrás, un collector que tarda en arrancar también retrasa las primeras muestras. Sin paso asignado.

## Verificación y límites

- Contrasté con el código del repo: `compose.yaml`, `deploy/otel-collector-config.yaml`, `ObservabilityExtensions.cs`, `TaskMetrics.cs`, `AuthMetrics.cs`, `HealthFeature.cs`, `GlobalExceptionHandler.cs`, `ApiResults.cs`, `SessionHttpPolicy.cs`, `ApiRateLimiting.cs`, `ApiRateLimitOptions.cs`, `ci.yml`, `smoke-api.mjs`, `check-all.mjs`, `dune/check-comments.mjs` y `frontend/package.json` (`yaml` 2.9.1).
- Contrasté con datos reales: el export del paso 16 (buckets, temporalidad, rutas, status codes, recurso).
- Sin comprobar: ningún comando de Prometheus ni de `promtool`; la traducción exacta de nombres en una consulta real; el tiempo hasta que salta la alerta; el comportamiento de `docker compose pause db`; que un 500 manejado lleve o no `error.type`. Todo eso lo comprueba el paso al ejecutarse.

## Fuentes

**Google SRE**
- [SRE book, cap. 3: Embracing Risk](https://sre.google/sre-book/embracing-risk/)
- [SRE book, cap. 4: Service Level Objectives](https://sre.google/sre-book/service-level-objectives/)
- [SRE book, cap. 6: Monitoring Distributed Systems](https://sre.google/sre-book/monitoring-distributed-systems/)
- [SRE book, introducción](https://sre.google/sre-book/introduction/)
- [SRE workbook: Implementing SLOs](https://sre.google/workbook/implementing-slos/)
- [SRE workbook: Alerting on SLOs](https://sre.google/workbook/alerting-on-slos/)
- [SRE workbook: Example SLO document](https://sre.google/workbook/slo-document/)
- [SRE workbook: Example error budget policy](https://sre.google/workbook/error-budget-policy/)

**OpenTelemetry**
- [Semconv: HTTP metrics](https://opentelemetry.io/docs/specs/semconv/http/http-metrics/)
- [Compatibilidad con Prometheus y OpenMetrics](https://opentelemetry.io/docs/specs/otel/compatibility/prometheus_and_openmetrics/)
- [opentelemetry-dotnet: customizing the SDK (métricas)](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md)
- [opentelemetry-dotnet: README del exporter OTLP, `core-1.19.1`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md)
- [opentelemetry-dotnet-contrib: meters de `AddAspNetCoreInstrumentation`, 1.19.0](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/Instrumentation.AspNetCore-1.19.0/src/OpenTelemetry.Instrumentation.AspNetCore/AspNetCoreInstrumentationMeterProviderBuilderExtensions.cs#L45-L66)
- [Collector: exporter `otlp_http`, v0.162.0](https://github.com/open-telemetry/opentelemetry-collector/blob/v0.162.0/exporter/otlphttpexporter/README.md)
- [Collector contrib: `prometheusremotewrite`, v0.162.0](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/prometheusremotewriteexporter/README.md)
- [Collector contrib: `prometheus`, v0.162.0](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.162.0/exporter/prometheusexporter/README.md)

**ASP.NET Core**
- [`HostingMetrics.cs`, `release/10.0`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/Hosting/src/Internal/HostingMetrics.cs)
- [`MetricsConstants.cs`, `release/10.0`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Shared/Metrics/MetricsConstants.cs)
- [Learn: ASP.NET Core built-in HTTP metrics (10.0)](https://learn.microsoft.com/en-us/aspnet/core/metrics/http?view=aspnetcore-10.0)
- [Learn: ASP.NET Core metrics (10.0)](https://learn.microsoft.com/en-us/aspnet/core/metrics/overview?view=aspnetcore-10.0)

**Prometheus**
- [Guía: usar Prometheus como backend de OpenTelemetry](https://prometheus.io/docs/guides/opentelemetry/)
- [Configuración](https://prometheus.io/docs/prometheus/latest/configuration/configuration/)
- [Alerting rules](https://prometheus.io/docs/prometheus/latest/configuration/alerting_rules/)
- [Unit testing for rules](https://prometheus.io/docs/prometheus/latest/configuration/unit_testing_rules/)
- [promtool](https://prometheus.io/docs/prometheus/latest/command-line/promtool/)
- [HTTP API](https://prometheus.io/docs/prometheus/latest/querying/api/)
- [Management API](https://prometheus.io/docs/prometheus/latest/management_api/)
- [Feature flags](https://prometheus.io/docs/prometheus/latest/feature_flags/)
- [Storage](https://prometheus.io/docs/prometheus/latest/storage/)
- [Migración a Prometheus 3](https://prometheus.io/docs/prometheus/latest/migration/)
- [Release cycle y LTS](https://prometheus.io/docs/introduction/release-cycle/)
- [Prácticas: alerting](https://prometheus.io/docs/practices/alerting/)
- [Prácticas: recording rules](https://prometheus.io/docs/practices/rules/)
- [Alerting overview](https://prometheus.io/docs/alerting/latest/overview/)
- [Dockerfile, v3.13.5](https://github.com/prometheus/prometheus/blob/v3.13.5/Dockerfile)
- [Releases de Prometheus](https://github.com/prometheus/prometheus/releases)
- [otlptranslator](https://github.com/prometheus/otlptranslator)

**Otras herramientas**
- [VictoriaMetrics: integración OpenTelemetry](https://docs.victoriametrics.com/victoriametrics/integrations/opentelemetry/)
- [VictoriaMetrics: vmalert](https://docs.victoriametrics.com/victoriametrics/vmalert/)
- [VictoriaMetrics: vmalert-tool](https://docs.victoriametrics.com/victoriametrics/vmalert-tool/)
- [Sloth: introduction](https://sloth.dev/introduction/), [CLI](https://sloth.dev/usage/cli/), [README v0.16.0](https://github.com/slok/sloth/blob/v0.16.0/README.md), [ventanas google-30d](https://github.com/slok/sloth/blob/v0.16.0/internal/alert/windows/google-30d.yaml), [google-28d](https://github.com/slok/sloth/blob/v0.16.0/internal/alert/windows/google-28d.yaml), [ejemplo](https://github.com/slok/sloth/blob/v0.16.0/examples/getting-started.yml), [salida generada](https://github.com/slok/sloth/blob/v0.16.0/examples/_gen/getting-started.yml), [manifiesto con la imagen](https://github.com/slok/sloth/blob/v0.16.0/deploy/kubernetes/raw/sloth.yaml)
- [Pyrra README v0.10.2](https://github.com/pyrra-dev/pyrra/blob/v0.10.2/README.md)
- [Aspire: standalone dashboard](https://aspire.dev/dashboard/standalone/)
- Licencias de [grafana/mimir](https://github.com/grafana/mimir), [grafana/grafana](https://github.com/grafana/grafana) y [grafana/alloy](https://github.com/grafana/alloy) (vía la API de GitHub)

**Runbooks**
- [prometheus-operator/runbooks: sitio](https://runbooks.prometheus-operator.dev/)
- [prometheus-operator/runbooks: add-runbook.md (plantilla)](https://github.com/prometheus-operator/runbooks/blob/main/content/docs/add-runbook.md)
- [prometheus-operator/runbooks: KubePodCrashLooping](https://github.com/prometheus-operator/runbooks/blob/main/content/runbooks/kubernetes/KubePodCrashLooping.md)
- [kubernetes-mixin: add-runbook-links.libsonnet (commit fijado)](https://github.com/kubernetes-monitoring/kubernetes-mixin/blob/2002557eb82c0795cb03f01ba15245786f55c0db/lib/add-runbook-links.libsonnet)
