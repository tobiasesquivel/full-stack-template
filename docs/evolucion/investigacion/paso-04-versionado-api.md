# Paso 4: versionado de API declarado (investigación)

- Fecha: 2026-10-18.
- Alcance: el paso 4 del [plan](../plan.md). `/api/v1` deja de ser un texto fijo y pasa a ser una versión gestionada.
- Decisiones que ya tomó Tobi y que este informe no reabre: la librería es `Asp.Versioning.Http` (10.x, MIT) y la versión viaja en un segmento de la URL (`/api/v1/...`). El contrato HTTP de v1 no cambia.
- Método: solo fuentes primarias. Son la documentación oficial de la librería, su código fuente y sus ejemplos en el commit [`1a22bb4`](https://github.com/dotnet/aspnet-api-versioning/tree/1a22bb4281c03d44f387169eefa46765a6219118), el commit del paquete 10.2.3 según su nuspec. También Microsoft Learn, el código de ASP.NET Core 10 y los RFC.
- Evidencia propia: un experimento descartable fuera del repo (`/tmp/apiv-exp`). Usó `Asp.Versioning.Http` 10.2.3, `Asp.Versioning.OpenApi` 10.2.3, `Microsoft.AspNetCore.OpenApi` 10.0.12 y el SDK 10.0.401. Cada vez que este informe dice "experimento", habla de esa corrida. No se tocó código del repo.

## Conclusión

- **Paquete:** `Asp.Versioning.Http` 10.2.3 (MIT). Si se quiere que el documento OpenAPI siga mostrando `/api/v1/...`, hace falta además `Asp.Versioning.OpenApi` 10.2.3 (MIT). Ese segundo paquete es una herramienta nueva: ⏸ **DECISIÓN DE TOBI** (regla 5 de AGENTS.md).
- **Configuración:** `ApiVersionReader = new UrlSegmentApiVersionReader()` y `ReportApiVersions = true`. Los demás valores quedan por defecto: `DefaultApiVersion`, `AssumeDefaultVersionWhenUnspecified` y la restricción `apiVersion`. Con versión en la URL, "asumir la versión por defecto" no tiene efecto.
- **Forma:** un solo grupo raíz versionado en `backend/Infrastructure/Http/VersionedApi.cs`: `app.NewVersionedApi().MapGroup("/api/v{version:apiVersion}").HasApiVersion(1.0)`. `Program.cs` lo crea y se lo pasa a cada `Map<Feature>Endpoints(this RouteGroupBuilder api)`. Las features no conocen el versionado. Infrastructure no nombra ninguna feature.
- **Location de los 201:** cada endpoint de destino lleva un nombre (`WithName`). El endpoint que crea arma la ruta con `LinkGenerator.GetPathByName(http, nombre, valores)`. La librería agrega sola la versión pedida, y el `Location` queda relativo e igual al de hoy (`/api/v1/tasks/{id}`). `Results.CreatedAtRoute` no conviene: genera una URL absoluta.
- **Versión no soportada:** `/api/v9/tasks` y `/api/tasks` responden `404` con el cuerpo vacío y sin `api-supported-versions`. Es lo mismo que hoy responde cualquier ruta inexistente. Convertir ese 404 en ProblemDetails (`UseStatusCodePages`) es otro cambio observable: queda como pendiente para el paso 6.
- **Deprecación y sunset:** no hay nada que configurar ahora, porque v1 es la única versión. Más adelante: `HasDeprecatedApiVersion` para `api-deprecated-versions`, y `options.Policies.Deprecate(...)` / `.Sunset(...)` para los headers `Deprecation` (RFC 9745), `Sunset` (RFC 8594) y `Link`.
- **Pruebas:** antes del cambio, tests de caracterización del `Location` en `CreateTaskTests` y `RegisterUserTests`. Después, un test transversal en `backend/Tests/Versioning/`: el header `api-supported-versions: 1.0`, el 404 de `/api/v9/...` y de `/api/...`, y que todo endpoint bajo `/api` tenga metadata de versión. El smoke suma dos aserciones.
- **Regla de Dune:** DUNE0012 nueva, al lado de DUNE0011 y sin cambiarla. Exige tres cosas. Un `MapGroup` sobre el host solo vale sobre un `IVersionedEndpointRouteBuilder` y con el prefijo literal `/api/v{version:apiVersion}`. `Map*Endpoints` de un `*Feature` recibe un `RouteGroupBuilder`. `NewVersionedApi`, `WithApiVersionSet` y `NewApiVersionSet` solo aparecen en `backend/Infrastructure/`.
- **Trampa principal:** un grupo con `{version:apiVersion}` en la ruta pero sin version set responde con **cualquier** versión (`/api/v9/raw` dio 200 en el experimento). Por eso el grupo raíz versionado tiene que ser único y obligatorio.

## 1. Paquetes para Minimal APIs en .NET 10 y OpenAPI

**Respuesta:** alcanza con `Asp.Versioning.Http` 10.2.3. Para OpenAPI existe `Asp.Versioning.OpenApi` 10.2.3, que se integra con `Microsoft.AspNetCore.OpenApi`.

| Paquete | Versión | Licencia | Depende de | Fuente |
|---|---|---|---|---|
| `Asp.Versioning.Http` | 10.2.3 (2026-08-22) | MIT | `Asp.Versioning.Abstractions` 10.2.1; framework `Microsoft.AspNetCore.App` | [NuGet](https://www.nuget.org/packages/Asp.Versioning.Http/10.2.3), [nuspec](https://api.nuget.org/v3-flatcontainer/asp.versioning.http/10.2.3/asp.versioning.http.nuspec) |
| `Asp.Versioning.OpenApi` | 10.2.3 (2026-08-22) | MIT | `Asp.Versioning.Mvc.ApiExplorer` 10.2.1, `Microsoft.AspNetCore.OpenApi` ≥ 10.0.10, `Microsoft.OpenApi` [2.7.5, 3.0.0) | [NuGet](https://www.nuget.org/packages/Asp.Versioning.OpenApi/10.2.3), [nuspec](https://api.nuget.org/v3-flatcontainer/asp.versioning.openapi/10.2.3/asp.versioning.openapi.nuspec) |

- La documentación de la librería dice que `Asp.Versioning.OpenApi` sirve para Minimal APIs y controllers en ASP.NET Core 10+. Se activa con `AddApiVersioning().AddApiExplorer(...).AddOpenApi(...)` y `app.MapOpenApi().WithDocumentPerVersion()` ([docs/overview](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/docs/overview.html), [ejemplo MinimalOpenApiExample](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/examples/AspNetCore/WebApi/MinimalOpenApiExample/Program.cs)).
- El repo ya tiene `Microsoft.AspNetCore.OpenApi` 10.0.12 (`backend/TemplateApp.csproj:12`). Cumple el mínimo 10.0.10.
- **Sin** `Asp.Versioning.OpenApi`, el documento cambia de forma. En el experimento, `/openapi/v1.json` mostró `"/api/v{version}/tasks/{id}"`, y el parámetro `version` ni siquiera aparecía declarado. El documento queda roto. Pasa porque `Microsoft.AspNetCore.OpenApi` incluye por defecto todo endpoint sin `GroupName` ([OpenApiOptions.cs:32](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/OpenApi/src/Services/OpenApiOptions.cs#L32)) y usa la plantilla de ruta tal cual.
- **Con** `Asp.Versioning.OpenApi`, `GroupNameFormat = "'v'VVV"` y `SubstituteApiVersionInUrl = true`, el experimento mostró `/openapi/v1.json` con `"/api/v1/tasks/{id}"`. Igual cambian dos detalles: `info.version` pasa de `"1.0.0"` a `"1.0"` y aparece `"description": ""`. `SubstituteApiVersionInUrl` reemplaza el parámetro de versión por su valor ([docs/options](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/docs/options.html)).
- Costos de `Asp.Versioning.OpenApi`, leídos en el código:
  - Trae `Asp.Versioning.Mvc` y su ApiExplorer: MVC Core entra al proceso. En el experimento apareció un log `info` de MVC ("No action descriptors found"). El repo filtra `Microsoft.AspNetCore` a `Warning`, así que no se vería.
  - Usa reflexión sobre tipos **internos** de `Microsoft.AspNetCore.OpenApi` ([Reflection/Type.cs:7](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.OpenApi/Reflection/Type.cs#L7): "all of these types are internal in Microsoft.AspNetCore.OpenApi"). Un parche de `Microsoft.AspNetCore.OpenApi` podría romperlo. Conviene fijar las dos versiones juntas.
- El documento OpenAPI se publica solo en Development (`Program.cs`, `if (app.Environment.IsDevelopment()) app.MapOpenApi()`). No forma parte del contrato que consume el frontend.

## 2. Configuración de `AddApiVersioning` para versión en la URL

**Respuesta:** solo se cambian dos opciones. Las otras ya tienen el valor correcto o no aplican.

```csharp
services.AddApiVersioning(options =>
{
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
    options.ReportApiVersions = true;
});
```

| Opción | Valor | Por qué | Fuente |
|---|---|---|---|
| `ApiVersionReader` | `new UrlSegmentApiVersionReader()` | El lector por defecto combina query string y segmento de URL. Con el combinado, `/api/v1/tasks/{id}?api-version=2.0` responde **400** `AmbiguousApiVersion` (experimento). Hoy ese query se ignora. Con solo el segmento, el experimento dio 200, igual que hoy. | [reader](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/reader.html), [ApiVersionPolicyJumpTable.cs:140](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Routing/ApiVersionPolicyJumpTable.cs#L140) |
| `ReportApiVersions` | `true` | Agrega `api-supported-versions` (y `api-deprecated-versions` si hay) a cada respuesta versionada. Es lo que usan los ejemplos oficiales. Es un header **nuevo** en las respuestas: ver "Choques". | [options](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/options.html), [MinimalApiExample](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/examples/AspNetCore/WebApi/MinimalApiExample/Program.cs) |
| `DefaultApiVersion` | sin tocar (1.0) | Solo sirve para servicios sin versión explícita. Aquí la versión siempre está en la URL. | [options](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/options.html) |
| `AssumeDefaultVersionWhenUnspecified` | sin tocar (`false`) | La doc dice que con segmento de URL "no es posible tener una versión por defecto" y que la opción "probablemente no tenga efecto". | [version-by-url](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/how-to/version-by-url.html), [limitations](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/limitations.html) |
| `RouteConstraintName` | sin tocar (`"apiVersion"`) | Es el nombre que usan la doc y los ejemplos: `v{version:apiVersion}`. | [options](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/options.html) |
| `UnsupportedApiVersionStatusCode` | sin tocar | Con segmento de URL "siempre se devuelve 404", sin importar esta opción. | [options](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/options.html) |

Detalles:

- La `v` no es parte de la versión; va en la plantilla de ruta ([version-format](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/version-format.html)).
- `/api/v1.0/tasks/{id}` también responde 200 (experimento): `1` y `1.0` son la misma versión. Es una URL nueva que hoy no existe. El cambio es aditivo, pero conviene saberlo.
- La doc advierte que la versión en la URL "viola la restricción de interfaz uniforme de REST" y que es el método más lento ([reader](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/reader.html)). Tobi ya eligió la URL; queda anotado como costo conocido.
- `AddApiVersioning` registra `IHttpContextAccessor` y, si la versión va en la URL, reemplaza `LinkGenerator` por un decorador ([IServiceCollectionExtensions.cs:155](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/DependencyInjection/IServiceCollectionExtensions.cs#L155)). Ver la pregunta 4.

## 3. Dónde viven el version set y el grupo raíz

**Respuesta:** un solo grupo raíz versionado, en `Infrastructure/`, que `Program.cs` pasa a cada feature. Es la forma que mejor cumple ADR-0004 y la que la librería permite sin trucos.

### Lo que dice la librería

- Un "version set" es el conjunto de versiones de una API lógica. La librería agrupa por el **nombre lógico de la API**, no por la plantilla de ruta ([how-to/overview](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/how-to/overview.html)).
- `NewVersionedApi(name)` crea un grupo con prefijo vacío y su version set. **Falla si se llama sobre un grupo** (`"A versioned API group cannot be mapped as a nested group."`): se llama sobre el host ([IEndpointRouteBuilderExtensions.cs:39-59](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Builder/IEndpointRouteBuilderExtensions.cs#L39-L59)).
- `WithApiVersionSet(name)` convierte un grupo en versionado. Falla si ese grupo ya está dentro de otro grupo versionado ([L88-L104](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Builder/IEndpointRouteBuilderExtensions.cs#L88-L104)).
- Los grupos anidados heredan el version set. `HasApiVersion` sobre un grupo anidado **agrega** la versión al set compartido ([IEndpointConventionBuilderExtensions.cs:131](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Builder/IEndpointConventionBuilderExtensions.cs#L131), [L362](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Builder/IEndpointConventionBuilderExtensions.cs#L362)).
- El ejemplo oficial usa un `NewVersionedApi("Orders")` y un `NewVersionedApi("People")`: una API lógica por recurso ([ApiBuilder.cs](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/examples/AspNetCore/WebApi/MinimalOpenApiExample/Services/ApiBuilder.cs)). En ese ejemplo la versión va en el query string, no en la URL.

### Las dos opciones

| | A. Un grupo raíz versionado (recomendada) | B. Un version set por feature |
|---|---|---|
| Forma | `Infrastructure` crea `app.NewVersionedApi().MapGroup("/api/v{version:apiVersion}").HasApiVersion(1.0)`. Cada feature recibe ese `RouteGroupBuilder` y hace `api.MapGroup("/tasks")`. | Cada feature hace `NewVersionedApi("Tasks").MapGroup("/api/v{version:apiVersion}/tasks").HasApiVersion(1.0)`, o recibe un grupo de prefijo común y llama a `WithApiVersionSet("Tasks")`. |
| Texto `/api/v{version:apiVersion}` | Una sola vez. | Repetido en cada feature, salvo con un grupo de prefijo común. |
| Olvidar el versionado | Imposible: todo lo que cuelga del grupo está versionado. | Posible. Si a una feature le falta el set, responde con **cualquier** versión (experimento: `/api/v9/raw` dio 200). |
| `api-supported-versions` | Una sola lista para toda la API: hoy `1.0`. | Una lista por feature. |
| Una feature pasa a v2 y las otras no | Mal resuelto: el set compartido informa `1.0, 2.0` en **todas** las features, aunque `/api/v2/users` dé 404. Además, el grupo de la feature conserva v1 (experimento: `/api/v1/reports` respondió con el handler de v2). | Bien resuelto: cada feature tiene sus versiones. |
| ADR-0004 | Las features no se conocen. Infrastructure no nombra features. La complejidad del versionado queda abajo, en un solo lugar (módulo profundo). | También cumple, pero cada feature carga el versionado y su regla. |

**Por qué A ahora.** Hoy hay una sola versión, v1, y el frontend consume `/api/v1` como un todo: un solo proxy (`frontend/proxy.conf.json`). La opción A cumple el objetivo del paso, que es "un solo lugar declara la versión". También elimina la trampa del grupo sin set. La opción B resuelve un problema que todavía no existe: versiones distintas por feature. Cuando aparezca una v2, hay que decidir entre "toda la API pasa a v2" y "versiones por feature". Esa decisión no es de este paso. [INFERENCIA, basada en las fuentes de arriba y en el experimento]

### Forma propuesta

`backend/Infrastructure/Http/VersionedApi.cs`:

```csharp
using Asp.Versioning;

namespace TemplateApp.Infrastructure.Http;

public static class VersionedApi
{
    public static IServiceCollection AddVersionedApi(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
            options.ReportApiVersions = true;
        });
        return services;
    }

    public static RouteGroupBuilder MapVersionedApi(this IEndpointRouteBuilder app) =>
        app.NewVersionedApi()
            .MapGroup("/api/v{version:apiVersion}")
            .HasApiVersion(1.0);
}
```

Si Tobi aprueba `Asp.Versioning.OpenApi`, `AddVersionedApi` encadena además `.AddApiExplorer(o => { o.GroupNameFormat = "'v'VVV"; o.SubstituteApiVersionInUrl = true; }).AddOpenApi()`, y `Program.cs` cambia `app.MapOpenApi()` por `app.MapOpenApi().WithDocumentPerVersion()`. En el experimento no hizo falta el `builder.Services.AddOpenApi()` aparte.

`backend/Program.cs`, solo las líneas que cambian:

```csharp
builder.Services.AddVersionedApi();

RouteGroupBuilder api = app.MapVersionedApi();
api.MapAuthEndpoints();
api.MapUsersEndpoints();
api.MapTasksEndpoints();
api.MapHealthEndpoints();
```

`backend/Features/Tasks/TasksFeature.cs`:

```csharp
public static RouteGroupBuilder MapTasksEndpoints(this RouteGroupBuilder api)
{
    RouteGroupBuilder tasks = api.MapGroup("/tasks").RequireAuthorization();

    CreateTask.CreateTaskEndpoint.Map(tasks);
    ListTasks.ListTasksEndpoint.Map(tasks);
    GetTask.GetTaskEndpoint.Map(tasks);
    UpdateTask.UpdateTaskEndpoint.Map(tasks);
    DeleteTask.DeleteTaskEndpoint.Map(tasks);

    return api;
}
```

- Por qué `Infrastructure/` y no `Shared/`: ADR-0004 punto 6 deja `Infrastructure/` para "la fontanería técnica del host", y `architecture.md` le da "composición del host". `Shared/` es para tipos que usan varias features, como `ApiResults`. Las features no usan `VersionedApi`: solo reciben un `RouteGroupBuilder`.
- `InfrastructureBoundaryAnalyzer` no se queja: `VersionedApi` no nombra ninguna feature.
- Health queda versionado, igual que hoy: `/api/v1/health/*`. La CI usa `/api/v1/health/ready` (`.github/workflows/ci.yml:113`). Marcarlo `IsApiVersionNeutral()` haría que `/api/v9/health/live` responda 200 ([version-neutral](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/how-to/version-neutral.html)), y eso sería un contrato nuevo.

## 4. URLs de `Location` en los 201 sin `/api/v1` fijo

**Respuesta:** endpoint con nombre más `LinkGenerator.GetPathByName(HttpContext, ...)`. La librería no tiene una guía escrita para esto. Su código y el experimento muestran que agrega la versión sola.

- Microsoft recomienda nombrar endpoints para generar URLs "sin escribir rutas a mano". Los nombres distinguen mayúsculas, **tienen que ser únicos en toda la app** y se usan como `operationId` de OpenAPI ([route-handlers, Named endpoints and link generation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0#named-endpoints-and-link-generation)).
- Con versión en la URL, la librería envuelve `LinkGenerator` en `ApiVersionLinkGenerator`. En las sobrecargas que reciben `HttpContext`, agrega el valor de ruta `version` con la versión pedida, si no se pasó otro ([ApiVersionLinkGenerator.cs:74](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Routing/ApiVersionLinkGenerator.cs#L74)). Las sobrecargas **sin** `HttpContext` no agregan nada.
- Experimento: `links.GetPathByName(http, "GetTask", new { id })` dio `Location: /api/v1/tasks/{id}`. Ese valor es relativo e igual al de hoy. Desde otro grupo, `GetPathByName(http, "GetUser", new { authId = "abc" })` dio `/api/v1/users/abc`.
- `Results.CreatedAtRoute(...)` llama a `GetUriByRouteValues` y genera una URL **absoluta** ([CreatedAtRouteOfT.cs:95-100](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Http/Http.Results/src/CreatedAtRouteOfT.cs#L95-L100)). El experimento dio `Location: http://127.0.0.1:5591/api/v1/tasks/{id}`. El formato del header cambia y, detrás del proxy, el host sería el interno. No conviene.
- El `Location` relativo de Learn (`TypedResults.Created($"{todo.Id}", todo)`) funciona allí porque el POST va a `/public/todos/` con barra final ([route-handlers, Route groups](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0#route-groups)). Aquí el POST va a `/api/v1/tasks`, sin barra. Según la resolución de referencias relativas, `{id}` resolvería a `/api/v1/{id}` ([RFC 3986 §5.2](https://www.rfc-editor.org/rfc/rfc3986#section-5.2)). [INFERENCIA]

Forma en `CreateTaskEndpoint.cs`:

```csharp
private static async Task<IResult> HandleAsync(
    CreateTaskRequest request,
    CreateTask slice,
    ClaimsPrincipal user,
    HttpContext http,
    LinkGenerator links,
    CancellationToken ct)
{
    var result = await slice.ExecuteAsync(user.GetCallerId(), request, ct);
    return ApiResults.ToResult(result, value =>
        Results.Created(links.GetPathByName(http, TaskEndpointNames.GetTask, new { id = value.Id }), value));
}
```

- `GetTaskEndpoint.Map` agrega `.WithName(TaskEndpointNames.GetTask)` al `MapGet("/{id:guid}", ...)`.
- `TaskEndpointNames` va en la raíz de `Features/Tasks/`. Dos slices lo comparten, así que ADR-0004 punto 5 lo manda a la raíz de la feature, no a `Contracts/`.
- `RegisterUser` (Auth) apunta a `GET /users/{authId}` (Users). El nombre de ese endpoint lo ve otra feature, así que va en `Features/Users/Contracts/` (ADR-0004 punto 3). Esto toca la dirección Auth → Users: ver "Choques".
- `GetUserEndpoint` hoy mapea `/me` y `/{authId}` en el mismo `Map`. El nombre va solo en el `MapGet("/{authId}", ...)`.
- Si el nombre no existe, `GetPathByName` devuelve `null`. `Results.Created(null, value)` responde 201 **sin** `Location`, y nadie se entera. El test de caracterización lo detecta (pregunta 7). [INFERENCIA por la firma `Results.Created(string? uri, ...)`; sin verificar en ejecución]
- Poner nombres agrega `operationId` al documento OpenAPI de Development: es un cambio aditivo.

## 5. Versión no soportada y ruta sin versión

**Respuesta:** las dos dan `404` con el cuerpo vacío. No llevan `api-supported-versions`. Es el mismo 404 que hoy da una ruta que no existe.

- La doc: con versión en la URL, una versión no soportada da "siempre 404" ([options](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/options.html), [errors](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/errors.html), nota 2 de "Unsupported"). El test de aceptación de la librería solo verifica el código ([when using a url segment.cs, `then_get_should_return_404_for_an_unsupported_version`](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/Acceptance/Asp.Versioning.Mvc.Acceptance.Tests/Mvc/UsingAttributes/given%20a%20versioned%20Controller/when%20using%20a%20url%20segment.cs)).
- El código: cuando la versión vino de la URL y no hay endpoint para ella, la política "sale" del ruteo (`rejection.Exit`). No elige un endpoint de error ([ApiVersionPolicyJumpTable.cs:86-132](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Routing/ApiVersionPolicyJumpTable.cs#L86-L132)). Sin endpoint, ningún reporter escribe headers.
- Experimento, configuración recomendada:

| Request | Status | Body | `api-supported-versions` |
|---|---|---|---|
| `GET /api/v1/tasks/{id}` | 200 | JSON | `1.0` |
| `GET /api/v9/tasks/{id}` | 404 | vacío (`Content-Length: 0`) | no |
| `GET /api/tasks/{id}` | 404 | vacío | no |
| `GET /api/vabc/tasks/{id}` | 404 | vacío | no |

- Los errores propios de la librería (`ApiVersionUnspecified`, `UnsupportedApiVersion`, `InvalidApiVersion`, `AmbiguousApiVersion`) son ProblemDetails con extensión `code`, si está `AddProblemDetails()` ([errors](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/errors.html)). El repo ya llama a `AddProblemDetails()` (`Infrastructure/DependencyInjection/BackendInfrastructure.cs:14`). Con versión solo en la URL, el único que puede salir en la práctica es el 400 `AmbiguousApiVersion`, y solo con el lector combinado. Con el lector recomendado, ninguno.
- **Cómo volverlo ProblemDetails:** `app.UseStatusCodePages()` con `AddProblemDetails()` escribe ProblemDetails en toda respuesta 4xx/5xx **sin cuerpo**, "incluidos los errores de ruteo (404)" ([error-handling-api, Client and Server error responses y Problem details](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0)). En el experimento, `/api/v9/tasks/{id}` dio `application/problem+json` con `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":...}`, sin `code`.
- Recomendación: **no** sumarlo en el paso 4. Cambia la respuesta de **todas** las rutas inexistentes y de todo 4xx sin cuerpo, no solo las del versionado. El paso 6 ("Catálogo de errores estable") es el lugar natural para eso, con un `code` estable. Va como pendiente en `progreso.md` (regla 7 de AGENTS.md).

## 6. Deprecación y sunset

**Respuesta:** hoy no hay nada que configurar. Queda anotado cómo se hace.

- `HasDeprecatedApiVersion(x)` sobre el grupo publica `x` en `api-deprecated-versions`. "Deprecada" no significa "sin soporte": significa que dejará de tener soporte en seis meses o más ([deprecate-version](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/how-to/deprecate-version.html)).
- Las políticas (`options.Policies.Deprecate(v)` y `.Sunset(v)`) agregan los headers `Deprecation` ([RFC 9745](https://www.rfc-editor.org/rfc/rfc9745.html)), `Sunset` ([RFC 8594](https://www.rfc-editor.org/rfc/rfc8594.html)) y `Link` con `rel="deprecation"` / `rel="sunset"` ([RFC 8288](https://datatracker.ietf.org/doc/html/rfc8288)). Solo se aplican a la versión pedida y requieren `ReportApiVersions = true` ([version-policies](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/version-policies.html)).
- Formato real, según el código: `Deprecation: @<segundos unix>` y `Sunset` en formato RFC 1123 ([HttpResponseExtensions.cs:28, :113](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Http/HttpResponseExtensions.cs#L113)). `Deprecation` sale solo si se vuelve efectivo antes del sunset ([Common/DefaultApiVersionReporter.cs](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/Common/src/Common/DefaultApiVersionReporter.cs)).
- Experimento: con `HasDeprecatedApiVersion(0.9)` y `Policies.Sunset(0.9).Effective(2027, 1, 1).Link(...)`, `GET /api/v0.9/old` devolvió `api-deprecated-versions: 0.9`, `Sunset: Fri, 01 Jan 2027 03:00:00 GMT` y `Link: <...>; rel="sunset"`. Sin `Policies.Deprecate`, no salió `Deprecation`: `HasDeprecatedApiVersion` solo llena `api-deprecated-versions`.
- **Trampa:** `Effective(2027, 1, 1)` usó la hora local de la máquina (UTC−3), y el header salió a las 03:00 GMT. Conviene pasar un `DateTimeOffset` en UTC.
- Con `Asp.Versioning.OpenApi`, las políticas también se documentan en OpenAPI ([version-policies](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/version-policies.html), [openapi-options](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/docs/openapi-options.html)).
- **Ahora:** nada. v1 es la única versión y no hay página de política que enlazar.
- **Después**, cuando exista v2: `HasDeprecatedApiVersion(1.0)`, una política `Deprecate(1.0)` con fecha y link, y una `Sunset(1.0)` con fecha. Encaja con la "política de deprecación de API con fecha" de [informe-dotnet-empresarial.md](../informes/informe-dotnet-empresarial.md) (línea 382).

## 7. Cómo probarlo

**Respuesta:** primero tests que fijan el contrato de hoy. Después el cambio. Al final, un test transversal del versionado.

1. **Caracterización, antes de tocar el código.** Hoy ningún test mira el `Location` (búsqueda de `Location` en `backend/`: cero resultados). Se suman:
   - `CreateTaskTests`: el 201 trae `Location == $"/api/v1/tasks/{id}"`.
   - `RegisterUserTests`: el 201 trae `Location == $"/api/v1/users/{authId}"`.
   - `SessionResponse` (`Tests/Support`) no expone headers. Necesita una propiedad, por ejemplo `Location` o `Headers`. Va en `Tests/Support`, según `conventions.md` ("Los helpers no se copian entre archivos de test").
   - Estos tests tienen que pasar **antes** y **después** del cambio. Esa es la evidencia de que v1 no cambió.
2. **Test transversal**, en `backend/Tests/Versioning/ApiVersioningTests.cs`. `conventions.md` pone las pruebas de `Infrastructure/` en `backend/Tests/<Área>/`. Casos:
   - `GET /api/v1/health/live` → 200 y `api-supported-versions` igual a `1.0`.
   - `GET /api/v9/health/live` → 404, cuerpo vacío, sin `api-supported-versions`.
   - `GET /api/health/live` → 404.
   - `GET /api/v1/tasks/{id}?api-version=2.0` → no 400. Fija la elección del lector.
   - Todo `RouteEndpoint` bajo `/api` de `EndpointDataSource` (desde `ApiFixture.Services`) tiene `ApiVersionMetadata` y no es version-neutral (`ApiVersionMetadata.IsApiVersionNeutral`, [ApiVersionMetadata.cs:67](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/Abstractions/src/Asp.Versioning.Abstractions/ApiVersionMetadata.cs#L67)). Es la versión en ejecución de la regla de Dune: atrapa lo que el análisis estático no ve. Que `RoutePattern.RawText` traiga el prefijo combinado del grupo está **sin verificar**.
3. **Las escrituras sin versión válida:** un `POST /api/v9/tasks` sin token antiforgery da **400** `Auth.AntiforgeryFailed`, no 404. El middleware mira el path (`/api`), no el endpoint (`SessionHttpPolicy.cs:38`). Hoy pasa lo mismo con cualquier POST a una ruta inexistente. El 404 se prueba con GET, o con POST y token. [INFERENCIA por el código del repo; sin verificar en ejecución]
4. **Smoke** (`.agents/skills/verify-template/scripts/smoke-api.mjs`). La base ya es `http://localhost:8080/api/v1/`, así que v1 queda cubierto sin cambios. Se suman dos aserciones:

```js
const live = await client.request('GET', 'health/live');
assert.equal(live.headers.get('api-supported-versions'), '1.0');
await client.request('GET', '../v9/health/live', undefined, { expected: 404 });
```

   `request` ya devuelve `headers` y tolera un cuerpo vacío (`payload = text ? JSON.parse(text) : undefined`). `new URL('../v9/health/live', base)` resuelve a `/api/v9/health/live`. El nombre de la variable del cliente es ilustrativo.

## 8. Trampas y la regla de Dune

### Trampas

- **Grupo con `{version:apiVersion}` sin version set.** Responde con cualquier versión. La librería trata un candidato sin versión como match válido ([ApiVersionMatcherPolicy.cs:96-98](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Routing/ApiVersionMatcherPolicy.cs#L96-L98)). En el experimento, `/api/v9/raw` dio 200.
- **`NewVersionedApi` anidado.** Lanza `InvalidOperationException` al arrancar. Va sobre el host, no sobre un grupo (pregunta 3).
- **Agregar versiones en grupos hijos del set compartido.** Las suma al set de toda la API y el grupo conserva la versión heredada (experimento, pregunta 3).
- **Grupos anidados y convenciones.** El prefijo se compone (`/api/v{version:apiVersion}` + `/tasks`). `RequireAuthorization()` y `AllowAnonymous()` sobre el grupo de la feature se aplican igual que hoy: la metadata de un grupo equivale a ponerla en cada endpoint ([route-handlers, Route groups](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0#route-groups)). Health conserva `.AllowAnonymous()`. `RegisterUser` conserva `.RequireRateLimiting("auth")`.
- **Antiforgery.** `UseAuthAntiforgery` filtra por `StartsWithSegments("/api")`. `/api/v{n}/...` sigue cubierto. No hace falta tocarlo.
- **DUNE0011 hoy.** El `MapGroup("/api/v{version:apiVersion}")` en Infrastructure tiene como receptor un `IVersionedEndpointRouteBuilder`, no un `RouteGroupBuilder`. DUNE0011 lo trata como host y mira el literal: empieza con `/api/`, así que pasa. Las features hacen `api.MapGroup("/tasks")` sobre un `RouteGroupBuilder`, así que DUNE0011 no las mira. Las slices siguen recibiendo un `RouteGroupBuilder`. **DUNE0011 pasa sin cambios.** El `MapGroup(string.Empty)` interno de `NewVersionedApi` vive en la librería, y el analyzer no lo ve. [INFERENCIA por `ApiRouteAnalyzer.cs`; sin verificar con build]
- **Lo que DUNE0011 no garantiza.** Una feature que recibiera el host podría seguir haciendo `app.MapGroup("/api/v1/x")` sin versión. Pasaría DUNE0011 y quedaría fuera del versionado. Además, `MapGet` sobre el host queda libre por decisión (`decisions.tsv:102`): `app.MapGet("/api/v1/x", ...)` tampoco tendría versión.
- **Nombres de endpoint.** Tienen que ser únicos en toda la app (Learn, pregunta 4). Conviene un prefijo de feature: `"Tasks.GetTask"`, `"Users.GetUser"`.

### Regla propuesta: DUNE0012 "Ruta fuera de la API versionada"

Es una regla nueva, no un cambio de DUNE0011. DUNE0011 protege el antiforgery (su título es "Ruta fuera del antiforgery de /api"). Esta protege el versionado. Las dos conviven: el prefijo que exige DUNE0012 cumple DUNE0011. Usa la misma técnica que DUNE0011: operaciones de invocación, tipo estático del receptor y literales constantes. Va con su línea en `decisions.tsv`, su fixture válida y su violación deliberada (reglas 4 y 9 de AGENTS.md).

Reglas:

1. Un `MapGroup` de ASP.NET Core con un receptor que **no** es `RouteGroupBuilder` solo vale si el receptor es `Asp.Versioning.Builder.IVersionedEndpointRouteBuilder` y el prefijo es el literal exacto `/api/v{version:apiVersion}`.
2. Un método estático `Map*Endpoints` de una clase `*Feature` en `TemplateApp.Features.*` declara su primer parámetro como `RouteGroupBuilder`. Con eso, el compilador rechaza `app.MapTasksEndpoints()` sobre el host.
3. `NewVersionedApi`, `WithApiVersionSet` y `NewApiVersionSet` solo se llaman en `backend/Infrastructure/`. Una feature no crea su propio set (opción A de la pregunta 3).
4. Opcional, para cerrar el hueco de `MapGet`: un `MapGet` sobre el host con un literal que empieza con `/api` se rechaza. Cambia el "MapGet queda libre" de `decisions.tsv:102` para las rutas `/api`, así que necesita su propia línea en `decisions.tsv`.

Los stubs del test agregan, junto a los de `ApiRouteAnalyzerTests`, `namespace Asp.Versioning.Builder { public interface IVersionedEndpointRouteBuilder : IEndpointRouteBuilder { } }` y una extensión `NewVersionedApi(this IEndpointRouteBuilder, string? name = null)` en `Microsoft.AspNetCore.Builder`, que es el namespace real ([IEndpointRouteBuilderExtensions.cs:5](https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Builder/IEndpointRouteBuilderExtensions.cs#L5)).

Fixture válida (cero diagnósticos):

```csharp
("backend/Infrastructure/Http/VersionedApi.cs", """
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;
    namespace TemplateApp.Infrastructure.Http;
    public static class VersionedApi
    {
        public static RouteGroupBuilder MapVersionedApi(this IEndpointRouteBuilder app) =>
            app.NewVersionedApi().MapGroup("/api/v{version:apiVersion}");
    }
    """),
("backend/Features/Tasks/TasksFeature.cs", """
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;
    using TemplateApp.Features.Tasks.CreateTask;
    namespace TemplateApp.Features.Tasks;
    public static class TasksFeature
    {
        public static RouteGroupBuilder MapTasksEndpoints(this RouteGroupBuilder api)
        {
            RouteGroupBuilder tasks = api.MapGroup("/tasks");
            CreateTaskEndpoint.Map(tasks);
            return api;
        }
    }
    """)
```

Violación deliberada (un diagnóstico por línea marcada en el `Assert`):

```csharp
("backend/Features/Webhooks/WebhooksFeature.cs", """
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;
    namespace TemplateApp.Features.Webhooks;
    public static class WebhooksFeature
    {
        public static IEndpointRouteBuilder MapWebhooksEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGroup("/api/v1/webhooks");
            app.NewVersionedApi("Webhooks").MapGroup("/api/v1/webhooks");
            return app;
        }
    }
    """)
```

Diagnósticos esperados:

- Línea 6: `MapWebhooksEndpoints` recibe `IEndpointRouteBuilder` (regla 2).
- Línea 8: grupo del host sin versionar (regla 1).
- Línea 9: dos diagnósticos. `NewVersionedApi` está fuera de Infrastructure (regla 3) y el prefijo no es `/api/v{version:apiVersion}` (regla 1).

El test de violación afirma las líneas, como hace `ApiRouteAnalyzerTests` con `Assert.Equal(new[] { ... }, Lines(diagnostics))`.

## Choques con los docs del repo

Se listan sin resolver. La regla 10 de AGENTS.md pide preguntarle a Tobi.

1. **`informe-dotnet-empresarial.md:110`** recomienda `Asp.Versioning.Mvc` / `.ApiExplorer`. Tobi eligió `Asp.Versioning.Http`, que es el paquete para Minimal APIs. `Asp.Versioning.Mvc` es para controllers. Si se suma `Asp.Versioning.OpenApi`, `Asp.Versioning.Mvc.ApiExplorer` entra de todos modos, como dependencia.
2. **`conventions.md:67`** dice "Rutas bajo `/api/v1`". Con el paso, las rutas quedan bajo `/api/v{version}`, con v1 como única versión. El texto sigue siendo cierto para v1, pero describe un literal que el paso elimina.
3. **ADR-0004 punto 4** dice "Una sola dirección entre Auth y Users. Users depende de `Auth.Contracts`. Auth no conoce el interior de Users". En cambio, **`architecture.md:51`** dice que Auth usa `Users.Contracts.IUserProfiles`, y hoy `RegisterUserEndpoint.cs:22` arma una URL de Users. La recomendación de la pregunta 4 pone el nombre del endpoint de Users en `Users/Contracts/`, para que lo use Auth. Eso suma otra dependencia Auth → `Users.Contracts`. Los dos documentos ya no coinciden entre sí, y este paso lo hace visible.
4. **`plan.md:245`** pide que "`smoke-api.mjs` responda igual en v1", y la **regla 3 de AGENTS.md** dice que un cambio de contrato HTTP va completo. `ReportApiVersions = true` agrega el header `api-supported-versions` a toda respuesta versionada. Body y status no cambian, pero el header es nuevo. Falta decidir si eso cuenta como cambio de contrato o como agregado aceptable. Lo mismo pasa con la URL nueva `/api/v1.0/...`, que la librería acepta sola.
5. **Regla 5 de AGENTS.md** ("Las decisiones de herramienta o licencia son de Tobi"). `Asp.Versioning.OpenApi` es un paquete más, no incluido en la decisión de `Asp.Versioning.Http`. Sin él, el documento OpenAPI de Development queda roto (pregunta 1). ⏸ **DECISIÓN DE TOBI.**
6. **`decisions.tsv:102`** (DUNE0011) dice "MapGet queda libre". La regla 4 opcional de DUNE0012 lo restringe para las rutas `/api`. Solo choca si se adopta esa regla 4.

## Fuentes

Librería (documentación oficial; el código fuente de esas páginas está en `wiki/src/` del repo):

- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/how-to/version-by-url.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/options.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/config/reader.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/limitations.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/errors.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/version-policies.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/how-to/deprecate-version.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/how-to/overview.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/how-to/version-neutral.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/version-format.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/docs/overview.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/docs/options.html
- https://dotnet.github.io/aspnet-api-versioning/aspnet-core/docs/openapi-options.html

Librería (código y ejemplos, commit `1a22bb4281c03d44f387169eefa46765a6219118`):

- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Builder/IEndpointRouteBuilderExtensions.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Builder/IEndpointConventionBuilderExtensions.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Builder/EndpointBuilderFinalizer.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Routing/ApiVersionPolicyJumpTable.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Routing/ApiVersionMatcherPolicy.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Routing/ApiVersionLinkGenerator.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/DependencyInjection/IServiceCollectionExtensions.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.Http/Http/HttpResponseExtensions.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/Common/src/Common/DefaultApiVersionReporter.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/Abstractions/src/Asp.Versioning.Abstractions/ApiVersionMetadata.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/WebApi/src/Asp.Versioning.OpenApi/Reflection/Type.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/examples/AspNetCore/WebApi/MinimalApiExample/Program.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/examples/AspNetCore/WebApi/MinimalOpenApiExample/Program.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/examples/AspNetCore/WebApi/MinimalOpenApiExample/Services/ApiBuilder.cs
- https://github.com/dotnet/aspnet-api-versioning/blob/1a22bb4281c03d44f387169eefa46765a6219118/src/AspNetCore/Acceptance/Asp.Versioning.Mvc.Acceptance.Tests/Mvc/UsingAttributes/given%20a%20versioned%20Controller/when%20using%20a%20url%20segment.cs

NuGet:

- https://www.nuget.org/packages/Asp.Versioning.Http/10.2.3
- https://www.nuget.org/packages/Asp.Versioning.OpenApi/10.2.3
- https://api.nuget.org/v3-flatcontainer/asp.versioning.http/10.2.3/asp.versioning.http.nuspec
- https://api.nuget.org/v3-flatcontainer/asp.versioning.openapi/10.2.3/asp.versioning.openapi.nuspec

Microsoft Learn y ASP.NET Core:

- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.routing.linkgeneratorendpointnameaddressextensions.getpathbyname?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.results.createdatroute?view=aspnetcore-10.0
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Http/Http.Results/src/CreatedAtRouteOfT.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/OpenApi/src/Services/OpenApiOptions.cs

RFC:

- https://www.rfc-editor.org/rfc/rfc9745.html (Deprecation)
- https://www.rfc-editor.org/rfc/rfc8594.html (Sunset)
- https://datatracker.ietf.org/doc/html/rfc8288 (Web Linking)
- https://www.rfc-editor.org/rfc/rfc9457.html (Problem Details; reemplaza al RFC 7807 que cita la librería)
- https://www.rfc-editor.org/rfc/rfc3986#section-5.2 (resolución de referencias relativas)

Repo (leídos para fijar el estado actual): `backend/Program.cs`, `backend/TemplateApp.csproj`, `backend/Features/*/*Feature.cs`, `backend/Features/Auth/RegisterUser/RegisterUserEndpoint.cs`, `backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs`, `backend/Features/Users/GetUser/GetUserEndpoint.cs`, `backend/Features/Auth/SessionHttpPolicy.cs`, `backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs`, `backend/Shared/Http/ApiResults.cs`, `backend/Tests/Support/*`, `dune/Dune.Analyzers/ApiRouteAnalyzer.cs`, `dune/Dune.Analyzers.Tests/ApiRouteAnalyzerTests.cs`, `.agents/skills/verify-template/scripts/smoke-api.mjs`, `frontend/proxy.conf.json`, `docs/adr/0004-vertical-slices-deep-modules.md`, `docs/architecture.md`, `docs/conventions.md`, `docs/decisions.tsv`, `docs/evolucion/plan.md`, `docs/evolucion/informes/informe-dotnet-empresarial.md`.
