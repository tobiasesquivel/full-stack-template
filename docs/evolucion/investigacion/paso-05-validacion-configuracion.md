# Paso 5: validación de configuración al arranque (investigación)

- Fecha: 2026-10-18.
- Alcance: el paso 5 del [plan](../plan.md). La configuración pasa a ser un contrato: si un valor es inválido, la API no arranca y el log dice qué clave falla.
- Pregunta: cómo se hace esto de forma profesional en ASP.NET Core 10, y si la rama `fase2/05-config-validation` (commit `8627bb8`, PR #46) lo hace así.
- Método: solo fuentes primarias. Son Microsoft Learn y el código de `dotnet/runtime` en [`591ede8`](https://github.com/dotnet/runtime/tree/591ede817344fa749dd1af56d7e73b356da3e71a) y de `dotnet/aspnetcore` en [`b6da38d`](https://github.com/dotnet/aspnetcore/tree/b6da38d94a8e132a8be53ee90afa8c04483f84c1). Los dos son la punta de `release/10.0` el 2026-10-18. También los issues de esos repos, el README de OpenTelemetry .NET 1.19.1 y el código de Npgsql 10.0.3, la versión que resuelve el repo.
- Evidencia propia: SDK 10.0.401. `dotnet test --filter ConfigurationValidationTests` dio 7 de 7 en verde. Además corrí a mano `TemplateApp.dll` (el de `backend/Tests/bin/Debug/net10.0/`) desde una carpeta temporal y con variables de entorno. Cada vez que este informe dice "experimento", habla de esas corridas. No se tocó código del repo.

## Conclusión

- **La rama sigue la práctica profesional en lo esencial.** Usa tipos de options, el validador generado con `[OptionsValidator]`, `ValidateOnStart`, options dependientes que se configuran de forma diferida y un test que corre la API como proceso. Cada pieza tiene respaldo en la documentación o en el código del framework.
- **La única pieza poco común es `IStartupValidator.Validate()` en `Program.cs:55`.** Funciona y es correcta según el código del host. Pero ninguna guía la recomienda para el código de la app: la interfaz dice "Used by hosts", y .NET 11 la marca `[Obsolete]` a favor de `IAsyncStartupValidator`. La alternativa estándar es mover la migración de la base a un hosted service. Así corre **dentro** de `Host.StartAsync`, después de la validación y antes de que Kestrel escuche.
- **El `ErrorMessage` ignorado en `[Range]` es un bug conocido**: [dotnet/runtime#123137](https://github.com/dotnet/runtime/issues/123137), abierto y con milestone `Future`. Hay un PR abierto desde el 2026-10-16: [#135927](https://github.com/dotnet/runtime/pull/135927). La rama lo esquiva bien: no pone `ErrorMessage` y el test espera el texto del generador.
- **El generador sí soporta `IValidatableObject`, con una diferencia que importa.** Llama a `Validate` **siempre**. `ValidateDataAnnotations` (reflexión) no la llama si un atributo ya falló. `DatabaseOptions.Validate` ya contempla ese caso.
- **Cambios recomendados (ninguno urgente):** registrar con `AddOptionsWithValidateOnStart<TOptions, TValidator>()` y `BindConfiguration`; decidir si la migración pasa a un hosted service antes de .NET 11; filtrar `HostAbortedException` en el `catch` de `Program.cs`; y citar el issue en la nota de `progreso.md`. El detalle va en "Contraste con la rama".

## 1. Options pattern: `Bind`, `ValidateDataAnnotations`, `[OptionsValidator]` o `IValidateOptions` a mano

**Respuesta:** el registro profesional en .NET 10 es `AddOptions<T>()` + `Bind`/`BindConfiguration` + un validador + `ValidateOnStart`. Si las reglas entran en atributos y en `IValidatableObject`, conviene el validador generado. A mano solo se escribe lo que necesita servicios o lógica entre opciones.

### Cómo se cargan los valores

- `Bind(section)` recibe la sección ya resuelta. `BindConfiguration("Seccion")` registra un `Configure<IConfiguration>` y lee la sección del `IConfiguration` del contenedor, recién cuando se crean las options ([OptionsBuilderConfigurationExtensions.cs:64-87](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options.ConfigurationExtensions/src/OptionsBuilderConfigurationExtensions.cs#L64-L87)). Con `BindConfiguration`, la feature no necesita recibir `IConfiguration`.
- Las dos usan el binder por reflexión: llevan `[RequiresDynamicCode]` y `[RequiresUnreferencedCode]` ([mismo archivo, líneas 26-28 y 62-63](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options.ConfigurationExtensions/src/OptionsBuilderConfigurationExtensions.cs#L26-L28)). Para trimming o AOT existe el generador del binder: `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` ([Learn: configuration-generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-generator)). El repo no publica con AOT, así que hoy no hace falta.
- Una sección que falta no es un error para el binder: el objeto queda con sus valores por defecto. Por eso las reglas tienen que rechazar el valor por defecto. Ejemplo: `[Range(1, 365)]` rechaza el `0` de un `int` sin configurar (experimento C, abajo).
- Un valor que no convierte (por ejemplo `abc` en un `int`) no llega al validador. El binder lanza `InvalidOperationException` al crear las options. En el experimento B salió: `Failed to convert configuration value 'abc' at 'Session:AbsoluteExpirationDays' to type 'System.Int32'`. El mensaje del binder por reflexión **incluye el valor**. El generador del binder no lo incluye ([ejemplo de código generado en Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-generator#explore-the-source-generated-code)).
- `AddOptionsWithValidateOnStart<TOptions, TValidateOptions>()` hace dos cosas en una línea. Registra el validador con `TryAddEnumerable`, así que una segunda llamada no lo duplica. Y llama a `ValidateOnStart` ([OptionsServiceCollectionExtensions.cs:64-74](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/src/OptionsServiceCollectionExtensions.cs#L64-L74)). La doc de Learn la muestra como forma de activar la validación al arranque ([Learn: options](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#options-validation)).

### Las tres formas de validar

| Forma | Cómo funciona | Reflexión / AOT | Mensajes | Fuente |
|---|---|---|---|---|
| `ValidateDataAnnotations()` | Registra `DataAnnotationValidateOptions<T>`, que llama a `Validator.TryValidateObject` en ejecución | Sí, usa reflexión. Lleva `[RequiresUnreferencedCode]` | Respeta `ErrorMessage`. Corta antes de `IValidatableObject` si un atributo falló | [OptionsBuilderDataAnnotationsExtensions.cs:20-22](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options.DataAnnotations/src/OptionsBuilderDataAnnotationsExtensions.cs#L20-L22), [DataAnnotationValidateOptions.cs:85](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options.DataAnnotations/src/DataAnnotationValidateOptions.cs#L85) |
| `[OptionsValidator]` sobre una `partial class : IValidateOptions<T>` | El generador escribe el `Validate` en compilación, a partir de los atributos | Sin reflexión y compatible con AOT. Reemplaza `[Range]`, `[MinLength]`, `[MaxLength]` y `[Length]` por copias propias | `[Range]` ignora `ErrorMessage` (sección 3). Nombra el campo como `Tipo.Propiedad` | [Learn: options-validation-generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator) |
| `IValidateOptions<T>` a mano | Una clase con `Validate(name, options)` que devuelve `ValidateOptionsResult` | Lo que escribas | Totalmente libres | [Learn: IValidateOptions for complex validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ivalidateoptions-for-complex-validation) |

Detalles que importan:

- Con el generador, `ValidateDataAnnotations()` **no** hace falta. Alcanza con registrar el validador como `IValidateOptions<T>` ([Learn: options-validation-generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator#automatic-ivalidateoptions-implementation-generation)). El generador viene activado con `Microsoft.Extensions.Options` 8+ y en toda app ASP.NET ([misma página](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)). No suma paquetes.
- La doc del generador registra con `AddSingleton`. La doc del options pattern registra los validadores con `TryAddEnumerable` ([Learn: options](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ivalidateoptions-for-complex-validation)). Con `AddSingleton`, registrar dos veces corre el validador dos veces y duplica los mensajes.
- `[ValidateObjectMembers]` y `[ValidateEnumeratedItems]` extienden la validación a objetos anidados y a colecciones. Funcionan con el generador ([Learn: options](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#recursive-validation-with-validateobjectmembers-and-validateenumerateditems)). El repo no los necesita: sus options son planas.
- Microsoft explica el reemplazo de atributos así: "`RangeAttribute` relies on reflection for validation". La copia generada "doesn't depend on reflection, making the code AOT-compatible" ([Learn: Replaced data annotation attributes](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator#replaced-data-annotation-attributes)).

## 2. `ValidateOnStart`: cuándo corre de verdad

**Respuesta:** `ValidateOnStart` no valida nada al registrarse. Solo anota qué options hay que construir. La validación corre dentro de `Host.StartAsync`, o sea, dentro de `app.RunAsync()`. Todo lo que corre entre `builder.Build()` y `app.RunAsync()` lo hace **antes** de esa validación.

### El mecanismo

1. `ValidateOnStart<T>()` registra `IStartupValidator` → `StartupValidator` y guarda una acción que llama a `IOptionsMonitor<T>.Get(name)` ([OptionsBuilderExtensions.cs:24-39](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/src/OptionsBuilderExtensions.cs#L24-L39)). Desde .NET 8 vive en `Microsoft.Extensions.Options` y no en Hosting ([dotnet/runtime#84347](https://github.com/dotnet/runtime/issues/84347)).
2. `StartupValidator.Validate()` ejecuta cada acción. Junta **solo** las `OptionsValidationException`: con una, la relanza; con varias, lanza `AggregateException` ([ValidateOnStart.cs:20-52](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/src/ValidateOnStart.cs#L20-L52)). Otra excepción, como la del binder, sale directo y corta la vuelta.
3. `Host.StartAsync` sigue este orden. Primero `IHostLifetime.WaitForStartAsync`. Después resuelve los `IHostedService` y llama a `IStartupValidator.Validate()`. Después vienen `StartingAsync`, `StartAsync` y `StartedAsync` de los hosted services ([Host.cs:63-71 y 96-99](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs#L63-L99)).
4. En una `WebApplication`, Kestrel arranca desde `GenericWebHostService`. `WebApplicationBuilder` lo agrega **después** de los hosted services del usuario ([WebApplicationBuilder.cs:207](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DefaultBuilder/src/WebApplicationBuilder.cs#L207), [línea 382](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DefaultBuilder/src/WebApplicationBuilder.cs#L382)).
5. Sin `ValidateOnStart`, la validación corre al crear la options por primera vez, por ejemplo en `IOptions<T>.Value` ([Learn: ASP.NET Core options, ValidateOnStart](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0)).

### Qué significa para este repo

`Program.cs:62` llama a `await app.InitializeDatabaseAsync()` antes de `app.RunAsync()`. Esa llamada abre una conexión, toma un advisory lock, migra y siembra roles (`DatabaseInitializer.cs:20-38`). Sin nada más, una `Session` o un `Otel` inválidos dejarían migrar la base y fallarían recién en `RunAsync`. `DatabaseOptions` sí fallaría antes, porque el `DbContext` lee `IOptions<DatabaseOptions>.Value` (`BackendInfrastructure.cs:22-23`). El rojo de la rama lo confirma: sin la línea 55 fallaron los 3 casos de `Session` y `Otel` (`progreso.md:140`).

### ¿Llamar a `IStartupValidator.Validate()` a mano es práctica documentada?

**No.** Funciona, pero es una API pensada para hosts:

- El XML doc de la interfaz dice "Used by hosts to validate options during startup" ([IStartupValidator.cs:6-18](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/src/IStartupValidator.cs#L6-L18)). La propuesta que la creó la describe como algo que "a host" obtiene con `GetService<IStartupValidator>()` ([dotnet/runtime#84347](https://github.com/dotnet/runtime/issues/84347)).
- Ni la doc de options de .NET ni la de ASP.NET Core muestran esa llamada en `Program.cs`. Las dos dicen "call `ValidateOnStart`" ([Learn: options](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#options-validation), [Learn: ASP.NET Core options](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0)).
- **.NET 11 la deja obsoleta.** La propuesta aprobada pone `[Obsolete("Implement IAsyncStartupValidator instead.")]` sobre `IStartupValidator`. Sus "Risks" dicen que los "direct consumers of `IStartupValidator` receive an obsoletion warning". También dicen que es "a path that cannot execute asynchronous validators" ([dotnet/runtime#131906](https://github.com/dotnet/runtime/issues/131906), milestone 11.0.0). En ASP.NET Core 11 la validación asíncrona corre solo en `StartAsync` ([Learn: Asynchronous options validation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-11.0)).
- Correr la validación dos veces no cuesta nada. `Host.StartAsync` vuelve a llamar a `Validate()`. Las options ya están en el caché de `IOptionsMonitor` y no se recalculan ([OptionsBuilderExtensions.cs:35](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/src/OptionsBuilderExtensions.cs#L35)).

### Alternativas

| Opción | Cómo queda | A favor | En contra |
|---|---|---|---|
| A. Lo de hoy: `Validate()` explícito antes de migrar | `Program.cs:55` | Un renglón. Probado por el test | API de host; aviso `[Obsolete]` en .NET 11; no ve validadores asíncronos |
| B. Migración en un hosted service | Un `IHostedService` (o `IHostedLifecycleService.StartingAsync`) llama a `DatabaseInitializer.InitializeAsync(services)`. `Program.cs` pierde las líneas 55 y 62 | El orden lo garantiza el host: validación → hosted services → Kestrel ([Host.cs:96-99](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs#L96-L99), [WebApplicationBuilder.cs:207](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DefaultBuilder/src/WebApplicationBuilder.cs#L207)). Sirve igual en .NET 11 | Más piezas. La excepción sale de `RunAsync`, no de una línea propia. `StartingAsync` corre antes que cualquier `StartAsync` ([Learn: generic host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host#ihostapplicationlifetime)) |
| C. Migrar fuera de la app (bundle o script SQL) | Un job de despliegue corre `efbundle` | Es lo que EF recomienda para producción: "Prefer a migration bundle for automation" ([Learn: Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#apply-migrations-at-runtime)) | Cambia el despliegue y el compose. Es otro paso, no este |

La opción B mantiene `DatabaseInitializer.InitializeAsync(IServiceProvider)`, que también usa `ConcurrentStartupTests.cs:73`. Con `WebApplicationFactory`, la migración seguiría corriendo al arrancar la fábrica: `DeferredHost.StartAsync` espera a `ApplicationStarted` ([DeferredHostBuilder.cs:144-155](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/Mvc/Mvc.Testing/src/DeferredHostBuilder.cs#L144-L155)). [INFERENCIA por el código; sin experimento]

## 3. ¿El generador respeta `ErrorMessage` en `[Range]`?

**Respuesta:** no, en .NET 10. Es el bug [dotnet/runtime#123137](https://github.com/dotnet/runtime/issues/123137) ("[OptionsValidator] Source Generator for [Range] Ignores ErrorMessage and DisplayName"). Está abierto, con las etiquetas `area-Extensions-Options` y `source-generator`, y milestone `Future`.

- El generador copia `ErrorMessage` en la instancia del atributo. Pero `__SourceGen__RangeAttribute.FormatErrorMessage` usa `GetValidationErrorMessage()`, que devuelve siempre "The field {0} must be between {1} and {2}." ([Emitter.cs:484-485](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/gen/Emitter.cs#L484-L485), [líneas 539-547](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/gen/Emitter.cs#L539-L547)).
- Las copias de `MaxLength`, `MinLength`, `Length` y `Compare` sí usan `ErrorMessageString` ([Emitter.cs:209](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/gen/Emitter.cs#L209)). Solo `Range` tiene el bug ([comentario del 2026-10-16 en #123137](https://github.com/dotnet/runtime/issues/123137)).
- Duplicado cerrado: [#126153](https://github.com/dotnet/runtime/issues/126153) (.NET 10.0.5). PR abierto con el arreglo: [#135927](https://github.com/dotnet/runtime/pull/135927), "Honor ErrorMessage in OptionsValidator generated Range attribute".
- Lo irónico: la propia página de Learn usa `[Range(0, 1_000, ErrorMessage = "Value for {0} must be between {1} and {2}.")]` como ejemplo del generador ([Learn: options-validation-generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)).
- Mensaje real en el experimento A: `AbsoluteExpirationDays: The field SessionOptions.AbsoluteExpirationDays must be between 1 and 365.` El prefijo `AbsoluteExpirationDays:` sale de `ValidateOptionsResultBuilder.AddResult`, que antepone los `MemberNames` ([ValidateOptionsResultBuilder.cs:42-50](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/src/ValidateOptionsResultBuilder.cs#L42-L50)).

Salidas posibles mientras el bug siga abierto:

1. Aceptar el texto del generador (lo de hoy). Nombra tipo y propiedad, no la clave `Session:...`, pero se entiende.
2. Mover la regla de rango a `IValidatableObject` con un mensaje propio, como ya hace `DatabaseOptions`. Ganás la clave en el mensaje y perdés el atributo declarativo.
3. Usar `ValidateDataAnnotations()`. Respeta `ErrorMessage`, pero vuelve la reflexión.

## 4. Options dependientes, configuradas de forma diferida

**Respuesta:** el patrón documentado es `AddOptions<TDependiente>(name).Configure<TDep>((o, dep) => ...)`. La lectura ocurre cuando se crean las options dependientes, no al registrar. Así la options base pasa por su validación antes de usarse.

- Learn: `OptionsBuilder<TOptions>` tiene sobrecargas de `Configure` con hasta cinco servicios. Microsoft recomienda el delegado antes que una clase `IConfigureOptions` "since creating a service is more complex" ([Learn: Use DI services to configure options](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0#use-di-services-to-configure-options)).
- La doc de ASP.NET Core avisa, para el modelo `Startup`: "Don't use `IOptions<TOptions>` or `IOptionsMonitor<TOptions>` in `Startup.ConfigureServices`. An inconsistent options state may exist due to the ordering of service registrations" ([Learn: ASP.NET Core options](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0#access-options-in-the-request-processing-pipeline)). Es la misma idea: no leer configuración mientras se registran servicios.
- **Cookie:** `AddCookie(scheme)` configura la instancia **con nombre** `scheme` de `CookieAuthenticationOptions`. Por eso `AddOptions<CookieAuthenticationOptions>(SessionHttpPolicy.Scheme).Configure<IOptions<SessionOptions>>(...)` apunta a la instancia correcta. Las options con nombre se explican en [Learn: Named options](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#named-options-support-using-iconfigurenamedoptions).
- **OpenTelemetry 1.19:** `OtlpExporterOptions` es compartida entre señales. Para configurar cada una, el README indica usar el parámetro `name`: `Configure<OtlpExporterOptions>("tracing", ...)` y `AddOtlpExporter("tracing", configure: null)`. Para usar servicios del contenedor, `AddOptions<OtlpExporterOptions>().Configure<MiServicio>(...)` ([README OTLP 1.19.1, Configuration](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md#configuration)).
- Con `HttpProtobuf`, el `Endpoint` que se pone por código tiene que ser la URL completa, con `/v1/{signal}` ([mismo README, OtlpExporterOptions](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md#otlpexporteroptions)). Lo que se pone por código le gana a `OTEL_EXPORTER_OTLP_ENDPOINT` ("The `OtlpExporterOptions` type setters take precedence over the environment variables", [mismo README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md#configuration)).
- **Efecto observado:** en el experimento E puse `Session` **y** `Otel` inválidos a la vez. El log mostró solo el error de `Otel`, sin `AggregateException`. El stack muestra el camino. Resolver `IStartupValidator` en `Program.cs:55` terminó construyendo el `MeterProviderSdk` de OpenTelemetry. Este creó el exportador OTLP de métricas, y su `Configure` leyó `IOptions<TelemetryOptions>.Value`, que lanzó la excepción **fuera** del bucle que junta errores en `StartupValidator`. Qué registro arrastra el `MeterProvider` a ese grafo quedó **sin verificar**. La consecuencia práctica es chica: el arranque falla igual y con un mensaje claro, pero no lista todos los errores juntos.

## 5. Cómo probar que el arranque falla

**Respuesta:** con el `catch` actual de `Program.cs`, que loguea y devuelve 1, la única prueba que ve el fallo es correr la API como proceso. Esa prueba mira el código de salida y el log. `WebApplicationFactory` no ve la excepción.

### Por qué `WebApplicationFactory` no la ve

- `WebApplicationFactory` corre el `Main` de la app en otro hilo con `HostFactoryResolver`. Si el `Main` lanza, la excepción pasa al test (`_hostTcs.TrySetException`). Si el `Main` **termina normal**, solo intenta marcar "The entry point exited without ever building an IHost" ([HostFactoryResolver.cs:236-266](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.HostFactoryResolver/src/HostFactoryResolver.cs#L236-L266)).
- Acá el host ya se construyó (`Build()` en la línea 54), así que esa marca no cambia nada. Después, `DeferredHostBuilder.EntryPointCompleted(null)` completa el `TaskCompletionSource` **sin error** ([DeferredHostBuilder.cs:100-110](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/Mvc/Mvc.Testing/src/DeferredHostBuilder.cs#L100-L110)). El test no recibe la `OptionsValidationException`. Coincide con la decisión de `decisions.tsv:117`.
- Para probar en proceso habría que relanzar en el `catch` (`throw;`) o validar los tipos fuera del host, por ejemplo con `new SessionOptionsValidator().Validate(null, options)`. Lo segundo prueba las reglas, no que estén conectadas al arranque.

### `HostAbortedException` y el `catch` amplio

- Las herramientas como `dotnet ef` usan `HostFactoryResolver` con `stopApplication = true`. Cuando el host se construye, lanzan `HostAbortedException` desde `Build()` para cortar el `Main` ([HostFactoryResolver.cs:338-346](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.HostFactoryResolver/src/HostFactoryResolver.cs#L338-L346)). La excepción se hizo pública en .NET 7 para que las apps puedan filtrarla ([dotnet/runtime#60600](https://github.com/dotnet/runtime/issues/60600)).
- Un `catch (Exception)` alrededor de `Build()` la atrapa y loguea un Fatal falso. El equipo de EF dice: "This exception is expected in some situations ... it should be caught and handled" ([dotnet/efcore#29809](https://github.com/dotnet/efcore/issues/29809#issuecomment-1344101370)). El patrón que circula en [dotnet/efcore#29923](https://github.com/dotnet/efcore/issues/29923) es `catch (Exception ex) when (ex is not HostAbortedException)`. Es un comentario de la comunidad en el repo oficial, no doc de Learn.
- El repo no tiene `IDesignTimeDbContextFactory` (búsqueda en `backend/`: cero resultados). Entonces `dotnet ef migrations add` ejecuta `Program.cs`, y el `catch` de la línea 80 escribiría "Application terminated unexpectedly during startup" con salida 1. [INFERENCIA por el código; sin experimento]
- `WebApplicationFactory` usa `stopApplication: false` y no lanza esa excepción. Los tests de hoy no se ven afectados.

### El test de la rama

`ConfigurationValidationTests` hace lo profesional para este `catch`:

- Arranca `TemplateApp.dll` como proceso, en una carpeta vacía (sin `appsettings.json`) y solo con variables de entorno.
- Cubre 6 casos inválidos y 1 válido, como control.
- Comprueba cuatro cosas: código 1, `OptionsValidationException`, el mensaje exacto y que **no** aparezca `NpgsqlException`. Eso último prueba que falló antes de tocar la base.
- El caso válido comprueba lo inverso: llega a la base y falla por `NpgsqlException`.

## 6. Cadena de conexión e `IValidatableObject` en el generador

**Respuesta:** validar la forma de la cadena con `NpgsqlConnectionStringBuilder` es razonable y barato. El generador soporta `IValidatableObject` y llama a `Validate` siempre.

### `NpgsqlConnectionStringBuilder`

- Una clave desconocida lanza `ArgumentException("Keyword not supported: ...")` ([NpgsqlConnectionStringBuilder.cs:92-97](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/NpgsqlConnectionStringBuilder.cs#L92-L97)). En el test, una URL `postgres://...` también da `ArgumentException`.
- Npgsql exige `Host`, pero recién al construir el data source: `PostProcessAndValidate` hace `ArgumentException.ThrowIfNullOrWhiteSpace(Host)` ([NpgsqlConnectionStringBuilder.cs:1500-1502](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/NpgsqlConnectionStringBuilder.cs#L1500-L1502), llamado en [NpgsqlSlimDataSourceBuilder.cs:822](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/NpgsqlSlimDataSourceBuilder.cs#L822)). `DatabaseOptions` adelanta ese mismo chequeo al arranque.
- Conectar de verdad no es tarea de la validación de options: es síncrona y corre en cada creación. Para chequeos con I/O, ASP.NET Core 11 trae validación asíncrona solo al arranque ([Learn: Asynchronous options validation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-11.0)). En .NET 10, eso lo cubren el readiness (`architecture.md:110`) y la propia migración.
- Por seguridad, el mensaje no debe repetir la cadena, porque lleva la contraseña. `DatabaseOptions` descarta el texto de la `ArgumentException` y usa uno propio.

### `IValidatableObject` con el generador

- El parser detecta si el tipo implementa `IValidatableObject` ([Parser.cs:734-745](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/gen/Parser.cs#L734-L745)). El emisor agrega la llamada a `Validate(context)` al final del método, **sin condición** ([Emitter.cs:656-664 y 733](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/gen/Emitter.cs#L656-L664)).
- `Validator.TryValidateObject`, que usa `ValidateDataAnnotations`, hace lo contrario: "We only proceed to Step 2 if there are no errors". Solo llega a `IValidatableObject` si los atributos pasaron ([Validator.cs:410-430](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs#L410-L430)).
- Consecuencia: con el generador, `Validate` tiene que tolerar valores que ya fallaron un atributo. Si no, salen dos mensajes para un mismo error, o una excepción.
- La doc de ASP.NET Core documenta `IValidatableObject` junto con `ValidateDataAnnotations` ([Learn: Class-level validation with IValidatableObject](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0#class-level-validation-with-ivalidatableobject)). Que el generador también lo soporta se ve en su código, no en esa página.

## Trampas

1. **Código entre `Build()` y `RunAsync()`** corre con configuración sin validar (sección 2).
2. **`StartupValidator` solo junta `OptionsValidationException`.** Un error del binder (experimento B) o una options leída mientras se arma otro servicio (experimento E) sale solo y oculta los demás errores.
3. **Sección ausente = valores por defecto.** La regla tiene que rechazar el `0` o el `""`. `[Required]` sobre `string` rechaza `""` porque `AllowEmptyStrings` es `false` por defecto ([Learn: RequiredAttribute.AllowEmptyStrings](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.requiredattribute.allowemptystrings)).
4. **El binder por reflexión escribe el valor crudo en el error de conversión.** Hoy no expone secretos: la cadena de conexión es `string` y no se convierte. Puede pasar si un día se liga un secreto a una propiedad tipada.
5. **Doble registro con `AddSingleton<IValidateOptions<T>, ...>`** duplica mensajes. `TryAddEnumerable` lo evita (sección 1).
6. **.NET 11** deja obsoleto `IStartupValidator` (sección 2).

## Contraste con la rama

Estados: **coincide** (sigue la práctica documentada), **coincide con matiz** o **difiere**.

| # | Punto | Estado | Qué hace la rama | Cambio recomendado |
|---|---|---|---|---|
| 1 | Options + validador | Coincide con matiz | Tres tipos de options (`SessionOptions`, `DatabaseOptions`, `TelemetryOptions`). Cada uno con `[OptionsValidator]`, `Bind(configuration.GetSection(...))`, `ValidateOnStart()` y `AddSingleton<IValidateOptions<T>, V>()` (`AuthFeature.cs:60-63`, `BackendInfrastructure.cs:18-21`, `ObservabilityExtensions.cs:19-23`) | Opcional, de forma: `services.AddOptionsWithValidateOnStart<SessionOptions, SessionOptionsValidator>().BindConfiguration(SessionOptions.Section);`. Hace lo mismo en una línea, registra con `TryAddEnumerable` y deja a `AddAuthFeature` sin `IConfiguration` (`AuthFeature.cs:15`, `:18`, `:58`; `Program.cs:48`). Igual en `BackendInfrastructure.cs:18-21`. En `ObservabilityExtensions.cs:19-22`, el override de `OTEL_EXPORTER_OTLP_ENDPOINT` pasaría a `.Configure<IConfiguration>(...)` |
| 2 | Momento de la validación | Difiere de la forma documentada (funciona) | `Program.cs:55` llama a `IStartupValidator.Validate()` antes de migrar (`Program.cs:62`) | Decisión de diseño para Tobi. **Opción B** (recomendada): mover `InitializeDatabaseAsync` a un hosted service registrado en `BackendInfrastructure` y borrar `Program.cs:55` y `:62`. El orden queda garantizado por el host y no depende de una API de host que .NET 11 deja obsoleta. **Opción A**: dejarlo y sumar a `progreso.md:113` que en .NET 11 la línea da aviso `[Obsolete]` y no corre validadores asíncronos. En los dos casos, `ConfigurationValidationTests` sigue valiendo sin cambios: mira el proceso, no la línea |
| 3 | `ErrorMessage` en `[Range]` | Coincide | No pone `ErrorMessage` (`SessionOptions.cs:9-13`). El test espera el texto del generador (`ConfigurationValidationTests.cs:14-15`). `progreso.md:112` explica el porqué | Cambiar en `progreso.md:112` "Se revisa al subir de SDK" por un enlace a dotnet/runtime#123137 y al PR #135927. Si el PR entra, no cambia nada mientras no se ponga `ErrorMessage` |
| 4 | Options dependientes diferidas | Coincide | Cookie: `Configure<IOptions<SessionOptions>>` sobre la instancia con nombre (`AuthFeature.cs:67-68`). OTLP: dos `OtlpExporterOptions` con nombre, configurados desde `IOptions<TelemetryOptions>`, con URL completa por señal (`ObservabilityExtensions.cs:24-29`, `:44`, `:49-51`). `DbContext` lee `IOptions<DatabaseOptions>` en la fábrica (`BackendInfrastructure.cs:22-23`) | Ninguno. Para enseñar: con `Otel` inválido, el error sale fuera de la agregación y oculta otros (experimento E). El bootstrap de Serilog (`Program.cs:13-14`) y `DataProtection:KeysDirectory` (`BackendInfrastructure.cs:35`) siguen leyendo configuración suelta. Ya están como pendientes en `progreso.md:110-111` |
| 5 | Test de arranque | Coincide | Proceso real, código de salida, mensaje exacto, ausencia de `NpgsqlException` y un control válido (`ConfigurationValidationTests.cs:13-39`). La razón (el `catch` devuelve 1) está en `decisions.tsv:117` | Fuera del alcance estricto del paso: `Program.cs:80` → `catch (Exception ex) when (ex is not HostAbortedException)`, para que `dotnet ef` no deje un Fatal falso ni salida 1. Anotarlo como pendiente si no entra en este paso (regla 7 de AGENTS.md) |
| 6 | Cadena de conexión e `IValidatableObject` | Coincide | `[Required]` con mensaje que nombra la clave. `Validate` parsea con `NpgsqlConnectionStringBuilder`, exige `Host` y no repite la cadena. Sale temprano si está vacía (`DatabaseOptions.cs:10-31`). `TelemetryOptions.Validate` exige URL absoluta http/https (`TelemetryOptions.cs:14-22`) | Ninguno. El `return []` de `DatabaseOptions.cs:15-16` es **necesario** con el generador, porque llama a `Validate` aunque `[Required]` haya fallado. Conviene un comentario o mencionarlo al enseñar, porque con `ValidateDataAnnotations` sobraría |
| — | Regla 4 de AGENTS.md (regla ejecutable en Dune) | Hueco | `decisions.tsv:116` fija una regla ("cada bloque de configuración es un tipo de options con validador generado y `ValidateOnStart`"). Ningún analyzer la exige. `progreso.md:113` ya lo admite para la línea 55 | Candidata a regla de Dune en un paso propio. Por ejemplo: prohibir el indexador de `IConfiguration`, `GetValue`, `GetConnectionString` y `Environment.GetEnvironmentVariable` fuera de los registros de options, con excepciones para el bootstrap. O exigir `ValidateOnStart` en cada `AddOptions<T>()`. Necesita su fixture válida y su violación deliberada |

## Choques con los docs del repo

Se listan sin resolver. La regla 10 de AGENTS.md pide preguntarle a Tobi.

1. **`informe-dotnet-empresarial.md:139`** recomienda `Microsoft.Extensions.Options.DataAnnotations` para validar al arranque. La rama usa el validador generado, que no necesita ese paquete y evita la reflexión. El informe no menciona el generador. Lo de la rama está mejor respaldado ([Learn: options-validation-generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)), pero el informe quedó desactualizado.
2. **`decisions.tsv:116`** justifica la línea 55 con "ValidateOnStart solo corre en RunAsync y la base se migra antes". El hecho es correcto. Pero la decisión elige una API que .NET 11 deja obsoleta ([dotnet/runtime#131906](https://github.com/dotnet/runtime/issues/131906)). Si se adopta la opción B, esa fila se reemplaza.

## Fuentes

Microsoft Learn:

- https://learn.microsoft.com/en-us/dotnet/core/extensions/options
- https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator
- https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-generator
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-11.0
- https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
- https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying
- https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.requiredattribute.allowemptystrings

Código de `dotnet/runtime` (commit `591ede817344fa749dd1af56d7e73b356da3e71a`, `release/10.0`):

- `src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs`
- `src/libraries/Microsoft.Extensions.Options/src/OptionsBuilderExtensions.cs`
- `src/libraries/Microsoft.Extensions.Options/src/ValidateOnStart.cs`
- `src/libraries/Microsoft.Extensions.Options/src/IStartupValidator.cs`
- `src/libraries/Microsoft.Extensions.Options/src/OptionsServiceCollectionExtensions.cs`
- `src/libraries/Microsoft.Extensions.Options/src/ValidateOptionsResultBuilder.cs`
- `src/libraries/Microsoft.Extensions.Options/gen/Emitter.cs`, `gen/Parser.cs`
- `src/libraries/Microsoft.Extensions.Options.ConfigurationExtensions/src/OptionsBuilderConfigurationExtensions.cs`
- `src/libraries/Microsoft.Extensions.Options.DataAnnotations/src/OptionsBuilderDataAnnotationsExtensions.cs`, `DataAnnotationValidateOptions.cs`
- `src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs`
- `src/libraries/Microsoft.Extensions.HostFactoryResolver/src/HostFactoryResolver.cs`

Código de `dotnet/aspnetcore` (commit `b6da38d94a8e132a8be53ee90afa8c04483f84c1`, `release/10.0`):

- `src/DefaultBuilder/src/WebApplicationBuilder.cs`
- `src/Mvc/Mvc.Testing/src/DeferredHostBuilder.cs`

Issues y PRs:

- https://github.com/dotnet/runtime/issues/123137 (bug de `[Range]`, abierto)
- https://github.com/dotnet/runtime/issues/126153 (duplicado, cerrado)
- https://github.com/dotnet/runtime/pull/135927 (arreglo propuesto, abierto)
- https://github.com/dotnet/runtime/issues/84347 (nace `IStartupValidator`, .NET 8)
- https://github.com/dotnet/runtime/issues/131906 (`IStartupValidator` obsoleto en .NET 11)
- https://github.com/dotnet/runtime/issues/128100 y https://github.com/dotnet/runtime/issues/130719 (validación asíncrona de options, .NET 11)
- https://github.com/dotnet/runtime/issues/60600 (`HostAbortedException` pública)
- https://github.com/dotnet/efcore/issues/29809, https://github.com/dotnet/efcore/issues/29923 (`HostAbortedException` con `dotnet ef`)

Terceros (fuente primaria de cada librería):

- https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md
- https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/NpgsqlConnectionStringBuilder.cs
- https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/NpgsqlSlimDataSourceBuilder.cs

Experimentos (corridas de `TemplateApp.dll` con `ASPNETCORE_ENVIRONMENT=Testing`, una base inalcanzable y carpeta vacía):

- A: `Session__AbsoluteExpirationDays=0`, `Session__IdleTimeoutMinutes=2000` → una `OptionsValidationException` con los dos mensajes, salida 1.
- B: `Session__AbsoluteExpirationDays=abc` → `InvalidOperationException` del binder, con el valor en el mensaje, salida 1.
- C: sin sección `Session` → los dos `[Range]` fallan por el `0` por defecto, salida 1.
- E: `Session__AbsoluteExpirationDays=0` y `Otel__Endpoint=foo` → solo el error de `Otel`, lanzado desde la construcción del `MeterProviderSdk` al resolver `IStartupValidator` en `Program.cs:55`, salida 1.
