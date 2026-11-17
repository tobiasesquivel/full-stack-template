# SLOs de la API

Este documento fija qué promete la API, cómo se mide y qué pasa cuando el servicio gasta el presupuesto de error. Las reglas que lo hacen ejecutable están en [`deploy/prometheus/rules/slo-api.rules.yml`](../../deploy/prometheus/rules/slo-api.rules.yml). Cada alerta tiene su runbook en [`runbooks/`](runbooks/). La investigación que sostiene cada número está en [paso-17-slos-runbooks.md](../evolucion/investigacion/paso-17-slos-runbooks.md).

## Palabras

- **SLI** (indicador de nivel de servicio): una proporción medida, por ejemplo "requests sin 5xx sobre requests totales".
- **SLO** (objetivo de nivel de servicio): la meta para ese SLI en una ventana, por ejemplo "99,5 % en 30 días".
- **Presupuesto de error**: lo que el SLO deja fallar. Con 99,5 %, el presupuesto es el 0,5 % de los requests de 30 días.
- **Burn rate** (velocidad de gasto): cuántas veces más rápido que lo permitido se gasta el presupuesto. Un burn rate de 1 lo gasta justo en 30 días; uno de 14,4 lo gasta en dos días.

## Servicio y requests que cuentan

- Servicio: la API (`job="templateapp-api"` en Prometheus, que sale de `service.name`).
- Cuentan todos los requests salvo los de `/api/v1/health/*`. Las sondas de liveness y readiness no son tráfico de usuario, y readiness responde 503 a propósito cuando una dependencia cae.
- Métrica: el histograma `http.server.request.duration` que ASP.NET Core emite para cada request. Llega a Prometheus como `http_server_request_duration_seconds_count` y `_bucket`, con las etiquetas `http_route` y `http_response_status_code`.

## SLOs

| SLO | SLI (bueno / total) | Meta | Ventana | Presupuesto |
|---|---|---|---|---|
| Disponibilidad | requests sin 5xx / requests | 99,5 % | 30 días móviles | 0,5 % de los requests |
| Latencia | requests en 500 ms o menos / requests | 99 % | 30 días móviles | 1 % de los requests |

- Un 4xx y un 429 son respuestas correctas del servicio: el cliente pidió algo inválido o se pasó del límite. Solo un 5xx gasta el presupuesto de disponibilidad.
- Un 500 rápido es bueno para latencia y malo para disponibilidad. Un 200 lento es al revés. Cada SLO mide una sola cosa.
- 500 ms es un límite de bucket del histograma de ASP.NET Core (`0.25`, `0.5`, `0.75`). Un umbral que no es límite de bucket no se puede medir sin cambiar los buckets.
- Las metas son flojas a propósito: es un template sin tráfico real. Se ajustan cuando haya datos de uso.

## Cómo se consulta cada SLI

Prometheus guarda cada SLI ya calculado en reglas de grabación, una por ventana (5m, 30m, 1h, 2h, 6h, 1d, 3d). El valor es la proporción de requests malos: 0 es perfecto, 1 es todo malo.

```promql
job:slo_api_availability_errors:ratio_rate1h
job:slo_api_latency_errors:ratio_rate1h
```

La regla se escribe como `1 - buenos / total`. Así vale 0 cuando hay tráfico sin errores; con `malos / total`, si nunca hubo un 5xx, la serie de 5xx no existe y la consulta vuelve vacía.

El presupuesto consumido en 30 días (1 = gastado entero):

```promql
(1 - sum(increase(http_server_request_duration_seconds_count{job="templateapp-api", http_route!~".*/health/.*", http_response_status_code!~"5.."}[30d]))
   / sum(increase(http_server_request_duration_seconds_count{job="templateapp-api", http_route!~".*/health/.*"}[30d]))) / 0.005
```

Con Compose, la interfaz de Prometheus está en `http://localhost:9090` (o el puerto de `PROMETHEUS_PORT`).

## Alertas

Alertas multiventana de burn rate, la forma que recomienda el SRE Workbook de Google. Cada condición pide que la ventana larga **y** la corta pasen el umbral: la larga prueba que se gastó presupuesto de verdad, la corta prueba que sigue pasando ahora (y apaga la alerta rápido cuando se arregla).

| Alerta | Severidad | Ventanas | Burn rate | Disponibilidad: 5xx más que | Latencia: lentos más que |
|---|---|---|---|---|---|
| Gasta 2 % del presupuesto en 1 h | `page` | 1h y 5m | 14,4 | 7,2 % | 14,4 % |
| Gasta 5 % del presupuesto en 6 h | `page` | 6h y 30m | 6 | 3 % | 6 % |
| Gasta 10 % del presupuesto en 1 día | `ticket` | 1d y 2h | 3 | 1,5 % | 3 % |
| Gasta 10 % del presupuesto en 3 días | `ticket` | 3d y 6h | 1 | 0,5 % | 1 % |

- `page` = hay que mirarlo ahora. `ticket` = se arregla en horario de trabajo.
- Cada SLO tiene **un** nombre de alerta (`ApiAvailabilityBudgetBurn`, `ApiLatencyBudgetBurn`) con dos severidades. Así cada SLO tiene un solo runbook.
- No usan `for`: las ventanas ya filtran los picos cortos.
- No hay Alertmanager: las alertas se ven en Prometheus (`/alerts` en la interfaz, `GET /api/v1/alerts` en la API). Nadie recibe una notificación todavía.

## Política de presupuesto

- Presupuesto con saldo: se despliega normalmente.
- Presupuesto gastado en la ventana de 30 días: se frenan los cambios que no arreglan confiabilidad hasta que el SLI vuelva a la meta, y el incidente que lo gastó tiene su post-mortem sin culpables.

## Límites conocidos

- **Poco tráfico.** Con pocos requests, un solo error mueve mucho la proporción y puede disparar una página. El template casi no tiene tráfico fuera de las pruebas. El SRE Workbook sugiere tráfico sintético o ventanas más largas; queda pendiente.
- **El primer valor de cada serie se pierde.** Prometheus no agrega un cero inicial a las series que llegan por OTLP, y `rate()` necesita dos muestras. Una ruta con un solo request no cuenta hasta su segundo export.
- **API caída sin alerta.** Con OTLP no hay serie `up`: si la API o el collector dejan de mandar métricas, el SLI queda vacío y ninguna alerta salta. Queda pendiente.
- **Retención.** Prometheus guarda 35 días (`--storage.tsdb.retention.time=35d` en `compose.yaml`). Con el valor por defecto, 15 días, la ventana de 30 días no entraría.

## Cómo se prueba

- `promtool test rules deploy/prometheus/rules/slo-api.test.yml`: series sintéticas prueban que la caída total pagina, que un pico corto no, que una quema lenta abre un ticket, que los 503 de readiness, los 4xx y los 429 no cuentan, y que los requests lentos disparan la alerta de latencia.
- `node dune/check-runbooks.mjs`: toda alerta enlaza su runbook, el runbook existe y tiene sus secciones, y no hay runbooks huérfanos.
- `node .agents/skills/verify-template/scripts/check-slo-alerts.mjs`: con el stack de Compose arriba, consulta cada SLI en Prometheus, congela la base para producir requests lentos y la para para producir 5xx, y espera cada alerta en `firing`.

Revisión: 2027-05-17, o antes si cambia el tráfico.
