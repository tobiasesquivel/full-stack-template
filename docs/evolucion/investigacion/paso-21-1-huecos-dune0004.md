# Paso 21.1: cerrar los huecos de DUNE0004 (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 21.1 del [plan](../plan.md) (`plan.md:378-382`). Concepto: un gate vale lo que su caso más débil. Cambia: `CrossFeatureAnalyzer.cs`, `NamingAnalyzer.cs` y sus tests, `docs/conventions.md`. Verifica: un test rojo por hueco con el analyzer de antes, verde después; la suite de Dune y el build del backend sin DUNE0004 nuevos.
- Punto de partida: la sección 3 de [paso-21-contrato-publico.md](paso-21-contrato-publico.md) (EX-HUECOS) y la revisión del paso 21 (hueco de DUNE0005).
- Método: fuentes primarias (documentación de la API de Roslyn en Microsoft Learn, el documento de semántica de acciones del repo `dotnet/roslyn`, la especificación de miembros de extensión de C# 14, el código de NetArchTest y el README de ArchUnitNET). Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA]. Lo que salió de una corrida mía va marcado [EXPERIMENTO].
- Evidencia propia. Todo corrió fuera del repo, en copias con `git archive HEAD` en el scratchpad. El repo solo suma este archivo.
  - **EX-SIMBOLOS:** una consola con `Microsoft.CodeAnalysis.CSharp` 5.9.0 compila una feature `Users` con un método de extensión clásico, un bloque `extension(string s)` (método, propiedad, método estático) y un operador de extensión, y una feature `Tasks` que los usa. Imprime, para cada nombre y cada operación, el símbolo, su `ContainingType`, `IsExtension`, `ReducedFrom` y `AssociatedExtensionImplementation`.
  - **EX-BATERIA:** un archivo de tests descartable en `dune/Dune.Analyzers.Tests` con 49 construcciones (un `[Theory]`): 34 formas de llegar al interior de `Users` desde `Tasks` que hoy no se ven, 7 que ya se veían en el paso 21 (control) y el tipo en `Contracts/Inner/`. Suma una fixture válida (contratos, extensiones de `Contracts/`, la propia feature, LINQ de la BCL) que no debe dar DUNE0004, y dos tests de DUNE0005 (`record struct` y `delegate` con namespace `...Contracts` dentro de una slice).
  - **EX-ROJO:** EX-BATERIA con los analyzers de hoy: 154 tests, 118 pasan, **36 fallan** (las 34 construcciones y los 2 tests de DUNE0005). Los 7 de control y la fixture válida pasan.
  - **EX-NUCLEO:** EX-BATERIA con el diseño recomendado sin la parte de patrones del compilador (sección 2): fallan 5 de 147, los 5 casos de patrón (`foreach`, `Deconstruct` x3, `await`).
  - **EX-VERDE:** EX-BATERIA con el diseño recomendado completo: **154 de 154**.
  - **EX-BACKEND:** `dotnet build TemplateApp.slnx --configuration Release` sobre una copia sin cambios y sobre la copia con el diseño recomendado. Las dos: 0 errores, 393 advertencias, mismos códigos y mismas cantidades. Ningún DUNE0004 ni DUNE0005 en el backend.
  - **EX-INYECCION:** en las dos copias agregué `backend/Features/Tasks/ProbeHueco.cs`, que llama `services.AddUsersFeature()`, un método de extensión del interior de `Users` (`backend/Features/Users/UsersFeature.cs:7`). Con el analyzer de hoy compila (0 errores). Con el recomendado: `ProbeHueco.cs(8,74): error DUNE0004 ... 'TemplateApp.Features.Users.UsersFeature' es interior de otra feature`.
  - **EX-RUIDO:** cuántos DUNE0004 produce una sola violación, con el analyzer de hoy y con el recomendado.

## Conclusión

1. **Los huecos no son tres: son una familia de 34 construcciones con dos causas.** EX-ROJO lo muestra. Causa A: DUNE0004 mira el tipo de cada *nombre*, pero no el tipo que *declara* el miembro usado; por eso no ve métodos, propiedades ni operadores de extensión. Causa B: no mira el tipo de las expresiones que no son un nombre (una invocación, un `new()`, un `await`, una lambda); por eso no ve un resultado que no se guarda. A eso se suman `Contracts.*` (hueco 3) y la lista incompleta de DUNE0005 (hueco 4).
2. **Recomiendo un solo diseño para DUNE0004, en tres piezas, todas con estilos que el repo ya usa:**
   - Se queda la acción de sintaxis de hoy sobre nombres (`CrossFeatureAnalyzer.cs:20`): cubre las declaraciones (campos, parámetros, clases base, atributos, restricciones, `using`), que el árbol de operaciones no tiene.
   - Se suma una acción de operaciones (`RegisterOperationAction`) que mira dos cosas de cada invocación, referencia a miembro, creación de objeto o delegado, operador y conversión: el tipo que declara el miembro usado y el tipo del resultado. Es el estilo de `DomainDependencyAnalyzer.cs:27-35` y `:96-106`.
   - Se suma una acción de sintaxis chica para los métodos que el compilador busca por patrón (`GetEnumerator` en `foreach`, `Deconstruct`, `GetAwaiter` en `await`), porque `IOperation` no los expone.
3. **El hueco 3 se cierra con una línea:** `IsForeignInterior` acepta solo el namespace exacto `...Contracts` (borrar `CrossFeatureAnalyzer.cs:96`). Coincide con `conventions.md:19`, que ya dice "(no en una subcarpeta)".
4. **El hueco 4 se cierra pasando DUNE0005 a `RegisterSymbolAction(SymbolKind.NamedType)`**, como `PublicSurfaceAnalyzer.cs:22` y `ContractsAnalyzer.cs:34`. El hueco nació de una lista de tipos de sintaxis que se olvidó dos; con la acción de símbolo no hay lista que olvidar.
5. **Sin falsos positivos medidos:** el backend compila igual que hoy (EX-BACKEND), la fixture válida pasa y una violación real inyectada en el backend falla (EX-INYECCION).
6. **No hay choques con los docs.** Cambian dos líneas de `conventions.md` (24 y 29) y la descripción de DUNE0004 en `DuneDiagnostics.cs:41`.

> **Corrección (revisiones independientes del paso 21.1):** el diseño de la sección 2 no cierra la familia. Registra una lista de 11 tipos de operación, y dos revisiones encontraron 25 construcciones más que esa lista no veía: asignación compuesta e incremento con operador de extensión, deconstrucción anidada y `is var (a, b)`, un tipo anidado dentro de un genérico (`Dictionary<UserThing, int>.KeyCollection`), `params`, el resultado de un `await` con un awaiter de `Infrastructure`, el elemento de un `foreach` o de un spread, un `Deconstruct` con un `out` de tipo ajeno, índices y rangos implícitos (`[^1]`, `[1..]`), patrones de lista, inline arrays y un `Count` de extensión usado por `[^1]`. Todas tienen la misma causa que el hueco 4: una lista de casos que olvida alguno. El diseño final revisa **todas** las operaciones (`Enum.GetValues(typeof(OperationKind))`), y en cada una mira su tipo, los tipos de entrada y de salida de los patrones, el tipo de elemento de un spread, y el tipo que declara cada miembro usado junto con su firma (retorno y parámetros). La acción de sintaxis sigue para `foreach`, `await` y `Deconstruct` (también anidado), y ahora mira también el tipo de elemento y los demás miembros del patrón. Batería final: 211 de 211 [EXPERIMENTO]. **Límite que queda:** un `Add` o un `GetEnumerator` de extensión usados solo por una expresión de colección (`Bag b = ["x"]`, `[.. (ushort)3]`). Roslyn 5.9 no los expone: `ISpreadOperation` solo tiene `Operand`, `ElementType` y `ElementConversion`, e `ICollectionExpressionOperation` solo `ConstructMethod`, `ConstructArguments` y `Elements` [EXPERIMENTO: reflexión sobre `Microsoft.CodeAnalysis` 5.9.0]. **Ruido medido con el diseño final** [EXPERIMENTO]: `s.Shout()` 1, `Bridge.Get().Name` 2, `var u = Bridge.Get(); return u.Name;` 5, `u => u.Next = null` 9, `typeof(UserThing)` 1.

## 1. Cómo se detecta "este código depende del tipo X" en un analyzer de Roslyn

Roslyn ofrece tres tipos de acción, y cada una ve una parte distinta del código:

| Acción | Qué ve | Qué no ve |
|---|---|---|
| Sintaxis (`RegisterSyntaxNodeAction`) | Cada nodo del árbol de sintaxis del tipo pedido: "invoked once per syntax node, provided that the syntax node has a kind matching" ([Analyzer Actions Semantics](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md)). Ve las declaraciones. | Solo los tipos de nodo de la lista. Lo que el compilador agrega por su cuenta (el `Add` de un inicializador de colección, el `Select` de una consulta LINQ) no tiene nodo propio. |
| Operaciones (`RegisterOperationAction`) | El árbol `IOperation`, la vista semántica del código ejecutable: "executed at completion of semantic analysis of an IOperation" ([RegisterOperationAction](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analysiscontext.registeroperationaction?view=roslyn-dotnet-5.9.0)). Cada operación trae su `Type`, "Result type of the operation" ([IForEachLoopOperation, propiedades heredadas](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.operations.iforeachloopoperation?view=roslyn-dotnet-5.9.0)). | Las declaraciones (tipo de un campo, clase base, atributo). Los métodos de patrón: `IForEachLoopOperation` no tiene una propiedad con el `GetEnumerator` ([misma página](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.operations.iforeachloopoperation?view=roslyn-dotnet-5.9.0)). |
| Símbolos (`RegisterSymbolAction`) | Cada declaración de tipo o miembro: "invoked once per complete semantic processing of a declaration of a type or type member" ([Analyzer Actions Semantics](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md)). | El cuerpo de los métodos. |

- **Lo que hace hoy DUNE0004.** Corre sobre `IdentifierName` y `SimpleMemberAccessExpression` (`CrossFeatureAnalyzer.cs:20`). Si el nombre es un tipo, lo mira; si no, mira el tipo de la expresión (`CrossFeatureAnalyzer.cs:34-37`). En `Bridge.Get().Name`, el tipo foráneo `UserThing` está solo en la invocación `Bridge.Get()`, que no es un nombre. Y en `s.Shout()` ningún nombre tiene un tipo foráneo: `s` es `string` y `Shout` es un método sin tipo. [EXPERIMENTO EX-SIMBOLOS]
- **Qué recomiendan los mantenedores.** No encontré una guía oficial que diga "usá operaciones en vez de sintaxis" para este problema. Lo que sí documentan es qué ve cada acción (tabla de arriba). [INFERENCIA] La consecuencia directa es combinar: sintaxis o símbolos para las declaraciones y operaciones para el código ejecutable.
- **El repo ya combina así.** `DomainDependencyAnalyzer` usa las tres acciones a la vez: símbolos para las firmas, operaciones para invocaciones y referencias, y sintaxis para nombres de tipo (`DomainDependencyAnalyzer.cs:21-49`). Para una invocación mira `invocation.TargetMethod.ContainingType` y para una referencia `member.Member.ContainingType` (`DomainDependencyAnalyzer.cs:96-106`). Siete analyzers más usan `RegisterOperationAction` (por ejemplo `ApiRouteAnalyzer.cs:32`, `ReadSliceAnalyzer.cs:96`).
- **Una operación implícita también llega a la acción.** El `Add` que el compilador llama en `new List<int> { "x" }` y el `Select` de `from x in v select x` aparecen como invocaciones, y la acción de operaciones las ve. [EXPERIMENTO EX-NUCLEO: los casos `ext_pattern_collection_add` y `ext_pattern_linq` pasan sin la pieza de patrones]

### Qué hacen NetArchTest y ArchUnitNET

Las dos leen el ensamblado compilado, no el código fuente. ArchUnitNET: "This is done by analyzing C# bytecode" ([README](https://github.com/TNG/ArchUnitNET)). NetArchTest usa Mono.Cecil y recorre las instrucciones de cada método ([TypeDefinitionCheckingContext.cs, `CheckMethodBodyInstructions`](https://github.com/BenMorris/NetArchTest/blob/master/src/NetArchTest.Rules/Dependencies/TypeDefinitionCheckingContext.cs)):

| Construcción | ¿NetArchTest la ve? | Por qué |
|---|---|---|
| Método de extensión clásico | Sí | La llamada es un `MethodReference` y NetArchTest mira su `DeclaringType` ([mismo archivo](https://github.com/BenMorris/NetArchTest/blob/master/src/NetArchTest.Rules/Dependencies/TypeDefinitionCheckingContext.cs)). |
| Miembro de un bloque `extension` (C# 14) | Sí [INFERENCIA] | El compilador emite cada uso como llamada estática al "implementation method" de la clase estática ([especificación de C# 14, *Implementation methods* y *Lowering*](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions#implementation-methods)). |
| `Bridge.Get().Name` | Sí | `get_Name` es un `MethodReference` cuyo `DeclaringType` es `UserThing`. |
| `Convert.ToString(Bridge.Get())` | No | En una llamada, NetArchTest mira el `DeclaringType` (acá `Bridge`), no el tipo que devuelve; el tipo de retorno solo lo mira en la declaración del método ([mismo archivo, `CheckMethodHeader`](https://github.com/BenMorris/NetArchTest/blob/master/src/NetArchTest.Rules/Dependencies/TypeDefinitionCheckingContext.cs)). |
| `nameof(UserThing)` | No | `nameof` se evalúa al compilar ([C#, `nameof`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/nameof)): en el IL queda solo un texto. |

[INFERENCIA] Mirar el IL no es más completo que Roslyn: gana en extensiones (todo termina en una llamada) y pierde en lo que el compilador borra. Un analyzer con operaciones ve las dos cosas, y además falla en el editor y en el build.

### Opciones de diseño para DUNE0004

| Opción | Qué es | Gana | Pierde |
|---|---|---|---|
| **A. Nombres + operaciones + patrones (recomendada)** | La acción de hoy, más `RegisterOperationAction` sobre 11 tipos de operación, más una acción de sintaxis para 5 patrones | Cierra las 34 construcciones [EX-VERDE]. Cada pieza ve lo que las otras no ven. Mismo estilo que `DomainDependencyAnalyzer`. | `CrossFeatureAnalyzer.cs` pasa de 98 a 181 líneas. Más diagnósticos por línea (EX-RUIDO). |
| A sin patrones | A, sin la tercera pieza | Unas 35 líneas menos. | Quedan 5 huecos [EX-NUCLEO]: una extensión `GetEnumerator`, `Deconstruct` o `GetAwaiter` del interior de otra feature. Raros, pero son una puerta abierta, y el concepto del paso es justo ese. |
| B. Solo sintaxis, sumando tipos de nodo | Registrar `InvocationExpression`, `ElementAccess`, `ImplicitObjectCreation`, `Await` y más, y mirar el `ContainingType` del símbolo | No suma `IOperation`. | Es una lista que hay que adivinar: no ve el `Add` ni el `Select` implícitos, y cada versión de C# agrega nodos. Es el mismo error que causó el hueco 4. |
| C. Recorrer todas las operaciones del cuerpo (`RegisterOperationBlockAction`) | Un callback por método, que recorre `Descendants()` | Ninguna lista de tipos de operación. | Reporta cada nivel de una expresión (más ruido) y no es el estilo de ningún analyzer del repo. |

**Qué hace la industria:** con un ensamblado por módulo, el compilador separa los módulos y los tests de arquitectura (NetArchTest, ArchUnitNET) controlan los namespaces sobre el IL. Este repo eligió un solo ensamblado y analyzers (ADR-0004 punto 7, citado en [paso-21-contrato-publico.md](paso-21-contrato-publico.md)). Un analyzer serio de dependencias combina sintaxis o símbolos para las declaraciones con operaciones para el código, como ya hace `DomainDependencyAnalyzer`. [INFERENCIA]

## 2. El diseño recomendado, en código

Probado en la copia (EX-VERDE, EX-BACKEND, EX-INYECCION). Lo nuevo de `CrossFeatureAnalyzer`:

```csharp
context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.SimpleMemberAccessExpression); // igual que hoy
context.RegisterOperationAction(AnalyzeOperation,
    OperationKind.Invocation, OperationKind.MethodReference, OperationKind.PropertyReference,
    OperationKind.FieldReference, OperationKind.EventReference, OperationKind.ObjectCreation,
    OperationKind.DelegateCreation, OperationKind.Binary, OperationKind.Unary,
    OperationKind.Conversion, OperationKind.DefaultValue);
context.RegisterSyntaxNodeAction(AnalyzePatternMember,
    SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement, SyntaxKind.AwaitExpression,
    SyntaxKind.SimpleAssignmentExpression, SyntaxKind.RecursivePattern);

// AnalyzeOperation: el tipo que declara el miembro usado, y si no, el tipo del resultado.
ISymbol? member = operation switch
{
    IInvocationOperation invocation => invocation.TargetMethod,
    IMemberReferenceOperation reference => reference.Member,
    IBinaryOperation binary => binary.OperatorMethod,
    IUnaryOperation unary => unary.OperatorMethod,
    IConversionOperation conversion => conversion.OperatorMethod,
    _ => null,
};
var foreign = FindForeignInterior(DeclaringType(member), owner) ?? FindForeignInterior(operation.Type, owner);

// Un miembro de un bloque extension vive en un tipo "extension": se sube a la clase estática.
static INamedTypeSymbol? DeclaringType(ISymbol? member) =>
    member?.ContainingType is { IsExtension: true } block ? block.ContainingType : member?.ContainingType;
```

`AnalyzePatternMember` pide el método que eligió el compilador con `GetForEachStatementInfo(...).GetEnumeratorMethod` ([GetForEachStatementInfo](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.csharpextensions.getforeachstatementinfo?view=roslyn-dotnet-5.9.0)), `GetDeconstructionInfo(...).Method`, `GetAwaitExpressionInfo(...).GetAwaiterMethod` e `IRecursivePatternOperation.DeconstructSymbol`, y mira su `DeclaringType`. [EXPERIMENTO EX-VERDE: los 5 casos de patrón pasan]

`TryGetOwner` (ruta en `backend/`, no migración, dentro de una feature) y `FindForeignInterior` se reusan sin cambios de las líneas de hoy (`CrossFeatureAnalyzer.cs:26-31`, `:50-77`).

## 3. Cómo aparecen los miembros de extensión de C# 14 en Roslyn 5.9

[EXPERIMENTO EX-SIMBOLOS], con `LanguageVersion.Latest` = C# 14:

| Uso | Símbolo | `ContainingType` | Cómo llegar a la clase estática |
|---|---|---|---|
| `s.Shout()` (clásico), en sintaxis | `string.Shout()`, `MethodKind = ReducedExtension` | `UserText` (la clase estática) | Directo. `ReducedFrom` = `UserText.Shout(string)` ([ReducedFrom](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.imethodsymbol.reducedfrom?view=roslyn-dotnet-5.9.0)). |
| `s.Shout()`, en `IInvocationOperation.TargetMethod` | `UserText.Shout(string)`, ya sin reducir | `UserText` | Directo. |
| `s.Whisper()` (bloque `extension(string s)`) | `UserBlocks.extension(string).Whisper()` | El bloque: `IsExtension = true`, `TypeKind = Extension` ([IsExtension](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.inamedtypesymbol.isextension?view=roslyn-dotnet-5.9.0)) | `ContainingType.ContainingType` = `UserBlocks`. |
| `s.Weight` (propiedad de extensión) | `IPropertySymbol` del bloque | El bloque | Igual. |
| `string.Make()` (estático de extensión) | `IMethodSymbol` del bloque, `IsExtensionMethod = false` | El bloque | Igual. |
| `new Version() + 1` (operador de extensión) | `MethodKind = UserDefinedOperator`, en `IBinaryOperation.OperatorMethod` | El bloque | Igual. |

- `AssociatedExtensionImplementation` da el método estático que el compilador emite, ya en la clase estática ([AssociatedExtensionImplementation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.imethodsymbol.associatedextensionimplementation?view=roslyn-dotnet-5.9.0)). Sirve igual, pero solo existe en métodos; subir un nivel desde el bloque sirve también para propiedades.
- Un bloque `extension` solo puede vivir en una clase estática de primer nivel: "Extensions are declared inside top-level non-generic static classes" ([especificación de C# 14, *Static classes as extension containers*](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions#static-classes-as-extension-containers)). Por eso subir un solo nivel alcanza.
- El namespace del bloque es el de la clase estática (`TemplateApp.Features.Users`), así que `DuneSymbols.NamespaceOf` ya da el resultado correcto. Subir a la clase sirve para que el mensaje nombre `UserBlocks` y no `UserBlocks.extension(string)`.
- Los operadores de extensión existen en C# 14 ([especificación, *Operators*](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions#operators)) y compilan en la batería. Por eso `Binary` y `Unary` están en la lista.

## 4. Los cuatro huecos y lo que encontró la batería

Resultado por construcción. "Hoy" = EX-ROJO. "Después" = EX-VERDE. Todas viven en `Features/Tasks/Probe.cs` y usan `Users` (interior) o `Bridge` (en `Infrastructure/`, devuelve tipos de `Users`).

| Construcción | Hoy | Después | ¿Hueco real? | Lo cierra |
|---|---|---|---|---|
| **Hueco 1.** `s.Shout()`, `Func<string> f = s.Shout` | No | Sí | Sí | Operaciones: `TargetMethod`, `MethodReference` |
| Bloque `extension`: método, propiedad, estático, operador | No | Sí | Sí | Operaciones + subir del bloque a la clase |
| `new List<int> { "x" }` con `Add` de extensión; LINQ con `Select` de extensión | No | Sí | Sí | Operaciones (invocación implícita) |
| `foreach (var i in 3)` con `GetEnumerator` de extensión | No | Sí | Sí, raro | Patrones |
| `var (a, b) = u;`, `foreach (var (a, b) in us)`, `u is ("h", _)` con `Deconstruct` de extensión | No | Sí | Sí, raro | Patrones |
| `await TimeSpan.Zero` con `GetAwaiter` de extensión | No | Sí | Sí, raro | Patrones |
| **Hueco 2.** `Bridge.Get().Name`, `Convert.ToString(Bridge.Get())`, `Bridge.Get();` suelto, `return Bridge.Get();` | No | Sí | Sí | Operaciones: `Type` de la invocación |
| `Bridge.List()[0]` (acceso por índice) | No | Sí | Sí | Ídem (`List<UserThing>`) |
| `await Bridge.GetAsync()` | No | Sí | Sí | Ídem (`Task<UserThing>`) |
| `foreach (object u in Bridge.All())` | No | Sí | Sí | Ídem (`UserThing[]`) |
| `Bridge.Take(new())`, `Bridge.Take(new() { Name = "x" })` | No | Sí | Sí | `ObjectCreation` |
| `Bridge.Take(default!)` | No | Sí | Sí | `DefaultValue` |
| `Bridge.Each(_ => { })`, `Bridge.Run(Bridge.Get)` | No | Sí | Sí | `DelegateCreation` (`Action<UserThing>`) |
| `with`, `?:`, `??`, `new[] { ... }`, `[ ... ]`, `$"{...}"`, `Echo(Bridge.Get())`, `is { Name: "x" }` | No | Sí | Sí | La invocación de adentro |
| `(await Bridge.GetAsync()).Next`, `Bridge.Find()?.Next`, `foreach (var u in ...)`, `u => u.Next = null`, `var (u, n) = Bridge.Pair()`, `Bridge.Pair().Item1`, `new { U = Bridge.Get() }`, `Bridge.Changed += ...` | Sí | Sí | No | Algún nombre ya tiene el tipo foráneo |
| **Hueco 3.** `Users.Contracts.Inner.Secret` | No | Sí | Sí | `tail == "Contracts"` |
| **Hueco 4.** `record struct` o `delegate` en `CreateTask/` con namespace `...Tasks.Contracts` | No (DUNE0005) | Sí (DUNE0005) | Sí | DUNE0005 con acción de símbolo |
| Control del paso 21: `typeof`, `nameof`, atributo, restricción genérica, `is UserThing`, clase base (anidada), miembro estático | Sí | Sí | No | Sin cambios |

Lo que el diseño todavía no ve, a propósito:

| Construcción | ¿Hueco real? | Por qué |
|---|---|---|
| Reflexión por texto: `Type.GetType("...Users.UserThing")` | No para este paso | Ningún análisis estático lo cubre en general; el repo no usa reflexión por nombre (igual que en el paso 21). |
| Código generado | No | `ConfigureGeneratedCodeAnalysis(None)` lo saltea a propósito (`CrossFeatureAnalyzer.cs:18`); el único generador es el de validación de options, dentro de su feature. |
| `dynamic` | No hoy [INFERENCIA] | Con `dynamic` el tipo real se conoce al ejecutar. El repo no usa `dynamic` (búsqueda sin resultados en `backend/`). |
| `using static` y alias de otra feature, archivo `*Tests.cs` | No | El paso 21 los vio; la acción de nombres no cambia. No los repetí en la batería. |

**Ruido** [EXPERIMENTO EX-RUIDO]: una violación puede dar más de un DUNE0004 en la misma línea, porque cada pieza reporta su nodo. Antes → después: `s.Shout()` 0 → 1, `Bridge.Get().Name` 0 → 2, `Users.Contracts.Inner.Secret` 0 → 2, `u => u.Next = null` 3 → 6, `typeof(UserThing)` 1 → 1. Solo pasa en líneas que ya son error. `DomainDependencyAnalyzer` evita repetir con un diccionario `reported` (`DomainDependencyAnalyzer.cs:23`, `:425-462`); se puede copiar, pero no es necesario para cerrar los huecos.

**Tests recomendados para el paso** (estilo `LayerAnalyzerTests.cs:251-337`, un rojo por hueco como pide el plan):

- `CrossFeatureRejectsForeignExtensionMethod` y `CrossFeatureRejectsForeignExtensionProperty` (hueco 1: clásico y bloque C# 14).
- `CrossFeatureRejectsForeignResultOfInvocation` (hueco 2: `Bridge.Get().Name` y `Convert.ToString(Bridge.Get())`).
- `CrossFeatureRejectsForeignPatternMember` (patrones: `foreach` con `GetEnumerator` de extensión).
- `CrossFeatureRejectsContractsSubfolder` (hueco 3).
- `NamingRejectsRecordStructOutsideItsNamespace` y `NamingRejectsDelegateOutsideItsNamespace` (hueco 4).
- Fixture válida (regla 4): `CrossFeatureAcceptsContractsExtensionsAndOwnFeature`, con un método y una propiedad de extensión en `Users/Contracts/`, la propia feature y LINQ de la BCL. Debe dar cero DUNE0004.

## 5. Hueco 4: DUNE0005

Hoy `NamingAnalyzer` registra cinco tipos de sintaxis (`NamingAnalyzer.cs:23-27`) y castea a `BaseTypeDeclarationSyntax` (`NamingAnalyzer.cs:32`). `record struct` es `RecordStructDeclaration`, un tipo de nodo distinto de `RecordDeclaration`, y `delegate` es `DelegateDeclarationSyntax`, que no hereda de `BaseTypeDeclarationSyntax`. Ninguno de los dos entra. [EXPERIMENTO EX-ROJO: los dos tests fallan]

| Opción | Qué cambia | Gana | Pierde |
|---|---|---|---|
| **A. `RegisterSymbolAction(SymbolKind.NamedType)` (recomendada)** | `AnalyzeType` recorre `named.Locations` y aplica las mismas reglas a cada ubicación en `backend/`. `CheckSuffix` y `Report` reciben un `Location` en vez del nodo. | Cubre todo tipo de primer nivel, también los que agregue C# más adelante. Mismo estilo que `PublicSurfaceAnalyzer.cs:22` y `ContractsAnalyzer.cs:34`. El archivo baja de 155 a 150 líneas. | Diff un poco más grande que B. Las reglas de sufijo (DUNE0008) pasan a valer también para un `delegate` [INFERENCIA: hoy no hay delegates en `backend/`, y el build no cambió, EX-BACKEND]. |
| B. Sumar `RecordStructDeclaration` y `DelegateDeclaration` a la lista | Dos tipos de nodo más; cambiar el cast a `MemberDeclarationSyntax` y sacar el `Identifier` de cada caso | El diff más chico. | Repite la causa del hueco: una lista que alguien tiene que acordarse de completar. |
| C. Que DUNE0004 decida "contrato" por la carpeta del archivo y no por el namespace | Mirar la ruta de declaración del tipo usado | No depende de DUNE0005. | En `TemplateApp.Tests` los tipos del backend llegan como metadata, sin ruta de archivo [INFERENCIA], así que igual hace falta el namespace. Dos fuentes de verdad. |

Con A: tests de DUNE0005 en verde, suite completa en verde y el backend con las mismas advertencias. [EXPERIMENTO EX-VERDE, EX-BACKEND]

Un tipo anidado sigue afuera (`named.ContainingType is not null`), igual que hoy (`NamingAnalyzer.cs:40`); un bloque `extension` siempre es anidado, así que no lo toca. Las partes de una clase `partial` se revisan una por una, cada una en su archivo.

## 6. Docs que cambian

- `docs/conventions.md:24` dice: "DUNE0004 lo hace cumplir: mira el tipo nombrado y también el tipo de cada expresión, así que `var` no lo esquiva." Propuesta: "DUNE0004 lo hace cumplir: mira el tipo nombrado, el tipo que declara cada miembro usado (también métodos, propiedades y operadores de extensión, y los que el compilador busca solo, como `GetEnumerator` o `Deconstruct`) y el tipo de cada resultado, aunque no se guarde en una variable. Un tipo en una subcarpeta de `Contracts/` es interior."
- `docs/conventions.md:29` dice: "El namespace coincide con la ruta, bajo `TemplateApp`." Propuesta: sumar "DUNE0005 lo hace cumplir para todo tipo de primer nivel: clase, struct, record, `record struct`, interface, enum y delegate."
- `dune/Dune.Analyzers/DuneDiagnostics.cs:41`, la descripción de DUNE0004, repite la frase de `conventions.md:24`: conviene cambiarla igual.
- `docs/architecture.md:29` y ADR-0004 no cambian: dicen qué es el límite, no cómo lo mira DUNE0004.
- `docs/decisions.tsv`: una línea por decisión de diseño (operaciones + patrones en DUNE0004; DUNE0005 por símbolo).

## Choques con los docs

No encontré choques. Las piezas que se tocan van en la misma dirección que los docs:

- ADR-0004 punto 3: "Ninguna feature usa el `Domain`, la `Persistence` ni las slices de otra" (`docs/adr/0004-vertical-slices-deep-modules.md:22`). Un resultado que llega por `Infrastructure` o un método de extensión también es "usar". El diseño lo hace literal.
- `conventions.md:19` ya dice que un contrato vive en `Contracts/` "(no en una subcarpeta)". Hoy DUNE0004 acepta `Contracts.*` (`CrossFeatureAnalyzer.cs:95-96`): el arreglo alinea Dune con el doc (regla 10).
- `conventions.md:17` permite `*Extensions` con métodos de extensión en `Contracts/`. La fixture válida confirma que siguen permitidos.

## Qué quedó sin comprobar

- No corrí `dotnet test` del backend (necesita Docker). El cambio es solo de analyzers: el build compila `TemplateApp` y `TemplateApp.Tests` con ellos, y da lo mismo que hoy (EX-BACKEND).
- No repetí en la batería tres construcciones del paso 21 que ya se veían (`using static`, alias, archivo `*Tests.cs`); la acción que las ve no cambia.
- No medí el tiempo de análisis. El build completo tardó lo mismo en las dos copias (unos 5 a 7 segundos con caché), pero eso no es una medición seria.
- No revisé si `InfrastructureBoundaryAnalyzer` (DUNE0003) y `DomainDependencyAnalyzer` tienen la misma familia de huecos. DUNE0003 ya mira el tipo que declara cada miembro (`InfrastructureBoundaryAnalyzer.cs:60-66`), así que probablemente solo le falten los patrones [INFERENCIA]. Va como pendiente (regla 7).
- `OwnerPredicateAnalyzer.cs:91` usa `IOperation.Children`, que está obsoleto (advertencia CS0618 en los dos builds). No es de este paso.

## Fuentes

- Roslyn: [Analyzer Actions Semantics](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md); [RegisterOperationAction](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analysiscontext.registeroperationaction?view=roslyn-dotnet-5.9.0); [IForEachLoopOperation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.operations.iforeachloopoperation?view=roslyn-dotnet-5.9.0); [GetForEachStatementInfo](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.csharpextensions.getforeachstatementinfo?view=roslyn-dotnet-5.9.0); [INamedTypeSymbol.IsExtension](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.inamedtypesymbol.isextension?view=roslyn-dotnet-5.9.0); [IMethodSymbol.AssociatedExtensionImplementation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.imethodsymbol.associatedextensionimplementation?view=roslyn-dotnet-5.9.0); [IMethodSymbol.ReducedFrom](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.imethodsymbol.reducedfrom?view=roslyn-dotnet-5.9.0).
- C#: [especificación de miembros de extensión (C# 14)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions); [`nameof`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/nameof).
- Tests de arquitectura: [NetArchTest, `TypeDefinitionCheckingContext.cs`](https://github.com/BenMorris/NetArchTest/blob/master/src/NetArchTest.Rules/Dependencies/TypeDefinitionCheckingContext.cs); [ArchUnitNET, README](https://github.com/TNG/ArchUnitNET).
