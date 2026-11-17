# ApiLatencyBudgetBurn

## Significado

Más requests de los que permite el SLO de latencia tardan más de 500 ms (meta: 99 % en 500 ms o menos en 30 días, ver [slos.md](../slos.md)). Los endpoints de health no cuentan. Un request lento cuenta aunque termine en 200.

- `severity=page`: a este ritmo el presupuesto de 30 días se gasta en horas o pocos días (burn rate 14,4 en 1h y 5m, o 6 en 6h y 30m). Hay que mirarlo ahora.
- `severity=ticket`: el ritmo gasta el presupuesto antes de fin de mes (burn rate 3 en 1d y 2h, o 1 en 3d y 6h). Se arregla en horario de trabajo.

## Impacto

La app funciona pero se siente trabada: listas que tardan, botones que esperan. Si la lentitud crece, los requests empiezan a vencer y la falla pasa a gastar también el presupuesto de disponibilidad.

## Diagnóstico

1. Ver qué rutas están lentas. El percentil 99 por ruta, en Prometheus (`http://localhost:9090/graph`):

   ```promql
   histogram_quantile(0.99, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket{job="templateapp-api", http_route!~".*/health/.*"}[5m])))
   ```

   Si todas las rutas con sesión están lentas a la vez, la causa suele estar en la base: cada request valida la sesión contra PostgreSQL.

2. Ver si la base responde. Readiness corre `SELECT 1` con un límite de 2 s:

   ```sh
   curl -s -w "\n%{time_total}s\n" http://localhost:8080/api/v1/health/ready
   ```

   Un readiness que tarda cerca de 2 s o responde `database: unhealthy` apunta a la base.

3. Buscar consultas trabadas en PostgreSQL:

   ```sh
   docker compose exec db psql -U postgres -d templateapp -c "select pid, state, now() - query_start as duracion, left(query, 80) from pg_stat_activity where state <> 'idle' order by duracion desc;"
   ```

4. Mirar una traza lenta. Los spans de Npgsql de `otel-export/traces.json` muestran cuánto tardó cada consulta dentro del request.

5. Ver el estado de los contenedores: `docker compose ps` (¿la base está `Paused`?) y `docker stats --no-stream` (CPU o memoria al límite).

## Mitigación

- Base congelada: `docker compose unpause db`. Los requests en espera terminan solos.
- Consulta trabada: cancelarla con `select pg_cancel_backend(<pid>);` y buscar por qué tardó (índice faltante, bloqueo).
- Contenedor sin recursos: darle más CPU o memoria, o reducir la carga.
- Después de arreglar la causa, la página no se apaga enseguida: la condición de 1h y 5m cae en unos 5 minutos, pero la de 6h y 30m sigue hasta que la ventana de 30 minutos baja del umbral, hasta media hora. El ticket tarda más, porque mira 2h y 6h.

## Cómo probarla

- Unitaria: `promtool test rules deploy/prometheus/rules/slo-api.test.yml`. Los casos "requests lentos disparan la pagina y el ticket de latencia", "una caida total dispara la pagina y el ticket de disponibilidad, no los de latencia" (los 500 rápidos no cuentan como lentos) y "los 503 de readiness no cuentan contra el SLO" cubren esta alerta.
- En vivo: `node .agents/skills/verify-template/scripts/check-slo-alerts.mjs` con el stack arriba. La fase "base congelada 3 s: requests 200 lentos" congela `db` 3 s por vuelta, manda requests con sesión que terminan en 200 después de la espera, y espera esta alerta en `firing` con `page` y `ticket`, sin que salte la de disponibilidad.
