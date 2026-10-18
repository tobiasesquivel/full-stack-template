# Paso 5.3: la configuración se lee solo con options validadas (investigación)

- Fecha: 2026-10-18.
- Alcance: hacer ejecutable en Dune la decisión de los pasos 5 y 5.1 (`docs/decisions.tsv:116` y `:119`): "cada bloque de configuración es un tipo de options con validador generado y `ValidateOnStart`". Dicho al revés: el código del backend no lee configuración suelta.
- Pregunta: cómo se sacan las cuatro lecturas sueltas que quedan y qué forma toma la regla de Dune.
- Método: solo fuentes primarias. Son Microsoft Learn y el código de `dotnet/runtime` en [`591ede8`](https://github.com/dotnet/runtime/tree/591ede817344fa749dd1af56d7e73b356da3e71a) y de `dotnet/aspnetcore` en [`b6da38d`](https://github.com/dotnet/aspnetcore/tree/b6da38d94a8e132a8be53ee90afa8c04483f84c1), los mismos commits de `release/10.0` que usó el paso 5. También `open-telemetry/opentelemetry-dotnet` en el tag [`core-1.19.1`](https://github.com/open-telemetry/opentelemetry-dotnet/tree/core-1.19.1), las versiones de Serilog que resuelve `TemplateApp.csproj` y los BannedApiAnalyzers en `dotnet/roslyn` [`3645b6a`](https://github.com/dotnet/roslyn/tree/3645b6aa36ad41775a73e57a620787a3d38327a0). Los analyzers se mudaron de `dotnet/roslyn-analyzers` a `dotnet/roslyn/src/RoslynAnalyzers`, y el link de ayuda del propio RS0030 apunta ahí.
- Evidencia propia: SDK 10.0.401. Dos experimentos en una carpeta temporal, fuera del repo. **Experimento 1**: un consola con `Microsoft.Extensions.*` 10.0.0 que liga una options con `BindConfiguration("Otel")` y después `BindConfiguration(string.Empty)`. **Experimento 2**: una librería con `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0, la versión de `dune/Dune.props:16`, y un `BannedSymbols.txt` de prueba. No se tocó código del repo.

## Conclusión

- **Hay cuatro lecturas sueltas en `backend/` fuera de los tests.** `Program.cs:11-12` (dos `Environment.GetEnvironmentVariable`), `BackendInfrastructure.cs:35` (indexador de `IConfiguration`), `ObservabilityExtensions.cs:20-21` (`Configure<IConfiguration>` con indexador). Además `HealthFeature.cs:7-9` recibe un `IConfiguration` que no usa, y `Program.cs:44` y `:49` pasan `builder.Configuration`.
- **Las cuatro se pueden sacar sin excepción en la regla.** No hace falta exceptuar ningún archivo:
  - Data Protection: un tipo de options `DataProtectionKeysOptions` y un `Configure` diferido sobre `KeyManagementOptions`. Es lo mismo que hace `PersistKeysToFileSystem` por dentro (sección 1).
  - Telemetría: `[ConfigurationKeyName("OTEL_EXPORTER_OTLP_ENDPOINT")]` y un segundo `BindConfiguration(string.Empty)` reemplazan el indexador. El experimento 1 confirma la prioridad (sección 2).
  - Serilog: logger de dos etapas. El bootstrap escribe solo a consola, como el README de Serilog. El logger final sale de los servicios: `TelemetryOptions` e `IHostEnvironment` (sección 2).
- **La regla conviene como analyzer propio de Dune (DUNE0013), no como `BannedSymbols.txt`.** El experimento 2 muestra que RS0030 no ve `ConfigurationManager["x"]` ni `ConfigurationManager.GetSection(...)`, que es justo el tipo de `builder.Configuration`. Además, exceptuar `Program.cs` con `.editorconfig` apaga **todas** las prohibiciones ahí, también las de antiforgery (sección 3).
- **No hay decisión de herramienta ni de licencia.** No entra ningún paquete. Sí hay tres decisiones de diseño para Tobi (ver "Decisiones para Tobi").

## 1. Data Protection: la carpeta de claves como options

**Respuesta:** `PersistKeysToFileSystem` solo registra un `IConfigureOptions<KeyManagementOptions>` que pone `XmlRepository = new FileSystemXmlRepository(directory, loggerFactory)`. Se puede escribir lo mismo con `AddOptions<KeyManagementOptions>().Configure<IOptions<DataProtectionKeysOptions>, ILoggerFactory>(...)`. Así la carpeta llega validada y la lectura queda diferida.

### Qué hace `PersistKeysToFileSystem` por dentro

- Registra un singleton `IConfigureOptions<KeyManagementOptions>`. Ese singleton pide `ILoggerFactory` al contenedor y pone `options.XmlRepository = new FileSystemXmlRepository(directory, loggerFactory)` ([DataProtectionBuilderExtensions.cs:168-183](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DataProtection/DataProtection/src/DataProtectionBuilderExtensions.cs#L168-L183)).
- `KeyManagementOptions.XmlRepository` es una propiedad pública con setter ([KeyManagementOptions.cs:152-154](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DataProtection/DataProtection/src/KeyManagement/KeyManagementOptions.cs#L152-L154)). `FileSystemXmlRepository` también es pública y tiene un constructor `(DirectoryInfo, ILoggerFactory)` ([FileSystemXmlRepository.cs:20 y 29](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DataProtection/DataProtection/src/Repositories/FileSystemXmlRepository.cs#L20-L40)).
- El repositorio crea la carpeta si no existe: `Directory.Create(); // won't throw if the directory already exists` ([mismo archivo, línea 77](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DataProtection/DataProtection/src/Repositories/FileSystemXmlRepository.cs#L77)).
- Si `XmlRepository` queda `null`, `XmlKeyManager` usa la carpeta por defecto (`GetFallbackKeyRepositoryEncryptorPair`) ([XmlKeyManager.cs:98-102](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DataProtection/DataProtection/src/KeyManagement/XmlKeyManager.cs#L98-L102)). Entonces "sin valor, carpeta del perfil" (`decisions.tsv:98`) se mantiene si el `Configure` no toca nada cuando la clave está vacía.
- El `KeyManagementOptionsSetup` del framework no toca `XmlRepository` ([KeyManagementOptionsSetup.cs:40-75](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DataProtection/DataProtection/src/Internal/KeyManagementOptionsSetup.cs#L40-L75)). El orden de registro entre los dos no importa.

### ¿Está documentado configurar `KeyManagementOptions` desde DI?

**Sí.** Hay dos piezas documentadas, y juntas dan esta forma:

- La doc de Data Protection muestra `services.Configure<KeyManagementOptions>(options => options.XmlRepository = new MyCustomXmlRepository())` para cambiar el repositorio de toda la app ([Learn: Key management extensibility, IXmlRepository](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/extensibility/key-management#ixmlrepository)).
- La doc de options documenta `OptionsBuilder<T>.Configure<TDep1, TDep2>` con servicios del contenedor, y recomienda el delegado antes que una clase `IConfigureOptions` ([Learn: Use DI services to configure options](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0#use-di-services-to-configure-options)).

`Configure<IOptions<DataProtectionKeysOptions>, ILoggerFactory>` es la combinación de las dos. No encontré un ejemplo literal con `FileSystemXmlRepository` en Learn. [INFERENCIA por el código y las dos docs]

### Cuándo se lee

- `AddDataProtection` registra `DataProtectionHostedService` como `IHostedService` ([DataProtectionServiceCollectionExtensions.cs:77](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DataProtection/DataProtection/src/DataProtectionServiceCollectionExtensions.cs#L77)). `XmlKeyManager` lee `keyManagementOptions.Value` en su constructor ([XmlKeyManager.cs:98-99](https://github.com/dotnet/aspnetcore/blob/b6da38d94a8e132a8be53ee90afa8c04483f84c1/src/DataProtection/DataProtection/src/KeyManagement/XmlKeyManager.cs#L98-L99)).
- El host valida las options antes de arrancar los hosted services (paso 5, sección 2: [Host.cs:96-99](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs#L96-L99)). Entonces, con `ValidateOnStart`, una carpeta inválida frena el arranque antes de crear claves. Qué otro servicio podría resolver `IOptions<KeyManagementOptions>` antes de `StartAsync` quedó **sin verificar**.

### Regla de validación razonable

- **Ruta absoluta** (`Path.IsPathFullyQualified`). Learn define: una ruta completa "does not depend on the current drive or current directory". Además avisa que "rooted" no es lo mismo que absoluta: `C:a` y `/Documents` en Windows son "rooted" pero relativas ([Learn: Path.IsPathFullyQualified](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.ispathfullyqualified?view=net-10.0)). Por eso conviene `IsPathFullyQualified` y no `IsPathRooted`.
- Por qué importa acá: con una ruta relativa, las claves caen en una carpeta que depende de dónde arrancó el proceso. Otro directorio de trabajo da otras claves, y las sesiones dan 401 (el mismo síntoma del paso 0.27). `progreso.md:113` ya lo anotaba.
- Valor vacío: válido, porque significa "carpeta por defecto". `appsettings.example.json:25` trae `"KeysDirectory": ""`.
- No conviene exigir que la carpeta exista. El repositorio la crea (línea 77, arriba), y el `Dockerfile:11` la crea para dar el dueño. Una regla de existencia rompería el desarrollo local con una carpeta nueva.
- El `Dockerfile:10` usa `/var/lib/templateapp/data-protection-keys` y `DataProtectionKeysTests.cs:12-13` usa `Path.GetTempPath()`. Las dos son absolutas: ninguna se rompe.

### Cómo queda (boceto, no código final)

- `Infrastructure/DependencyInjection/DataProtectionKeysOptions.cs`: `Section = "DataProtection"`, `string? KeysDirectory`, `IValidatableObject` con "DataProtection:KeysDirectory must be an absolute path." si no está vacía y no es absoluta. Más su validador `[OptionsValidator]`, como `TelemetryOptions`.
- `BackendInfrastructure.AddDataProtectionKeys(services)` sin `IConfiguration`: `AddOptionsWithValidateOnStart<DataProtectionKeysOptions, DataProtectionKeysOptionsValidator>().BindConfiguration(DataProtectionKeysOptions.Section)` y `AddOptions<KeyManagementOptions>().Configure<IOptions<DataProtectionKeysOptions>, ILoggerFactory>(...)`, que asigna `XmlRepository` solo si hay valor.
- `ConfigurationValidationTests` suma un caso: `DataProtection__KeysDirectory=relative/keys` → código 1 y el mensaje.

## 2. `OTEL_EXPORTER_OTLP_ENDPOINT` y el logger de Serilog

**Respuesta:** el exportador OTLP de OpenTelemetry ya lee `OTEL_EXPORTER_OTLP_ENDPOINT` del `IConfiguration` del contenedor. Pero el repo pone `Endpoint` por código en cada señal, y lo que se pone por código gana. La forma más limpia de conservar `TelemetryOptions` validada y respetar la variable estándar es ligarla con `[ConfigurationKeyName]` y un segundo `BindConfiguration(string.Empty)`. El bootstrap de Serilog puede dejar de leer variables: escribe a consola, y el logger final se arma desde los servicios.

### Qué hace el SDK de OpenTelemetry 1.19.1

- El XML doc dice: "OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_EXPORTER_OTLP_HEADERS, OTEL_EXPORTER_OTLP_TIMEOUT, and OTEL_EXPORTER_OTLP_PROTOCOL environment variables are parsed during object construction" ([OtlpExporterOptions.cs:20-24](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExporterOptions.cs#L20-L24)).
- Con DI, no lee el entorno directo: lee el `IConfiguration` del contenedor. `AddOtlpExporter` registra una fábrica de options (`RegisterOptionsFactory(OtlpExporterOptions.CreateOtlpExporterOptions)`) ([OtlpServiceCollectionExtensions.cs:54](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/Implementation/OtlpServiceCollectionExtensions.cs#L54)). Esa fábrica recibe `IConfiguration` y aplica las claves `Default` ([OtlpExporterOptions.cs:333-339](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExporterOptions.cs#L333-L339), [líneas 409-424](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExporterOptions.cs#L409-L424)). Con un host, ese `IConfiguration` es el del host. Solo sin host el SDK registra uno propio con `AddEnvironmentVariables()` ([ProviderBuilderServiceCollectionExtensions.cs:74-80](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry/Internal/Builder/ProviderBuilderServiceCollectionExtensions.cs#L74-L80)).
- Con la variable genérica, el SDK marca `AppendSignalPathToEndpoint = true` ([OtlpExporterOptions.cs:350-354](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExporterOptions.cs#L341-L354)). Con HTTP, el cliente agrega `/v1/traces` o `/v1/metrics` ([OtlpExportClient.cs:30-44](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/Implementation/ExportClient/OtlpExportClient.cs#L30-L44)).
- **El setter de `Endpoint` pone `AppendSignalPathToEndpoint = false`** ([OtlpExporterOptions.cs:151-158](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExporterOptions.cs#L136-L158)). Por eso el repo arma la URL completa con `SignalEndpoint("traces")` (`TelemetryOptions.cs:12`, `ObservabilityExtensions.cs:22-27`). El README lo dice así: "The `OtlpExporterOptions` type setters take precedence over the environment variables" ([README OTLP 1.19.1](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md#configuration)).
- Consecuencia: hoy la variable la lee **el repo** (`ObservabilityExtensions.cs:20-21`), no el SDK. El SDK también la lee, pero el setter de la línea 56 la pisa siempre.
- Dato útil para enseñar: el propio SDK usa el patrón que la regla va a prohibir. `AddOptions<MetricReaderOptions>(name).Configure<IConfiguration>((readerOptions, config) => config[...])` ([OtlpServiceCollectionExtensions.cs:20-23](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.19.1/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/Implementation/OtlpServiceCollectionExtensions.cs#L20-L23)). En una librería tiene sentido: no conoce las options de la app. En la app, la regla lo reemplaza por un tipo validado.

### Tres formas de respetar la variable sin leer `IConfiguration` a mano

| Opción | Cómo queda | A favor | En contra |
|---|---|---|---|
| A. Dejar que el SDK la lea y no poner `Endpoint` | `ConfigureExporter` solo fija `Protocol` | Cero código propio para la variable | `Otel:Endpoint` deja de servir: el SDK no lo conoce. La variable no pasa por `TelemetryOptionsValidator`. El sink de Serilog sigue necesitando la URL. No sirve |
| B. Dos claves, una options (**recomendada**) | `TelemetryOptions` suma `[ConfigurationKeyName("OTEL_EXPORTER_OTLP_ENDPOINT")] public string? StandardEndpoint`. El registro queda `.BindConfiguration("Otel").BindConfiguration(string.Empty)`, y un `EffectiveEndpoint => StandardEndpoint ?? Endpoint` alimenta `SignalEndpoint` y la validación | Mantiene el contrato del `README.md:19`: `Otel:Endpoint`, con prioridad para `OTEL_EXPORTER_OTLP_ENDPOINT`. Sin indexador. Valida las dos | Ligar la raíz también busca una clave raíz `Endpoint` para la propiedad `Endpoint`. Hoy no existe, pero si alguien la define, pisaría `Otel:Endpoint` [INFERENCIA por el binder] |
| C. Una sola clave | `TelemetryOptions` liga solo la raíz con `[ConfigurationKeyName("OTEL_EXPORTER_OTLP_ENDPOINT")] Endpoint`. `Otel:Endpoint` desaparece | Un nombre para una cosa, y es el estándar de OpenTelemetry. Sin regla de prioridad | Cambia el contrato de configuración: `README.md:19` y `:21`, `appsettings.example.json:21-23` y `ConfigurationValidationTests.cs:19` y `:34` |

Respaldo de la opción B:

- `BindConfiguration` con ruta vacía liga **la raíz**: `string.Equals("", configSectionPath, ...) ? config : config.GetSection(configSectionPath)` ([OptionsBuilderConfigurationExtensions.cs:73-78](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options.ConfigurationExtensions/src/OptionsBuilderConfigurationExtensions.cs#L64-L87)).
- El binder busca cada propiedad por su nombre o por `[ConfigurationKeyName]` ([ConfigurationBinder.cs:1165-1180](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Configuration.Binder/src/ConfigurationBinder.cs#L1165-L1180), usado en la [línea 304](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Configuration.Binder/src/ConfigurationBinder.cs#L296-L306)). Learn: "Specifies the key name for a configuration property" ([Learn: ConfigurationKeyNameAttribute](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration.configurationkeynameattribute)).
- Los `Configure` corren en orden de registro: `foreach (IConfigureOptions<TOptions> setup in _setups)` ([OptionsFactory.cs:55-58](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options/src/OptionsFactory.cs#L55-L58)).
- **Experimento 1** (tres corridas):
  - Sin claves: `Endpoint=http://localhost:4318`, `Standard=-`.
  - Con `Otel:Endpoint=http://json:4318`: queda `json`.
  - Con las dos claves: `Endpoint=http://json:4318`, `Standard=http://env:4318`, y el efectivo es `env`.

  Es la misma prioridad que hoy (`ObservabilityExtensions.cs:20-21`).

### Serilog: el bootstrap y el logger final

**Respuesta:** sí, el bootstrap podría seguir leyendo variables, porque corre antes de que exista el host y no tiene otra fuente. Pero no le hace falta. El README de Serilog arma el bootstrap solo con consola, y el logger final con los servicios.

- El README de `Serilog.AspNetCore` 10.0.0 explica el problema: al iniciar Serilog primero, "services from the ASP.NET Core host, including the `appsettings.json` configuration and dependency injection, aren't available yet". La solución que da es "two-stage initialization": `CreateBootstrapLogger()` con `WriteTo.Console()`, y después `builder.Services.AddSerilog((services, lc) => ...)` ([README v10.0.0, Two-stage initialization](https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/README.md#two-stage-initialization)). También avisa: "the final logger **completely replaces** the bootstrap logger".
- Dentro del callback, `services` es el `IServiceProvider` de la app. Ahí se pueden pedir `IOptions<TelemetryOptions>` (el endpoint ya validado) e `IHostEnvironment` (para `deployment.environment.name`, que hoy sale de `ASPNETCORE_ENVIRONMENT` en `Program.cs:12`).
- **Cuándo corre el callback:** `AddSerilog(Action<IServiceProvider, LoggerConfiguration>)` lo ejecuta al resolver el `RegisteredLogger`, hace `Reload` del bootstrap y lo congela con `Freeze()` ([SerilogServiceCollectionExtensions.cs:129-175](https://github.com/serilog/serilog-extensions-hosting/blob/v10.0.0/src/Serilog.Extensions.Hosting/SerilogServiceCollectionExtensions.cs#L129-L175)). El `ILoggerFactory` depende de ese logger. `Host` pide `ILogger<Host>` en su constructor ([Host.cs:34-40](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs#L34-L40)), y `Build()` resuelve `IHost` ([HostBuilder.cs:366-377](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Hosting/src/HostBuilder.cs#L366-L377)). Entonces el callback corre **dentro de `builder.Build()`**, antes de `ValidateOnStart`. [INFERENCIA por el código; sin experimento]
- Consecuencia: si el callback lee `IOptions<TelemetryOptions>.Value` con un endpoint inválido, la `OptionsValidationException` sale de `Build()`, fuera de la agregación de `StartupValidator`. Es el mismo efecto del experimento E del paso 5. El arranque falla igual, con código 1. El `catch` de `Program.cs:75-78` la escribe con el bootstrap, porque el `Freeze()` no llegó a correr. `ConfigurationValidationTests` mira la consola, así que debería seguir verde. [INFERENCIA; correr el test lo confirma]
- **El sink OTLP de Serilog también lee la variable por su cuenta.** `WriteTo.OpenTelemetry(configure)` usa `ignoreEnvironment = false` por defecto y pasa `Environment.GetEnvironmentVariable` ([OpenTelemetryLoggerConfigurationExtensions.cs:52-60](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/OpenTelemetryLoggerConfigurationExtensions.cs#L52-L60)). Después de tu `configure`, pisa `Endpoint` con `OTEL_EXPORTER_OTLP_ENDPOINT` ([OpenTelemetryEnvironment.cs:20 y 34-35](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/Sinks/OpenTelemetry/Configuration/OpenTelemetryEnvironment.cs#L19-L35); [README v4.2.0](https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/README.md#environment-variable-overrides)). Con la opción B el valor coincide, pero conviene pasar `ignoreEnvironment: true` en el logger final para que la única fuente sea `TelemetryOptions`, ya validada.
- Qué se pierde: si el arranque falla **antes** de `Build()`, por ejemplo con un error de registro, el `Log.Fatal` va solo a consola y no al collector. Si falla después (validación, migración), el logger final ya está activo [INFERENCIA]. En Compose la consola queda en `docker compose logs api`. Hoy el bootstrap también intenta OTLP, y con un collector caído tampoco llega.

### Cómo queda (boceto)

- `Program.cs:11-36`: bootstrap con `MinimumLevel`, `Enrich.FromLogContext()`, `WriteTo.Console()` y `.CreateBootstrapLogger()`. Sin variables de entorno.
- `Program.cs:42`: `builder.Host.UseSerilog()` pasa a un `services.AddSerilog((sp, lc) => ...)` dentro de `AddTemplateObservability`, con el sink OTLP, `ActivityTraceEnricher` y el entorno de `IHostEnvironment`. Program.cs no sabe de OTLP.
- `Program.cs:44`: `AddBackendInfrastructure()` sin `builder.Configuration`. `Program.cs:49`: `AddHealthFeature()` sin parámetro, y `HealthFeature.cs:7-9` pierde `_ = configuration;`.

## 3. La regla de Dune

**Respuesta:** un analyzer propio, **DUNE0013**, que marque en `backend/**` (salvo tests y migraciones) cualquier uso de un tipo de la familia `IConfiguration` y las lecturas de variables de entorno. Sin excepción por archivo si se adoptan las secciones 1 y 2. `OptionsBuilder<T>.BindConfiguration(string)` queda permitido solo, porque no nombra `IConfiguration`.

### Opción (a): `BannedSymbols.txt` (RS0030)

- Formato: un ID de comentario de documentación por línea, con `T:`, `M:`, `P:`, `N:` ([BannedApiAnalyzers.Help.md](https://github.com/dotnet/roslyn/blob/3645b6aa36ad41775a73e57a620787a3d38327a0/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md)).
- **Agujero 1, el que decide:** el analyzer compara el símbolo y los que **sobrescribe** (`IsOverride` → `GetOverriddenMember()`), no las implementaciones de interfaz ([SymbolIsBannedAnalyzerBase.cs:433-447](https://github.com/dotnet/roslyn/blob/3645b6aa36ad41775a73e57a620787a3d38327a0/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/Core/SymbolIsBannedAnalyzerBase.cs#L433-L447)). `ConfigurationManager`, el tipo de `builder.Configuration`, declara su propio indexador ([ConfigurationManager.cs:25 y 53](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Configuration/src/ConfigurationManager.cs#L53)). **Experimento 2**, con BannedApiAnalyzers 5.6.0:
  - `M:...IConfiguration.GetSection(System.String)` marca `c.GetSection("x")` sobre `IConfiguration`, pero **no** `m.GetSection("x")` sobre `ConfigurationManager`.
  - `T:Microsoft.Extensions.Configuration.IConfiguration` marca los miembros usados a través de la interfaz, pero **no** `m["b"]` ni `m.GetSection("x")` sobre `ConfigurationManager`.
  - `P:...IConfiguration.Item(System.String)` no marcó ni siquiera `c["a"]` sobre la interfaz. No investigué por qué.
  - `M:System.Environment.GetEnvironmentVariable(System.String)` sí funciona.
- **Agujero 2:** las extensiones (`ConfigurationBinder.GetValue<T>`, `Get<T>`, `Bind`, `ConfigurationExtensions.GetConnectionString`, `GetRequiredSection`) piden una línea por sobrecarga ([ConfigurationBinder.cs:155 y 170](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Configuration.Binder/src/ConfigurationBinder.cs#L155-L170), [ConfigurationExtensions.cs:36 y 98](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Configuration.Abstractions/src/ConfigurationExtensions.cs#L36)). Una sobrecarga nueva en .NET 11 pasa sin aviso.
- **Agujero 3, la excepción:** el analyzer solo sabe excluir código generado (`dotnet_banned_api_analyzer.exclude_generated_code`, [SymbolIsBannedAnalyzerBase.cs:24 y 257](https://github.com/dotnet/roslyn/blob/3645b6aa36ad41775a73e57a620787a3d38327a0/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/Core/SymbolIsBannedAnalyzerBase.cs#L257)). Para exceptuar `Program.cs` queda una sección `[backend/Program.cs]` en `.editorconfig` con `dotnet_diagnostic.RS0030.severity = none`. Las secciones de EditorConfig aplican "to specific source files or folders" ([Learn: Configuration files for code analysis rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files#editorconfig)). Pero todas las prohibiciones comparten el id RS0030: se apagan también las de `Task.Wait`, `DateTime.Now` y `UseAntiforgery()`. `Program.cs:64` es justo donde se registra el antiforgery. Un `#pragma warning disable RS0030` alrededor de las dos líneas acota mejor, pero sigue apagando todo en esas líneas.
- Bonus: la prohibición de tipo `T:` no marcó la declaración del parámetro `IConfiguration c` en el experimento, solo los usos de sus miembros. `AddBackendInfrastructure(IConfiguration)` pasaría.

### Opción (b): analyzer propio DUNE0013 (recomendada)

Sigue el molde de `InfrastructureBoundaryAnalyzer.cs` y `VersionedApiAnalyzer.cs`: filtro por ruta con `DunePaths` y decisión por símbolo.

**Dónde mira:** `DunePaths.IsBackendFile(path) && !DunePaths.IsMigration(path) && !DunePaths.IsTestsFile(path)`, igual que `VersionedApiAnalyzer.cs:36-40`. Los tests quedan fuera porque leen variables con razón: `PostgresFixture.cs:17`, `ConcurrentStartupTests.cs:25`, `ConfigurationValidationTests.cs:45`.

**Qué marca:** "familia de configuración" es cualquier tipo que sea o implemente `Microsoft.Extensions.Configuration.IConfiguration`. Eso cubre `IConfiguration`, `IConfigurationSection`, `IConfigurationRoot`, `IConfigurationManager`, `ConfigurationManager` y `ConfigurationSection`. Se compara por `AllInterfaces`, no por nombre, y así se cierra el agujero 1.

1. **Nombrar un tipo de la familia** en código propio: parámetro, campo, variable, argumento de tipo (`Configure<IConfiguration>`) o `typeof`. Marca `BackendInfrastructure.cs:13` y `:31`, `HealthFeature.cs:7` y `ObservabilityExtensions.cs:20`.
2. **Una expresión cuyo tipo es de la familia**, como `builder.Configuration` (`Program.cs:44` y `:49`). Es la misma idea de DUNE0004, que "mira el tipo nombrado y también el tipo de cada expresión, para que var no la esquive" (`DuneDiagnostics.cs:41`).
3. **Un miembro de un tipo de la familia**: indexador, `GetSection`, `GetChildren`, `Value`. Marca `BackendInfrastructure.cs:35`.
4. **Una extensión cuyo primer parámetro es de la familia**: `ConfigurationBinder.*`, `ConfigurationExtensions.*` y cualquier extensión nueva. Se mira `method.ReducedFrom ?? method` y su `Parameters[0].Type`. No `DatabaseFacade.GetConnectionString()` de EF (`DatabaseInitializer.cs:18`), porque su receptor no es de la familia.
5. **`System.Environment.GetEnvironmentVariable`** (las dos sobrecargas), **`GetEnvironmentVariables`** (las dos) y **`ExpandEnvironmentVariables`**. Marca `Program.cs:11-12`.

**Qué no marca:** `OptionsBuilder<T>.BindConfiguration(string)`, porque vive en `Microsoft.Extensions.DependencyInjection` y su receptor es `OptionsBuilder<T>` ([OptionsBuilderConfigurationExtensions.cs:9](https://github.com/dotnet/runtime/blob/591ede817344fa749dd1af56d7e73b356da3e71a/src/libraries/Microsoft.Extensions.Options.ConfigurationExtensions/src/OptionsBuilderConfigurationExtensions.cs#L9)). Tampoco `AddOptionsWithValidateOnStart`, `IOptions<T>` ni `IHostEnvironment`. La regla no exige `ValidateOnStart` en cada `AddOptions<T>()`: eso sería otra regla. Además daría falsos positivos con las options de terceros configuradas de forma diferida (`OtlpExporterOptions`, `CookieAuthenticationOptions`, `KeyManagementOptions`).

**Excepción:** ninguna, si entran las secciones 1 y 2. Si Tobi prefiere conservar el bootstrap actual, la excepción mínima es solo la regla 5 en `backend/Program.cs`, por ruta y dentro del analyzer. No apaga nada más.

**Mensaje sugerido:** `"'{0}' lee configuración suelta; declarala en un tipo de options con BindConfiguration y validador"`.

**Id libre:** **DUNE0013**. `DuneDiagnostics.cs` llega hasta DUNE0012 (`VersionedApi`, líneas 104-113).

**Cableado a tocar:**

- `DuneDiagnostics.cs`: el descriptor nuevo.
- `dune/Dune.props:5`: sumar `DUNE0013` a `WarningsAsErrors`. Ojo: hoy esa lista **no tiene DUNE0012**, y `.editorconfig:21-29` llega solo hasta DUNE0009. Como los descriptores son `DiagnosticSeverity.Error` por defecto, igual fallan. Alinear la lista es fuera del alcance: anotarlo como pendiente (regla 7).
- `AnalyzerHost.All()` (`AnalyzerHost.cs:184-195`) tampoco incluye `VersionedApiAnalyzer`. Mismo tratamiento.
- `docs/conventions.md`: una línea con la regla y su id, como DUNE0010 a DUNE0012.

### Fixture válida y violación deliberada

Se arman como `VersionedApiAnalyzerTests.cs`: rutas falsas `backend/...` en `AnalyzerHost.AnalyzeAsync(..., AnalyzerHost.Only<ConfigurationAccessAnalyzer>())` y aserción por número de línea. No hacen falta stubs. `TestReferences.All()` suma todos los `.dll` de la salida del test (`TestReferences.cs:397-402`), y ahí ya están `Microsoft.Extensions.Configuration*.dll` y `Microsoft.Extensions.Options.ConfigurationExtensions.dll`, que llegan por EF e Identity. Lo verifiqué en `dune/Dune.Analyzers.Tests/bin/`.

- **Válida** (`Assert.Empty`):
  - `backend/Infrastructure/Observability/ObservabilityExtensions.cs`: `services.AddOptionsWithValidateOnStart<TelemetryOptions, TelemetryOptionsValidator>().BindConfiguration("Otel").BindConfiguration(string.Empty)`.
  - Un `TelemetryOptions` con `[ConfigurationKeyName("OTEL_EXPORTER_OTLP_ENDPOINT")]`.
  - Un `Configure<IOptions<X>>((o, x) => ...)`.
  - Un tipo propio con un método `GetConnectionString()`, como el de EF.
  - `backend/Tests/Support/PostgresFixture.cs` con `Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")`: ruta de tests, exenta.
- **Violación** (`Assert.Equal(new[] { ... }, Lines(...))`), en `backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs`, un caso por línea:
  - un parámetro `IConfiguration configuration`;
  - `configuration["DataProtection:KeysDirectory"]`;
  - `configuration.GetValue<int>("X")`;
  - `configuration.GetConnectionString("Default")`;
  - `configuration.GetSection("Otel")`;
  - `new ConfigurationManager()["x"]`, el caso que RS0030 no ve;
  - `.Configure<IConfiguration>((o, c) => ...)`;
  - `Environment.GetEnvironmentVariable("X")`.

  Si una línea genera más de un diagnóstico (tipo y miembro), usar `.Distinct()` en las líneas, como `AntiforgeryOptOutBanTests.cs:328`.
- **Rojo real** para la evidencia del paso: antes de mover las lecturas, el build de `TemplateApp` falla con DUNE0013 en `Program.cs:11-12`, `:44` y `:49`, `BackendInfrastructure.cs:13`, `:31` y `:35`, `ObservabilityExtensions.cs:20-21` y `HealthFeature.cs:7`. Es el equivalente a `fase2-04-rule-red/`.

## Trampas

1. **`builder.Configuration` es `ConfigurationManager`, no `IConfiguration`.** Una regla por símbolo de interfaz no lo ve (experimento 2). La regla tiene que mirar el tipo implementado.
2. **El callback de `AddSerilog` corre en `Build()`.** Si lee options, las valida fuera de la agregación de `StartupValidator` y oculta otros errores (sección 2).
3. **El sink de Serilog pisa el endpoint con la variable de entorno** salvo `ignoreEnvironment: true` (sección 2).
4. **`BindConfiguration(string.Empty)` liga la raíz con todas las propiedades del tipo**, no solo la marcada. Una clave raíz con el nombre de otra propiedad la pisaría (opción B).
5. **Ruta "rooted" no es absoluta.** `IsPathRooted("/keys")` en Windows es `true`, pero depende de la unidad actual. Usar `IsPathFullyQualified`.
6. **DUNE0012 no figura en `Dune.props:5` ni en `.editorconfig`.** Hoy no cambia nada, porque es `Error` por defecto, pero la lista miente.

## Contraste con el código de `main` (`1c5473d`)

| # | Punto | Estado | Código hoy | Cambio recomendado |
|---|---|---|---|---|
| 1 | Carpeta de claves | Difiere | `BackendInfrastructure.cs:31-37` lee `configuration["DataProtection:KeysDirectory"]` y llama a `PersistKeysToFileSystem` sin validar | `DataProtectionKeysOptions` + validador (ruta absoluta o vacía) + `Configure<IOptions<...>, ILoggerFactory>` sobre `KeyManagementOptions`. Sección 1 |
| 2 | Override de `OTEL_EXPORTER_OTLP_ENDPOINT` | Difiere | `ObservabilityExtensions.cs:20-21` usa `Configure<IConfiguration>` y el indexador | Opción B: `[ConfigurationKeyName]` + `BindConfiguration(string.Empty)`. Mensaje de `TelemetryOptions.cs:21` sin cambios. Sección 2 |
| 3 | Bootstrap de Serilog | Difiere | `Program.cs:11-12` lee dos variables. `Program.cs:21-35` arma el sink OTLP antes del host. `Program.cs:42` usa `UseSerilog()` sin callback | Bootstrap solo de consola con `CreateBootstrapLogger()`. Logger final en `AddTemplateObservability` con `AddSerilog((sp, lc) => ...)`, `ignoreEnvironment: true` y `IHostEnvironment`. Sección 2 |
| 4 | Parámetros `IConfiguration` | Difiere | `BackendInfrastructure.cs:13`, `HealthFeature.cs:7-9` (sin uso), `Program.cs:44` y `:49` | Quitarlos. `AddAuthFeature` y `AddTemplateObservability` ya no lo reciben desde el paso 5.1 (`progreso.md:146`) |
| 5 | Regla ejecutable | Hueco | Ningún analyzer. `progreso.md:115` lo anota | DUNE0013 en `dune/Dune.Analyzers/ConfigurationAccessAnalyzer.cs`, con tests en `dune/Dune.Analyzers.Tests/ConfigurationAccessAnalyzerTests.cs`. Sección 3 |
| 6 | Lecturas en tests | Coincide | `PostgresFixture.cs:17`, `ConcurrentStartupTests.cs:25` y `ConfigurationValidationTests.cs:45` leen variables | Ninguno: la regla excluye tests con `DunePaths.IsTestsFile` |
| 7 | `GetConnectionString` de EF | Coincide | `DatabaseInitializer.cs:18` usa `db.Database.GetConnectionString()` | Ninguno. La fixture válida lo cubre para que la regla no lo confunda |

## Choques con los docs del repo

Se listan sin resolver. La regla 10 de AGENTS.md pide preguntarle a Tobi.

1. **`informe-vsa-deep-modules.md:79`** fija la firma `Add<X>Feature(services, configuration)`. DUNE0013 prohíbe ese parámetro. El paso 5.1 ya lo sacó de `AddAuthFeature` (`progreso.md:146`), así que el código ya se apartó del informe. Falta decidir si se corrige el informe en este paso.
2. **`README.md:19`** documenta "`Otel:Endpoint` (o `OTEL_EXPORTER_OTLP_ENDPOINT`, que tiene prioridad)". La opción B lo respeta. La opción C lo cambia, y con él `README.md:21`, `appsettings.example.json:21-23` y `ConfigurationValidationTests.cs:19` y `:34`.
3. **`progreso.md:115`** propone la regla "fuera de los registros de options, con excepción del bootstrap de Serilog". Esta investigación recomienda algo más estricto: tampoco dentro de los registros, y sin excepción. No choca con una regla escrita, pero cambia lo anotado. Si se adopta, ese pendiente y el de `progreso.md:112-113` se cierran.
4. **`decisions.tsv:98`** dice que `BackendInfrastructure` lee `DataProtection:KeysDirectory`. Sigue siendo cierto con options. La fila nueva agrega la regla de ruta absoluta.

## Decisiones para Tobi

No hay decisión de herramienta ni de licencia: todo usa paquetes que ya están. Son decisiones de diseño:

1. **Clave del endpoint:** opción B (dos claves con prioridad, recomendada porque no cambia el contrato del README) u opción C (una sola clave estándar).
2. **Bootstrap:** solo consola, recomendado porque deja la regla sin excepciones. O conservar las dos lecturas y exceptuar `Program.cs` solo de la regla 5.
3. **Tamaño del paso (regla 1 de AGENTS.md):** mover las lecturas y la regla en un paso (5.3), o en dos: 5.3 mueve las lecturas a options y 5.4 agrega DUNE0013 con su rojo. La regla no puede entrar en verde sin el movimiento. Pero el movimiento sí es observable solo: una carpeta de claves relativa frena el arranque.

## Fuentes

Microsoft Learn:

- https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/extensibility/key-management#ixmlrepository
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0#use-di-services-to-configure-options
- https://learn.microsoft.com/en-us/dotnet/api/system.io.path.ispathfullyqualified?view=net-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration.configurationkeynameattribute
- https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files#editorconfig

Código de `dotnet/aspnetcore` (`b6da38d`, `release/10.0`), en `src/DataProtection/DataProtection/src/`:

- `DataProtectionBuilderExtensions.cs`, `DataProtectionServiceCollectionExtensions.cs`
- `KeyManagement/KeyManagementOptions.cs`, `KeyManagement/XmlKeyManager.cs`
- `Internal/KeyManagementOptionsSetup.cs`, `Repositories/FileSystemXmlRepository.cs`

Código de `dotnet/runtime` (`591ede8`, `release/10.0`), en `src/libraries/`:

- `Microsoft.Extensions.Options.ConfigurationExtensions/src/OptionsBuilderConfigurationExtensions.cs`
- `Microsoft.Extensions.Options/src/OptionsFactory.cs`
- `Microsoft.Extensions.Configuration.Binder/src/ConfigurationBinder.cs`
- `Microsoft.Extensions.Configuration/src/ConfigurationManager.cs`
- `Microsoft.Extensions.Configuration.Abstractions/src/ConfigurationExtensions.cs`
- `Microsoft.Extensions.Hosting/src/Internal/Host.cs`, `HostBuilder.cs`

Código de `dotnet/roslyn` (`3645b6a`), en `src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/`:

- `BannedApiAnalyzers.Help.md`, `Core/SymbolIsBannedAnalyzerBase.cs`

OpenTelemetry .NET (`core-1.19.1`):

- `src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExporterOptions.cs`, `README.md`
- `src/OpenTelemetry.Exporter.OpenTelemetryProtocol/Implementation/OtlpServiceCollectionExtensions.cs`
- `src/OpenTelemetry.Exporter.OpenTelemetryProtocol/Implementation/ExportClient/OtlpExportClient.cs`
- `src/OpenTelemetry/Internal/Builder/ProviderBuilderServiceCollectionExtensions.cs`

Serilog (versiones de `TemplateApp.csproj`):

- https://github.com/serilog/serilog-aspnetcore/blob/v10.0.0/README.md#two-stage-initialization
- https://github.com/serilog/serilog-extensions-hosting/blob/v10.0.0/src/Serilog.Extensions.Hosting/SerilogServiceCollectionExtensions.cs
- https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/README.md#environment-variable-overrides
- https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/OpenTelemetryLoggerConfigurationExtensions.cs
- https://github.com/serilog/serilog-sinks-opentelemetry/blob/v4.2.0/src/Serilog.Sinks.OpenTelemetry/Sinks/OpenTelemetry/Configuration/OpenTelemetryEnvironment.cs

Experimentos (carpeta temporal, fuera del repo):

- 1: `BindConfiguration("Otel").BindConfiguration(string.Empty)` con `[ConfigurationKeyName("OTEL_EXPORTER_OTLP_ENDPOINT")]`. Sin claves → valor por defecto. Solo `Otel:Endpoint` → ese. Las dos → gana la variable.
- 2: BannedApiAnalyzers 5.6.0. `M:` sobre `IConfiguration.GetSection` y `T:IConfiguration` no marcan el indexador ni `GetSection` de `ConfigurationManager`. `P:...IConfiguration.Item(System.String)` no marcó el indexador sobre la interfaz. `M:System.Environment.GetEnvironmentVariable(System.String)` sí marca.
