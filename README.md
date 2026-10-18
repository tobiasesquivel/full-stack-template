# TemplateApp full-stack

Usa este monorepo como Git template para una aplicación con registro, sesión, perfil y tareas personales. Combina backend ASP.NET Core 10, frontend Angular 22 y PostgreSQL.

## Estado

Es un laboratorio de aprendizaje: el dominio es simple a propósito y el foco es el rigor técnico. La hoja de ruta está en [docs/evolucion/plan.md](docs/evolucion/plan.md) y el estado de cada paso en [docs/evolucion/progreso.md](docs/evolucion/progreso.md). La última línea base verificada está en [docs/evolucion/linea-base.md](docs/evolucion/linea-base.md).

## Crear tu repositorio

Crea un repositorio desde la copia completa mediante la opción GitHub **Use this template**, si el repositorio remoto tiene activada esa opción. Si trabajas con una copia local, copia el monorepo sin `.git`, `node_modules`, `bin`, `obj`, `dist`, `.angular`, `.env` ni resultados de pruebas e inicializa un repositorio nuevo. No copies solo `backend`.

`TemplateApp` es el nombre y namespace de partida. Si lo cambias, actualiza proyectos, solución, namespaces, configuración, tests y referencias del monorepo en el mismo cambio. `dune/Dune.props` y las reglas de Dune usan ese nombre: si no los actualizás, Dune deja de aplicarse sin avisar. Ejecuta los gates después. No hay distribución `dotnet new cleanapi`, feature flags ni opción includeSample. Tasks es una capacidad real del template, no un stub.

## Preparar herramientas y secrets

Instala SDK .NET 10.0.401, Node 26.7 y Docker con Compose v2 y soporte de contenedores Linux. Las versiones Microsoft runtime y paquetes son 10.0.12. Angular está fijado en 22.2.1. Respeta `global.json` y `frontend/package-lock.json`.

Para ejecutar el backend fuera de Compose, configura `ConnectionStrings__DefaultConnection` en el entorno del proceso. La conexión debe apuntar a PostgreSQL con una base y credenciales reales. Los dos guiones bajos representan la jerarquía de configuración ASP.NET. No hay secrets de respaldo en appsettings. La sesión no necesita una clave propia: la cookie la cifra Data Protection de ASP.NET Core con sus claves. `DataProtection:KeysDirectory` elige la carpeta de esas claves. Sin ese valor, ASP.NET Core usa la carpeta del perfil del usuario, que sobrevive entre corridas locales. `Session:AbsoluteExpirationDays` y `Session:IdleTimeoutMinutes` fijan el vencimiento absoluto y la inactividad.

Para Compose, copia `.env.example` a `.env`. Sustituye el valor inválido de `POSTGRES_PASSWORD` por un secret propio antes de iniciar. `.env` no debe versionarse. Los demás nombres de configuración son `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PORT`, `API_PORT`, `WEB_PORT` y `OTEL_EXPORT_DIR`. `docker compose config` puede imprimir secrets interpolados. No adjuntes su salida completa como evidencia pública.

## Ejecutar con Docker

Desde la raíz, después de configurar `.env`, ejecuta los comandos siguientes.

```sh
docker compose config
docker compose up --build --wait
```

El conjunto incluye PostgreSQL persistente, collector OpenTelemetry, API y frontend. El arranque aplica migraciones y seed idempotente. Un error de migración o configuración no es un arranque saludable. No borres el volumen para ocultar errores ni uses EnsureCreated.

Con los puertos de partida, abre `http://localhost:8081` para el frontend. La API está en `http://localhost:8080/api/v1`. `web` hace proxy al servicio `api`. `db` usa PostgreSQL 18 con volumen `pgdata` montado en `/var/lib/postgresql`. `api` guarda las claves de Data Protection en el volumen `dataprotection`, montado en `/var/lib/templateapp/data-protection-keys`: recrear el contenedor no cierra las sesiones. `otel` recibe OTLP en 4317 y 4318. Si cambias puertos en `.env`, usa los valores configurados.

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

Abre `http://localhost:4200`. Las peticiones `/api/v1` usan el proxy a la API local. No configures orígenes CORS abiertos para sustituir este diseño. Las cookies de sesión y de antiforgery son `Secure` siempre: piden HTTPS, salvo en `localhost` y `127.0.0.1`, que los navegadores tratan como origen seguro.

## Usar la aplicación

Regístrate con nombre de login, email, contraseña y nombre visible. Inicia sesión por nombre de login. Edita el nombre visible y una zona horaria IANA del perfil. Crea tareas, cambia título y estado final, recorre páginas y elimina tareas. Recarga para comprobar restauración de sesión y cierra sesión para borrarla en el servidor.

Si consumes HTTP directamente, primero pide `GET /api/v1/auth/csrf` y conserva cookies. Register y login abren la sesión: devuelven el usuario y la cookie `__Host-session`. Esa cookie autentica Users y Tasks; no hay Bearer. `GET /api/v1/users/me` dice quién es el dueño de la sesión. Envía el valor de `XSRF-TOKEN` como `X-XSRF-TOKEN` en toda escritura (`POST`, `PUT`, `PATCH`, `DELETE`); sin él la API responde 400 con `Auth.AntiforgeryFailed`. Abrir o cerrar sesión renueva `XSRF-TOKEN`: usa el valor nuevo. Logout tiene body vacío y borra la sesión en el servidor.

Los errores usan ProblemDetails. Un recurso inexistente devuelve 404 y un recurso ajeno sin permiso devuelve 403. La lista Tasks siempre es personal, incluso para un administrador. Los tests de integración, uno por slice (`backend/Features/<Feature>/<Slice>/<Slice>Tests.cs`), fijan payloads y estados exactos.

## Ejecutar gates

Esta es la lista única de comandos de verificación. Ejecutalos sin desactivar analyzers ni omitir tests.

Un solo comando los corre todos en este orden, y después el runtime de la skill `verify-template`: `launch`, `doctor`, `api`, las e2e y `cleanup`. Desde la raíz:

```sh
E2E_CHANNEL=msedge node .agents/skills/verify-template/scripts/check-all.mjs --run-id <id>
```

Deja `summary.json` en `artifacts/verify-template/<id>/`, con `PASS`, `FAIL` o `NOT_RUN` por etapa y un log por comando. El primer `FAIL` corta la corrida: las etapas siguientes quedan `NOT_RUN`. `cleanup` corre siempre después de `launch`. `--gates-only` corre solo los comandos de esta sección; `--no-e2e` omite las e2e. Los comandos sueltos siguen valiendo para repetir una etapa. Las pruebas de integración necesitan Docker para PostgreSQL real. `check-comments` necesita las dependencias de `frontend/node_modules`, por eso va después de `npm ci`.

Desde la raíz:

```sh
dotnet build TemplateApp.slnx --configuration Release
dotnet test TemplateApp.slnx --configuration Release
dotnet run --project dune/Dune.CommentVerifier -- --root .
node dune/check-structure.mjs
node --test dune/check-structure.test.mjs
npm ci --prefix frontend
node dune/check-comments.mjs --root .
node --test dune/check-comments.test.mjs
```

Backend usa Microsoft Testing Platform (MTP): `dotnet test` ejecuta el runner MTP de `backend/Tests/TemplateApp.Tests.csproj` y de `dune/Dune.Analyzers.Tests/Dune.Analyzers.Tests.csproj` (xunit.v3). No hay `vstest` ni adaptador clásico; los proyectos Dune viven bajo `dune/`, no bajo `backend/`. La política MSBuild central vive en `dune/Dune.props`; `Directory.Build.props` solo la importa. Los símbolos prohibidos viven en `dune/BannedSymbols.txt`.

`npm run format:check` corre `prettier --check .` con `frontend/.prettierrc`: falla si un archivo del frontend no tiene el formato de Prettier. `npm run format` lo arregla. El frontend tiene una suite de Vitest con jsdom: funciones puras y componentes de presentación, cada spec junto a su archivo (`*.spec.ts`). No reemplaza la prueba en el navegador contra la API: registro, login, restauración tras recargar, perfil, tareas y logout. Desde `frontend`, compilá, corré la suite y el gate:

```sh
npm run build
npm run format:check
npm test -- --watch=false
node --test ../dune/check-frontend.test.mjs
node ../dune/check-frontend.mjs --root ..
```

Para levantar una instancia aislada y probar la API real, usá la skill `verify-template` ([SKILL.md](.agents/skills/verify-template/SKILL.md)): `launch`, `doctor`, `api` y `cleanup`. Con el conjunto Docker iniciado, el escenario HTTP también corre directo desde la raíz:

```sh
node .agents/skills/verify-template/scripts/smoke-api.mjs
node .agents/skills/verify-template/scripts/smoke-api.mjs --compose
```

El recorrido del navegador vive como specs de Playwright Test en `frontend/e2e/`, por feature: `auth`, `session`, `tasks`, `profile` y `tabs`. Corre contra una instancia de `verify-template`: tomá `frontUrl` de `run.json` y esperá 65 segundos después del smoke HTTP, porque el limiter de `/auth` admite 20 requests por minuto para toda la API. Desde `frontend`:

```sh
E2E_BASE_URL=<frontUrl> E2E_CHANNEL=msedge npm run e2e
```

`E2E_CHANNEL=msedge` usa el Edge instalado; sin la variable, Playwright usa su Chromium (`npx playwright install chromium`). Las pruebas corren en serie, con un solo worker, y antes de cada una esperan hasta tener lugar en la ventana del limiter. `E2E_OUTPUT_DIR` elige dónde quedan el reporte JSON, las capturas y los traces de las fallas. Una ruta relativa se resuelve desde la carpeta donde corrés el comando: desde `frontend`, `../artifacts/...` queda en `artifacts/` de la raíz. CI todavía no corre e2e.

El escenario HTTP no reemplaza la prueba Angular contra la API real. Comprueba registro, login, restauración, perfil, tareas y logout en el navegador. Las pruebas backend deben observar aislamiento 403, ausencia 404, permisos vigentes en el siguiente request, sesión cerrada o borrada que da 401, antiforgery en toda escritura, registro duplicado sin filas parciales y cascadas. No sustituyas esos checks por búsquedas de texto del source.

Comprueba `GET /api/v1/health/live` y `/ready` antes y después de interrumpir PostgreSQL en un conjunto de prueba. Captura logs, métricas y spans recibidos por el collector y su correlación. Registrar exporters en DI no prueba exportación. No publiques secrets en la evidencia.

`deploy/otel-collector-config.yaml` configura el collector. Con `OTEL_EXPORT_DIR` sin cambio, busca `otel-export/logs.json`, `metrics.json` y `traces.json`, archivos JSONL escritos por el collector. El flush de batch puede requerir hasta 30 segundos después de una petición. Observa `http.server.request.duration` y correlaciona traceId de un log con un span recibido. Revisa también la consola de `docker compose logs otel`. No confundas archivos creados pero vacíos con señales observadas.

## Cambiar el código

Cada paso del plan va en su rama y su PR, con un solo cambio observable. Si cambia HTTP, el PR incluye backend, frontend y tests. Adjuntá los comandos ejecutados y su resultado, también los fallos. Un PR sin runtime observado no puede afirmar runtime verificado.

Leé [arquitectura](docs/architecture.md), [convenciones](docs/conventions.md) y [ADR-0004](docs/adr/0004-vertical-slices-deep-modules.md). [CONTEXT](CONTEXT.md) es el glosario del dominio.
