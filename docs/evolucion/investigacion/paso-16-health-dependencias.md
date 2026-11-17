# Paso 16: health por dependencia (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 16 del [plan](../plan.md) (`plan.md:343-346`). Concepto: disponibilidad accionable; un readiness que refleje cada dependencia real. Cambia: la feature `Health` y sus tests. Verifica: readiness 503 con la base caída y con migraciones pendientes; live sigue 200 sin base.
- Cómo está hoy:
  - `GetReadiness` (`Features/Health/GetReadiness/GetReadiness.cs:12-31`) devuelve un solo `bool`. Llama a `CanConnectAsync` y a `GetPendingMigrationsAsync`, y atrapa toda excepción con `catch (Exception) { return false; }`. La razón de la falla no queda en ningún lado: ni en el body ni en el log.
  - `GetReadinessEndpoint` (`GetReadinessEndpoint.cs:13-17`) responde `{status: "ready" | "not-ready", timestamp}` con 200 o 503. `GetLivenessEndpoint` responde `{status: "live", timestamp}` sin tocar la base (`GetLivenessEndpoint.cs:8-9`).
  - El grupo `/api/v1/health` es anónimo y está exento del limiter (`HealthFeature.cs:13`; `decisions.tsv:173`).
  - Los tests: `Ready_Returns200_WhenDatabaseReady`, `Ready_Body_ContainsNoInternals` (el body sano no contiene `connection`, `password` ni `Host=`) y `Readiness_IsFalse_WhenDatabaseUnreachable`, que prueba la clase de la slice sin HTTP (`GetReadinessTests.cs:16-53`). No hay test de migraciones pendientes ni de live sin base. El smoke de Compose sí para la base y pide live 200 y ready 503 (`smoke-api.mjs:256-264`).
- Decisiones que ya están tomadas y que este informe no reabre:
  - .NET 10, EF Core 10 con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`TemplateApp.csproj`).
  - La API migra y siembra al arrancar, dentro del hosted service `DatabaseInitialization`, bajo un `pg_advisory_lock` (`DatabaseInitializer.cs:26-49`).
  - Health queda anónimo y sin limiter (paso 12, `decisions.tsv:173`).
  - Una herramienta o librería la elige Tobi (regla 5 y excepción del modo autónomo). Por eso la sección "Opciones" deja la elección abierta y solo recomienda.
- Método: solo fuentes primarias. Son el código de `dotnet/aspnetcore` (`release/10.0`), `dotnet/efcore` (`release/10.0`), `npgsql/efcore.pg` (`v10.0.3`), `Xabaril/AspNetCore.Diagnostics.HealthChecks` (el commit del paquete 9.0.0), `dotnet/aspire` y `dotnet/eShop` (commits fijados en cada link); Microsoft Learn (ASP.NET Core, EF Core, Azure Architecture Center); la documentación de Kubernetes, de Npgsql y de PostgreSQL 18; los `.nuspec` de NuGet; y el artículo de AWS Builders' Library sobre health checks. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA].
- Evidencia propia: listé la carpeta del shared framework instalado (`C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\10.0.12`). No corrí la API ni los tests. No toqué código del repo.

## Glosario corto

- **Sonda** (*probe*): un pedido periódico que hace un orquestador (Kubernetes, Docker, un balanceador) para decidir qué hacer con una instancia.
- **Liveness**: "¿el proceso está vivo?". Si falla, el orquestador **reinicia** el contenedor.
- **Readiness**: "¿esta instancia puede atender tráfico ahora?". Si falla, el orquestador **saca la instancia del balanceo** pero no la reinicia.
- **Startup**: "¿terminó de arrancar?". Mientras no pasa, Kubernetes no corre las otras dos.
- **Health check**: en ASP.NET Core, una clase que implementa `IHealthCheck` y devuelve `Healthy`, `Degraded` o `Unhealthy` para una cosa concreta (la base, las migraciones).
- **`HealthReport`**: el resultado de correr varios checks. Tiene un estado total (el peor de todos) y una **entrada** (`HealthReportEntry`) por check, con estado, descripción, duración y excepción.
- **Tag**: una etiqueta de texto que se le pone a un check al registrarlo (`"ready"`). Sirve para elegir qué checks corre cada endpoint.
- **Predicate**: la función que elige qué checks corren (`r => r.Tags.Contains("ready")`).
- **`Degraded`**: "funciona, pero algo opcional falla". Por defecto responde 200.
- **Shared framework**: el conjunto de librerías que trae el runtime de ASP.NET Core instalado (`Microsoft.AspNetCore.App`). Lo que está ahí no necesita paquete NuGet.
- **Migración pendiente**: una migración que está compilada en la API pero no figura en la tabla `__EFMigrationsHistory` de la base.
- **Fallo en cascada**: una falla que, por la forma de reaccionar del sistema, se extiende a otras partes que estaban sanas.
- **Fail open**: cuando todas las instancias fallan el chequeo a la vez, el balanceador deja de hacerle caso al chequeo y manda tráfico a todas.

## Conclusión

- **ASP.NET Core ya trae un framework de health checks, sin NuGet.** `Microsoft.AspNetCore.Diagnostics.HealthChecks` y `Microsoft.Extensions.Diagnostics.HealthChecks` están en el shared framework de .NET 10. Da `IHealthCheck`, registro con nombre, tags y timeout por check, ejecución en paralelo, un scope de DI por check y logs de cada falla (`Error` para `Unhealthy`, con la excepción).
- **El problema de hoy no es la base: es que la razón se pierde.** `GetReadiness` traga toda excepción y devuelve `false`. El orquestador recibe 503 sin saber por qué, y el log tampoco lo dice. El framework resuelve justo eso: cada falla queda en el log con su excepción, y el body puede llevar solo el nombre y el estado de cada dependencia.
- **Qué dependencias son "reales" para readiness en este repo:** PostgreSQL y las migraciones. El collector de OpenTelemetry **no**: si se cae, la API sigue atendiendo, y sacarla del balanceo por eso sería un fallo en cascada (Kubernetes, Azure Architecture Center, AWS). El directorio de claves de Data Protection tampoco entra en este paso: la API lo lee al arrancar y lo cachea en memoria.
- **Las migraciones pendientes casi no pasan en runtime hoy**, porque la API migra antes de abrir el puerto. Pasan si alguien recrea o restaura la base mientras la API corre, o si un día las migraciones salen a un job aparte (lo que recomienda EF Core). Por eso el check vale como red de seguridad, y el test se arma con una base dedicada vacía.
- **Una sorpresa de EF Core 9+ con Npgsql:** `MigrateAsync` toma `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE`. Mientras otra instancia migra, el `SELECT` del check de migraciones **espera** al lock. Un timeout por check evita que readiness quede colgado.
- **El body cambia el contrato HTTP de forma aditiva:** se mantiene `{status, timestamp}` y se agrega `checks` (`{"database": "healthy", "migrations": "unhealthy"}`). El frontend no llama a health (grep en `frontend/` sin resultados), así que la regla 3 se cumple con backend, tests y los scripts de `verify-template`.
- **Recomendación** (sección "Recomendación para este repo"): opción (b), el framework integrado con dos `IHealthCheck` propios (`database` y `migrations`, tag `ready`). La slice `GetReadiness` sigue siendo un `MapGet` y llama a `HealthCheckService` filtrando por tag. Los checks viven en `Infrastructure/Persistence`, que es la dueña de la base y del arranque. Sin paquetes nuevos.

## 1. El framework de health checks de ASP.NET Core 10

**Respuesta:** sí está en el shared framework de `net10.0`, sin NuGet. Hace casi todo lo que el paso necesita; lo único que hay que escribir es el JSON de salida.

**Está en el shared framework**

- La lista de ensamblados del shared framework en `release/10.0` incluye `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`, `Microsoft.Extensions.Diagnostics.HealthChecks` y `Microsoft.AspNetCore.Diagnostics.HealthChecks` ([`SharedFramework.Local.props:37-38`](https://github.com/dotnet/aspnetcore/blob/release/10.0/eng/SharedFramework.Local.props#L37-L38), [`:76`](https://github.com/dotnet/aspnetcore/blob/release/10.0/eng/SharedFramework.Local.props#L76)).
- Comprobado en esta máquina: los tres `.dll` están en `C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\10.0.12\`, la misma versión del runtime de la imagen Docker (`backend/Dockerfile`, `aspnet:10.0.12`).
- El paquete de EF Core (`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`) **no** está en esa lista: es un NuGet aparte (sección 2).

**Piezas y comportamiento**

| Pieza | Qué hace | Fuente |
|---|---|---|
| `AddHealthChecks()` | Registra `HealthCheckService` como singleton. Es idempotente: se puede llamar desde varios lugares y suma checks al mismo registro. También registra el hosted service de publishers, que no hace nada si no hay publishers. | [`HealthCheckServiceCollectionExtensions.cs:19-31`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DependencyInjection/HealthCheckServiceCollectionExtensions.cs#L19-L31); [`HealthCheckPublisherHostedService.cs:62-67`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/HealthCheckPublisherHostedService.cs#L62-L67) |
| `AddCheck<T>(name, failureStatus, tags, timeout)` | Registra un check con nombre, tags y timeout. Crea la instancia con `ActivatorUtilities.GetServiceOrCreateInstance<T>`, así que el check puede inyectar `AppDbContext`. | [`HealthChecksBuilderAddCheckExtensions.cs:116-131`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DependencyInjection/HealthChecksBuilderAddCheckExtensions.cs#L116-L131) |
| Timeout por registro | Sin timeout es infinito. Un timeout menor o igual a cero lanza excepción. | [`HealthCheckRegistration.cs:56-70`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/Abstractions/src/HealthCheckRegistration.cs#L56-L70) |
| Ejecución | Un `Task.Run` por check (en paralelo) y un scope de DI asíncrono por check. | [`DefaultHealthCheckService.cs:54-59`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs#L54-L59), [`:78-81`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs#L78-L81) |
| Timeout en acción | Arma un `CancellationTokenSource` enlazado con `CancelAfter(timeout)`. Si vence, la entrada sale con el `FailureStatus` y la descripción `"A timeout occurred while running check."`. | [`DefaultHealthCheckService.cs:99-104`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs#L99-L104), [`:121-133`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs#L121-L133) |
| Excepción dentro del check | La entrada sale con el `FailureStatus`, `Description = ex.Message` y `Exception = ex`. | [`DefaultHealthCheckService.cs:136-148`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs#L136-L148); [Learn, Create health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#create-health-checks) |
| Estado total | El peor de las entradas: `Unhealthy` gana a `Degraded`, que gana a `Healthy`. | [`HealthReport.cs:60-72`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/Abstractions/src/HealthReport.cs#L60-L72) |
| Entrada (`HealthReportEntry`) | `Data`, `Description`, `Duration`, `Exception`, `Status`, `Tags`. | [`HealthReportEntry.cs:54-79`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/Abstractions/src/HealthReportEntry.cs#L54-L79) |
| `HealthCheckService.CheckHealthAsync(predicate, ct)` | API pública: corre los checks que elige el predicate y devuelve el `HealthReport`. Se puede llamar desde un endpoint propio. | [`HealthCheckService.cs:32`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/HealthCheckService.cs#L32), [`:60-62`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/HealthCheckService.cs#L60-L62) |

**El endpoint `MapHealthChecks`**

- `MapHealthChecks(pattern, options)` arma un mini pipeline con `HealthCheckMiddleware` y lo mapea con `endpoints.Map(pattern, pipeline)` ([`HealthCheckEndpointRouteBuilderExtensions.cs:55-72`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HealthChecks/src/Builder/HealthCheckEndpointRouteBuilderExtensions.cs#L55-L72)). `Map` no fija método HTTP: el endpoint acepta GET, POST y el resto [INFERENCIA, del código: no agrega `HttpMethodMetadata`].
- El middleware corre `CheckHealthAsync(options.Predicate, RequestAborted)`, traduce el estado a código HTTP con `ResultStatusCodes`, pone cabeceras `no-store, no-cache` salvo que `AllowCachingResponses` sea `true`, y llama al `ResponseWriter` ([`HealthCheckMiddleware.cs:41-74`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HealthChecks/src/HealthCheckMiddleware.cs#L41-L74)).
- `ResultStatusCodes` por defecto: `Healthy` → 200, **`Degraded` → 200**, `Unhealthy` → 503. Si se reemplaza el diccionario, tiene que traer los tres estados o lanza excepción ([`HealthCheckOptions.cs:25-62`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HealthChecks/src/HealthCheckOptions.cs#L25-L62); [Learn, Customize the HTTP status code](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#customize-the-http-status-code)).
- El writer por defecto escribe solo `Healthy`, `Degraded` o `Unhealthy` en `text/plain` ([`HealthCheckResponseWriters.cs:16-26`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HealthChecks/src/HealthCheckResponseWriters.cs#L16-L26)). El JSON con entradas lo escribe la app: "The health checks API doesn't provide built-in support for complex JSON return formats" ([Learn, Customize output](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#customize-output)). El ejemplo de Learn escribe `description` y `data` de cada entrada; eso es justo lo que este repo **no** quiere publicar (sección 7).
- La separación live/ready que muestra Learn: `/healthz/ready` con `Predicate = r => r.Tags.Contains("ready")` y `/healthz/live` con `Predicate = _ => false` (no corre ningún check) ([Learn, Separate readiness and liveness probes](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#separate-readiness-and-liveness-probes)).
- Los checks no se cachean ni corren por timer: "They run each time the health endpoint is requested" ([Learn, Create health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#create-health-checks)).

**¿Se puede usar en el grupo versionado `/api/v1/health`?**

- Sí. `RouteGroupBuilder` implementa `IEndpointRouteBuilder` y delega `CreateApplicationBuilder` al builder externo ([`RouteGroupBuilder.cs:18`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Http/Routing/src/RouteGroupBuilder.cs#L18), [`:35`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Http/Routing/src/RouteGroupBuilder.cs#L35)). `MapHealthChecks` recibe un `IEndpointRouteBuilder`, así que acepta el grupo, y las convenciones del grupo (`AllowAnonymous`, `DisableRateLimiting`) se aplican a sus endpoints ([Learn, Route groups](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0#route-groups)).
- Dos roces con las reglas del repo [INFERENCIA, de leer el código y las reglas]:
  - Acepta cualquier método HTTP. DUNE0011 existe para que `Map` (cualquier método) solo aparezca en un `*Endpoint.cs` (`conventions.md:108`; `ApiRouteAnalyzer.cs:16-23`). `MapHealthChecks` no está en esa lista, así que el analyzer no lo vería.
  - `check-structure.mjs` exige que cada feature tenga al menos una carpeta con `<Slice>Endpoint.cs` y su `<Slice>Tests.cs` (`check-structure.mjs:77-105`). Con `MapHealthChecks` llamado desde `HealthFeature.cs`, la forma de slice se pierde.
- Por eso la recomendación usa `HealthCheckService` desde el `MapGet` de la slice, no `MapHealthChecks` (sección "Recomendación").

## 2. El check de EF Core (`AddDbContextCheck`)

**Respuesta:** solo prueba `CanConnectAsync`. No mira migraciones. Es MIT y su versión 10.0.x acompaña al runtime.

- Por defecto corre `dbContext.Database.CanConnectAsync(ct)`. Si da `true`, `Healthy`; si da `false`, devuelve el `FailureStatus` **sin descripción ni excepción**; si lanza, `Unhealthy(exception.Message, exception)` ([`DbContextHealthCheck.cs:11-14`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HealthChecks.EntityFrameworkCore/src/DbContextHealthCheck.cs#L11-L14), [`:28-47`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HealthChecks.EntityFrameworkCore/src/DbContextHealthCheck.cs#L28-L47)).
- `CanConnectAsync` traga toda excepción que no sea cancelación y devuelve `false` ([`RelationalDatabaseCreator.cs:392-404`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Storage/RelationalDatabaseCreator.cs#L392-L404)). Consecuencia: con la base caída, el check sale `Unhealthy` **sin la excepción**, y el log del framework no dice por qué [INFERENCIA, de juntar los dos códigos]. Es el mismo problema que tiene hoy `GetReadiness`.
- El mismo comentario de EF lo aclara: "being able to connect to the database does not mean that it is up-to-date with regard to schema creation" ([`RelationalDatabaseCreator.cs:386-388`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Storage/RelationalDatabaseCreator.cs#L386-L388)). **Confirmado: no chequea migraciones.**
- Acepta un `customTestQuery` que reemplaza a `CanConnectAsync`; con eso se podría escribir el chequeo de migraciones dentro del mismo registro ([`EntityFrameworkCoreHealthChecksBuilderExtensions.cs:52-73`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HealthChecks.EntityFrameworkCore/src/DependencyInjection/EntityFrameworkCoreHealthChecksBuilderExtensions.cs#L52-L73)). El nombre por defecto del check es el nombre del tipo (`AppDbContext`) ([Learn, Entity Framework Core DbContext probe](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#entity-framework-core-dbcontext-probe)).
- Paquete: `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`. Licencia **MIT**. La versión 10.0.12 (la línea que usa el repo) apunta a `net10.0` y depende de `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`, `Microsoft.Extensions.Diagnostics.HealthChecks` y `Microsoft.EntityFrameworkCore.Relational`, todos 10.0.12 ([nuspec 10.0.12](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.diagnostics.healthchecks.entityframeworkcore/10.0.12/microsoft.extensions.diagnostics.healthchecks.entityframeworkcore.nuspec)). La última 10.x publicada hoy es 10.0.15 ([índice de versiones](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.diagnostics.healthchecks.entityframeworkcore/index.json)).
- Uso en la industria: la integración de Aspire para EF Core con PostgreSQL registra exactamente `AddDbContextCheck<TContext>()` con el nombre del contexto, sin tags ([`AspireEFPostgreSqlExtensions.cs:175-181`](https://github.com/dotnet/aspire/blob/f27d7fd9a21595d8f37376280d6b401e6bc52a1f/src/Components/Aspire.Npgsql.EntityFrameworkCore.PostgreSQL/AspireEFPostgreSqlExtensions.cs#L175-L181); [csproj](https://github.com/dotnet/aspire/blob/f27d7fd9a21595d8f37376280d6b401e6bc52a1f/src/Components/Aspire.Npgsql.EntityFrameworkCore.PostgreSQL/Aspire.Npgsql.EntityFrameworkCore.PostgreSQL.csproj#L25)).

## 3. `AspNetCore.HealthChecks.NpgSql` (Xabaril)

**Respuesta:** abre una conexión de Npgsql y corre `SELECT 1;`. Es Apache-2.0. Su última versión (9.0.0, diciembre de 2024) apunta a `net8.0`, no tiene build para .NET 10 y Microsoft no lo mantiene.

- El check abre una `NpgsqlConnection` (o una del `NpgsqlDataSource`), ejecuta `CommandText` y, si algo lanza, devuelve el `FailureStatus` con `description: ex.Message` y la excepción ([`NpgSqlHealthCheck.cs:22-45`](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks/blob/2b47b154cb48f1cdf7a7708b3708e8187dc09502/src/HealthChecks.NpgSql/NpgSqlHealthCheck.cs#L22-L45)). La consulta por defecto es `"SELECT 1;"` y el nombre por defecto `"npgsql"` ([`NpgSqlHealthCheckBuilderExtensions.cs:12-13`](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks/blob/2b47b154cb48f1cdf7a7708b3708e8187dc09502/src/HealthChecks.NpgSql/DependencyInjection/NpgSqlHealthCheckBuilderExtensions.cs#L12-L13)).
- Necesita una connection string o un `NpgsqlDataSource` registrado en DI ([`NpgSqlHealthCheckBuilderExtensions.cs:116-137`](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks/blob/2b47b154cb48f1cdf7a7708b3708e8187dc09502/src/HealthChecks.NpgSql/DependencyInjection/NpgSqlHealthCheckBuilderExtensions.cs#L116-L137)). El repo no registra `NpgsqlDataSource` (`BackendInfrastructure.cs:27-29`), así que habría que pasarle la connection string de `DatabaseOptions`.
- No mira migraciones: no conoce EF [INFERENCIA, del código: solo usa Npgsql].
- Paquete 9.0.0: licencia **Apache-2.0**; apunta a `net8.0` y `netstandard2.0`; depende de `Microsoft.Extensions.Diagnostics.HealthChecks` ≥ 8.0.11 y `Npgsql` ≥ 8.0.3 ([nuspec 9.0.0](https://api.nuget.org/v3-flatcontainer/aspnetcore.healthchecks.npgsql/9.0.0/aspnetcore.healthchecks.npgsql.nuspec)). Un `net8.0` se puede usar desde `net10.0`, y el repo ya trae Npgsql 10.0.3 por el proveedor de EF ([nuspec del proveedor](https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/10.0.3/npgsql.entityframeworkcore.postgresql.nuspec)) [INFERENCIA: no lo compilé].
- Mantenimiento: la última versión es 9.0.0, publicada el 2024-12-19 ([registro de NuGet](https://api.nuget.org/v3-flatcontainer/aspnetcore.healthchecks.npgsql/index.json)). El PR "chore: update to dotnet 10" sigue abierto desde 2025-11-14 ([#2447](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks/pull/2447)), y hay incompatibilidades abiertas con .NET 10 en otros paquetes del mismo repo ([#2446](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks/issues/2446), [#2465](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks/issues/2465)). Learn lo nombra como ejemplo y aclara: "Microsoft doesn't maintain or support `AspNetCore.Diagnostics.HealthChecks`" ([Learn, Database probe](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#database-probe)).
- Uso en la industria: Aspire lo usa en su integración de Npgsql sin EF ([`Aspire.Npgsql.csproj:20`](https://github.com/dotnet/aspire/blob/f27d7fd9a21595d8f37376280d6b401e6bc52a1f/src/Components/Aspire.Npgsql/Aspire.Npgsql.csproj#L20); [`AspirePostgreSqlNpgsqlExtensions.cs:77-82`](https://github.com/dotnet/aspire/blob/f27d7fd9a21595d8f37376280d6b401e6bc52a1f/src/Components/Aspire.Npgsql/AspirePostgreSqlNpgsqlExtensions.cs#L77-L82)). El informe empresarial del repo ya lo lista para liveness/readiness (`informe-dotnet-empresarial.md:191`).

## 4. Qué dice Kubernetes de cada sonda

**Respuesta:** liveness reinicia, readiness saca del balanceo, startup frena a las otras dos. Liveness tiene que indicar una falla que solo se arregla reiniciando; las dependencias van en readiness.

- "Liveness probes determine when to restart a container" y "If a container fails its liveness probe more times than the configured tolerance, the kubelet restarts that container" ([Kubernetes, Liveness probe](https://kubernetes.io/docs/concepts/configuration/liveness-readiness-startup-probes/#liveness-probe)).
- "Readiness probes determine when a container is ready to accept traffic"; si falla, el controlador de EndpointSlices "removes the Pod's IP address from the EndpointSlices of all Services that match the Pod" ([Kubernetes, Readiness probe](https://kubernetes.io/docs/concepts/configuration/liveness-readiness-startup-probes/#readiness-probe)). El contenedor sigue corriendo y se sigue sondeando ([Kubernetes, Configuration fields](https://kubernetes.io/docs/concepts/configuration/liveness-readiness-startup-probes/#configure-probes)).
- "If a startup probe is configured, Kubernetes does not execute liveness or readiness probes until the startup probe succeeds" ([Kubernetes, Startup probe](https://kubernetes.io/docs/concepts/configuration/liveness-readiness-startup-probes/#startup-probe)).
- Sobre dependencias: "The liveness probe passes when the app itself is healthy, but the readiness probe additionally checks that each required back-end service is available" ([Kubernetes, When should you use a readiness probe?](https://kubernetes.io/docs/concepts/configuration/liveness-readiness-startup-probes/#when-should-you-use-a-readiness-probe)). Y la advertencia: "Incorrect implementation of liveness probes can lead to cascading failures" (advertencia de [Kubernetes, Liveness probe](https://kubernetes.io/docs/concepts/configuration/liveness-readiness-startup-probes/#liveness-probe)). La página no dice textual "liveness no mira dependencias"; se deduce del contraste entre las dos frases [INFERENCIA]. Azure sí lo dice: "Use shallow checks for liveness signals so that a dependency outage doesn't trigger unnecessary application restarts" ([Azure Architecture Center, Health Endpoint Monitoring](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#solution)).
- Códigos que cuentan como éxito en una sonda HTTP: "Any code greater than or equal to 200 and less than 400 indicates success. Any other code indicates failure" ([Kubernetes, Define a liveness HTTP request](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/#define-a-liveness-http-request)). Por eso `Degraded` → 200 deja la instancia en el balanceo.
- Valores por defecto: `timeoutSeconds` 1, `periodSeconds` 10, `failureThreshold` 3 ([Kubernetes, Configuration fields](https://kubernetes.io/docs/concepts/configuration/liveness-readiness-startup-probes/#configure-probes)). "The readiness and liveness probes do not depend on each other to succeed" ([Kubernetes, Define readiness probes](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/#define-readiness-probes)).
- En el repo: hoy no hay Kubernetes. Las sondas son `waitReady` de `smoke-api.mjs` (cada 500 ms, abort a 3 s, `smoke-api.mjs:135-144`), el `waitFor` de `verify.mjs` (timeout 5 s, `verify.mjs:1182`) y el `curl -fsS` del job `compose` de CI (`ci.yml:154`). Compose no define healthcheck para `api` (`compose.yaml:31-49`).

## 5. Migraciones pendientes como señal de readiness

**Respuesta:** con Npgsql, `GetPendingMigrationsAsync` es un solo `SELECT` sobre `__EFMigrationsHistory`. En este repo casi nunca da algo en runtime, porque la API migra antes de abrir el puerto. Igual vale como red de seguridad, y se prueba con una base dedicada vacía.

**Qué hace y cuánto cuesta**

- `GetPendingMigrationsAsync` = las migraciones del ensamblado menos las aplicadas ([`RelationalDatabaseFacadeExtensions.cs:29-30`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Extensions/RelationalDatabaseFacadeExtensions.cs#L29-L30), [`:93-97`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Extensions/RelationalDatabaseFacadeExtensions.cs#L93-L97)). Las del ensamblado salen de memoria; las aplicadas, de un `SELECT` a la tabla de historia ([`HistoryRepository.cs:289-313`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/HistoryRepository.cs#L289-L313)).
- Npgsql no pregunta si la tabla existe: `ExistsAsync` devuelve `true` siempre y, si el `SELECT` falla con "tabla inexistente" o "base inexistente", devuelve una lista vacía ([`NpgsqlHistoryRepository.cs:282-290`](https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Migrations/Internal/NpgsqlHistoryRepository.cs#L282-L290), [`:307-308`](https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Migrations/Internal/NpgsqlHistoryRepository.cs#L307-L308)). Consecuencia: con una base vacía, **todas** las migraciones salen pendientes; con la base caída, lanza la excepción de conexión [INFERENCIA, del código].
- Costo: un viaje a la base por sonda, más el de la conexión. Azure advierte que los checks profundos en cada sonda consumen conexiones y CPU, y pide dimensionar profundidad e intervalo ([Azure, Problems and considerations](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#problems-and-considerations)). Con una sonda cada pocos segundos y el pool de Npgsql (100 conexiones por defecto, [Npgsql, connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html)) no es un problema en este repo [INFERENCIA].

**El lock de migraciones (sorpresa de EF Core 9+)**

- Desde EF Core 9, `Migrate`/`MigrateAsync` toman un lock de toda la base mientras migran y siembran ([Learn, Migration locking](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#migration-locking)).
- En Npgsql ese lock es `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE`, dentro de la transacción de la migración ([`NpgsqlHistoryRepository.cs:33`](https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Migrations/Internal/NpgsqlHistoryRepository.cs#L33), [`:58-66`](https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Migrations/Internal/NpgsqlHistoryRepository.cs#L58-L66)). EF lo toma en cada `MigrateAsync`, haya o no migraciones pendientes ([`Migrator.cs:292-300`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/Migrator.cs#L292-L300)).
- En PostgreSQL, `ACCESS EXCLUSIVE` choca con el `ACCESS SHARE` que toma cualquier `SELECT` ([PostgreSQL 18, Table-Level Locks](https://www.postgresql.org/docs/18/explicit-locking.html#LOCKING-TABLES)).
- Consecuencia: mientras **otra** instancia arranca y migra, el check de migraciones de esta instancia **espera** al lock. Sin timeout, readiness queda colgado hasta que esa migración termina [INFERENCIA, de juntar las tres fuentes]. El `DatabaseInitializer` del repo agrega su propio `pg_advisory_lock`, pero ese no bloquea `SELECT` (`DatabaseInitializer.cs:34-37`).

**Cuándo hay migraciones pendientes en runtime en este repo**

- **Al arrancar, no.** `DatabaseInitialization` es un `IHostedService`, y `StartAsync` corre antes de que se configure el pipeline y arranque el servidor ([Learn, StartAsync](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0#startasync)). Mientras migra, el puerto está cerrado y la sonda recibe "conexión rechazada". Si la migración falla, `StartAsync` lanza y el host no arranca (`Program.cs:51-55`) [INFERENCIA, del código].
- **Despliegue gradual** (*rolling*), versión vieja y nueva a la vez: la nueva migra al arrancar; la vieja tiene **menos** migraciones que las aplicadas, así que no ve pendientes (el cálculo es "las mías menos las aplicadas") [INFERENCIA, del código de arriba]. Este caso no lo detecta el check; es asunto del paso 19 (expand/contract).
- **La base se recrea o se restaura mientras la API corre:** un `docker compose down -v` del servicio `db` con la API viva, una restauración de un backup viejo o una fila borrada de `__EFMigrationsHistory`. La API vieja ve migraciones pendientes y no tiene cómo migrar sin reiniciar. Es el caso que el check cubre de verdad [INFERENCIA].
- **Si las migraciones salen a un job aparte.** EF Core recomienda un *migration bundle* como job de una vez, y "don't make every application replica run migrations from its entrypoint" ([Learn, Containers and deployment jobs](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#containers-and-deployment-jobs)). Con ese esquema, readiness es lo que deja afuera a una instancia nueva hasta que el job termina [INFERENCIA]. No es parte de este paso.

**Cómo reproducirlo en un test sin romper la base compartida**

- No tocar `templateapp_tests`: la usan todas las clases de test en paralelo (`PostgresFixture.cs`).
- Crear una base dedicada vacía con `CREATE DATABASE`, como ya hace `ConcurrentStartupTests.CreateEmptyDatabaseAsync` (`ConcurrentStartupTests.cs:83-94`). El usuario de Testcontainers puede crear bases.
- Levantar un host aislado contra esa base **sin** `DatabaseInitialization`: `ApiFixture.CreateIsolatedClient(settings, configureServices, out _)` ya acepta settings (la connection string nueva pisa la base, porque se aplica después, `ApiFixture.cs:70-75`) y un `Action<IServiceCollection>` que corre en `ConfigureTestServices`. Ahí se quita el descriptor de `IHostedService` cuyo tipo de implementación es `DatabaseInitialization` (es el único hosted service propio del backend, `BackendInfrastructure.cs:31`).
- Resultado esperado: la base existe (el check de conexión da sano) y la tabla de historia no existe (Npgsql devuelve lista vacía, todas pendientes) → 503 con `migrations: unhealthy` [INFERENCIA, de la sección anterior].
- `conventions.md:118` pide que los helpers no se copien: `CreateEmptyDatabaseAsync` tiene que pasar de `ConcurrentStartupTests` a `backend/Tests/Support/`.
- El mismo host aislado sin `DatabaseInitialization` sirve para "base caída": connection string a un puerto cerrado (el test actual usa `127.0.0.1:54399` con `Timeout=2`, `GetReadinessTests.cs:43`). Sin quitar el hosted service, el host no arrancaría, porque la migración falla en `StartAsync`.

## 6. Qué dependencias son "reales" para readiness

**Respuesta:** PostgreSQL y las migraciones, sí. El collector de OpenTelemetry, no. El directorio de claves de Data Protection, no en este paso.

| Dependencia | ¿Sin ella la API puede atender? | ¿Va en readiness? | Por qué |
|---|---|---|---|
| PostgreSQL | No: casi todo endpoint lee o escribe la base, también la sesión (`architecture.md`, sección "Autorización y sesión"). | **Sí**, `Unhealthy` → 503. | Es una dependencia requerida: "checks that each required back-end service is available" (sección 4). |
| Migraciones al día | No: el modelo de EF y el esquema no coinciden. | **Sí**, `Unhealthy` → 503. | Mismo motivo, sección 5. |
| Collector OTel | Sí: los exportadores son en lote y en segundo plano. Si el collector no responde, el SDK descarta datos cuando se llena la cola ([OTel spec, Batching processor](https://opentelemetry.io/docs/specs/otel/trace/sdk/#batching-processor)); el request no espera. | **No.** | Sacar la API del balanceo porque se cayó la telemetría convierte un problema de observabilidad en una caída del servicio: es el fallo en cascada. |
| Claves de Data Protection (`/var/lib/templateapp/data-protection-keys`) | Sí, mientras el anillo de claves esté en memoria: se lee al iniciar y se revisa cada ~24 h o al vencer la clave ([Learn, Automatic key ring refresh](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0#automatic-key-ring-refresh)). Se escribe una clave nueva 2 días antes de que venza la actual (90 días) ([Learn, Key expiration and rolling](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0#key-expiration-and-rolling)). | **No en este paso.** | Probar "se puede escribir" en cada sonda escribiría un archivo cada pocos segundos, y Azure pide "Prefer read-only checks" ([Azure, Problems and considerations](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#problems-and-considerations)). Va como pendiente. |

**Qué dice la industria sobre dependencias no críticas**

- Azure: "Exclude optional dependencies from the decision to remove an instance from service" ([Azure, Solution](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#solution)). Y en "Cascading failure from deep probes": mitigar con "dependency-specific timeouts within the probe, using circuit-breaker logic for unreliable dependencies, or degrading the health status gracefully (for example, reporting degraded rather than unhealthy when a noncritical dependency is unavailable)" ([Azure, Problems and considerations](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#problems-and-considerations)).
- AWS Builders' Library: "trouble arises when the health check fails for a non-critical reason and when that failure is correlated across servers", y recomienda que el balanceador haga solo chequeos de vida y locales, y que los chequeos de dependencias los haga un monitoreo externo ([AWS, Implementing health checks](https://builder.aws.com/content/3Ev53O39izHCtWLzp4XU6t8PC1O/implementing-health-checks)).
- Hay una tensión: AWS sugiere no poner ni siquiera la base en el chequeo del balanceador; Kubernetes y Azure dicen que las dependencias **requeridas** sí van en readiness. El plan del repo pide la segunda ("readiness que refleje cada dependencia real") y es lo que hace la plantilla de Aspire (todos los checks en `/health`, solo `self` en `/alive`, [`Extensions.cs:100-121`](https://github.com/dotnet/aspire/blob/f27d7fd9a21595d8f37376280d6b401e6bc52a1f/src/Aspire.ProjectTemplates/templates/aspire-servicedefaults/Extensions.cs#L100-L121); igual en eShop, [`Extensions.cs:99-123`](https://github.com/dotnet/eShop/blob/5e526931f74a61243c314ceb9978bdacb80cb92a/src/eShop.ServiceDefaults/Extensions.cs#L99-L123)). Con una base compartida, si la base cae, todas las instancias quedan fuera a la vez; Kubernetes y los balanceadores con *fail open* lo toleran, y la API igual no podría atender [INFERENCIA].
- **`Degraded` sin uso en este paso.** No hay ninguna dependencia opcional que valga la pena chequear. Si un día se agrega una, va con `failureStatus: Degraded` (sigue 200) o fuera del tag `ready` [INFERENCIA, de Azure y del mapeo de la sección 1].

## 7. El body: estado por dependencia, sin datos internos

**Respuesta:** mantener `{status, timestamp}` y agregar `checks`, un mapa de nombre de check a estado. Nada más: ni `description`, ni `exception`, ni `data`, ni duraciones.

- **Por qué nada más.** La descripción de una entrada es el `Message` de la excepción cuando el check lanza (sección 1). El de Npgsql para un puerto cerrado nombra host y puerto [INFERENCIA: no lo ejecuté]. `Include Error Detail` está en `false` por defecto porque los detalles de PostgreSQL "can contain sensitive data" ([Npgsql, connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html)). Azure pide, para sondas sin autenticación, "return only the information needed to determine health" ([Azure, Problems and considerations](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#problems-and-considerations)). Aspire mapea sus endpoints de health solo en Development por esto mismo ([`Extensions.cs:111-113`](https://github.com/dotnet/aspire/blob/f27d7fd9a21595d8f37376280d6b401e6bc52a1f/src/Aspire.ProjectTemplates/templates/aspire-servicedefaults/Extensions.cs#L111-L113)). Este repo necesita readiness en Production (Compose y CI), así que la defensa es un body mínimo.
- **El test existente sigue valiendo y hay que extenderlo.** `Ready_Body_ContainsNoInternals` mira solo el body sano. Lo peligroso es el body con fallas, donde hay excepciones. Hace falta el mismo test con la base caída (sección "Recomendación").
- **Cuidado con los nombres.** El test busca la subcadena `connection`. Un check llamado `db-connection` lo rompería: los nombres tienen que ser `database` y `migrations`.
- **Forma propuesta** (camelCase, como todo el JSON del repo, `conventions.md:82`):

```json
{ "status": "not-ready", "timestamp": "2026-11-17T12:00:00+00:00",
  "checks": { "database": "healthy", "migrations": "unhealthy" } }
```

- **Valores de `checks`:** `healthy`, `degraded`, `unhealthy` en minúscula. El código los escribe explícito desde `HealthStatus`; no conviene depender de un convertidor de enums global [INFERENCIA].
- **Claves del mapa:** `System.Text.Json` cambia nombres de propiedades con las opciones web, pero no claves de diccionario salvo que se configure `DictionaryKeyPolicy` ([Learn, Customize property names](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/customize-properties)). Con nombres ya en minúscula no importa.
- **`status` no cambia.** `ready`/`not-ready` arriba sigue igual, así que `healthBodySays` de `verify.mjs:233-241`, `waitReady` de `smoke-api.mjs` y el `curl -fsS` de CI siguen andando sin cambios.
- **Contrato HTTP (regla 3).** Agregar `checks` cambia el contrato de readiness. Es un cambio aditivo. El frontend no llama a health: `grep -ri health frontend/` (sin `node_modules`) no devuelve nada, y nginx solo hace proxy de `/api/v1/` (`frontend/nginx.conf`). Entonces la regla 3 se cumple con backend y tests, más `smoke-api.mjs` si se quiere afirmar `checks.database === 'unhealthy'` con la base parada.
- **Tipos (ADR-0004 punto 5):** el body de readiness pasa a ser un tipo propio de una sola slice, `ReadinessResponse`, en `GetReadiness/`. `HealthStatusResponse` queda solo para liveness; por el mismo punto 5 le corresponde `GetLiveness/` (hoy está en la raíz porque lo usaban las dos slices, `decisions.tsv:137`). Mover el archivo no cambia el nombre del schema de OpenAPI, porque sale del nombre del tipo (`decisions.tsv:137`).

## 8. Timeouts, caché y sondas concurrentes

**Respuesta:** un timeout corto por check (2 s), sin caché, y los checks corren en paralelo. Una base colgada no cuelga readiness.

- **Timeout por check.** El framework corta cada check con su propio `CancellationTokenSource` (sección 1). Azure pide "Bound each dependency check with a timeout" ([Azure, Endpoint behavior](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#endpoint-behavior)).
- **El timeout de Npgsql no alcanza.** `Timeout` (conexión) vale 15 s y `Command Timeout` 30 s por defecto ([Npgsql, connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html)); el repo no los cambia (`compose.yaml:44`). Una sonda de Kubernetes corta a 1 s por defecto y el smoke a 3 s (sección 4). Sin timeout del check, una base que no responde dejaría el request abierto hasta 15 s; y el `SELECT` bloqueado por el lock de migraciones (sección 5) esperaría hasta 30 s [INFERENCIA].
- **Valor propuesto: 2 s por check.** Corren en paralelo, así que readiness responde en ~2 s en el peor caso: debajo de los 3 s del smoke y los 5 s de `verify.mjs`. Una sonda futura de Kubernetes necesita `timeoutSeconds` ≥ 3 [INFERENCIA].
- **Cancelación real.** EF pasa el token a Npgsql en `OpenAsync` y en los comandos. Que Npgsql corte la conexión en curso al cancelarse el token no lo verifiqué en su documentación [sin comprobar]. El test con un servidor que acepta y no responde (sección "Recomendación") lo prueba de punta a punta.
- **Sin caché.** Los checks corren en cada pedido (sección 1). Azure advierte que cachear el resultado agrega una ventana en la que readiness miente ([Azure, Problems and considerations](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring#problems-and-considerations)). Además, el repo prohíbe cachés entre requests antes del paso 33 (`check-structure.mjs`, `checkNoCrossRequestCache`).
- **Sondas concurrentes.** Cada sonda abre un scope y un `AppDbContext` por check (sección 1), y toma conexiones del pool. Como health no tiene limiter (`decisions.tsv:173`), alguien de afuera puede pegarle a `/ready` en ráfaga y cada pedido va a la base. Ya está anotado como pendiente (`progreso.md:731`); este paso no lo empeora, porque el readiness de hoy ya hace dos consultas [INFERENCIA].

## 9. Logs de los checks que fallan

**Respuesta:** el framework ya los escribe. La razón queda en el log, no en el body.

- Cada check que termina `Unhealthy` se loguea en nivel `Error`, `Degraded` en `Warning`, y `Healthy` en `Debug`. El mensaje es `"Health check {HealthCheckName} with status {HealthStatus} completed after {ElapsedMilliseconds}ms with message '{HealthCheckDescription}'"` y lleva la excepción ([`DefaultHealthCheckService.cs:218-249`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs#L218-L249)). Si el check lanza, sale `"Health check {HealthCheckName} threw an unhandled exception"` en `Error`, con la excepción ([`:251-255`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs#L251-L255)).
- Cada check abre un scope de log con su nombre ([`DefaultHealthCheckService.cs:83-85`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/HealthChecks/HealthChecks/src/DefaultHealthCheckService.cs#L83-L85)).
- La categoría es `Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService`. El repo deja `Information` por defecto y baja a `Warning` solo `Microsoft.AspNetCore` y `Microsoft.EntityFrameworkCore` (`ObservabilityExtensions.cs:89-94`), así que los `Error` llegan a la consola y al OTLP [INFERENCIA, de la configuración]. Learn sugiere subir esas categorías a `Warning` si las sondas generan ruido; así solo se loguean las fallas ([Learn, Filter health check logs](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#filter-health-check-logs)). Hoy no hace falta: `Healthy` ya es `Debug`.
- **Detalle para el check de base.** Si el check usa `CanConnectAsync`, la excepción se pierde (sección 2) y el log dice "Unhealthy" sin motivo. Para que el motivo llegue al log, el check tiene que abrir la conexión y **dejar que la excepción salga**; el framework la convierte en entrada `Unhealthy` y la loguea [INFERENCIA, de las secciones 1 y 2].
- **Paso 15.** Estos logs se escriben dentro del request de `/ready`. `Task.Run` copia el contexto de ejecución, así que llevan `RequestId` y el trace del span SERVER, y no rompen `check-otel-export.mjs` [INFERENCIA: no lo ejecuté].

## 10. Dónde viven los checks en este repo

**Respuesta:** en `Infrastructure/Persistence`, que es dueña de la base y del arranque. La feature `Health` expone los endpoints y elige los checks por tag, sin conocer `AppDbContext`.

| Lugar | A favor | En contra |
|---|---|---|
| A. En la slice `Features/Health/GetReadiness/` | Todo el caso de uso en una carpeta (ADR-0004 punto 2). Un tipo de una sola slice va en la slice (punto 5). | Health sigue dependiendo de `AppDbContext`. Después del paso 22 (un contexto por feature) Health tendría que conocer el contexto de cada feature: el informe de VSA ya marca este riesgo, "Después del paso 22 necesita un chequeo de conexión que no dependa de una feature" (`informe-vsa-deep-modules.md:129`). |
| B. En `Infrastructure/Persistence/` (recomendado) | `architecture.md` pone "arranque de la base" en `Infrastructure` (sección "Forma del backend"); el check de migraciones es el espejo de `DatabaseInitialization`, que vive ahí. ADR-0004 punto 6: lo que no nombra una feature es fontanería del host. Health queda sin `AppDbContext`: filtra por tag y no sabe qué checks hay. `AddHealthChecks()` es idempotente (sección 1), así que en el paso 22 cada feature registra el check de **sus** migraciones con el tag `ready` desde su `<Feature>Feature.cs`, sin tocar Health. Es el patrón de Aspire: la integración registra el check, "calling MapHealthChecks is the responsibility of the app" ([`AspireEFPostgreSqlExtensions.cs:177`](https://github.com/dotnet/aspire/blob/f27d7fd9a21595d8f37376280d6b401e6bc52a1f/src/Components/Aspire.Npgsql.EntityFrameworkCore.PostgreSQL/AspireEFPostgreSqlExtensions.cs#L177)). | El caso de uso "readiness" queda repartido: la slice decide la respuesta y Infrastructure decide qué se chequea. Hay que compartir el nombre del tag entre los dos. |

- El tag no puede vivir en `Features/Health`, porque `Infrastructure/` no nombra features (ADR-0004 punto 6; `InfrastructureBoundaryAnalyzer`). Va en `Infrastructure` (por ejemplo `Infrastructure/Health/HealthCheckTags.cs`), y Health lo usa, como hoy usa `Infrastructure.Persistence`.
- Con la opción (a), "slice a mano" (sección siguiente), no hay registro ni tags: el lugar natural es A.

## Opciones (elige Tobi, regla 5)

| Opción | Qué gana Tobi | Qué pierde | Licencia | NuGet extra | Cómo la usa la industria |
|---|---|---|---|---|---|
| **(a) Slice a mano.** `GetReadiness` devuelve un estado por dependencia. | Nada nuevo que aprender; control total; la slice queda como está. | Escribir a mano lo que el framework ya trae: paralelo, timeout por dependencia, log de cada falla. Health tiene que conocer cada dependencia (choca con el paso 22). | — | Ninguno. | Poco común en .NET: Learn, Aspire y eShop usan el framework (secciones 1, 2 y 6). |
| **(b) Framework integrado con `IHealthCheck` propios.** | El modelo estándar de .NET (`IHealthCheck`, tags, `HealthReport`), con timeout, paralelo y logs gratis. Dos clases cortas que hacen exactamente lo que el repo necesita, incluida la excepción en el log. | Escribir los dos checks (unas 15 líneas cada uno) y el JSON de salida. | MIT (shared framework). | Ninguno. | Es la base de todo lo demás: Aspire y eShop lo usan y le suman checks de integraciones (sección 6). Learn lo documenta como camino principal. |
| **(c) (b) + `AddDbContextCheck` de Microsoft.** | Una clase menos: el check de conexión viene hecho. Es lo que usa Aspire para EF Core. | Un paquete más para unas 40 líneas. Usa `CanConnectAsync`: con la base caída, el log no dice por qué (sección 2). Igual hay que escribir el check de migraciones (propio o con `customTestQuery`). | MIT. | `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.x. | Aspire (sección 2). |
| **(d) (b) + `AspNetCore.HealthChecks.NpgSql` (Xabaril).** | Un check de PostgreSQL que no depende de EF (útil después del paso 22). Muy conocido. | Un tercero sin versión para .NET 10 y sin release desde diciembre de 2024 (sección 3). Pide la connection string aparte. Igual hay que escribir el check de migraciones. Microsoft no lo mantiene. | Apache-2.0. | `AspNetCore.HealthChecks.NpgSql` 9.0.0 (+ `Npgsql` ≥ 8.0.3 y `Microsoft.Extensions.Diagnostics.HealthChecks` ≥ 8.0.11, que el repo ya tiene en versión 10). | Aspire para Npgsql sin EF; listado en `informe-dotnet-empresarial.md:191`. |

**Recomendación: (b).** Es el estándar de la plataforma, no suma paquetes y deja el motivo de cada falla en el log, que es lo que hoy falta. (c) y (d) agregan un paquete para ahorrar una clase de pocas líneas, y ninguna de las dos resuelve las migraciones; (c) además pierde la excepción. (a) obliga a reimplementar el timeout y los logs, y deja a Health atada a cada dependencia.

## Recomendación para este repo

**Diseño (opción b)**

- **Checks.** Dos clases en `backend/Infrastructure/Persistence/`, registradas en `AddBackendInfrastructure` junto al `DbContext` (`BackendInfrastructure.cs:27-31`):

| Nombre | Clase | Qué hace | Tag | Timeout | Si falla |
|---|---|---|---|---|---|
| `database` | `DatabaseHealthCheck` | Abre y cierra la conexión de `AppDbContext` (`OpenConnectionAsync`). Si lanza, deja salir la excepción para que el framework la loguee (sección 9). | `ready` | 2 s | `Unhealthy` |
| `migrations` | `PendingMigrationsHealthCheck` | `GetPendingMigrationsAsync`. Si hay, `Unhealthy` con una descripción fija, por ejemplo "3 migraciones pendientes" (va al log, no al body). | `ready` | 2 s | `Unhealthy` |

- **Tag.** Una constante `ready` en `Infrastructure` (sección 10).
- **Readiness.** `GetReadiness` (la operación de la slice) inyecta `HealthCheckService` y corre `CheckHealthAsync(r => r.Tags.Contains(<tag>), ct)`. Arma un `ReadinessResponse(Status, Timestamp, Checks)` en `GetReadiness/ReadinessResponse.cs`. `GetReadinessEndpoint` sigue siendo un `MapGet` (solo GET, forma de slice, DUNE0011 y `check-structure` intactos).
- **Códigos.** `Unhealthy` → 503 con `status: "not-ready"`. `Healthy` o `Degraded` → 200 con `status: "ready"`. El mapeo es el mismo que el default del framework (sección 1).
- **Body.** `{status, timestamp, checks: {database, migrations}}` con valores `healthy`/`degraded`/`unhealthy`. Nada de `description`, `exception`, `data` ni duración (sección 7).
- **Liveness.** No cambia: sigue sin checks y sin base. `HealthStatusResponse` pasa a `GetLiveness/` (ADR-0004 punto 5).
- **Sin** `MapHealthChecks`, sin caché, sin check del collector ni de Data Protection.
- **Docs.** `architecture.md` suma "health checks de la base" a la lista de `Infrastructure`. Una fila en `decisions.tsv` por la opción elegida y otra por el lugar de los checks.

**Tests, primero en rojo**

Todos por HTTP con `WebApplicationFactory` y PostgreSQL real (`conventions.md:117`). Los de base caída y migraciones usan un host aislado sin `DatabaseInitialization` (sección 5).

| Test | Escenario | Afirma | ¿Rojo hoy? |
|---|---|---|---|
| `Ready_Returns200_WithEachDependencyHealthy` | Fixture compartida. | 200, `status: ready`, `checks.database` y `checks.migrations` = `healthy`. | Sí: no hay `checks`. |
| `Ready_Returns503_WhenDatabaseUnreachable` | Host aislado, puerto cerrado (`127.0.0.1:54399`, `Timeout=2`). Reemplaza al test actual `Readiness_IsFalse_WhenDatabaseUnreachable`, que prueba la clase sin HTTP. | 503, `status: not-ready`, `checks.database = unhealthy`. | Sí: no hay `checks`. |
| `Ready_Returns503_WhenMigrationsPending` | Base dedicada vacía. | 503, `checks.database = healthy`, `checks.migrations = unhealthy`. | Sí: no hay `checks` (el 503 ya sale hoy). |
| `Live_Returns200_WhenDatabaseUnreachable` | Mismo host que el de base caída. | 200, `status: live`. | No: hoy ya pasa. Fija el contrato dentro de la suite (hoy solo lo prueba el smoke de Compose). Rojo deliberado: inyectar `AppDbContext` en el handler de live y ver que falla. |
| `Ready_Body_ContainsNoInternals_WhenDatabaseUnreachable` | Host con base caída. | El body no contiene `connection`, `password`, `Host=`, `127.0.0.1`, `54399` ni `Exception`. | No hoy (el body es mínimo). Rojo deliberado: escribir `entry.Description` en el body y ver que falla. |
| `Ready_LogsTheFailureReason_WhenDatabaseUnreachable` | Host con base caída y `CapturedLogs` (`Tests/Support`, ya usado en `ErrorContractTests.cs:223`). | Hay una entrada `Error` con excepción. | Sí: hoy `GetReadiness` traga la excepción sin log. |
| `Ready_AnswersWithinTimeout_WhenDatabaseHangs` | Host aislado contra un `TcpListener` local que acepta y nunca responde, con `Timeout=15`. | Responde 503 en menos de 5 s, `checks.database = unhealthy`. | Sí: hoy espera el timeout de conexión de Npgsql, ~15 s [INFERENCIA: no lo ejecuté]. |

- `CreateEmptyDatabaseAsync` sale de `ConcurrentStartupTests` a `backend/Tests/Support/`, y el "quitar `DatabaseInitialization`" va ahí también (`conventions.md:118`).
- `smoke-api.mjs --compose` (`smoke-api.mjs:256-264`): con la base parada, sumar `checks.database === 'unhealthy'` al 503 que ya pide. `verify.mjs` no cambia.
- Las e2e de Playwright quedan `NOT_RUN`: el paso no toca el frontend (AGENTS.md, "Verificación").

## Cosas que el paso tiene que decidir y que este informe deja abiertas

- **La opción (a), (b), (c) o (d).** Es una elección de herramienta: la decide Tobi.
- **El lugar de los checks** (sección 10): recomiendo `Infrastructure/Persistence`. Es diseño y va a `decisions.tsv`.
- **Pendientes para `progreso.md`** (regla 7, fuera de este paso):
  - Un check del directorio de claves de Data Protection (sección 6). Sin paso asignado.
  - El collector arranca en `service_started` y puede no estar listo (`progreso.md:171`). Este informe recomienda **no** resolverlo con el readiness de la API (sección 6). Se resuelve en Compose, con un healthcheck del collector. Sin paso asignado.
  - `/health/ready` sigue anónimo, sin límite y publicado por nginx bajo `/api/v1/` (`progreso.md:731`). Con `checks` publica además los nombres de las dependencias, sin datos internos. Restringirlo por red es lo que pide Azure. Sigue sin paso asignado.
  - Las sondas de health generan spans y logs en cada pedido; Aspire las filtra de las trazas ([`Extensions.cs:66-69`](https://github.com/dotnet/aspire/blob/f27d7fd9a21595d8f37376280d6b401e6bc52a1f/src/Aspire.ProjectTemplates/templates/aspire-servicedefaults/Extensions.cs#L66-L69)). Filtrarlas acá cambiaría lo que mira `check-otel-export.mjs` (paso 15). Sin paso asignado.

## Verificación y límites

- **Leído:** el código citado en cada link, en las ramas o commits fijados; Microsoft Learn (health checks, hosted services, Data Protection, EF Core migrations, route groups, System.Text.Json); Azure Architecture Center (Health Endpoint Monitoring); Kubernetes (concepto y tarea de sondas); Npgsql (parámetros de connection string); PostgreSQL 18 (locks); OpenTelemetry (batch processor); AWS Builders' Library; los `.nuspec` y los índices de versiones de NuGet; los issues y PR citados del repo de Xabaril.
- **Comprobado en el repo:** cada línea citada de `backend/`, `compose.yaml`, `frontend/nginx.conf`, `smoke-api.mjs`, `verify.mjs`, `check-structure.mjs`, `ApiRouteAnalyzer.cs`, `BannedSymbols.txt` y los docs. El grep de `health` en `frontend/` (sin `node_modules`) no devuelve nada.
- **Comprobado en la máquina:** los tres ensamblados de health checks en el shared framework 10.0.12 instalado.
- **No comprobado:**
  - No corrí la API ni los tests. La columna "¿Rojo hoy?" sale de leer el código.
  - Que Npgsql corte una conexión colgada al cancelarse el token (sección 8). Lo prueba el test del `TcpListener`.
  - El texto exacto del mensaje de Npgsql con un puerto cerrado (sección 7).
  - Que `Task.Run` dentro de `DefaultHealthCheckService` conserve `RequestId` y el trace en los logs (sección 9).
  - Que el paquete Xabaril 9.0.0 compile y corra sobre `net10.0` con Npgsql 10 (sección 3).
  - El código de `aspnetcore` y `efcore` lo leí en `release/10.0`, no en la etiqueta exacta 10.0.12.

## Contraste con la implementación (2026-11-17)

Lo que cambió al implementar la opción (b), que eligió Tobi. Cada punto tiene su fila en `docs/decisions.tsv`.

- **Sin tag `ready`.** Liveness no corre checks, así que readiness corre todos los registrados. El tag no separaba nada y obligaba a compartir una constante entre `Infrastructure` y `Health`.
- **La clase `GetReadiness` desaparece.** Quedaba como un envoltorio de una línea sobre `HealthCheckService`; el endpoint lo llama directo.
- **El timeout del framework no alcanza contra una base colgada.** Lo que la sección 8 dejaba sin comprobar resultó falso: Npgsql no corta la apertura de la conexión al cancelarse el token. Contra un servidor que acepta y no responde, readiness tardó 15 s con el límite de 2 s del check (`artifacts/verify-template/paso16-verde/health-tests-1-sin-limite-npgsql.log`). `HealthCheckConnection` pone el límite del check como `Timeout` y `Command Timeout` de la conexión del check, y readiness responde en ~2 s.
- **La resolución DNS tampoco se cancela.** Con el contenedor `db` de Compose parado, el nombre `db` no resuelve y readiness respondió 503 en 4 s (`artifacts/verify-template/paso16-compose/db-down.txt`). El resultado es correcto; solo tarda más que el límite.
- **Un puerto cerrado en Windows tarda ~2 s.** El check vence por timeout y la descripción es "A timeout occurred while running check.", no el mensaje de Npgsql. Por eso el test de "body sin datos internos" con la base caída no detectaba nada. Los tests de body y de log usan una base que rechaza la contraseña: falla rápido y su mensaje (empieza con `28P01: password authentication f…`, `paso16-rojo-deliberado/description-en-body.log`) sí trae datos internos.
