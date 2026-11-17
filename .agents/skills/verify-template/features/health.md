# Health

Health distingue un proceso que responde de un backend preparado para atender requests con PostgreSQL disponible.

## Sub-features

- `health-live` responde sin consultar PostgreSQL.
- `health-ready` confirma PostgreSQL y migraciones, y nombra cada dependencia en `checks` (`database`, `migrations`) con `healthy` o `unhealthy`.
- `health-db-down` mantiene liveness y devuelve readiness 503 con `checks.database = unhealthy` cuando PostgreSQL cae.

## How to get to it (user POV)

- Consulta `GET /api/v1/health/live` desde un cliente HTTP.
- Consulta `GET /api/v1/health/ready` desde un cliente HTTP.

## Driving it with verify-template

Preconditions:

- `launch` publicó `VERIFY_TEMPLATE_READY`.
- `doctor --run-id repro-template` identifica la instancia propia.

Ejecuta `node .agents/skills/verify-template/scripts/verify.mjs api --run-id repro-template`. El smoke exige 200 en ambos endpoints antes de autenticar un usuario. Conserva la URL, status y respuesta. Un socket abierto no demuestra readiness.

Para la caída de base, usa únicamente el contenedor propio identificado en `run.json`. Ejecuta `docker stop` con ese nombre. Consulta ambos endpoints en la URL propia. Exige live 200, ready 503 y `checks.database = unhealthy`. En un bloque `finally`, ejecuta `docker start` con el mismo nombre y espera ready 200. Captura ambas respuestas y los comandos. No detengas el servicio `db` de otro Compose ni la base del desarrollador.

## Gotchas

- Health fijo en 200 no prueba la dependencia.
- El body de readiness no lleva descripciones ni excepciones. El motivo de una falla está en el log de la API (`Health check database …`), no en la respuesta.
- La inicialización puede fallar antes de publicar endpoints. Ese caso es fallo de arranque, no readiness 503 observado.
- Un fallo de build o tests marca los tres recorridos `NOT_RUN`. La evidencia del fallo anterior sigue siendo el producto del repro.
