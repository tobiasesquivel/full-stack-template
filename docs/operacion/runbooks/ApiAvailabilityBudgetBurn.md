# ApiAvailabilityBudgetBurn

## Significado

La API responde 5xx más seguido de lo que permite el SLO de disponibilidad (99,5 % de requests sin 5xx en 30 días, ver [slos.md](../slos.md)). Los endpoints de health no cuentan.

- `severity=page`: a este ritmo el presupuesto de 30 días se gasta en horas o pocos días (burn rate 14,4 en 1h y 5m, o 6 en 6h y 30m). Hay que mirarlo ahora.
- `severity=ticket`: el ritmo gasta el presupuesto antes de fin de mes (burn rate 3 en 1d y 2h, o 1 en 3d y 6h). Se arregla en horario de trabajo.

## Impacto

Los usuarios ven errores al listar, crear o editar tareas, o al iniciar sesión. Un 5xx no se arregla reintentando desde el navegador si la causa sigue ahí. Mientras dura, el presupuesto baja y deja menos margen para desplegar.

## Diagnóstico

1. Ver qué rutas fallan y con qué código. En Prometheus (`http://localhost:9090/graph`):

   ```promql
   sum by (http_route, http_response_status_code) (rate(http_server_request_duration_seconds_count{job="templateapp-api", http_response_status_code=~"5.."}[5m]))
   ```

   Si fallan todas las rutas que usan sesión, la causa suele ser la base: la cookie de sesión se valida contra PostgreSQL en cada request.

2. Ver qué dependencia está caída. Readiness nombra cada una:

   ```sh
   curl -s http://localhost:8080/api/v1/health/ready
   ```

   `{"status":"not-ready",…,"checks":{"database":"unhealthy",…}}` dice que la base no responde. `migrations: unhealthy` con `database: healthy` dice que hay migraciones pendientes.

3. Leer el motivo en el log. Cada 500 deja un log `ERR` con su excepción y su `TraceId`:

   ```sh
   docker compose logs api --since 15m | grep -E "ERR|Exception"
   ```

   Con el `TraceId` se encuentra la traza completa en el export del collector (`otel-export/traces.json`), incluido el span de Npgsql que falló.

4. Ver si algo cambió: `docker compose ps` (contenedores reiniciados o parados) y el último despliegue.

## Mitigación

- Base parada: `docker compose start db`. La API se reconecta sola; readiness vuelve a 200.
- Migraciones pendientes: reiniciar la API (`docker compose restart api`), que migra al arrancar bajo un lock.
- Error de código después de un despliegue: volver a la imagen anterior de la API.
- Cuando el SLI de 5m vuelve a 0, la página se apaga sola en minutos. El ticket tarda más, porque mira 2h y 6h.
- Una base caída también gasta el presupuesto de latencia: con la base parada cada 500 tarda entre 7,5 y 10 s, porque EF Core reintenta. Puede saltar `ApiLatencyBudgetBurn` al mismo tiempo; se resuelve con la misma causa.

## Cómo probarla

- Unitaria: `promtool test rules deploy/prometheus/rules/slo-api.test.yml`. Los casos "una caida total dispara la pagina y el ticket de disponibilidad", "un pico corto no despierta a nadie", "una quema lenta abre un ticket y no pagina", "los 503 de readiness no cuentan contra el SLO" y "los 4xx y los 429 son respuestas correctas del servicio" cubren esta alerta.
- En vivo: `node .agents/skills/verify-template/scripts/check-slo-alerts.mjs` con el stack arriba. La fase "base parada: 5xx" para `db`, manda requests con sesión y espera esta alerta en `firing` con `page` y `ticket`.
