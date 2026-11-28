# Paso T3: los hosts descubren las features (investigación)

- Fecha: 2026-11-27.
- Estado: **Listo** como investigación. Hay tres choques entre el plan y el código (sección "Choques"). Ninguno es entre dos docs del repo, así que la regla 10 no obliga a frenar. **No pide herramientas nuevas**: la opción recomendada usa solo la BCL, ASP.NET Core y Roslyn, que el repo ya tiene.
- Alcance: el paso T3 de `docs/evolucion/plan.md` (Fase T). Cómo encuentra cada host las `<F>Feature` que compila, qué forma tiene la interfaz, qué orden usar, cómo probarlo y cómo evitar que una feature quede afuera sin aviso.
- Base: `main` en `94b968f`. .NET 10, ASP.NET Core 10, WolverineFx 6.64.2.

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `94b968f`.
- `[FUENTE]`: lo leí en un documento primario, con su link (Microsoft Learn, documentación de Wolverine).
- `[INFERENCIA]`: deducción mía. No la medí. No corrí ningún experimento para este informe.

## Choques (leer primero)

1. **El host de Tasks no compila una feature inventada sin tocar su `.csproj`.** El monolito compila `Features/**` menos Tasks `[CÓDIGO backend/Hosts/Monolith/TemplateApp.csproj:17-18]`, así que una carpeta nueva `Features/Probe/` entra sola. El host de Tasks compila solo `Features/Tasks/**` y `Features/Health/**` `[CÓDIGO backend/Hosts/Tasks/TemplateApp.Tasks.csproj:55-56]` y declara `DuneHostFeatures` = `Tasks,Health` `[CÓDIGO backend/Hosts/Tasks/TemplateApp.Tasks.csproj:10]`; `check-host-items` y DUNE0032 fallan si compila otra `[CÓDIGO docs/verificacion.md:15]`. El Verifica dice "sin tocar `Program.cs`", y eso se cumple: la feature inventada entra al host de Tasks cambiando su `.csproj` y `DuneHostFeatures`, que es justo como el repo decide qué despliega cada host. El paso tiene que decirlo así, y no "sin tocar nada".
2. **"El host de Tasks sigue viendo solo Tasks y Health" ya lo garantizan el `.csproj`, DUNE0032 y `check-host-items`.** El descubrimiento no lo cambia: recorre lo que el host compiló `[INFERENCIA]`. El test nuevo (sección 4) lo prueba en el proceso que arrancó, que es lo que pide el plan.
3. **No todas las features tienen rutas internas.** Users y Health no tienen `Map<F>InternalEndpoints` `[CÓDIGO backend/Features/Users/UsersFeature.cs:58-70]`, `[CÓDIGO backend/Features/Health/HealthFeature.cs:3-20]`. La interfaz necesita que las rutas internas sean opcionales (sección 2).

## Respuesta corta

1. **Cómo descubrir (pregunta 1).** Reflexión sobre el ensamblado del host: los tipos que implementan una interfaz de Platform, una instancia de cada uno y una llamada por instancia. Es lo más corto de leer, y el costo que tiene (advertencias de trimming) no aplica: ningún proyecto del repo publica con trimming ni con AOT (busqué `PublishTrimmed`, `PublishAot`, `IsAotCompatible` y `IsTrimmable` en todos los `.csproj`: cero resultados). Ninguna opción necesita una librería nueva.
2. **Forma (pregunta 2).** Interfaz de instancia (`internal sealed class AuthFeature : IFeature`) con un método por defecto vacío para las rutas internas. Los miembros `static abstract` complican el recorrido y no ganan nada acá. Los ayudantes privados `this IServiceCollection` pasan a métodos privados comunes.
3. **Orden (pregunta 3).** Ordenar por nombre de feature (orden ordinal), porque el orden de `GetTypes` no está documentado. Con el código de hoy, el orden entre features no cambia nada observable; lo que importa es que todas se registren **después** de `AddBackendInfrastructure` y `AddMessaging`, en el mismo lugar que hoy.
4. **Prueba (pregunta 4).** Permanente: un test por host que lee la lista de features arrancadas, y un test del mecanismo con una feature inventada en el ensamblado de tests. Evidencia única: una feature `Probe` real en `backend/Features/`, compilada en los dos hosts, que responde por `/api/v1/probe` y `/internal/v1/probe`, y se borra antes del PR.
5. **Falla silenciosa (pregunta 5).** Una regla de Dune barata: en `backend/Features/<F>/<F>Feature.cs`, el tipo `<F>Feature` tiene que ser una clase no estática que implementa `IFeature`. Más un chequeo al arrancar que compara los namespaces `TemplateApp.Features.<F>` compilados con las features encontradas.

## 1. Cómo descubre un host sus módulos

### 1.1 Cómo es hoy

- Cada `Program.cs` llama a cada feature tres veces: servicios, rutas públicas y rutas internas `[CÓDIGO backend/Hosts/Monolith/Program.cs:22-25, 47-54]`, `[CÓDIGO backend/Hosts/Tasks/Program.cs:20-21, 43-47]`.
- Cada `<F>Feature` es `internal static class` con métodos de extensión `[CÓDIGO backend/Features/Auth/AuthFeature.cs:23-124]`.
- Las features se compilan **dentro** del ensamblado del host, no en un ensamblado propio `[CÓDIGO backend/Hosts/Monolith/TemplateApp.csproj:9, 17]`. Entonces "las features que este host compiló" = "los tipos de `typeof(MonolithHost).Assembly`" `[INFERENCIA]`. Es el mismo ensamblado que ya recibe `AddMessaging` `[CÓDIGO backend/Hosts/Monolith/Program.cs:21]`.
- Wolverine ya no descubre solo: corre con `ExtensionDiscovery.ManualOnly` y `DisableConventionalDiscovery()`, y cada handler entra con `IncludeType` `[CÓDIGO backend/Infrastructure/Messaging/MessagingRegistration.cs:23]`, `[CÓDIGO backend/Infrastructure/Messaging/MessagingExtension.cs:57, 129]`. El paso no cambia eso: sigue llegando por los `AddMediatorHandler` de cada feature.

### 1.2 Las opciones

| Opción | Cómo funciona | Gana | Pierde | ¿Herramienta nueva? |
|---|---|---|---|---|
| **A. Reflexión + interfaz** | `assembly.GetTypes()`, filtra los que implementan `IFeature`, crea una instancia y llama a sus métodos | Pocas líneas, en un solo archivo de Platform. Es lo que hace el repo con Wolverine (le pasa el ensamblado) | `GetTypes()` tiene `[RequiresUnreferencedCode("Types might be removed")]` ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.gettypes?view=net-10.0)): con trimming da IL2026 ([FUENTE](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-warnings/il2026)). Acá no aplica: no hay trimming. Tira `ReflectionTypeLoadException` si un tipo no carga ([misma fuente](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.gettypes?view=net-10.0)) | No |
| **B. Miembros `static abstract`** | `interface IFeature { static abstract void AddServices(...); }` y clases que los implementan | El compilador obliga a cada feature a tener los métodos | Se resuelven "en tiempo de compilación", sin despacho en ejecución ([FUENTE](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/interface-implementation/static-virtual-interface-members)). Para llamarlos desde una lista de `Type` hay que usar reflexión de todos modos (`MethodInfo.Invoke` o `MakeGenericMethod`). Una interfaz así no sirve como argumento de tipo (CS8920, [FUENTE](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/static-abstract-interfaces)): no hay `List<IFeature>` | No |
| **C. Source generator de Roslyn** | Un generador escribe en compilación la lista de features del host ([FUENTE](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)) | Sin reflexión, apto para AOT; la lista se ve en el código generado | Un proyecto generador más, con sus tests, para resolver una lista de cinco. El lector tiene que saber que existe código que no ve en el repo `[INFERENCIA]` | No es librería externa (`Microsoft.CodeAnalysis` ya lo usa `dune/`), pero sí una pieza nueva de build. Si el paso la eligiera, conviene preguntarle a Tobi (regla 5) |
| **D. `IHostingStartup`** | Un atributo de ensamblado `[HostingStartup]`; el host lee la lista de ensamblados de la configuración `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` ([FUENTE](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/platform-specific-configuration?view=aspnetcore-10.0)) | Sirve para agregar comportamiento desde un ensamblado externo | Es por ensamblado, no por feature: acá todas viven en el mismo. Configura el `IWebHostBuilder`, no mapea rutas de minimal API sobre el grupo versionado `[INFERENCIA]`. Mete configuración donde hoy no hace falta | No |
| **E. `ApplicationPart` de MVC** | MVC descubre controllers en las partes registradas ([FUENTE](https://learn.microsoft.com/en-us/aspnet/core/mvc/advanced/app-parts?view=aspnetcore-10.0)) | — | Es de controllers. El repo usa minimal API (`IssueCsrfTokenEndpoint.Map(auth)`) `[CÓDIGO backend/Features/Auth/AuthFeature.cs:111]`. No aplica | No |
| **F. `WolverineModule` de Wolverine** | `[assembly: WolverineModule<T>]`; un generador de Wolverine escribe un manifiesto que se lee al arrancar ([FUENTE](https://wolverinefx.net/guide/extensions)) | — | Es para extensiones de Wolverine y por ensamblado. Con `ManualOnly`, que el repo usa, está apagado ([misma fuente](https://wolverinefx.net/guide/extensions)) `[CÓDIGO MessagingRegistration.cs:23]`. No registra rutas | No |

**Para este repo gana A.** Las features ya viven en el ensamblado del host, el repo ya pasa ese ensamblado a la infraestructura (`AddMessaging`), y el único costo real de la reflexión (trimming/AOT) no existe acá. Además Wolverine genera código en ejecución o lo versiona, y su soporte de AOT todavía es un tema abierto ([FUENTE](https://github.com/JasperFx/wolverine/issues/2746)): el repo no va hacia AOT en este paso `[INFERENCIA]`.

**Costo de arranque de A.** Un `GetTypes()` sobre un ensamblado y cinco `Activator.CreateInstance`. No lo medí; es una pasada sobre los tipos ya cargados, contra los segundos que tarda migrar la base `[INFERENCIA]`.

**Detalle de A: `ReflectionTypeLoadException`.** No hace falta atraparla. Si un tipo del host no carga, el host está roto igual; que el arranque falle con esa excepción es lo correcto `[INFERENCIA]`.

## 2. Forma de la interfaz

### 2.1 Instancia contra `static abstract`

**Instancia (recomendada):**

```csharp
// backend/Infrastructure/Http/IFeature.cs (Platform)
public interface IFeature
{
    void AddServices(IServiceCollection services);
    void MapEndpoints(RouteGroupBuilder api);
    void MapInternalEndpoints(RouteGroupBuilder internalApi) { }
}
```

- El método por defecto resuelve el choque 3: Users y Health no lo escriben. Un método por defecto de interfaz solo se llama a través de la interfaz ([FUENTE](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface)), y así lo llama el recorrido.
- El recorrido es `List<IFeature>` y tres `foreach`. Sin `MethodInfo`.
- `Activator.CreateInstance(type)` necesita un constructor sin parámetros ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/system.activator.createinstance?view=net-10.0)). Una clase `internal sealed` sin constructor declarado lo tiene `[INFERENCIA de la regla del constructor por defecto de C#]`. La feature no recibe dependencias por constructor: el contenedor todavía no existe cuando se registran servicios.
- `IFeature` es `public` en Platform y la clase sigue `internal`. Cumple `conventions.md`: `<Feature>Feature` es `internal` `[CÓDIGO docs/conventions.md:19]`.

**`static abstract`:** mismas tres firmas con `static abstract` y `static virtual` para la interna. El recorrido tiene que buscar cada método estático por reflexión e invocarlo, porque CS8920 impide una lista tipada (tabla, opción B). Gana que no hay instancia; pierde lectura y chequeo del compilador en el punto de llamada.

### 2.2 Qué pasa con lo que hay hoy en `AuthFeature`

- **Los ayudantes privados** `AddAuthIdentity`, `AddAuthSession` y `AddAuthHttpPolicy` son `private static ... (this IServiceCollection services)` `[CÓDIGO backend/Features/Auth/AuthFeature.cs:60, 82, 94]`. Un método de extensión tiene que estar en una clase estática, no genérica y no anidada ([FUENTE](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods)). En una clase no estática no compilan (CS1106). Pasan a `private static void AddIdentity(IServiceCollection services)` y se llaman `AddIdentity(services)`. Es un cambio mecánico.
- **Las constantes públicas** siguen: `AuthFeature.RateLimitPolicy`, `ProjectsFeature.TasksService`, `TasksFeature.MonolithService`, `UsersFeature.ClosuresConsumer` `[CÓDIGO AuthFeature.cs:25]`, `[CÓDIGO ProjectsFeature.cs:14]`, `[CÓDIGO TasksFeature.cs:21]`, `[CÓDIGO UsersFeature.cs:18]`. Una clase no estática puede tener `const`.
- **La excepción de Dune** `[DuneExemption(DuneCheck.TransactionOwner, ...)]` sobre `TasksFeature` `[CÓDIGO backend/Features/Tasks/TasksFeature.cs:18]` sigue funcionando: Dune busca el tipo por nombre (`<ns>.<F>Feature`) con `GetTypeByMetadataName`, sin mirar si es estático `[CÓDIGO dune/Dune.Analyzers/DuneExemptions.cs:63-73]`.
- **Los grupos de ruta** siguen en la feature: `api.MapGroup("/auth")` `[CÓDIGO AuthFeature.cs:109]`. El host pasa `MapVersionedApi()` y `MapInternalApi()` como hoy `[CÓDIGO backend/Infrastructure/Http/VersionedApi.cs:23]`, `[CÓDIGO backend/Infrastructure/Http/Remote/InternalApi.cs:7]`.

### 2.3 Cómo queda `Program.cs`

```csharp
Assembly host = typeof(MonolithHost).Assembly;
builder.Services.AddBackendInfrastructure(migratesPlatformSchema: true);
builder.Services.AddTemplateObservability(...);
builder.Services.AddMessaging(host, storesSagas: true);
builder.Services.AddFeatures(host);          // reemplaza las cuatro líneas
...
app.MapFeatures(app.MapVersionedApi(), app.MapInternalApi());
```

`AddFeatures` guarda la lista en un singleton (`HostFeatures`, con los nombres) para que `MapFeatures` y el test la lean sin volver a recorrer `[INFERENCIA de diseño]`.

## 3. Orden

### 3.1 Qué dice la plataforma

- **DI:** con varias registraciones del mismo servicio, al pedir uno gana la última; con `IEnumerable<T>` vienen todas, en el orden en que se registraron ([FUENTE](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-registration)). `TryAdd*` no registra si ya hay una ([misma fuente](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-registration)).
- **Hosted services:** arrancan uno tras otro en el orden en que se registraron, salvo `ServicesStartConcurrently = true` ([FUENTE](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)), que es `false` por defecto ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.hostoptions.servicesstartconcurrently?view=net-10.0-pp)). Se detienen en orden inverso ([FUENTE](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)).
- **Rutas:** en endpoint routing, el orden de los `Map*` no decide qué ruta gana; decide la precedencia de la plantilla (la más específica), y dos empates dan `AmbiguousMatchException` ([FUENTE](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-10.0)).
- **OpenAPI:** el documento lista los endpoints en el orden de la fuente de endpoints `[INFERENCIA; no lo medí]`. Cambiar el orden de las features puede cambiar el orden de rutas en el JSON, no su contenido.
- **`GetTypes()`:** su página no promete un orden ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.gettypes?view=net-10.0)). Por eso hay que ordenar.

### 3.2 Qué depende del orden en este repo

| Pieza | Dónde | ¿Depende del orden entre features? |
|---|---|---|
| Migraciones | `DatabaseInitializer` migra en el orden de `GetServices<MigratedContext>()` `[CÓDIGO backend/Infrastructure/Persistence/Initialization/DatabaseInitializer.cs:20, 34-35]`, `[CÓDIGO backend/Infrastructure/Persistence/ContextRegistration.cs:30, 59-63]` | **No hoy.** La única FK con `principalSchema` en las migraciones de features es dentro de `projects` `[CÓDIGO backend/Features/Projects/Persistence/Migrations/20261119160703_AddProjectTaskCounters.cs:44]` (busqué `principalSchema` en `Features/*/Persistence/Migrations`). Platform se registra antes, en `AddBackendInfrastructure` `[CÓDIGO backend/Infrastructure/BackendInfrastructure.cs:35]` |
| Base heredada | Baseline de los contextos que adoptan tablas `[CÓDIGO DatabaseInitializer.cs:30-31]` | No: cada uno marca su propio historial `[INFERENCIA]` |
| Seeders | `GetServices<IDatabaseSeeder>()` `[CÓDIGO DatabaseInitializer.cs:37-38]` | No: hay uno solo, `RoleSeeder` `[CÓDIGO AuthFeature.cs:47]` |
| Hosted services | `DatabaseInitialization` `[CÓDIGO BackendInfrastructure.cs:38]`, `RetiredBindings` `[CÓDIGO backend/Infrastructure/Messaging/MessagingRegistration.cs:32]`, relays del outbox `[CÓDIGO backend/Infrastructure/Messaging/Outbox/OutboxRegistration.cs:19]`, `RebuildTaskCountersOnStartup` `[CÓDIGO ProjectsFeature.cs:36]` | **Entre features, no.** Lo que sí importa: `DatabaseInitialization` arranca antes que todo lo de las features porque se registra antes. `AddFeatures` tiene que quedar después de `AddBackendInfrastructure` |
| `TryAdd*` | `IntegrationEventPublisher`, `OutboxEvents`, `AfterCommit`, handlers de eventos `[CÓDIGO MessagingRegistration.cs:31, 48]`, `[CÓDIGO OutboxRegistration.cs:14-16, 31]`, `[CÓDIGO ContextRegistration.cs:37]` | No: registran el mismo tipo concreto sea quien sea el primero |
| Cookie de sesión | `AddSessionCookie` lo llama Auth en el monolito y Tasks en su host `[CÓDIGO AuthFeature.cs:87]`, `[CÓDIGO TasksFeature.cs:33]` | No: una sola feature por host lo llama |
| Políticas del limiter | Auth agrega la política `"auth"` a `RateLimiterOptions` `[CÓDIGO AuthFeature.cs:98-102]`; `AddApiRateLimiting` va después de las features `[CÓDIGO backend/Hosts/Monolith/Program.cs:27]` | No entre features. Conviene dejar `AddFeatures` donde están hoy las líneas `[INFERENCIA]` |
| Rutas | Cada feature cuelga de su propio prefijo (`/auth`, `/users`, `/projects`, `/tasks`, `/health`) `[CÓDIGO AuthFeature.cs:109]`, `[CÓDIGO UsersFeature.cs:60]`, `[CÓDIGO ProjectsFeature.cs:42]`, `[CÓDIGO TasksFeature.cs:75]`, `[CÓDIGO HealthFeature.cs:13]` | No (precedencia por plantilla) |

### 3.3 Recomendación de orden

Ordenar por nombre de feature con `StringComparer.Ordinal`. El monolito pasa de Auth, Users, Projects, Health a **Auth, Health, Projects, Users**. Gana: el mismo orden en cada máquina y en cada build, fácil de explicar y de probar. Riesgo en este repo: el orden de arranque de los relays del outbox y de `RebuildTaskCountersOnStartup` cambia, y el orden de migración de Users y Projects se invierte. Con el código de hoy ninguno depende del otro (tabla 3.2). Riesgo futuro: una feature que dependa del orden de otra queda oculta. Un orden declarado (`Order` en la interfaz) sería una lista a mano disfrazada; si algún día hace falta, va con su ADR `[INFERENCIA]`.

## 4. Cómo probarlo

### 4.1 Permanente

- **Lista por host.** Un test con `WebApplicationFactory<MonolithHost>` y otro con `<TasksHost>` (las fábricas de `ApiPair` ya existen `[CÓDIGO backend/Tests/Support/Hosts/ApiPair.cs:109-125]`) leen `HostFeatures` del contenedor y esperan `[Auth, Health, Projects, Users]` y `[Health, Tasks]`. Cubre "un test lista las features que arrancaron" y "el host de Tasks sigue viendo solo Tasks y Health". `WebApplicationFactory` arranca el `Program.cs` real del host ([FUENTE](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)).
- **Mecanismo con una feature inventada.** Los tests compilan en otro ensamblado `[CÓDIGO docs/conventions.md:19]`, así que una feature de test no entra al host por `WebApplicationFactory`. Lo que sí se puede probar siempre: un `WebApplication` mínimo en el test llama al mismo `AddFeatures(typeof(ProbeFeatureForTests).Assembly)` y `MapFeatures(...)`, con `UseTestServer()`, y la feature inventada responde por su ruta pública y por la interna. Prueba el recorrido, el método por defecto y el orden, sin tocar ningún host.

### 4.2 Evidencia única (no queda en el repo)

1. Crear `backend/Features/Probe/ProbeFeature.cs` (`internal sealed class ProbeFeature : IFeature`) con un `GET /probe` público y un `GET /probe` interno.
2. Monolito: no hace falta tocar nada; su `.csproj` lo compila `[CÓDIGO backend/Hosts/Monolith/TemplateApp.csproj:17]`.
3. Host de Tasks: agregar `Features/Probe/**` a su `.csproj` y `Probe` a `DuneHostFeatures` (choque 1). `Program.cs` no cambia.
4. Arrancar los dos hosts y pedir `/api/v1/probe` y `/internal/v1/probe`; guardar las respuestas en `artifacts/verify-template/<runId>/`.
5. Mostrar `git diff --stat` con los dos `Program.cs` sin cambios, borrar `Probe` y revertir el `.csproj`.

Dune va a quejarse de la slice de prueba sin test y sin mediator (DUNE0028 y otras) `[INFERENCIA]`: la evidencia corre los hosts, no los gates, y se dice así en el cierre.

## 5. "Una feature nueva no se descubre y nadie se entera"

El caso típico: alguien copia una feature vieja con `internal static class XFeature` y métodos de extensión. El host compila, arranca, y la feature no registra nada `[INFERENCIA]`.

| Opción | Cuándo avisa | Costo |
|---|---|---|
| **Regla de Dune (build)** | Al compilar, con el archivo y la línea | Un analyzer chico: Dune ya sabe qué carpeta es una feature (`DunePaths.TryGetFeature`) `[CÓDIGO dune/Dune.Analyzers/DunePaths.cs:33-51]` y ya busca `<F>Feature` por nombre `[CÓDIGO DuneExemptions.cs:63-73]` |
| **Chequeo al arrancar** | Al iniciar el host | Unas líneas en `AddFeatures`: junta los namespaces `TemplateApp.Features.<F>` de los tipos del ensamblado y falla si alguno no tiene su `<F>Feature : IFeature`. Lo ve el test de 4.1 |

**La regla más barata** (DUNE0034, el número que sigue a DUNE0033 `[CÓDIGO dune/Dune.Analyzers/DuneDiagnostics.cs]`): en `backend/Features/<F>/<F>Feature.cs`, el tipo de primer nivel `<F>Feature` es una clase no estática que implementa `IFeature`; y todo tipo que implementa `IFeature` dentro de `backend/Features/` se llama `<F>Feature` y vive en ese archivo. La segunda mitad evita una feature con otro nombre, que el test de lista mostraría con un nombre raro. Fixture válida: una feature con `IFeature`. Violaciones: `static class XFeature`, `class XFeature` sin la interfaz y `class Otra : IFeature` en una slice. Los tests quedan fuera, como en las otras reglas (`DunePaths.IsTestsFile`) `[CÓDIGO DunePaths.cs:23-25]`.

**Las dos juntas.** La regla avisa antes y dice qué cambiar; el chequeo al arrancar cubre una feature sin archivo `<F>Feature.cs` (una carpeta con slices y sin registro), que la regla, mirando un archivo, no ve `[INFERENCIA]`. Ninguna pide herramienta nueva.

## Recomendación para este repo

1. `IFeature` de instancia en Platform (`AddServices`, `MapEndpoints`, `MapInternalEndpoints` por defecto vacío). Cada `<F>Feature` pasa a `internal sealed class ... : IFeature`; sus ayudantes de extensión pasan a métodos privados comunes.
2. `AddFeatures(Assembly)` recorre `GetTypes()`, ordena por nombre (ordinal), crea una instancia de cada una, guarda `HostFeatures` y registra servicios. `MapFeatures` mapea públicas e internas. Las dos van donde hoy está la lista, después de `AddBackendInfrastructure` y `AddMessaging`.
3. Chequeo al arrancar: todo namespace `TemplateApp.Features.<F>` compilado tiene su `<F>Feature`.
4. DUNE0034 con fixture válida y tres violaciones.
5. Tests permanentes: lista por host (`[Auth, Health, Projects, Users]` y `[Health, Tasks]`) y mecanismo con una feature inventada en el ensamblado de tests. Evidencia única: `Probe` real en los dos hosts, con el `.csproj` de Tasks tocado y los `Program.cs` intactos.
6. ADR nuevo (descubrimiento por reflexión, orden por nombre, por qué no generador ni `static abstract`) y actualizar `conventions.md:23` ("`Program.cs` lista las features de forma explícita") y `architecture.md:15` ("lista explícita de features").
7. Sin herramienta nueva: no hace falta preguntarle a Tobi. Si el paso eligiera un source generator (opción C), sí.
