# TemplateApp full-stack

Usa este monorepo como Git template para una aplicación con registro, sesión, perfil y tareas personales. Backend ASP.NET Core 10, frontend Angular 22 y PostgreSQL comparten contratos en OpenSpec.

## Estado de esta entrega

Verificación real 2026-10-05: backend 24/24 pass (reporte parent artifact://611); Docker `up --build` final con éxito (`artifacts/compose-final/compose-export-fix.stdout.log` + `.stderr.log`); smoke `--compose` exit 0 (`artifacts/compose-final/compose-smoke-fixed.stdout.log`) con health live 200, ready 200, tras caída de `db` live 200 y ready 503, reinicio con persistencia y OTLP `correlatedLogs true` + `httpDurationMetric true`; navegador PASS (`artifacts/compose-final/browser-result.json`, `browser-network.json`, `tasks.png`) con register, create, dos reloads sin perder login, checkbox persiste, perfil persiste, logout y login. Dune 22/22 PASS local (artifact://640); estructura 5/5 + check-structure OK + Angular build PASS (mismo 640). Gate duro real: EF DbContext en Domain throwaway → `dotnet build` backend exit 1 error DUNE0001 (`artifacts/compose-final/dune-negative-build.stdout.log`); throwaway removido y `dotnet build TemplateApp.slnx` exit 0, 0 errores (artifact://643). Docker smoke 49 PASS, API UID 1654. Backend Dockerfile usa `USER $APP_UID` (usuario `app` builtin, `adduser` ausente en .NET 10). OTel exporters usan base + `/v1/traces` y `/v1/metrics` porque el setter `Endpoint` no agrega ruta. Migración única `InitialCreate` generada por CLI en commit `6687af8`, sin edición manual; original no tocado; ADR en commit `55639b7`. Commits cierre: backend `b8c4236`, Angular `7ba6a4c`, CI `5363ec4`, helper `5cd9ca3`. GitHub Actions no ejecutado; sin afirmación de cero warnings. Un fallo reproducido y su evidencia son resultados válidos de la verificación, no un gate aprobado.

## Crear tu repositorio

Crea un repositorio desde la copia completa mediante la opción GitHub **Use this template**, si el repositorio remoto tiene activada esa opción. Si trabajas con una copia local, copia el monorepo sin `.git`, `node_modules`, `bin`, `obj`, `dist`, `.angular`, `.env` ni resultados de pruebas e inicializa un repositorio nuevo. No copies solo `backend`.

`TemplateApp` es el nombre y namespace de partida. Si lo cambias, actualiza proyectos, solución, namespaces, configuración, tests y referencias del monorepo en el mismo cambio. Ejecuta los gates después. No hay distribución `dotnet new cleanapi`, feature flags ni opción includeSample. Tasks es una capacidad real del template, no un stub.

## Preparar herramientas y secrets

Instala SDK .NET 10.0.401, Node 26.7 y Docker con Compose v2 y soporte de contenedores Linux. Las versiones Microsoft runtime y paquetes son 10.0.12. Angular está fijado en 22.2.1. Respeta `global.json` y `frontend/package-lock.json`.

Genera una clave aleatoria para JWT con Node. Guarda el resultado en tu secret store, nunca en el repositorio.

```sh
node -e "console.log(require('node:crypto').randomBytes(48).toString('base64'))"
```

Para ejecutar el backend fuera de Compose, configura `Jwt__Key` y `ConnectionStrings__DefaultConnection` en el entorno del proceso. La conexión debe apuntar a PostgreSQL con una base y credenciales reales. Los dos guiones bajos representan la jerarquía de configuración ASP.NET. No hay secrets de respaldo en appsettings.

Para Compose, copia `.env.example` a `.env`. Sustituye los valores inválidos de `POSTGRES_PASSWORD` y `JWT_KEY` por secrets propios antes de iniciar. JWT_KEY requiere al menos 32 caracteres. `.env` no debe versionarse. Los demás nombres de configuración son `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PORT`, `API_PORT`, `WEB_PORT`, `JWT_ISSUER`, `JWT_AUDIENCE` y `OTEL_EXPORT_DIR`. `docker compose config` puede imprimir secrets interpolados. No adjuntes su salida completa como evidencia pública.

## Ejecutar con Docker

Desde la raíz, después de configurar `.env`, ejecuta los comandos siguientes.

```sh
docker compose config
docker compose up --build --wait
```

El conjunto incluye PostgreSQL persistente, collector OpenTelemetry, API y frontend. El arranque aplica migraciones y seed idempotente. Un error de migración o configuración no es un arranque saludable. No borres el volumen para ocultar errores ni uses EnsureCreated.

Con los puertos de partida, abre `http://localhost:8081` para el frontend. La API está en `http://localhost:8080/api/v1`. `web` hace proxy al servicio `api`. `db` usa PostgreSQL 18 con volumen `pgdata` montado en `/var/lib/postgresql`. `otel` recibe OTLP en 4317 y 4318. Si cambias puertos en `.env`, usa los valores configurados.

Para detener contenedores conservando los datos, ejecuta el comando siguiente.

```sh
docker compose down
```

## Ejecutar con procesos locales

Mantén PostgreSQL accesible y configura las variables de entorno anteriores. Desde la raíz, inicia la API en el puerto que usa el proxy Angular.

```sh
dotnet run --project backend/TemplateApp.csproj --no-launch-profile --urls http://localhost:8080
```

En otro terminal, desde `frontend`, instala las dependencias fijadas y sirve Angular con el proxy.

```sh
npm ci
npm start -- --proxy-config proxy.conf.json
```

Abre `http://localhost:4200`. Las peticiones `/api/v1` usan el proxy a la API local. No configures orígenes CORS abiertos para sustituir este diseño. Las cookies Secure requieren HTTPS fuera de Development.

## Usar la aplicación

Regístrate con nombre de login, email, contraseña y nombre visible. Inicia sesión por nombre de login. Edita el nombre visible y una zona horaria IANA del perfil. Crea tareas, cambia título y estado final, recorre páginas y elimina tareas. Recarga para comprobar restauración de sesión y cierra sesión para revocar la familia actual.

Si consumes HTTP directamente, primero pide `GET /api/v1/auth/csrf` y conserva cookies. Envía el valor de `XSRF-TOKEN` como `X-XSRF-TOKEN` en register, login, refresh y logout. Usa el access token como Bearer para Users y Tasks. Refresh y logout tienen body vacío y no necesitan un access token vigente. No leas la cookie HttpOnly ni envíes refresh tokens en JSON.

Los errores usan ProblemDetails. Un recurso inexistente devuelve 404 y un recurso ajeno sin permiso devuelve 403. La lista Tasks siempre es personal, incluso para un administrador. Consulta los contratos [Auth](openspec/specs/auth/spec.md), [Users](openspec/specs/users/spec.md) y [Tasks](openspec/specs/tasks/spec.md) para payloads y estados exactos.

## Ejecutar gates

Ejecuta los gates sin desactivar analyzers ni omitir tests. Verificación real 2026-10-05: backend 24/24 pass (reporte parent artifact://611); Docker `up --build` final con éxito; smoke `--compose` exit 0 con OTLP `correlatedLogs true` + `httpDurationMetric true`; navegador PASS con register, create, dos reloads, checkbox y perfil persistentes, logout y login. Dune 22/22 PASS local (artifact://640) con gate duro real DUNE0001 verificado en throwaway (`artifacts/compose-final/dune-negative-build.stdout.log`) y `dotnet build TemplateApp.slnx` exit 0, 0 errores (artifact://643). GitHub Actions no ejecutado; sin afirmación de cero warnings.

Desde la raíz, ejecuta estructura, compilación y pruebas backend. Las pruebas de integración necesitan Docker para PostgreSQL real.

```sh
node tools/check-structure.mjs
node --test tools/check-structure.test.mjs
dotnet build TemplateApp.slnx --configuration Release
dotnet test TemplateApp.slnx --configuration Release
```

Backend usa Microsoft Testing Platform (MTP): `dotnet test` ejecuta el runner MTP de `backend/Tests/TemplateApp.Tests.csproj` y de `tools/Dune.Analyzers.Tests/Dune.Analyzers.Tests.csproj` (xunit.v3). No hay `vstest` ni adaptador clásico; los proyectos Dune viven bajo `tools/`, no bajo `backend/`.

No hay suite frontend vigente: no existe `*.spec.ts` en `frontend/` (`app.spec` fue eliminado de forma incidental) y `npm test` (`ng test`) no tiene runner configurado. La prueba real es el navegador contra la API: registro, login, restauración tras dos reloads, perfil, tareas y logout (`artifacts/compose-final/browser-result.json`, `browser-network.json`, `tasks.png`). Desde `frontend`, instala lo fijado y compila:

```sh
npm ci
npm run build
```

Con el conjunto Docker iniciado, ejecuta el escenario HTTP desde la raíz.

```sh
node .agents/skills/verify-template/scripts/smoke-api.mjs
node .agents/skills/verify-template/scripts/smoke-api.mjs --compose
```

El escenario HTTP no reemplaza la prueba Angular contra la API real. Comprueba registro, login, restauración, perfil, tareas y logout en el navegador. Las pruebas backend deben observar aislamiento 403, ausencia 404, permisos vigentes en el siguiente request, replay por familia, registro duplicado sin filas parciales y cascadas. No sustituyas esos checks por búsquedas de texto del source.

Comprueba `GET /api/v1/health/live` y `/ready` antes y después de interrumpir PostgreSQL en un conjunto de prueba. Observado 2026-10-05: live 200 y ready 200 en servicio, tras caída de `db` live 200 y ready 503, con reinicio y persistencia (`artifacts/compose-final/compose-smoke-fixed.stdout.log`, `.stderr.log`). Captura logs, métricas y spans recibidos por el collector y su correlación. Registrar exporters en DI no prueba exportación. No publiques secrets en la evidencia.

`deploy/otel-collector-config.yaml` configura el collector. Con `OTEL_EXPORT_DIR` sin cambio, busca `otel-export/logs.json`, `metrics.json` y `traces.json`, archivos JSONL escritos por el collector. El flush de batch puede requerir hasta 30 segundos después de una petición. Observa `http.server.request.duration` y correlaciona traceId de un log con un span recibido. Revisa también la consola de `docker compose logs otel`. No confundas archivos creados pero vacíos con señales observadas.

## Cambiar un contrato

Entrega un PR atómico por cambio observable. Incluye backend, frontend, tests y spec juntos cuando cambie HTTP. No impongas límite artificial de líneas ni uses continue-on-error. Adjunta comandos realmente ejecutados, resultados y evidencia de fallos. Un PR sin runtime observado no puede afirmar runtime verificado.

Lee [arquitectura](docs/architecture.md), [convenciones](docs/conventions.md), [ADR-0003](docs/adr/0003-full-stack-template-dune.md) y [Dune](openspec/specs/dune/spec.md). [CONTEXT](CONTEXT.md) es el glosario del dominio. [Changelog](docs/changelog.md) describe la consolidación. [Archivo OpenSpec](openspec/changes/archive/README.md) conserva historia y supersesiones sin convertirla en evidencia actual.
