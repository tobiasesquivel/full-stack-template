# Paso 21: contrato público por feature, y gate que lo haga cumplir (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 21 del [plan](../plan.md) (`plan.md:372-376`). Concepto: módulos con interfaz chica. Nota del plan: el paso 0.5 ya hace cumplir "solo `Contracts`" entre features; este paso cubre lo que falta, "por ejemplo impedir que una slice sea pública". Cambia: `dune/Dune.Analyzers`, `check-structure.mjs` y su test, `docs/conventions.md`. Verifica: una fixture negativa falla con el diagnóstico esperado y una fixture válida compila.
- Método: fuentes primarias (Microsoft Learn, la especificación de C#, el código de `dotnet/sdk` y `dotnet/efcore`, el README de Kamil Grzybek, Milan Jovanović, el código de NetArchTest). Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA]. Lo que salió de una corrida mía va marcado [EXPERIMENTO].
- Evidencia propia (todo fuera del repo; el repo solo suma este archivo):
  - **EX-HUECOS:** una copia del repo (`git archive HEAD`) en el scratchpad. Agregué tests descartables a `dune/Dune.Analyzers.Tests` que arman una feature `Users` con tipos interiores y una feature `Tasks` que los usa de 16 formas distintas. Cada test afirma que DUNE0004 salta. Corrí `dotnet test`: 113 tests, 6 fallan a propósito (los huecos de la sección 3).
  - **EX-INTERNAL-B:** en la misma copia, cambié a `internal` cada tipo de primer nivel de `backend/Features/**` fuera de `Contracts/`, `Domain/` y `Persistence/` (65 archivos), y agregué `<InternalsVisibleTo Include="TemplateApp.Tests" />` a `TemplateApp.csproj`. Compila con Dune sin errores. `dotnet test` del backend con PostgreSQL de Testcontainers: 130 de 130 en verde.
  - **EX-INTERNAL-A:** lo mismo, más `Domain/` y `Persistence/` (71 archivos de features). Aparecen 3 errores CS0053 en `AppDbContext`. Al pasar `AppDbContext` a `internal` aparecen 2 errores CS0051 en dos health checks; al pasarlos a `internal`, compila. 130 de 130 en verde. Las advertencias del build son las mismas que en la copia sin cambios (mismos códigos y cantidades).
  - **EX-OPENAPI:** un test descartable levanta la app en `Development` y baja `/openapi/v1.json`. El documento es idéntico byte a byte con los tipos `public` y con los tipos `internal` (EX-INTERNAL-A).
  - **EX-EF:** `dotnet ef dbcontext list` sobre EX-INTERNAL-A encuentra `TemplateApp.Infrastructure.Persistence.AppDbContext` aunque sea `internal`.

## Conclusión

1. **Recomiendo una regla nueva, DUNE0020: dentro de `backend/Features/`, un tipo de primer nivel es `public` si y solo si vive en `Contracts/`.** Todo lo demás pasa a `internal`: slices, endpoints, errores, validadores, options, `<Feature>Feature`, `<Feature>Module` y también `Domain/` y `Persistence/`. Es un solo cambio observable: la palabra `public` pasa a significar "esto es contrato".
2. **En un solo ensamblado, `internal` no separa una feature de otra.** `internal` es "visible en todo el ensamblado" ([C#, `internal`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/internal)), y todas las features viven en `TemplateApp.dll`. DUNE0004 sigue siendo el único guardián entre features. Lo que `internal` agrega es otra cosa: el compilador prohíbe que un tipo `public` exponga un tipo `internal` en su firma ([especificación de C#, §7.4.5](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/basic-concepts#745-accessibility-constraints)). Entonces un contrato no puede filtrar una entidad sin que el build falle, gratis y sin analyzer.
3. **El costo en este repo es chico y está medido.** Un `InternalsVisibleTo` para `TemplateApp.Tests`, `AppDbContext` y dos health checks a `internal`, y cero cambios en tests. EF Core, System.Text.Json, minimal APIs, OpenAPI y el generador de validación de options siguen andando (EX-INTERNAL-A, EX-OPENAPI, EX-EF).
4. **DUNE0004 tiene tres huecos reales:** métodos y propiedades de extensión de otra feature, el resultado de una invocación que no se guarda en variable, y subcarpetas de `Contracts/`. Recomiendo anotarlos como pendiente en `progreso.md` (regla 7) y cerrarlos en un paso aparte, porque son un segundo cambio observable (regla 1).

## 1. Cómo expresan los profesionales "solo el contrato es público"

- **Grzybek separa ensamblados por módulo y usa `internal` adentro.** "Implementing the Domain Model or Infrastructure in separate assembly allows encapsulation using the internal keyword" ([README §3.2](https://github.com/kgrzybek/modular-monolith-with-ddd#32-module-level-view)). Supuesto clave 9: "Each Module is highly encapsulated - only required types and members are public, the rest are internal or private" ([README §3.1](https://github.com/kgrzybek/modular-monolith-with-ddd#31-high-level-view)). Otro módulo solo ve el ensamblado `IntegrationEvents` ([README §3.2](https://github.com/kgrzybek/modular-monolith-with-ddd#32-module-level-view)).
- **Milan arranca sin nada público.** "Start with nothing public"; el modelo de dominio, los servicios y el esquema "stay internal"; la API pública es una interfaz por caso de uso, `public interface IOrdersModule` ([Milan, internal vs public](https://www.milanjovanovic.tech/blog/internal-vs-public-apis-in-modular-monoliths)). Es la misma forma que `I<Feature>Module` en `Contracts/` (`docs/conventions.md:15`).
- **Los tests de arquitectura cubren lo que el compilador no ve.** Milan prueba que "Modules can only call the public API of other modules" con NetArchTest ([Milan, architecture testing](https://www.milanjovanovic.tech/blog/shift-left-with-architecture-testing-in-dotnet)). Grzybek también usa NetArchTest ([README §3.12](https://github.com/kgrzybek/modular-monolith-with-ddd#312-architecture-unit-tests)). NetArchTest trae la condición `NotBePublic()` ([Conditions.cs](https://github.com/BenMorris/NetArchTest/blob/master/src/NetArchTest.Rules/Conditions.cs#L446)): una regla "estos tipos no son públicos" es práctica común.
- **Para una aplicación, Microsoft recomienda `internal` por defecto.** La regla CA1515 dice: "Unlike a class library, an application's API isn't typically referenced publicly, so types can be marked `internal`", y agrega que los tipos internos aprovechan analyzers que miran API no pública ([CA1515](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1515)). En el repo está en `suggestion` (`.editorconfig:51`). CA1515 marca todo tipo `public`, también los de `Contracts/`, así que no distingue contrato de interior.
- **Con un solo ensamblado, como acá** [INFERENCIA]: el compilador no puede separar features, porque `internal` alcanza a todo el ensamblado ([C#, `internal`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/internal)). Lo que queda es un analyzer o un test de arquitectura por namespace. Este repo ya eligió analyzers (ADR-0004, punto 7: "Un solo proyecto .NET. Dune hace cumplir los límites").

Qué compra `internal` en un solo ensamblado:

| Gana | Por qué |
|---|---|
| El contrato no puede filtrar interior | Un tipo `public` no puede tener en su firma un tipo menos accesible: base, parámetros, retorno, propiedades, campos ([§7.4.5](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/basic-concepts#745-accessibility-constraints)). Un `record UserProfileDetails(UserProfile Profile)` en `Contracts/` deja de compilar si `UserProfile` es `internal`. Hoy DUNE0015 lo controla solo para los métodos de la puerta (`ContractsAnalyzer.cs:85-101`). |
| `public` se lee como "contrato" | Quien abre un archivo ve en la primera línea si otra feature lo puede usar. |
| Prepara la extracción del paso 23 | Si una feature sale a su propio ensamblado, el compilador empieza a hacer cumplir el límite sin tocar código ([C#, `internal`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/internal): "It's an error to reference a type or a member with internal access outside the assembly"). [INFERENCIA] |

| No gana | Por qué |
|---|---|
| Separación entre features | Todo `TemplateApp.dll` ve todo lo `internal`. DUNE0004 sigue siendo necesario. |
| Detección de clases muertas | CA1812 se apaga sola cuando el ensamblado tiene `InternalsVisibleTo` ([CA1812](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1812), "Ignore InternalsVisibleTo attribute"). Hoy no detecta nada en el producto porque no hay tipos `internal`, así que no se pierde nada que exista. |

## 2. Qué cuesta pasar el interior a `internal` en este repo

Hoy los 71 archivos de `backend/Features/**` fuera de `Contracts/` (sin contar tests) declaran tipos `public` (búsqueda de `^public ` con `grep`). Cada pieza, contrastada:

| Pieza | Qué pasa con `internal` | Evidencia |
|---|---|---|
| Tests en otro ensamblado | Los tests viven junto a las slices pero compilan en `TemplateApp.Tests` (`backend/Tests/TemplateApp.Tests.csproj:31`, `backend/TemplateApp.csproj:40-41`). Usan interior: `GetRequiredService<GetTask>()` y `TaskItem` (`GetTaskTests.cs:84,90`), `AppRoles` y `RolePermissions` (`Tests/Startup/ConcurrentStartupTests.cs:96-108`), `AppDbContext` en 9 lugares. Hace falta `InternalsVisibleTo`. | El SDK convierte cada ítem `<InternalsVisibleTo Include="..." />` en el atributo de ensamblado ([Microsoft.NET.GenerateAssemblyInfo.targets](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.GenerateAssemblyInfo.targets)). Con eso, 130 de 130 [EXPERIMENTO EX-INTERNAL-A]. |
| Moq / DynamicProxy | No se usan (`grep` de `Moq`, `NSubstitute`, `Castle` sin resultados). No hace falta `DynamicProxyGenAssembly2`. | `backend/Tests/TemplateApp.Tests.csproj:13-25` |
| `AppDbContext` | Es `public` y expone `DbSet<UserProfile>`, `DbSet<TaskItem>` y `DbSet<Session>` (`AppDbContext.cs:16-18`). Con entidades `internal`, CS0053. Pasar `AppDbContext` a `internal` arrastra a `DatabaseHealthCheck` y `PendingMigrationsHealthCheck` (CS0051, sus constructores son públicos y piden `AppDbContext`). Las slices no: un constructor `public` de una clase `internal` tiene dominio de accesibilidad `internal` ([§7.4.3](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/basic-concepts#743-accessibility-domains)). | EX-INTERNAL-A: 3 errores, después 2, después 0. |
| EF Core, configuraciones | `ApplyConfigurationsFromAssembly` (`AppDbContext.cs:24`) busca constructores con `BindingFlags.Public \| BindingFlags.NonPublic` y crea la instancia con `Activator.CreateInstance(type, nonPublic: true)` ([ModelBuilder.cs](https://github.com/dotnet/efcore/blob/main/src/EFCore/ModelBuilder.cs)). Las configuraciones `internal` se aplican. | Tests de persistencia y migraciones en verde [EX-INTERNAL-A]. |
| `dotnet ef` | Encuentra el `AppDbContext` `internal`. | [EXPERIMENTO EX-EF]. No probé `migrations add`. |
| Minimal APIs y System.Text.Json | Los handlers ya son `private static` (`CreateTaskEndpoint.cs:12`, `HandleAsync`). Los requests y responses `internal` se leen y se escriben igual. | 130 de 130 [EX-INTERNAL-A]. |
| OpenAPI | Mismo documento. | [EXPERIMENTO EX-OPENAPI]: idéntico byte a byte. |
| Validación de options | `[OptionsValidator]` sobre una `partial class` (`SessionOptionsValidator.cs:5-6`). El generador escribe `partial class X` sin modificador de acceso ([options validation generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator)), así que toma el que declare el archivo. | `ConfigurationValidationTests` en verde [EX-INTERNAL-A]. |
| FluentValidation | No hay escaneo: cada slice hace `new()` de su validador (`CreateTask.cs:12`). **Trampa a futuro:** `AddValidatorsFromAssembly` encuentra solo "public non-abstract validators" ([FluentValidation, DI](https://docs.fluentvalidation.net/en/latest/di.html)); si alguien lo agrega, los validadores `internal` quedarían afuera sin error. | `grep AddValidators` sin resultados. |
| `WebApplicationFactory<Program>` | `Program` queda `public` (`Program.cs:60`). No está en `Features/` y la regla no lo toca. | 130 de 130. |
| `<Feature>Feature` y `<Feature>Module` | `Program.cs` los llama desde el mismo ensamblado: `internal` alcanza. | Compila [EX-INTERNAL-A]. |
| Advertencias nuevas | Ninguna. CA1812 no aparece porque `InternalsVisibleTo` la apaga ([CA1812](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1812)). | Mismo conteo de advertencias que la copia sin cambios. |

Resumen del costo: 71 archivos de features (cambio mecánico de una palabra), 1 línea en `TemplateApp.csproj`, 3 archivos de `Infrastructure/Persistence`. Cero tests cambiados.

## 3. Huecos de DUNE0004

DUNE0004 corre sobre `IdentifierName` y `SimpleMemberAccessExpression` (`CrossFeatureAnalyzer.cs:20`). Si el símbolo es un tipo, mira ese tipo; si no, mira el tipo de la expresión (`CrossFeatureAnalyzer.cs:35-37`). Resultado de EX-HUECOS:

| Construcción | ¿DUNE0004 la ve? | ¿Hueco real? |
|---|---|---|
| `typeof(Users.UserThing)` | Sí | No |
| `nameof(Users.UserThing)` | Sí | No |
| Atributo `[UserMarker]` (con y sin `using`) | Sí | No |
| `using static ...Users.UserHelpers;` | Sí, en la directiva | No |
| Restricción genérica `where X : IUserInterior` | Sí | No |
| Clase base `class Probe : UserBase` | Sí | No |
| Alias `using U = ...Users.UserThing;` | Sí | No |
| Patrón `obj is Users.UserThing` | Sí | No |
| Archivo `*Tests.cs` dentro de `Features/Tasks/` | Sí | No |
| Crefs de XML doc | No probado | No: NoCommentsAnalyzer prohíbe comentarios en el backend (`NoCommentsAnalyzer.cs:40-42`) |
| **Método de extensión de otra feature**: `s.Shout()` con `using TemplateApp.Features.Users;` | **No** | **Sí.** El `using` de un namespace no es un tipo, y un grupo de métodos no tiene tipo. |
| **Propiedad de extensión (C# 14)**: `extension(string s) { int Weight ... }` | **No** | **Sí**, misma causa. |
| **Resultado de una invocación sin variable**: `Bridge.Get().Name`, `Convert.ToString(Bridge.Get())`, con `Bridge` en `Infrastructure` devolviendo un tipo de `Users` | **No** | **Sí, pero chico hoy.** `InvocationExpression` no está registrado. Con `var x = Bridge.Get();` sí salta (lo cubre el test `CrossFeatureRejectsForeignDomainReachedWithoutNamingIt`, `LayerAnalyzerTests.cs:300-337`). Hoy `AppDbContext` solo expone propiedades, que sí se ven. |
| **Subcarpeta de `Contracts/`**: `Users/Contracts/Inner/Secret.cs` | **No** (ni DUNE0004, ni DUNE0015, ni DUNE0005) | **Sí.** DUNE0004 acepta `Contracts.*` (`CrossFeatureAnalyzer.cs:95-96`), pero DUNE0015 solo revisa namespaces que terminan en `.Contracts` (`ContractsAnalyzer.cs:201,207`). Una clase cualquiera ahí pasa sin control de forma. |
| Reflexión por texto: `Type.GetType("...Users.UserThing")` | No | No para este paso: ningún análisis estático lo cubre en general, y el repo no usa reflexión por nombre. |

**¿Un archivo en otra carpeta puede declarar `namespace ...Contracts`?** No. DUNE0005 exige que el namespace coincida con la ruta (`NamingAnalyzer.cs:50-58`, `DunePaths.ExpectedNamespace`). Excluye tests (`NamingAnalyzer.cs:34`), pero los tests compilan en otro ensamblado.

> **Corrección (revisión independiente del paso 21):** la respuesta "No" es incorrecta para dos clases de tipo. DUNE0005 solo visita clases, structs, records, interfaces y enums (`NamingAnalyzer.cs:23-27`): un `record struct` o un `delegate` en `CreateTask/` puede declarar `namespace TemplateApp.Features.Tasks.Contracts`, y DUNE0004 lo deja usar desde otra feature. Por eso DUNE0020 exige la carpeta `Contracts/` además del namespace. El hueco de DUNE0005 va al paso 21.1.

Idea de test por hueco real (mismo formato que `LayerAnalyzerTests.cs:251-272`):

- `CrossFeatureRejectsForeignExtensionMethod`: `Features/Users/UserText.cs` con `public static string Shout(this string s)`; `Features/Tasks/Probe.cs` llama `s.Shout()`. Espera DUNE0004. Arreglo: mirar el `ContainingType` del símbolo de método o propiedad, como ya hace `InfrastructureBoundaryAnalyzer.cs:65-66` (con `ReducedFrom` para extensiones).
- `CrossFeatureRejectsForeignResultOfInvocation`: `Infrastructure/Bridge.cs` con `public static UserThing Get()`; `Tasks/Probe.cs` hace `Bridge.Get().Name`. Espera DUNE0004. Arreglo: registrar también `InvocationExpression` y `ElementAccessExpression`, o pasar a `RegisterOperationAction` sobre invocaciones y referencias, que corre al terminar el análisis semántico de cada operación ([RegisterOperationAction](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analysiscontext.registeroperationaction)). `DomainDependencyAnalyzer.cs:27-35` (`RegisterOperationAction`) ya usa ese estilo.
- `CrossFeatureRejectsContractsSubfolder`: `Users/Contracts/Inner/Secret.cs`; `Tasks/Probe.cs` lo usa. Espera DUNE0004. Arreglo: aceptar solo `tail == "Contracts"`. Hoy ningún archivo vive en una subcarpeta de `Contracts/` (listado de `backend/Features`).

## 4. Opciones para el paso 21

| Opción | Qué cambia | Gana | Pierde |
|---|---|---|---|
| **A. `public` solo en `Contracts/` (DUNE0020)** | Regla nueva; 71 archivos a `internal`; `InternalsVisibleTo`; `AppDbContext` y 2 health checks a `internal` | Hace literal ADR-0004 punto 1 ("Todo lo demás es interno"). El compilador impide que un contrato exponga interior (§7.4.5). Es lo que la nota del plan pide ("impedir que una slice sea pública"). Coincide con Grzybek, Milan y CA1515. Prepara el paso 23. | No separa features entre sí: eso sigue en DUNE0004. Diff grande, aunque mecánico. Los tests ven todo el interior por `InternalsVisibleTo`. |
| A2. Igual que A, con excepción para `Domain/` y `Persistence/` hasta el paso 22 | 65 archivos; sin tocar `Infrastructure/` | No toca `AppDbContext`. | La regla nace con una lista de excepciones y el paso 22 tiene que cerrarla. A cambio de evitar 3 archivos. |
| B. Cerrar los huecos de DUNE0004 | `CrossFeatureAnalyzer.cs` y 3 tests | Cierra fugas que hoy compilan. Diff chico. | No cumple la nota del plan. Nada cambia en el código del producto: no hay un cambio observable para enseñar "interfaz chica". |
| C. A + B | Todo lo anterior | Cubre los dos frentes. | Son dos cambios observables en un paso (regla 1). |

**Recomendación: A.** Es el cambio que pide el plan, y el experimento muestra que cuesta poco: tres archivos de `Infrastructure` más que A2, a cambio de una regla sin excepciones. Además el paso 22 mueve el `DbContext` a cada feature, y ahí las entidades `internal` ya quedan en su lugar. B va como pendiente en `progreso.md` con los tres tests de la sección 3 ya escritos como idea.

**Qué hace la industria:** con ensamblados por módulo, `internal` más referencias de proyecto ([Grzybek §3.2](https://github.com/kgrzybek/modular-monolith-with-ddd#32-module-level-view)); con o sin ellos, tests de arquitectura por namespace ([Milan](https://www.milanjovanovic.tech/blog/shift-left-with-architecture-testing-in-dotnet)). Un analyzer de Roslyn hace lo mismo que el test de arquitectura, pero falla en el build y en el editor, no al correr tests [INFERENCIA].

## 5. Cómo lo haría cumplir Dune

**DUNE0020, "Tipo público fuera de Contracts"** (`dune/Dune.Analyzers/PublicSurfaceAnalyzer.cs`):

- `RegisterSymbolAction(..., SymbolKind.NamedType)`, como `ContractsAnalyzer.cs:34`.
- Mira solo tipos de primer nivel (`ContainingType is null`) con ubicación en `backend/Features/` (`DunePaths.TryGetFeature`). Salta tests y migraciones (`DunePaths.IsTestsFile`, `IsMigration`). Ignora código generado (`GeneratedCodeAnalysisFlags.None`), así la parte que escribe `[OptionsValidator]` no cuenta.
- Mensaje 1: un tipo `public` cuyo namespace no es exactamente `TemplateApp.Features.<X>.Contracts` → "'{0}' es public fuera de Contracts/; el interior de una feature es internal".
- Mensaje 2 (recomendado, mismo ID como hace DUNE0015 con tres descriptores en `DuneDiagnostics.cs:142-168`): un tipo de `Contracts/` que no es `public` → "'{0}' está en Contracts/ y no es public". Así `public` y `Contracts/` son lo mismo en las dos direcciones.
- Registrar DUNE0020 en `DuneDiagnostics.cs`, en `AnalyzerHost.All()` (`AnalyzerHost.cs:44-60`), en `WarningsAsErrors` de `dune/Dune.props:5` y como `error` en `.editorconfig` (junto a `.editorconfig:30-39`).

Tests en `dune/Dune.Analyzers.Tests/PublicSurfaceAnalyzerTests.cs`, con `AnalyzerHost.AnalyzeAsync` y `AnalyzerHost.Only<PublicSurfaceAnalyzer>()` como `ContractsAnalyzerTests`:

- **Violación deliberada:** `backend/Features/Tasks/CreateTask/CreateTask.cs` con `public sealed class CreateTask`. Espera DUNE0020 en ese archivo.
- **Violación deliberada 2:** `backend/Features/Users/Contracts/UserProfileDetails.cs` con `internal sealed record UserProfileDetails`. Espera DUNE0020 (mensaje 2).
- **Fixture válida:** `public interface IUsersModule` en `Contracts/`, `internal sealed class UsersModule`, `internal static class UsersFeature`, una clase `internal` con un tipo anidado `public` y un `public sealed class CreateTaskTests` en `*Tests.cs`. Espera cero DUNE0020.

`check-structure.mjs` (el plan lo nombra): una verificación chica de que `backend/TemplateApp.csproj` da `InternalsVisibleTo` solo a `TemplateApp.Tests`, con su caso válido y su violación en `check-structure.test.mjs`. Así nadie abre el interior a otro ensamblado sin que el gate lo vea [INFERENCIA: es la pieza de Node que encaja con el concepto; la regla de `public` necesita símbolos y queda en Roslyn].

**Docs que cambian:**

- `docs/conventions.md:14-18`: "Dentro de una feature, solo los tipos de `Contracts/` son `public`; todo lo demás es `internal`. DUNE0020 lo hace cumplir. Los tests ven el interior por `InternalsVisibleTo`."
- `docs/architecture.md:29`: "Su interfaz pública es `Contracts/` más sus rutas HTTP" pasa a decir que `public` en C# marca exactamente `Contracts/`, y que en un solo ensamblado `internal` no separa features (eso lo hace DUNE0004).
- `docs/decisions.tsv`: tres líneas. Interior `internal` con DUNE0020; `InternalsVisibleTo` para `TemplateApp.Tests` en vez de reescribir los tests; `AppDbContext` `internal` antes del paso 22.

## Choques con los docs

- **Grzybek no usa `InternalsVisibleTo` para tests:** "This exposes the Domain Model to the Unit Tests library, removing encapsulation" ([README §3.10](https://github.com/kgrzybek/modular-monolith-with-ddd#310-unit-tests)). El repo pone los tests junto a cada slice (ADR-0004 punto 2) pero los compila en otro ensamblado, y esos tests usan el interior (sección 2). Sin `InternalsVisibleTo`, el paso tendría que reescribir tests, y eso amplía el alcance (regla 7). No choca con un doc del repo; choca con una fuente. Recomiendo `InternalsVisibleTo` y anotarlo en `decisions.tsv`.
- **Grzybek y la industria separan ensamblados por módulo; ADR-0004 punto 7 dice "Un solo proyecto .NET".** El ADR ya lo decidió y este paso no lo reabre. Por eso `internal` acá es señal y protección de firmas, no un límite entre features.
- **ADR-0004, "Consecuencias", dice que el paso 21 "queda absorbido en gran parte por el paso 0.5".** Coincide con la nota del plan. La opción A es justo "lo que falta".
- No encontré contradicciones entre ADR-0004, `architecture.md` y `conventions.md` sobre este tema.

## Qué quedó sin comprobar

- No corrí las e2e de Playwright ni `smoke-api.mjs` sobre la copia con tipos `internal`. Los 130 tests de integración pasan por HTTP real con `WebApplicationFactory`, y el documento OpenAPI es idéntico. No cambia ningún contrato HTTP.
- No probé `dotnet ef migrations add` con `AppDbContext` `internal`, solo `dbcontext list`.
- No escribí el analyzer DUNE0020: la sección 5 es diseño, no código probado.
- No pude abrir la página CS190 de Ousterhout (el certificado falló). La cita de "interfaz = todo lo que otros módulos deben saber" está en `comunicacion-entre-features.md:57`.

## Fuentes

- C#: [`internal`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/internal); [especificación, conceptos básicos §7.4](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/basic-concepts#745-accessibility-constraints).
- Analyzers .NET: [CA1515](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1515); [CA1812](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1812); [Roslyn `RegisterOperationAction`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analysiscontext.registeroperationaction).
- SDK y librerías: [`GenerateAssemblyInfo.targets` (ítem `InternalsVisibleTo`)](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.GenerateAssemblyInfo.targets); [EF Core `ModelBuilder.cs`](https://github.com/dotnet/efcore/blob/main/src/EFCore/ModelBuilder.cs); [options validation generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator); [FluentValidation DI](https://docs.fluentvalidation.net/en/latest/di.html).
- Monolito modular: [Grzybek, README](https://github.com/kgrzybek/modular-monolith-with-ddd); [Milan, internal vs public APIs](https://www.milanjovanovic.tech/blog/internal-vs-public-apis-in-modular-monoliths); [Milan, architecture testing](https://www.milanjovanovic.tech/blog/shift-left-with-architecture-testing-in-dotnet); [NetArchTest `Conditions.cs`](https://github.com/BenMorris/NetArchTest/blob/master/src/NetArchTest.Rules/Conditions.cs#L446).
