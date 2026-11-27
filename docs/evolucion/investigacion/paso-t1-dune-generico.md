# Paso T1: Dune sin nombres fijos de features ni de hosts (investigación)

- Fecha: 2026-11-27.
- Estado: **Listo** como investigación. Hay tres choques entre el plan y el código (sección "Choques", al principio). Ninguno es entre dos docs del repo, así que no hace falta frenar por la regla 10. No pide herramientas nuevas.
- Alcance: el paso T1 de `docs/evolucion/plan.md` (Fase T). Cómo declara el código una excepción a una regla de Dune, cómo llegan los datos de un proyecto al analyzer, cómo se aplica `Dune.props` por carpeta y cómo descubren los gates de Node los hosts.
- Base: `main` en `87aa631`. Roslyn `Microsoft.CodeAnalysis.CSharp` 5.9.0 (`dune/Dune.Analyzers/Dune.Analyzers.csproj`), SDK de .NET 10.0.401.

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `87aa631`.
- `[FUENTE]`: lo leí en un documento primario, con su link: Microsoft Learn, el repo `dotnet/roslyn`, la documentación de Node.js.
- `[MEDIDO]`: lo corrí en un proyecto aparte, fuera del repo (sección "Mediciones").
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Choques (leer primero)

1. **DUNE0029: `MediatorTransaction.None` no alcanza para marcar "este handler maneja su transacción".** El plan dice "Tasks abre su propia transacción (DUNE0029, `MediatorTransaction.None`)". Pero en el monolito `None` significa otra cosa: Wolverine guarda al final, sin transacción, y lo usan las lecturas y `OpenSession` (`docs/architecture.md`, "Transacción del monolito") `[CÓDIGO backend/Features/Users/UsersFeature.cs:40-42]`. Si DUNE0029 exime a todo handler registrado con `None`, una lectura del monolito podría abrir su propia transacción sin que nada falle. Propuesta: la excepción de Tasks se declara aparte, con el mismo mecanismo que las demás (sección "Recomendación"). Los registros siguen con `None`.
2. **DUNE0032: el plan dice "con sus excepciones movidas a la feature", pero el dueño natural de esa decisión es el host.** La "excepción" de DUNE0032 es la tabla de qué compila cada host: el monolito todo menos Tasks, y el host de Tasks solo Tasks y Health `[CÓDIGO dune/Dune.Analyzers/HostBoundaryAnalyzer.cs:20-24]`. Qué features despliega un proceso lo decide el proyecto del host: sus `Compile` ya lo dicen, y el paso T3 hace que cada host descubra las features "de su compilación" (`plan.md`, T3). Propuesta: cada host declara en su `.csproj` qué features despliega (sección 2). Así la tabla sale de Dune y un host nuevo no la edita, pero no queda "en la feature". Si Tobi lee el plan como "en la feature", la alternativa está en la sección 2.3.
3. **DUNE0019 tiene dos descriptores con el mismo id.** "Slice de lectura que escribe" y "Slice de lectura que carga una entidad" son los dos `DUNE0019` `[CÓDIGO dune/Dune.Analyzers/DuneDiagnostics.cs:278-296]`. La excepción de `Users/GetUser` vale solo para "carga una entidad" `[CÓDIGO dune/Dune.Analyzers/ReadSliceAnalyzer.cs:16-18, 154]`. Todo mecanismo que filtra por id (`SuppressMessage`, `#pragma`, `.editorconfig`) apagaría también "escribe" `[MEDIDO]`. El mecanismo tiene que nombrar el chequeo, no el id.

## Respuesta corta

1. **Excepciones (pregunta 1).** Recomiendo un atributo propio, por ejemplo `[DuneException(DuneCheck.ReadSliceLoadsEntity, "motivo")]`, que el analyzer lee antes de reportar. Es el único mecanismo de los comparados en que Dune decide qué chequeos aceptan excepción, exige un motivo y distingue los dos chequeos de DUNE0019. `SuppressMessage`, `#pragma` y `.editorconfig` filtran por id, no piden motivo y sirven para apagar cualquier regla, también una regla guardiana `[MEDIDO]`. `DiagnosticSuppressor` no sirve: no suprime diagnósticos de severidad Error, y todos los de Dune lo son `[FUENTE]`.
2. **Datos de MSBuild (pregunta 2).** `CompilerVisibleProperty` ya funciona en este repo: DUNE0032 lee `build_property.MSBuildProjectDirectory` `[CÓDIGO dune/Dune.Analyzers/HostBoundaryAnalyzer.cs:14, 40]`. Un host puede declarar en su `.csproj` una propiedad con sus features, y el analyzer la lee igual. Derivarla de los `Compile` que recibe no sirve: la regla compararía lo que se compila con lo que se compila, y nunca fallaría.
3. **`Dune.props` por carpeta (pregunta 3).** Funciona con la ruta relativa a la raíz del repo: `$([MSBuild]::MakeRelative(raíz, $(MSBuildProjectDirectory)))`, barras normalizadas a `/` y `StartsWith('backend/', StringComparison.OrdinalIgnoreCase)`. Lo medí en Windows con un proyecto `Backend/Hosts/Billing`, uno en `gateway/`, uno en `backendx/` y uno en `dune/` `[MEDIDO]`. En Linux no lo probé.
4. **Gates de Node (pregunta 4).** Los hosts salen de `fs.readdirSync('backend/Hosts', { withFileTypes: true })`, ordenados a mano, con un `.csproj` por carpeta. Los datos de cada host salen de `dotnet msbuild -getProperty -getItem`, que ya usa `check-host-items` `[CÓDIGO dune/gates/check-host-items.mjs:51]`.
5. **Industria (pregunta 5).** Las librerías de tests de arquitectura eligen tipos por convención (namespace, nombre) y dan el motivo con `.Because(...)` `[FUENTE]`. Las excepciones permanentes de Microsoft están en configuración (`excluded_symbol_names` en `.editorconfig`) `[FUENTE]`. ASP.NET Core declara las excepciones en el endpoint mismo: `AllowAnonymous()`, `DisableAntiforgery()` `[FUENTE]`. La recomendación sigue a ASP.NET: la excepción va en el código que exime.

## 1. Cómo declara el código una excepción

### 1.1 Qué hay hoy

Cada excepción vive dentro del analyzer, como una lista:

| Regla | Excepción | Dónde |
|---|---|---|
| DUNE0016 (POST sin idempotencia) | la feature `Auth` | `[CÓDIGO dune/Dune.Analyzers/IdempotentPostAnalyzer.cs:12, 40]` |
| DUNE0019 (lectura que carga una entidad) | la slice `Users/GetUser` | `[CÓDIGO dune/Dune.Analyzers/ReadSliceAnalyzer.cs:16-18, 154]` |
| DUNE0028 (endpoint sin mediator) | la slice `Auth/IssueCsrfToken` | `[CÓDIGO dune/Dune.Analyzers/MediatorEndpointAnalyzer.cs:17, 37]` |
| DUNE0028 y DUNE0031 | solo `Tasks`, `Projects`, `Users`, `Auth` | `[CÓDIGO MediatorEndpointAnalyzer.cs:15]`, `[CÓDIGO MediatorHandlerShapeAnalyzer.cs:12]` |
| DUNE0029 (la slice maneja la transacción) | solo `Auth`, `Users`, `Projects` | `[CÓDIGO dune/Dune.Analyzers/TransactionOwnerAnalyzer.cs:17]` |

Con T1, DUNE0028, 0029 y 0031 valen para toda feature. Entonces aparecen tres excepciones nuevas: Tasks en DUNE0029, y `GetLiveness` y `GetReadiness` de Health en DUNE0028 (`plan.md`, T1).

### 1.2 Los mecanismos comparados

**`SuppressMessageAttribute`.** Se pone en un miembro, en un tipo o en el ensamblado. `Scope` acepta `module`, `resource`, `type`, `member`, `namespace` y `namespaceanddescendants`; este último "suppresses warnings in a namespace and all its descendant symbols" ([FUENTE: Visual Studio, in-source suppression](https://learn.microsoft.com/en-us/visualstudio/code-quality/in-source-suppression-overview#suppressmessageattribute-attribute-format)). `Target` usa el id de documentación, por ejemplo `~M:MyApp.Program.IgnorableCharacters` ([FUENTE: Suppress code analysis warnings](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/suppress-warnings#use-the-suppressmessageattribute)). `Justification` es opcional: solo `Category` y `CheckId` son obligatorios ([FUENTE: SuppressMessageAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.codeanalysis.suppressmessageattribute#properties)).
- Filtra por id: Roslyn toma `diagnostic.Id` como clave y no mira título ni mensaje ([FUENTE: SuppressMessageAttributeState.cs](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/DiagnosticAnalyzer/SuppressMessageAttributeState.cs)). Medido: una supresión de `T0001` con `namespaceanddescendants` apagó los dos descriptores `T0001`, también los de fin de compilación `[MEDIDO]`. Con DUNE0019, apagaría "escribe" junto con "carga".
- Un diagnóstico sin ubicación (`Location.None`, como los de DUNE0032 sobre el ensamblado) solo se apaga con una supresión de módulo sin `Target` ([FUENTE: SuppressMessageAttributeState.cs](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/DiagnosticAnalyzer/SuppressMessageAttributeState.cs)) `[MEDIDO]`.
- El atributo es `[Conditional("CODE_ANALYSIS")]` ([FUENTE](https://learn.microsoft.com/en-us/visualstudio/code-quality/in-source-suppression-overview#in-source-suppression-and-the-suppressmessageattribute-attribute)), pero Roslyn lo aplica igual sin ese símbolo, y un analyzer lo ve en `Compilation.Assembly.GetAttributes()` `[MEDIDO]`.
- ¿Lo puede verificar Dune? Una regla guardiana puede leer los `SuppressMessage` y reportarlos. Pero el mismo atributo la apaga: un `[assembly: SuppressMessage("Probe", "T0004")]` sin `Target` silenció a la guardiana `[MEDIDO]`. Entonces un agente puede apagar cualquier regla de Dune y también la que vigila las supresiones.

**`#pragma warning disable`.** Apaga desde esa línea. No tiene motivo: un comentario al lado lo prohíbe DUNE0009 (`docs/conventions.md`, "Comentarios"). Apaga también diagnósticos de severidad Error `[MEDIDO]`. Filtra por id. El código que genera Wolverine ya empieza con `#pragma warning disable` sin ids `[CÓDIGO backend/Hosts/Tasks/Internal/Generated/WolverineHandlers/]`.

**`.editorconfig` o `.globalconfig` por carpeta.** Un `.editorconfig` vale para su carpeta y las de abajo, y gana el más profundo: "The entry in the EditorConfig file that's deeper in the file system ... wins" ([FUENTE: Configuration files](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files)). Un `.globalconfig` vale para todo el proyecto, "regardless of their file names or file paths" ([misma fuente](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files)). Con `dotnet_diagnostic.DUNE0016.severity = none` en `backend/Features/Auth/.editorconfig`, la excepción queda en la carpeta de la feature. Pero filtra por id, no pide motivo y Dune no la ve.

**Opciones propias del analyzer en `.editorconfig`.** Es lo que hace Microsoft para sus propias reglas: `dotnet_code_quality.CA1040.api_surface = public`, o `excluded_symbol_names` con prefijo `M:`, `T:` o `N:` ([FUENTE: Code quality rule options](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/code-quality-rule-options)). El analyzer las lee con `AnalyzerConfigOptionsProvider.GetOptions(SyntaxTree)` ([FUENTE: AnalyzerConfigOptionsProvider](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analyzerconfigoptionsprovider)). Dune decidiría qué claves acepta y podría pedir motivo. Pierde: la excepción queda en un archivo oculto, fuera del C# que lee el agente, y el `BuildPropertiesProvider` de los tests devuelve opciones vacías por archivo `[CÓDIGO dune/Dune.Analyzers.Tests/BuildPropertiesProvider.cs:17]`.

**Un atributo propio que el analyzer reconoce por nombre.** Es el patrón de `ExcludeFromCodeCoverageAttribute`, que también tiene `Justification` ([FUENTE](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.codeanalysis.excludefromcodecoverageattribute#properties)). El atributo no apaga nada por sí solo: es un dato. El analyzer lo busca antes de reportar. Entonces Dune decide todo: qué chequeos aceptan excepción, sobre qué tipo va, que el motivo no esté vacío y si la excepción sigue haciendo falta.

**`DiagnosticSuppressor`.** Solo suprime diagnósticos cuya severidad por defecto no es Error y que no tienen la etiqueta `NotConfigurable` ([FUENTE: DiagnosticSuppressorDesign.md](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/DiagnosticSuppressorDesign.md)). Todos los descriptores de Dune son `DiagnosticSeverity.Error` `[CÓDIGO dune/Dune.Analyzers/DuneDiagnostics.cs]`. No aplica.

**Un atajo que no sirve: marcar el archivo como generado.** Roslyn trata como generado un archivo por su encabezado o por su nombre: "a file name ending with `.designer.cs` or `.generated.cs` is considered generated code" ([FUENTE: Exclude generated code](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options#exclude-generated-code)). Un analyzer con `GeneratedCodeAnalysisFlags.None` no mira esos archivos. Medido: renombrar un archivo a `.g.cs`, `.generated.cs` o `.designer.cs` lo sacó de todas las acciones de símbolo de la sonda `[MEDIDO]`. Por eso DUNE0009 analiza el código generado a propósito `[CÓDIGO dune/Dune.Analyzers/NoCommentsAnalyzer.cs:15]`: el encabezado `// <auto-generated/>` es un comentario y falla. El nombre de archivo es un hueco aparte (sección "Huecos previos").

### 1.3 Comparación

| Mecanismo | Visible en la feature | Dune exige motivo | Dune elige qué chequeos aceptan | Distingue los dos DUNE0019 | Sirve para apagar cualquier regla |
|---|---|---|---|---|---|
| `SuppressMessage` | sí (un archivo con `[assembly: ...]`) | no | no | no `[MEDIDO]` | sí, también a la guardiana `[MEDIDO]` |
| `#pragma warning disable` | sí | no (sin comentarios) | no | no | sí `[MEDIDO]` |
| `.editorconfig` de carpeta | sí, como archivo oculto | no | no | no | sí |
| Opción propia en `.editorconfig` | sí, como archivo oculto | sí | sí | sí | no |
| Atributo propio | sí, en el tipo eximido | sí | sí | sí | no |
| `DiagnosticSuppressor` | no | no aplica | no aplica | no aplica | no aplica (Dune es Error) |

### 1.4 Qué recomienda Microsoft para una excepción permanente

Las guías no tienen una regla para "excepción de arquitectura permanente". Lo que dicen:
- Las supresiones en código son "to suppress false positives or less important violations that you don't intend to fix" ([FUENTE: IDE0079](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0079#overview)). IDE0079 marca las que ya no hacen falta, pero solo en el IDE, no al compilar ([misma fuente](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0079)).
- La guía vieja de Visual Studio pide no dejar supresiones en el código de release: "Don't use in-source suppressions on release builds" ([FUENTE](https://learn.microsoft.com/en-us/visualstudio/code-quality/in-source-suppression-overview#in-source-suppression-and-the-suppressmessageattribute-attribute)).
- Para excluir símbolos de una regla a propósito, sus analyzers ofrecen opciones de configuración (`excluded_symbol_names`) ([FUENTE](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/code-quality-rule-options)).

[INFERENCIA] Las supresiones de Microsoft sirven para callar falsos positivos, no para declarar diseño. Una excepción de Dune es diseño: "esta slice no usa mediator porque solo escribe la cookie". Eso pide un dato que Dune lea, no un silenciador.

## 2. Datos de cada proyecto para el analyzer

### 2.1 El mecanismo

`CompilerVisibleProperty` y `CompilerVisibleItemMetadata` hacen que MSBuild escriba un `.editorconfig` global con esos valores: "MSBuild will automatically translate specified properties and metadata into a global analyzer config" ([FUENTE: Source generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md)). El target `GenerateMSBuildEditorConfigFileCore` lo agrega a `EditorConfigFiles`, que el compilador pasa a todos los analyzers, no solo a los generadores ([FUENTE: Microsoft.Managed.Core.targets](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/MSBuildTask/Microsoft.Managed.Core.targets)). Este repo ya lo usa para DUNE0032 `[CÓDIGO dune/Dune.props:18-19]`.

- Una propiedad llega como `build_property.<Nombre>` en `GlobalOptions`.
- Los metadatos de un ítem llegan como `build_metadata.<TipoDeÍtem>.<Metadato>`, en una sección por archivo. Sirven para cualquier tipo de ítem, porque el target recorre `@(%(CompilerVisibleItemMetadata.Identity))` ([FUENTE](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/MSBuildTask/Microsoft.Managed.Core.targets)). Pero se leen con `GetOptions(SyntaxTree)` o `GetOptions(AdditionalText)`, por archivo: un ítem que no es un archivo compilado no se puede leer.
- Los valores se escriben sin escapar: `build_property.X = valor`, una línea ([FUENTE: GenerateMSBuildEditorConfig.cs](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/MSBuildTask/GenerateMSBuildEditorConfig.cs)). El cookbook avisa de "data loss for non-trivial property values" ([FUENTE](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md)). Conviene un valor simple, separado por comas.
- Un analyzer no lee archivos del disco: la lista de APIs prohibidas para analyzers tiene `T:System.IO.File; Do not do file IO in analyzers` y `T:System.Environment` ([FUENTE: AnalyzerBannedSymbols.txt](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/Core/AnalyzerBannedSymbols.txt)). Entonces DUNE0032 no puede abrir los `.csproj` de los otros hosts. Lo que no llega por la compilación o por estas opciones, no lo ve.

### 2.2 ¿Declarar en el host o derivar de los `Compile`?

DUNE0032 ya recibe los `Compile` evaluados: son los `SyntaxTrees` de la compilación `[CÓDIGO dune/Dune.Analyzers/HostBoundaryAnalyzer.cs:101-117]`. Si la regla saca de ahí qué features "puede" compilar el host, compara un dato consigo mismo y nunca falla `[INFERENCIA]`. Hace falta una intención declarada aparte.

Propuesta: cada host declara en su `.csproj` una propiedad, y `Dune.props` la expone con `CompilerVisibleProperty`.
- El host de Tasks: las features que despliega (`Tasks,Health`).
- El monolito: las que deja afuera (`Tasks`). Así una feature nueva entra al monolito sin editar su `.csproj`, como hoy y como pide el template (monolito modular).
- DUNE0032 compara las features de los `Compile` con esa declaración. `check-host-items` lee lo mismo con `-getProperty`.

Probé si el `.csproj` puede armar sus `Compile` desde la misma propiedad, para no escribir dos veces. No: MSBuild no expande comodines (`**`) que salen de una transformación de ítems ni de una función de propiedad `[MEDIDO]`. Los `Compile` siguen escritos a mano y la regla verifica que coincidan con la declaración.

### 2.3 La alternativa "en la feature" (si Tobi lee así el plan)

Cada feature declara en qué host corre, y las que no declaran nada van a un host por defecto. Pierde dos cosas. Un host por defecto vuelve a ser un nombre fijo, o una propiedad más en un `.csproj`. Y la feature pasa a conocer la topología de despliegue, que el paso T3 deja en el host [INFERENCIA]. Una variante más fuerte: "cada feature se compila en un solo host, salvo las que se declaran compartidas (Health)". No necesita listas, pero DUNE0032 no ve los otros hosts (sección 2.1), así que el chequeo pasa a `check-host-items`. Es un cambio de alcance: lo dejaría como pendiente.

## 3. `Dune.props` por carpeta

Hoy `Dune.props` se aplica a cinco nombres de proyecto `TemplateApp*` o a una ruta que contiene `/backend/Hosts/` `[CÓDIGO dune/Dune.props:2, 11]`. El `Contains` no está anclado: un repo clonado dentro de otra carpeta `backend/` también coincide [INFERENCIA].

Lo que dicen las fuentes:
- `Directory.Build.props` se importa temprano en todo proyecto de su carpeta y subcarpetas: "The scope of these customizations can be controlled at the folder level by where these files are placed" ([FUENTE: Customize by directory](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory)). Este repo lo importa desde la raíz `[CÓDIGO Directory.Build.props]`, así que también lo reciben los proyectos de `dune/`, que no deben cargar su propio analyzer.
- `MSBuildProjectDirectory` no termina en barra; `MSBuildThisFileDirectory` sí ([FUENTE: Reserved properties](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-reserved-and-well-known-properties)).
- `MakeRelative` "Makes `path` relative to `basePath`"; `NormalizeDirectory` usa el separador del sistema y agrega la barra final ([FUENTE: Property functions](https://learn.microsoft.com/en-us/visualstudio/msbuild/property-functions)).
- En una condición, `==` y `!=` ignoran mayúsculas ([FUENTE: Conditions](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-conditions)). Los métodos de `string` no: `StartsWith('backend/')` sin más distingue mayúsculas `[MEDIDO]`.
- Linux distingue mayúsculas en los nombres de archivo ([FUENTE: Customize by directory](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory)).

Lo que medí en Windows `[MEDIDO]`, con `dune/Probe.props` importado desde la raíz:

```
raíz     = $([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)', '..'))
relativa = $([MSBuild]::MakeRelative('$(raíz)', '$(MSBuildProjectDirectory)').Replace('\', '/'))/
aplica   = relativa.StartsWith('backend/', StringComparison.OrdinalIgnoreCase) o lo mismo con 'gateway/'
```

| Proyecto | `relativa` | ¿Aplica? |
|---|---|---|
| `Backend/Hosts/Billing` (otra mayúscula) | `Backend/Hosts/Billing/` | sí |
| el mismo, invocado con la ruta en minúsculas | `backend/hosts/billing/` | sí |
| `gateway/` | `gateway/` | sí |
| `backendx/Foo` | `backendx/Foo/` | no (la barra final lo separa) |
| `dune/` | `dune/` | no |

`StringComparison.OrdinalIgnoreCase` funciona escrito así y como `System.StringComparison.OrdinalIgnoreCase` `[MEDIDO]`; la documentación no muestra ese caso ([FUENTE: Property functions](https://learn.microsoft.com/en-us/visualstudio/msbuild/property-functions)). El CI corre en `ubuntu-latest` `[CÓDIGO .github/workflows/ci.yml:21]`: en Linux `[NO PROBADO]`.

## 4. Gates de Node

- **Descubrir hosts.** `fs.readdirSync(ruta, { withFileTypes: true })` devuelve objetos `fs.Dirent` ([FUENTE: fs.readdirSync](https://nodejs.org/api/fs.html#fsreaddirsyncpath-options)), con `isDirectory()` ([FUENTE](https://nodejs.org/api/fs.html#direntisdirectory)). La documentación de `dir.read()` dice que el orden lo da el sistema operativo ([FUENTE](https://nodejs.org/api/fs.html#dirread); no pude leer la frase textual). Conviene ordenar la lista para que la salida del gate sea estable. `fs.globSync` existe desde la v22.0.0 y es estable desde la v22.17.0 y la v24.0.0 ([FUENTE](https://nodejs.org/api/fs.html#fsglobsyncpattern-options)); `readdirSync` alcanza y no depende de la versión.
- **Regla por carpeta.** Cada carpeta de `backend/Hosts/` tiene exactamente un `.csproj`. Cero o dos es un error con mensaje accionable [INFERENCIA].
- **Leer el proyecto.** `-getProperty` con una sola propiedad devuelve texto; con varias, o junto con `-getItem`, devuelve JSON. Existe desde MSBuild 17.8 ([FUENTE: Evaluate items and properties](https://learn.microsoft.com/en-us/visualstudio/msbuild/evaluate-items-and-properties#use-command-line-options-to-output-values)). `check-host-items` ya pide `-getItem:Compile -getItem:ProjectReference` y parsea JSON `[CÓDIGO dune/gates/check-host-items.mjs:51-66]`: sumar `-getProperty:` de la declaración del host no cambia el formato.
- **`shell: true` en Windows.** Los gates llaman `spawnSync(..., { shell: process.platform === 'win32' })` `[CÓDIGO dune/gates/check-codegen.mjs:31]`. Node avisa: con `shell`, "do not pass unsanitized user input" ([FUENTE: child_process](https://nodejs.org/api/child_process.html#child_processspawnsynccommand-args-options)). Con hosts descubiertos, el nombre de la carpeta pasa a ser parte del comando [INFERENCIA]. Conviene validar el nombre (letras, dígitos, punto, guion) antes de usarlo.
- **`check-codegen`.** La carpeta generada sale de la del host: `<host>/Internal/Generated` `[CÓDIGO dune/gates/check-codegen.mjs:6-9]`. Si un host nuevo no tiene código generado versionado, `coverage()` ya falla con un mensaje claro `[CÓDIGO dune/gates/check-codegen.mjs:24-28]`.

## 5. Qué hace la industria

- **Reglas por convención, con motivo.** ArchUnitNET elige tipos por namespace o nombre (`Types().That().ResideInNamespace(...)`) y da el motivo con `.Because("it's forbidden")`, que "is displayed when it fails" ([FUENTE: ArchUnitNET](https://github.com/TNG/ArchUnitNET)). NetArchTest hace lo mismo con `Types.InCurrentDomain().That().ResideInNamespace(...)` ([FUENTE: NetArchTest](https://github.com/BenMorris/NetArchTest)). Ninguna lista nombres de módulos: la regla describe la forma.
- **Excepciones en un archivo central.** ArchUnit (Java) ignora violaciones con `archunit_ignore_patterns.txt`, "in the root of the classpath" ([FUENTE: ArchUnit user guide](https://www.archunit.org/userguide/html/000_Index.html#_ignoring_violations)), y congela las existentes en un `ViolationStore` ([FUENTE](https://www.archunit.org/userguide/html/000_Index.html#_freezing_arch_rules)). Es justo lo que T1 saca: una lista central que nadie ve desde el módulo.
- **Excepciones en el lugar eximido.** ASP.NET Core aplica una política global y deja que cada endpoint se excluya en su propia línea. "Apply `[AllowAnonymous]` or call `AllowAnonymous()` for endpoints that are intentionally public" ([FUENTE: Authorization policies](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies#require-global-user-authentication)). "Calling `.DisableAntiforgery()` on an endpoint opts that endpoint out" y "should only be done when the endpoint isn't vulnerable to CSRF attacks" ([FUENTE: Anti-request forgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery)). Este repo ya lo usa: `IssueCsrfTokenEndpoint` encadena `.AllowAnonymous()` `[CÓDIGO backend/Features/Auth/IssueCsrfToken/IssueCsrfTokenEndpoint.cs:7]`.
- **Microsoft, para sus propias reglas,** pone las exclusiones permanentes en configuración (`excluded_symbol_names`) ([FUENTE](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/code-quality-rule-options)).

## Mediciones

Proyecto de consola fuera del repo, con `Microsoft.CodeAnalysis.CSharp` 5.9.0 (la versión de Dune) y `CompilationWithAnalyzers.GetAnalyzerDiagnosticsAsync`, la misma API que usan los tests de Dune `[CÓDIGO dune/Dune.Analyzers.Tests/AnalyzerHost.cs:43-49]`. Un analyzer de prueba reporta: `T0001` con dos descriptores (los dos Error y de fin de compilación, como DUNE0019), `T0002` sin ubicación (como DUNE0032), `T0003` con la etiqueta `NotConfigurable`, y `T0004`, una guardiana que reporta cada `SuppressMessage` del ensamblado. Hay dos tipos: uno en `App.Features.Auth` y otro en `App.Features.Users`.

| Caso | Qué quedó |
|---|---|
| Sin supresión | todo: los dos `T0001` de cada tipo, `T0002`, los dos `T0003` |
| `SuppressMessage` `T0001`, `namespaceanddescendants`, `~N:App.Features.Auth` | se fueron los **dos** `T0001` de Auth; quedan los de Users; la guardiana lo ve |
| `SuppressMessage` `T0002` de módulo, sin `Target` | se fue `T0002` |
| `SuppressMessage` `T0002` con `namespaceanddescendants` | `T0002` sigue (no tiene ubicación) |
| `SuppressMessage` `T0003` (`NotConfigurable`) | se fue el `T0003` de Auth |
| `#pragma warning disable T0001, T0003` | se fueron los `T0001`, aunque son Error; los `T0003` siguen |
| Opciones del compilador `T0001` y `T0003` en `Suppress` (= `NoWarn`) | se fueron los `T0001`; los `T0003` siguen |
| `SuppressMessage` de `T0001` y de la guardiana `T0004`, sin `Target` | se fueron los `T0001` y la guardiana no reportó nada |
| El archivo de los tipos renombrado a `.g.cs`, `.generated.cs` o `.designer.cs` | solo queda `T0002`: las acciones de símbolo no corrieron |

El código de Roslyn coincide con lo medido. `NotConfigurable` "cannot be suppressed or filtered or have its severity changed" ([FUENTE: WellKnownDiagnosticTags.cs](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/Diagnostic/WellKnownDiagnosticTags.cs)), y el filtro del compilador lo devuelve tal cual antes de mirar `#pragma` u opciones ([FUENTE: CSharpDiagnosticFilter.cs](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Compilation/CSharpDiagnosticFilter.cs)). Pero `SuppressMessageAttributeState` solo exceptúa los diagnósticos del compilador, no los `NotConfigurable` ([FUENTE: SuppressMessageAttributeState.cs](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/DiagnosticAnalyzer/SuppressMessageAttributeState.cs)). Por eso `SuppressMessage` sí lo apagó.

Medí MSBuild con `dotnet msbuild -getProperty` sobre proyectos vacíos (tabla de la sección 3), y la expansión de comodines con `-getItem` (sección 2.2).

## Recomendación para T1

**El mecanismo de excepción.** Un atributo en `backend/Shared/` (lo compila Platform y lo ven todas las features), por ejemplo `DuneExceptionAttribute(DuneCheck check, string reason)`, y un enum `DuneCheck` con solo los chequeos que aceptan excepción. Cada uno en su archivo (DUNE0010, un tipo por archivo). El motivo es un string, no un comentario, así que DUNE0009 no lo toca.
- El enum hace que el compilador rechace un chequeo mal escrito o que no acepta excepción.
- El analyzer reconoce el atributo por su nombre completo, como hace hoy con `MediatorTransaction` `[CÓDIGO dune/Dune.Analyzers/MediatorRegistrations.cs:35]`, porque Dune no referencia el código del producto.
- El analyzer falla si el motivo está vacío, si el atributo está en un tipo que ese chequeo no mira, o si la excepción ya no exime nada (lo que IDE0079 hace solo en el IDE, acá al compilar).
- Dónde va: las excepciones de una slice, en la clase `<Slice>Endpoint`; las de una feature, en la clase `<Feature>Feature`. Los endpoints son `static class` y aceptan atributos `[CÓDIGO backend/Features/Health/GetLiveness/GetLivenessEndpoint.cs:4]`.
- El nombre (`DuneException`, o uno por intención como `ArchitectureException`) lo elige el paso. `DuneException` gana en búsqueda: un `grep` lista todas las excepciones del repo.

**Por regla:**

| Pieza | Hoy | Recomendación |
|---|---|---|
| DUNE0016 | `ExcludedFeature = "Auth"` | Sin lista. Cada endpoint de Auth con `MapPost` (`RegisterUser`, `OpenSession`, `CloseSession`, `CheckSession`) declara `[DuneException(DuneCheck.IdempotentPost, "...")]` en su clase. Es por endpoint, como `AllowAnonymous()`: un POST nuevo de Auth que crea un recurso vuelve a fallar. Si Tobi prefiere una sola línea por feature, va en `AuthFeature`. |
| DUNE0019 | `EntityLoadExceptions = { "Users/GetUser" }` | `GetUserEndpoint` declara `DuneCheck.ReadSliceLoadsEntity`. Solo apaga "carga una entidad"; "escribe" sigue (choque 3). |
| DUNE0028 | 4 features y `IssueCsrfToken` | Vale para toda feature. `IssueCsrfTokenEndpoint`, `GetLivenessEndpoint` y `GetReadinessEndpoint` declaran `DuneCheck.MediatorEndpoint`. |
| DUNE0029 | `OwnedFeatures = { Auth, Users, Projects }` | Vale para toda feature. `TasksFeature` declara `DuneCheck.TransactionOwner` con el motivo "corre en su propio proceso, sin la integración de EF de Wolverine". No usa `MediatorTransaction.None` como marca (choque 1). Alternativa: un valor nuevo del enum (`MediatorTransaction.Handler`); pierde porque cambia `MediatorTransactionPolicy` en runtime para algo que solo lee Dune. |
| DUNE0031 | 4 features | Vale para toda feature, sin excepciones: Health no tiene handlers anidados [INFERENCIA, falta correrlo]. |
| DUNE0032 | tabla `Hosts` con nombres de carpeta y de ensamblado | Un host es todo proyecto en `backend/Hosts/<X>/`. Qué features compila lo declara su `.csproj` (sección 2.2): el de Tasks `Tasks,Health`, el monolito deja afuera `Tasks`. `Dune.props` lo expone con `CompilerVisibleProperty`. Se borran los chequeos por nombre de ensamblado (`isHostAssembly`) `[CÓDIGO dune/Dune.Analyzers/HostBoundaryAnalyzer.cs:39]`. |
| DUNE0009, código generado | dos rutas, una por host | Un solo patrón: `backend/Hosts/<cualquiera>/Internal/Generated/WolverineHandlers/`, comparando segmentos de la ruta. No usar el encabezado `<auto-generated/>`: cualquier archivo podría ponerlo. |
| `check-codegen` | `TARGETS` con dos `.csproj` | Hosts de `readdirSync('backend/Hosts')`, ordenados, un `.csproj` por carpeta, nombre validado. |
| `check-host-items` | `HOSTS` con dos proyectos y sus features | Los mismos hosts descubiertos; las features permitidas salen de la declaración de cada `.csproj` con `-getProperty`. |
| `Dune.props` | 5 nombres `TemplateApp*` o `Contains('/backend/Hosts/')` | Ruta relativa a la raíz con `MakeRelative`, barras normalizadas, `StartsWith('backend/')` o `StartsWith('gateway/')` con `OrdinalIgnoreCase` (sección 3). Los proyectos de `dune/` quedan afuera. |

**Verificación que esto habilita (el Verifica de T1):**
- Fixture de `Invoices` con una slice sin mediator, un handler fuera de forma y un `SaveChangesAsync` en un handler: DUNE0028, 0031 y 0029 fallan.
- Borrar el atributo de `IssueCsrfTokenEndpoint`, de un endpoint de Health o de `TasksFeature`: falla su regla.
- Un atributo con motivo vacío, o sobre un tipo que no lo necesita: falla.
- Un host de prueba en `backend/Hosts/<Otro>/` y un proyecto con otro nombre en `backend/` y en `gateway/` reciben Dune; uno en `dune/` no.

**Choque con los docs del repo.** Ninguno entre dos docs. `conventions.md` (sin comentarios, un tipo por archivo) y ADR-0004 (punto 5: `Shared/` no nombra ninguna feature; punto 9: la transacción del monolito es de Wolverine) son compatibles con la recomendación. Los choques son con el texto del plan (sección "Choques").

## Huecos previos (para `progreso.md`, regla 7)

Ninguno es de T1. Los anoto para que el paso los registre como pendientes:
1. **Cualquier regla de Dune se apaga con `#pragma warning disable DUNE00xx`, con `NoWarn` o con `SuppressMessage`** `[MEDIDO con una regla de prueba]`. Hoy nada lo vigila. La etiqueta `NotConfigurable` en los descriptores cierra `#pragma`, `NoWarn` y `.editorconfig`, pero no `SuppressMessage` `[MEDIDO]`. `SuppressMessage` puede apagar también a una regla guardiana, así que el chequeo tiene que estar fuera de Roslyn: un gate de Node que falle si el código propio nombra un id `DUNE` en un `#pragma` o en un `SuppressMessage`.
2. **Un archivo llamado `*.g.cs`, `*.generated.cs` o `*.designer.cs` queda fuera de los analyzers con `GeneratedCodeAnalysisFlags.None`** `[MEDIDO con una regla de prueba]`, que son la mayoría de Dune (por ejemplo `IdempotentPostAnalyzer.cs:21`, `TransactionOwnerAnalyzer.cs:42`). DUNE0009 no lo cubre, porque el nombre no es un comentario. `[NO PROBADO]` contra las reglas reales de Dune: otra regla de nombres podría frenarlo.
3. **Variante más fuerte de DUNE0032:** "cada feature se compila en un solo host, salvo las compartidas" en `check-host-items` (sección 2.3).

## Qué quedó sin comprobar

- Linux: la condición de `Dune.props` y el orden de `readdirSync`. El CI corre en Ubuntu.
- Que la recomendación compile contra Dune real: no escribí código en el repo.
- La frase textual de Node sobre el orden de `dir.read()`; la vi en un resumen de la página, no en el texto.
- `GeneratedCodeUtilities.cs` de Roslyn (la lista completa de sufijos y encabezados): la ruta dio 404. Me apoyo en la página de Microsoft Learn y en la medición.
- Que el build con `csc` aplique las supresiones igual que `CompilationWithAnalyzers`: medí la API, no un build completo [INFERENCIA: es el mismo `AnalyzerDriver`].
