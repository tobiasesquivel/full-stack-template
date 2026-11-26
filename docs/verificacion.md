# Verificación

Cómo se comprueba que un cambio no rompe nada. Esta es la lista única de comandos de verificación: `AGENTS.md`, la plantilla de PR y `check-all.mjs` apuntan acá.

## Ejecutar gates

Esta es la lista única de comandos de verificación. Ejecutalos sin desactivar analyzers ni omitir tests.

Un solo comando los corre todos en este orden, y después el runtime de la skill `verify-template`: `launch`, `doctor`, `api`, las e2e y `cleanup`. Desde la raíz:

```sh
E2E_CHANNEL=msedge node .agents/skills/verify-template/scripts/check-all.mjs --run-id <id>
```

Deja `summary.json` en `artifacts/verify-template/<id>/`, con `PASS`, `FAIL` o `NOT_RUN` por etapa y un log por comando. El primer `FAIL` corta la corrida: las etapas siguientes quedan `NOT_RUN`. `cleanup` corre siempre después de `launch`. `check-host-items` le pide a MSBuild la lista de `Compile` que evalúa cada host en Release y en Debug (`dotnet msbuild -getItem:Compile`, sin correr targets), con la ruta física de cada archivo, y falla si el monolito tiene algo de `Features/Tasks`, si el host de Tasks tiene otra feature que no sea Tasks o Health, o si alguno tiene `Infrastructure/`, `Shared/`, tests o un archivo de otra carpeta: así ve lo que llega por propiedades, `Import` y `Directory.Build.*`. Además, DUNE0032 (`HostBoundaryAnalyzer`) revisa en cada build los archivos que el compilador recibe de verdad en cada host, después de MSBuild: lo que agrega un target, una condición, un `Import` o `Directory.Build.*` ya está resuelto, y también ve las `ProjectReference` (entre los proyectos del repo, un host referencia solo `TemplateApp.Platform`). `check-host-items` sigue porque mira la ruta física detrás de un enlace, compara Release con Debug y revisa las `ProjectReference` evaluadas: un host referencia solo `backend/Platform/TemplateApp.Platform.csproj` (y el analyzer de Dune), también con `ReferenceOutputAssembly="false"`. `Dune.props` carga Dune en todo proyecto de `backend/Hosts/`, así un host nuevo falla con DUNE0032 hasta entrar en su tabla. DUNE0032 no ve un build que no carga Dune, como el del Dockerfile, ni un archivo que solo un target de Debug agrega (el CI compila en Release, que es lo que se despliega). `check-codegen` corre `codegen write` de Wolverine sobre el build Release y falla si el código de `backend/Hosts/Monolith/Internal/Generated/` o de `backend/Hosts/Tasks/Internal/Generated/` cambia: ese código se versiona (paso 27) y nadie lo edita a mano. `--gates-only` corre solo los comandos de esta sección; `--no-e2e` omite las e2e. Los comandos sueltos siguen valiendo para repetir una etapa. Las pruebas de integración necesitan Docker para PostgreSQL real. `check-comments` necesita las dependencias de `frontend/node_modules`, por eso va después de `npm ci`. `check-secrets` necesita gitleaks 8.30.1 en el `PATH` (en Windows: `winget install --id Gitleaks.Gitleaks --version 8.30.1 --exact`; o la ruta del ejecutable en `GITLEAKS_BIN`). Revisa el historial alcanzable desde `HEAD` y los cambios sin commitear; la auditoría NuGet y `npm audit` fallan ante un aviso alto o crítico en una dependencia de producción. CI corre lo mismo en el job `security`. `check-runbooks` también necesita `frontend/node_modules` (lee el YAML de las reglas) y falla si una alerta no enlaza su runbook, si el runbook no existe o le falta una sección, o si un runbook no tiene alerta. Los dos `promtool` usan la imagen de Prometheus de `compose.yaml` (`check-all` y CI la leen de ahí): validan la configuración y las reglas con lint fatal y corren las pruebas unitarias de las alertas. CI los corre en el job `slos`.

Desde la raíz:

```sh
dotnet build TemplateApp.slnx --configuration Release
node dune/gates/check-codegen.mjs --release
node --test dune/gates/check-codegen.test.mjs
node dune/gates/check-host-items.mjs
node --test dune/gates/check-host-items.test.mjs
dotnet test TemplateApp.slnx --configuration Release
dotnet run --project dune/Dune.CommentVerifier -- --root .
node dune/gates/check-structure.mjs
node --test dune/gates/check-structure.test.mjs
node dune/gates/check-secrets.mjs
node --test dune/gates/check-secrets.test.mjs
node --test .agents/skills/verify-template/scripts/check-otel-export.test.mjs
dotnet restore TemplateApp.slnx --force "-warnaserror:NU1903;NU1904"
npm ci --prefix frontend
npm audit --prefix frontend --omit=dev --audit-level=high
node dune/gates/check-comments.mjs --root .
node --test dune/gates/check-comments.test.mjs
node dune/gates/check-runbooks.mjs
node --test dune/gates/check-runbooks.test.mjs
docker run --rm --entrypoint promtool --volume "$PWD/deploy/prometheus:/etc/prometheus:ro" prom/prometheus:v3.13.5 check config --lint-fatal /etc/prometheus/prometheus.yml
docker run --rm --entrypoint promtool --volume "$PWD/deploy/prometheus:/etc/prometheus:ro" prom/prometheus:v3.13.5 test rules /etc/prometheus/rules/slo-api.test.yml
```

Backend usa Microsoft Testing Platform (MTP): `dotnet test` ejecuta el runner MTP de `backend/Tests/TemplateApp.Tests.csproj` y de `dune/Dune.Analyzers.Tests/Dune.Analyzers.Tests.csproj` (xunit.v3). No hay `vstest` ni adaptador clásico; los proyectos Dune viven bajo `dune/`, no bajo `backend/`, y los gates de Node con sus tests, en `dune/gates/`. La política MSBuild central vive en `dune/Dune.props`; `Directory.Build.props` solo la importa. Los símbolos prohibidos viven en `dune/BannedSymbols.txt`.

`npm run format:check` corre `prettier --check .` con `frontend/.prettierrc`: falla si un archivo del frontend no tiene el formato de Prettier. `npm run format` lo arregla. El frontend tiene una suite de Vitest con jsdom: funciones puras y componentes de presentación, cada spec junto a su archivo (`*.spec.ts`). No reemplaza la prueba en el navegador contra la API: registro, login, restauración tras recargar, perfil, tareas y logout. Desde `frontend`, compilá, corré la suite y el gate:

```sh
npm run build
npm run format:check
npm test -- --watch=false
node --test ../dune/gates/check-frontend.test.mjs
node ../dune/gates/check-frontend.mjs --root ..
```

Para levantar una instancia aislada y probar la API real, usá la skill `verify-template` ([SKILL.md](../.agents/skills/verify-template/SKILL.md)): `launch`, `doctor`, `api` y `cleanup`. `launch` arranca la API con un período de gracia de 20 segundos y recordatorios a los 5 y 10, para que el smoke y las e2e vean el cierre completo. Con el conjunto Docker iniciado, el escenario HTTP también corre directo desde la raíz; el smoke cierra cuentas de punta a punta y falla si el período de gracia pasa de 2 minutos, así que levantá Compose con `ACCOUNT_CLOSURE_GRACE_SECONDS=20`, `ACCOUNT_CLOSURE_FIRST_REMINDER_SECONDS=5` y `ACCOUNT_CLOSURE_SECOND_REMINDER_SECONDS=10`, como hace el job `compose` de CI:

```sh
node .agents/skills/verify-template/scripts/smoke-api.mjs
node .agents/skills/verify-template/scripts/smoke-api.mjs --compose
node .agents/skills/verify-template/scripts/check-slo-alerts.mjs slo-check
docker compose down --timeout 30 --volumes
node .agents/skills/verify-template/scripts/check-otel-export.mjs otel-export
```

El recorrido del navegador vive como specs de Playwright Test en `frontend/e2e/`, por feature: `auth`, `session`, `tasks`, `profile` y `tabs`. Corre contra una instancia de `verify-template`: tomá `frontUrl` de `run.json` y esperá 65 segundos después del smoke HTTP, porque el limiter de `/auth` admite 20 requests por minuto por IP y el smoke y el navegador llegan a la API desde la misma IP (`127.0.0.1`). Desde `frontend`:

```sh
E2E_BASE_URL=<frontUrl> E2E_CHANNEL=msedge npm run e2e
```

`E2E_CHANNEL=msedge` usa el Edge instalado; sin la variable, Playwright usa su Chromium (`npx playwright install chromium`). Las pruebas corren en serie, con un solo worker, y antes de cada una esperan hasta tener lugar en la ventana del limiter. `E2E_OUTPUT_DIR` elige dónde quedan el reporte JSON, las capturas y los traces de las fallas. Una ruta relativa se resuelve desde la carpeta donde corrés el comando: desde `frontend`, `../artifacts/...` queda en `artifacts/` de la raíz. CI todavía no corre e2e.

El escenario HTTP no reemplaza la prueba Angular contra la API real. Comprueba registro, login, restauración, perfil, tareas y logout en el navegador. Las pruebas backend deben observar aislamiento 403, ausencia 404, permisos vigentes en el siguiente request, sesión cerrada o borrada que da 401, antiforgery en toda escritura, registro duplicado sin filas parciales y cascadas. No sustituyas esos checks por búsquedas de texto del source.

Comprueba `GET /api/v1/health/live` y `/ready` antes y después de interrumpir PostgreSQL en un conjunto de prueba. Captura logs, métricas y spans recibidos por el collector y su correlación. Registrar exporters en DI no prueba exportación. No publiques secrets en la evidencia.

`deploy/otel-collector-config.yaml` configura el collector. Con `OTEL_EXPORT_DIR` sin cambio, busca `otel-export/logs.json`, `metrics.json` y `traces.json`, archivos JSONL escritos por el collector. El smoke de Compose espera hasta 30 segundos el flush de batch y comprueba las métricas HTTP y de negocio. La correlación se comprueba después de `docker compose down`, porque al apagarse la API y el collector vacían sus buffers y el export queda completo: `check-otel-export.mjs` falla si no hay logs de request (los que llevan `RequestId`), si uno no trae `traceId` y `spanId` válidos, si apunta a un span que el collector no recibió, si no hay spans SERVER, si un span SERVER no tiene su log de request, si no llegan métricas o una llega sin puntos, o si una línea del export quedó cortada. Deja `otel-check.json` en la carpeta del export. El collector agrega al final de cada archivo: usá una carpeta vacía por corrida, porque los datos de una corrida vieja tapan o ensucian el resultado. El job `compose` de CI lo corre igual y publica esa carpeta. Revisa también la consola de `docker compose logs otel`. No confundas archivos creados pero vacíos con señales observadas.

`check-slo-alerts.mjs` es la prueba en vivo de los SLOs, con el stack recién levantado y después del smoke (`PROMETHEUS_URL`, por defecto `http://localhost:9090/`). Comprueba que Prometheus cargó las reglas sin error, consulta cada SLI y la métrica cruda, y pide que no haya alertas con tráfico sano. Después congela `db` 3 s por vuelta (requests 200 lentos) hasta ver `ApiLatencyBudgetBurn` en `firing`, sin `ApiAvailabilityBudgetBurn`; y para `db` (5xx) hasta ver `ApiAvailabilityBudgetBurn`. Cada alerta tiene que llegar con `page`, `ticket` y su `runbook_url`. Tarda unos 3 minutos (manda 200 requests sanos antes de las fallas, para que un request lento del arranque no dispare el ticket) y deja `slo-check.json` en la carpeta que recibe. Necesita un stack sin alertas previas: con un stack viejo, bajalo con `--volumes`. CI la corre en el job `compose` y publica `slo-check/`.

