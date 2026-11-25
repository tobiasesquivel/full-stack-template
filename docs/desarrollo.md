# Desarrollo

Todo lo que hace falta para trabajar en el repo: crear tu copia, configurar secrets, levantar la aplicación con Docker o con procesos locales y hablar con la API. Para verificar cambios, mirá [verificación](verificacion.md).

## Crear tu repositorio

Crea un repositorio desde la copia completa mediante la opción GitHub **Use this template**, si el repositorio remoto tiene activada esa opción. Si trabajas con una copia local, copia el monorepo sin `.git`, `node_modules`, `bin`, `obj`, `dist`, `.angular`, `.env` ni resultados de pruebas e inicializa un repositorio nuevo. No copies solo `backend`.

`TemplateApp` es el nombre y namespace de partida. Si lo cambias, actualiza proyectos, solución, namespaces, configuración, tests y referencias del monorepo en el mismo cambio. `dune/Dune.props` y las reglas de Dune usan ese nombre: si no los actualizás, Dune deja de aplicarse sin avisar. Ejecuta los gates después. No hay distribución `dotnet new cleanapi`, feature flags ni opción includeSample. Tasks es una capacidad real del template, no un stub.

## Preparar herramientas y secrets

Instala SDK .NET 10.0.401, Node 26.7 y Docker con Compose v2 y soporte de contenedores Linux. Las versiones Microsoft runtime y paquetes son 10.0.12. Angular está fijado en 22.2.1. Respeta `global.json` y `frontend/package-lock.json`.

Para ejecutar el backend fuera de Compose, configura `ConnectionStrings__DefaultConnection` en el entorno del proceso. La conexión debe apuntar a PostgreSQL con una base y credenciales reales. Los dos guiones bajos representan la jerarquía de configuración ASP.NET. No hay secrets de respaldo en appsettings. La sesión no necesita una clave propia: la cookie la cifra Data Protection de ASP.NET Core con sus claves. `DataProtection:KeysDirectory` elige la carpeta de esas claves. Sin ese valor, ASP.NET Core usa la carpeta del perfil del usuario, que sobrevive entre corridas locales. `Session:AbsoluteExpirationDays` y `Session:IdleTimeoutMinutes` fijan el vencimiento absoluto y la inactividad. `Otel:Endpoint` (o `OTEL_EXPORTER_OTLP_ENDPOINT`, que tiene prioridad) es la URL del collector OTLP; sin valor, usa `http://localhost:4318`.

La API valida su configuración al arrancar, antes de tocar la base. Si un valor no cumple su regla, no levanta: sale con código 1 y el log dice qué clave falló. Las reglas son: `ConnectionStrings:DefaultConnection` obligatoria, con formato de PostgreSQL y con `Host`; `Session:AbsoluteExpirationDays` entre 1 y 365; `Session:IdleTimeoutMinutes` entre 1 y 1440; `Otel:Endpoint` (u `OTEL_EXPORTER_OTLP_ENDPOINT`) como URL absoluta `http` o `https`; `DataProtection:KeysDirectory` vacía o como ruta absoluta. `backend/Hosts/Monolith/appsettings.example.json` muestra cada clave.

Para Compose, copia `.env.example` a `.env`. Sustituye los valores inválidos de `POSTGRES_PASSWORD` y `RABBITMQ_PASSWORD` por secrets propios antes de iniciar. `.env` no debe versionarse. Los demás nombres de configuración son `POSTGRES_DB`, `POSTGRES_USER`, `RABBITMQ_USER`, `POSTGRES_PORT`, `API_PORT`, `WEB_PORT`, `PROMETHEUS_PORT`, `OTEL_EXPORT_DIR` y los plazos del cierre de cuenta: `ACCOUNT_CLOSURE_GRACE_SECONDS` (período de gracia, 14 días por defecto), `ACCOUNT_CLOSURE_FIRST_REMINDER_SECONDS` y `ACCOUNT_CLOSURE_SECOND_REMINDER_SECONDS` (recordatorios, días 7 y 13). `docker compose config` puede imprimir secrets interpolados. No adjuntes su salida completa como evidencia pública.

## Ejecutar con Docker

Desde la raíz, después de configurar `.env`, ejecuta los comandos siguientes.

```sh
docker compose config
docker compose up --build --wait
```

El conjunto incluye PostgreSQL persistente, RabbitMQ, collector OpenTelemetry, Prometheus, API y frontend. El arranque aplica migraciones y seed idempotente. Un error de migración o configuración no es un arranque saludable. No borres el volumen para ocultar errores ni uses EnsureCreated.

Con los puertos de partida, abre `http://localhost:8081` para el frontend. La API está en `http://localhost:8080/api/v1`, detrás del gateway. `web` hace proxy al servicio `gateway` (YARP, `gateway/`), que reenvía `/api/v1/tasks` al servicio `tasks` y el resto de `/api/` al servicio `api`; ninguno de los dos publica puertos. `docker compose up --wait` espera los healthchecks de `api`, `tasks`, `gateway`, `db`, `rabbitmq` y `prometheus`. `db` usa PostgreSQL 18 con volumen `pgdata` montado en `/var/lib/postgresql`. `api` y `tasks` comparten las claves de Data Protection en el volumen `dataprotection`, montado en `/var/lib/templateapp/data-protection-keys`: recrear el contenedor no cierra las sesiones. `otel` recibe OTLP en 4317 y 4318 y manda las métricas a `prometheus`, que las guarda 35 días y evalúa las alertas de SLO; su interfaz está en `http://localhost:9090`. Los SLOs, las alertas y sus runbooks están en [docs/operacion/slos.md](operacion/slos.md). Si cambias puertos en `.env`, usa los valores configurados.

Para detener contenedores conservando los datos, ejecuta el comando siguiente.

```sh
docker compose down
```

## Ejecutar con procesos locales

Mantén PostgreSQL accesible y configura las variables de entorno anteriores. La API son dos procesos detrás del gateway: el monolito, el proceso de Tasks y el gateway, cada uno en su terminal desde la raíz. El monolito arranca primero, porque migra las tablas de `platform`. `Services__Tasks__BaseAddress` y `Services__Monolith__BaseAddress` dicen dónde está el otro proceso; `appsettings.json` ya trae `http://localhost:5111/` y `http://localhost:5110/`.

```sh
dotnet run --project backend/Hosts/Monolith/TemplateApp.csproj --no-launch-profile --urls http://localhost:5110
dotnet run --project backend/Hosts/Tasks/TemplateApp.Tasks.csproj --no-launch-profile --urls http://localhost:5111
ReverseProxy__Clusters__monolith__Destinations__api__Address=http://localhost:5110/ ReverseProxy__Clusters__tasks__Destinations__tasks__Address=http://localhost:5111/ dotnet run --project gateway/TemplateApp.Gateway.csproj --no-launch-profile --urls http://localhost:8080
```

`verify-template` levanta los tres de la misma forma, con puertos libres.

En otro terminal, desde `frontend`, instala las dependencias fijadas y sirve Angular con el proxy.

```sh
npm ci
npm start -- --proxy-config proxy.conf.json
```

Abre `http://localhost:4200`. Las peticiones `/api/v1` usan el proxy al gateway local, que reparte entre los dos procesos. No configures orígenes CORS abiertos para sustituir este diseño. Las cookies de sesión y de antiforgery son `Secure` siempre: piden HTTPS, salvo en `localhost` y `127.0.0.1`, que los navegadores tratan como origen seguro.

## Usar la aplicación

Regístrate con nombre de login, email, contraseña y nombre visible. Inicia sesión por nombre de login. Edita el nombre visible y una zona horaria IANA del perfil. Crea tareas, cambia título y estado final, recorre páginas y elimina tareas. Recarga para comprobar restauración de sesión y cierra sesión para borrarla en el servidor.

Si consumes HTTP directamente, primero pide `GET /api/v1/auth/csrf` y conserva cookies. Register y login abren la sesión: devuelven el usuario y la cookie `__Host-session`. Esa cookie autentica Users y Tasks; no hay Bearer. `GET /api/v1/users/me` dice quién es el dueño de la sesión. Envía el valor de `XSRF-TOKEN` como `X-XSRF-TOKEN` en toda escritura (`POST`, `PUT`, `PATCH`, `DELETE`); sin él la API responde 400 con `Auth.AntiforgeryFailed`. Abrir o cerrar sesión renueva `XSRF-TOKEN`: usa el valor nuevo. Logout tiene body vacío y borra la sesión en el servidor.

Los errores usan ProblemDetails. Un recurso inexistente devuelve 404 y un recurso ajeno sin permiso devuelve 403. La lista Tasks siempre es personal, incluso para un administrador. Los tests de integración, uno por slice (`backend/Features/<Feature>/<Slice>/<Slice>Tests.cs`), fijan payloads y estados exactos.

La API limita la cantidad de requests por minuto. Con sesión, cada usuario tiene un balde para lecturas (300) y otro para escrituras (60); sin sesión, el balde es por IP (120). Login, registro y logout tienen además un límite de 20 por minuto por IP. Health no tiene límite. Al pasarse, la API responde 429 con `Http.TooManyRequests` y el header `Retry-After` en segundos. Los límites se cambian en las secciones `RateLimiting` y `AuthRateLimiting` de la configuración.

