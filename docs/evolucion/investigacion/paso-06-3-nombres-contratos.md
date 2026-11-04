# Paso 6.3: nombres de los contratos entre features y DUNE0015 (investigación)

- Fecha: 2026-11-03.
- Paso: 6.3 del [plan](../plan.md), "Convención de nombres para los contratos entre features (DUNE0015)".
- Qué no reabre: las decisiones de Tobi del 2026-10-20 (`docs/decisions.tsv`, filas 131 a 133) y la sección 4 de [comunicacion-entre-features.md](comunicacion-entre-features.md). Ahí está el porqué. Este informe dice **cómo** se implementa en este código.
- Método: el código se leyó directo (`archivo:línea`). Las fuentes externas se leyeron con WebFetch y la API de GitHub. Las citas salen del resumen de WebFetch, no del texto letra por letra.
- Etiquetas: **[REPO]** sale del código o de los docs del repo. **[INFERENCIA]** es una lectura propia que ninguna fuente dice tal cual.

## Conclusión

1. **Auth tiene una sola puerta, `IAuthModule`, que junta `IAccessPolicy` e `IUserAccounts`, con 4 métodos.** Sale `GetPermissionsAsync`, porque ninguna otra feature lo usa. Users tiene `IUsersModule` con 2 métodos. Las implementaciones se llaman `AuthModule` y `UsersModule` y viven en la raíz de su feature.
2. **Los datos que cruzan usan `<Cosa>Details`.** `UserAccount` pasa a `UserAccountDetails` y `UserProfileSnapshot` pasa a `UserProfileDetails`. La lista cerrada de sufijos es `Details`, `Summary` e `Info`.
3. **`UserResponse` se queda en `Users/Contracts`,** porque Auth lo devuelve como cuerpo HTTP de `register` y `login`. `TaskResponse` y `HealthStatusResponse` van a la raíz de su feature, y `TaskListResponse` a la slice `ListTasks/`. El JSON no cambia.
4. **DUNE0015 hace tres controles:** qué clase de tipo puede vivir en `Contracts/`, qué tipos puede mencionar la puerta, y que todo contrato lo use otra feature. El tercero es posible en Roslyn con un análisis al final de la compilación. Sobre el código de hoy, la regla reporta **8 errores**.
5. **`UserProfiles.FindAsync` se queda.** Si lo borrás, Auth tendría que leer la tabla de Users, y DUNE0004 lo prohíbe. Pasa el deletion test.
6. **Hay un choque real con los docs:** `conventions.md:26` y DUNE0008 prohíben el sufijo `*Module` en las features. ADR-0004 (punto 5) también manda a `Contracts/` "las responses HTTP que comparten varias slices". Las dos cosas cambian en este paso, con su línea en `decisions.tsv`.

## 1. Forma de `IAuthModule` e `IUsersModule`

### 1.1 Quién usa hoy cada método [REPO]

| Método actual | Implementación | Lo llaman desde otra feature | Lo llaman desde su feature |
|---|---|---|---|
| `IAccessPolicy.GetPermissionsAsync` | `Auth/Access/AccessPolicy.cs:20` | nadie | `Auth/UserAccounts.cs:27` |
| `IAccessPolicy.HasPermissionAsync` | `AccessPolicy.cs:48` | `Tasks/CreateTask/CreateTask.cs:31` | `AccessPolicy.cs:63` |
| `IAccessPolicy.CanAccessAsync` | `AccessPolicy.cs:54` | `Tasks/GetTask/GetTask.cs:26`, `Tasks/UpdateTask/UpdateTask.cs:36`, `Tasks/DeleteTask/DeleteTask.cs:26`, `Users/GetUser/GetUser.cs:31`, `Users/DeleteUser/DeleteUser.cs:30` | — |
| `IUserAccounts.FindAsync` | `Auth/UserAccounts.cs:18` | `Users/GetUser/GetUser.cs:34`, `Users/UpdateProfile/UpdateProfile.cs:35` | — |
| `IUserAccounts.DeleteAsync` | `UserAccounts.cs:37` | `Users/DeleteUser/DeleteUser.cs:33` | — |
| `UserAccounts.DescribeAsync` (fuera de la interfaz) | `UserAccounts.cs:24` | — | `Auth/OpenSession/OpenSession.cs:61`, `Auth/RegisterUser/RegisterUser.cs:65` |
| `IUserProfiles.CreateAsync` | `Users/UserProfiles.cs:18` | `Auth/RegisterUser/RegisterUser.cs:57` | — |
| `IUserProfiles.FindAsync` | `UserProfiles.cs:35` | `Auth/OpenSession/OpenSession.cs:54` | — |

Registro en DI [REPO]:

- `Auth/AuthFeature.cs:19`: `AddScoped<IAccessPolicy, AccessPolicy>()`.
- `Auth/AuthFeature.cs:20-21`: `AddScoped<UserAccounts>()` y `AddScoped<IUserAccounts>(sp => sp.GetRequiredService<UserAccounts>())`. Así las slices de Auth usan la clase concreta (por `DescribeAsync`) y Users usa la interfaz, con la misma instancia.
- `Users/UsersFeature.cs:9`: `AddScoped<IUserProfiles, UserProfiles>()`.

### 1.2 Qué hacen las fuentes cuando un módulo expone varias cosas

- **Grzybek usa una sola puerta por módulo, aunque el módulo haga varias cosas.** El módulo UserAccess tiene `Authorization/GetUserPermissions` y `Users/GetUser`, `Users/CreateUser`, y una sola interfaz, `IUserAccessModule` ([carpeta Application](https://github.com/kgrzybek/modular-monolith-with-ddd/tree/master/src/Modules/UserAccess/Application), [IUserAccessModule.cs](https://github.com/kgrzybek/modular-monolith-with-ddd/blob/master/src/Modules/UserAccess/Application/Contracts/IUserAccessModule.cs)). La puerta es genérica: `ExecuteCommandAsync` y `ExecuteQueryAsync`.
- **Grzybek implementa la puerta con una clase del mismo nombre sin `I`:** `public class MeetingsModule : IMeetingsModule`, en el proyecto Infrastructure del módulo ([MeetingsModule.cs](https://github.com/kgrzybek/modular-monolith-with-ddd/blob/master/src/Modules/Meetings/Infrastructure/MeetingsModule.cs)). Esa clase solo delega en el ejecutor de comandos o en MediatR.
- **Milan pone varios métodos por caso de uso en una sola puerta:** `IOrdersModule` con `GetOrderForShippingAsync`, `GetOrderForPaymentAsync` y `GetOrderForCustomerAsync`, y marca como mala la versión `GetOrderAsync` que devuelve la entidad ([Milan, internal vs public APIs](https://www.milanjovanovic.tech/blog/internal-vs-public-apis-in-modular-monoliths)). Regla: "Start with nothing public" y "Expose only what other modules actually need".
- **Milan implementa la puerta con `internal sealed class CatalogModule : ICatalogModule`.** La clase consulta su `DbContext` directo y se registra con `services.AddScoped<ICatalogModule, CatalogModule>()` ([Milan, step by step](https://www.milanjovanovic.tech/blog/build-modular-monolith-dotnet-step-by-step)).
- **Microsoft:** "DO ensure that the names differ only by the 'I' prefix on the interface name when you are defining a class–interface pair where the class is a standard implementation of the interface" ([Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-classes-structs-and-interfaces)). Por eso `IAuthModule` se implementa con `AuthModule`.

### 1.3 Propuesta

**Auth: juntar las dos interfaces en `IAuthModule`.** Lo piden la decisión de Tobi (una puerta por feature) y las dos fuentes. Antes, [informe-vsa-deep-modules.md](../informe-vsa-deep-modules.md) ya había fusionado `PermissionService` y `ResourceAuthorizer` en `IAccessPolicy` por la misma razón: una sola cosa que aprender. [REPO]

```csharp
public interface IAuthModule
{
    Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct);
    Task<bool> CanAccessAsync(string callerId, IOwnedResource resource, string permission, CancellationToken ct);
    Task<UserAccountDetails?> FindAccountAsync(string authId, CancellationToken ct);
    Task<ErrorOr<Success>> DeleteAccountAsync(string authId, CancellationToken ct);
}
```

- **Sale `GetPermissionsAsync`:** ninguna otra feature lo llama (tabla 1.1). Si queda en la puerta, contradice "un contrato nace solo cuando otra feature lo pide". `AccessPolicy` lo conserva para uso interno.
- **Los métodos de cuenta suman el sustantivo:** `FindAccountAsync` y `DeleteAccountAsync`. Hoy el sustantivo lo pone la interfaz (`IUserAccounts.FindAsync`). En una puerta que cubre toda la feature, `IAuthModule.FindAsync` no dice qué encuentra. [INFERENCIA]
- **El `= default` del `CancellationToken` desaparece** para que coincida con las otras firmas de contratos del repo, que lo piden siempre. Es opcional. [INFERENCIA]
- **No uso nombres al estilo `GetOrderForShippingAsync` de Milan.** Cada método tiene hoy un solo propósito, y los verbos `Find`, `Delete`, `Create` ya son el vocabulario del repo. [INFERENCIA]

**Implementación de Auth, dos opciones:**

- **A. `AuthModule` reemplaza a `UserAccounts`** (mismo código, otro nombre) y suma `HasPermissionAsync` y `CanAccessAsync`, que delegan en `AccessPolicy`. `AccessPolicy` queda en `Access/` sin interfaz. Las slices de Auth siguen usando `DescribeAsync` en la clase concreta. El DI queda como hoy: `AddScoped<AuthModule>()` + `AddScoped<IAuthModule>(sp => sp.GetRequiredService<AuthModule>())`, y `AddScoped<AccessPolicy>()`.
  - Ganás: el cambio es chico y cada clase sigue con un solo trabajo.
  - Perdés: dos métodos de una línea que solo reenvían.
- **B. `AuthModule` absorbe también `AccessPolicy`.** Una clase con `UserManager` y `RoleManager`.
  - Ganás: no hay reenvíos.
  - Perdés: la clase mezcla cuentas y permisos. El paso 11 (permisos guardados por request) tendría que tocar esa clase grande.
- **Recomendación: A.** El deletion test se aplica a la puerta entera: si borrás `IAuthModule`, Tasks y Users tendrían que conocer Identity, roles y claims. Los reenvíos internos no hacen pasamanos a la puerta. [INFERENCIA]

**Users: `IUsersModule`, implementada por `UsersModule`** (hoy `UserProfiles`, en la raíz de Users).

```csharp
public interface IUsersModule
{
    Task<ErrorOr<UserProfileDetails>> CreateProfileAsync(string authId, string displayName, CancellationToken ct);
    Task<UserProfileDetails?> FindProfileAsync(string authId, CancellationToken ct);
}
```

- DI: `AddScoped<IUsersModule, UsersModule>()`.
- El mapeo estático `UserProfiles.Snapshot(UserProfile)` lo usan `GetUser.cs:38` y `UpdateProfile.cs:43`. Queda interno de Users, como `UsersModule.Describe(UserProfile)` o en un archivo de mapeo en la raíz, igual que `Tasks/TaskResponses.cs`. No va a `Contracts/`, porque su firma nombra la entidad `UserProfile`. [INFERENCIA]

**Visibilidad:** Milan usa `internal`. Este repo es un solo proyecto, y todos los tipos son `public sealed`. El límite lo pone DUNE0004 (`dune/Dune.Analyzers/CrossFeatureAnalyzer.cs:207-225`). Mantener `public sealed` sigue la costumbre del repo. [REPO]

### 1.4 Qué llamadores cambian [REPO]

| Archivo | Cambio |
|---|---|
| `Tasks/CreateTask/CreateTask.cs`, `GetTask/GetTask.cs`, `UpdateTask/UpdateTask.cs`, `DeleteTask/DeleteTask.cs` | `IAccessPolicy` → `IAuthModule` |
| `Users/GetUser/GetUser.cs`, `Users/DeleteUser/DeleteUser.cs` | `IAccessPolicy` + `IUserAccounts` → un solo `IAuthModule` (el constructor pasa de 3 a 2 parámetros) |
| `Users/UpdateProfile/UpdateProfile.cs` | `IUserAccounts` → `IAuthModule`; `FindAsync` → `FindAccountAsync` |
| `Auth/OpenSession/OpenSession.cs`, `Auth/RegisterUser/RegisterUser.cs` | `UserAccounts` → `AuthModule`; `IUserProfiles` → `IUsersModule`; tipos de datos renombrados |
| `Users/Contracts/UserResponse.cs` | `From(UserAccountDetails, UserProfileDetails)` |
| `Auth/AuthFeature.cs`, `Users/UsersFeature.cs` | registros de DI |
| Tests | Ningún test nombra estas interfaces ni estos tipos: `grep` solo encuentra `AppIdentityUser` en `RegisterUserTests.cs` y `Tests/Support/TestAccounts.cs`. |

## 2. Lista cerrada de sufijos y nombres nuevos

### 2.1 Qué usan las fuentes

- `OrderShippingInfo`, `OrderPaymentInfo`, `OrderSummary` ([Milan](https://www.milanjovanovic.tech/blog/internal-vs-public-apis-in-modular-monoliths)).
- `OrderSummary` ([eShop, IOrderQueries](https://github.com/dotnet/eShop/blob/main/src/Ordering.API/Application/Queries/IOrderQueries.cs)).
- `MeetingDetailsDto` ([Grzybek](https://github.com/kgrzybek/modular-monolith-with-ddd)).
- Ninguna fuente usa `Snapshot` ([comunicacion-entre-features.md](comunicacion-entre-features.md), §2.2).

### 2.2 Lista propuesta

| Sufijo | Significa | Ejemplo |
|---|---|---|
| `Details` | todo lo público de una cosa, para mostrarla o describirla | `UserAccountDetails` |
| `Summary` | unos pocos campos de una cosa, para listarla o citarla | (ninguno todavía) |
| `Info` | solo lo que un consumidor concreto necesita: `<Cosa><Consumidor>Info` | (ninguno todavía) |

Sumar un sufijo = una línea en `decisions.tsv` + cambiar la lista de DUNE0015.

### 2.3 Nombres nuevos, por su uso [REPO]

- **`UserAccount` → `UserAccountDetails`.** Lleva `AuthId`, `UserName`, `Email`, `Roles` y `Permissions` (`Auth/Contracts/UserAccount.cs:3-8`). Users lo usa en `GetUser` y `UpdateProfile`, y Auth en `OpenSession` y `RegisterUser`, siempre para armar `UserResponse`. Es la descripción pública completa de la cuenta, entonces es `Details`. Hoy el nombre parece una entidad.
- **`UserProfileSnapshot` → `UserProfileDetails`.** Lleva `DisplayName` y `Timezone` (`Users/Contracts/UserProfileSnapshot.cs:3`): todo lo público del perfil menos la clave. Auth lo usa para armar `UserResponse` después de registrar o abrir sesión. `UserProfile` es el término de [CONTEXT.md](../../../CONTEXT.md) ("Perfil de usuario (UserProfile)").
- **¿Por qué no `ProfileSummary`?** Porque `Summary` es una parte chica de algo más grande. Acá viaja todo lo público. [INFERENCIA]
- **Prefijo `User`:** la `<Cosa>` es el término del dominio (`UserProfile`). "Cuenta" no está definida en CONTEXT.md, y CONTEXT.md la lista como palabra a evitar para el perfil. Por eso `UserAccountDetails` deja claro que no es el perfil. Sumar "Cuenta" a CONTEXT.md queda como pendiente.

## 3. `Response` solo para HTTP

### 3.1 Quién usa cada response fuera de su feature [REPO]

| Tipo | Fuera de su feature | Dentro |
|---|---|---|
| `UserResponse` | Auth: `RegisterUser.cs:33,66` y `OpenSession.cs:32,62`; lo devuelven como cuerpo de `POST /auth/register` (`RegisterUserEndpoint.cs:24-26`) y `POST /auth/login` (`OpenSessionEndpoint.cs:22`) | `GetUser`, `UpdateProfile` |
| `TaskResponse` | nadie | `CreateTask`, `GetTask`, `UpdateTask`, `TaskResponses.cs` |
| `TaskListResponse` | nadie | solo `ListTasks/ListTasks.cs:17,36` |
| `HealthStatusResponse` | nadie | `GetLivenessEndpoint.cs:10`, `GetReadinessEndpoint.cs:15` |

### 3.2 Propuesta

- **`UserResponse` se queda en `Users/Contracts`.** Es HTTP, y Auth lo usa, así que cumple "Response solo para HTTP" y "lo usa otra feature".
  - La alternativa sería una `SessionUserResponse` propia de Auth con el mismo JSON. Son dos tipos que tienen que coincidir a mano. Además el frontend ya modela "Auth usa `UserResponse` desde `features/users`" (`architecture.md:103`).
  - Recomendación: no duplicar. [INFERENCIA]
- **Las responses que usa solo su feature salen de `Contracts/`.** Siguen `conventions.md:13` ("Un tipo que usa una sola slice vive en la carpeta de la slice") y `conventions.md:15` (lo que comparten varias slices va en la raíz de la feature):
  - `TaskResponse` → `Features/Tasks/TaskResponse.cs`, junto a `TaskResponses.cs` y `TaskErrors.cs`.
  - `TaskListResponse` → `Features/Tasks/ListTasks/TaskListResponse.cs`, porque solo la usa `ListTasks`.
  - `HealthStatusResponse` → `Features/Health/HealthStatusResponse.cs`. `Health/Contracts/` desaparece.
- **El JSON no cambia.** System.Text.Json escribe los nombres de las propiedades, no el tipo ni el namespace: "By default, property names and dictionary keys are unchanged in the JSON output" ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/customize-properties)). Mover un tipo de namespace no toca ningún nombre de propiedad.
- **El OpenAPI tampoco cambia.** El nombre de cada schema sale de `CreateDefaultSchemaReferenceId`, que "uses the name of the type" ([Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0)). Los tipos `*Response` conservan su nombre.
- **`UserAccountDetails` y `UserProfileDetails` no salen por HTTP:** ningún endpoint los devuelve (tabla 1.4). Renombrarlos no toca el contrato HTTP.
- `smoke-api.mjs` revisa las claves JSON (`.agents/skills/verify-template/scripts/smoke-api.mjs:64`). Es la evidencia de que el JSON no cambió.

## 4. DUNE0015

### 4.1 Qué controla

**Dónde mira:** cada tipo de primer nivel declarado en `backend/Features/<F>/Contracts/`. Quedan fuera los tests y las migraciones, con `DunePaths.IsTestsFile` e `IsMigration` (`dune/Dune.Analyzers/DunePaths.cs:244-251`).

**Control A, la clase de tipo.** Cada tipo tiene que ser uno de estos:

| Clase | Cómo la reconoce el analyzer |
|---|---|
| Puerta | `interface` con nombre exacto `I<F>Module` (`F` = la carpeta: `IAuthModule`, `IUsersModule`) |
| Dato entre features | `record` (clase o struct) cuyo nombre termina en `Details`, `Summary` o `Info` |
| Response HTTP | `record` cuyo nombre termina en `Response` |
| Constantes | `static class` con solo campos `const`, y nombre que termina en `Permissions` o `EndpointNames` |
| Extensiones | `static class` con solo métodos de extensión, y nombre que termina en `Extensions` |
| Excepción hasta el paso 22 | exactamente `TemplateApp.Features.Auth.Contracts.AppIdentityUser` |

Todo lo demás da error: otra interfaz, una clase común, un enum, un record con otro sufijo.

- **La excepción va por nombre completo y no por regla general** (por ejemplo, "cualquier clase que herede de `IdentityUser`"). Así no abre un hueco: un segundo tipo de Identity en `Contracts/` daría error. Es la misma forma que la lista cerrada de excepciones de comentarios (`conventions.md:57-60`). El paso 22 la borra junto con `AppIdentityUser`. [INFERENCIA]

**Control B, la firma de la puerta.** Cada método de `I<F>Module` puede mencionar en sus parámetros y en su retorno solo estos tipos:

- la BCL;
- `ErrorOr`;
- `TemplateApp.Shared.*` (por `IOwnedResource`);
- datos `Details`, `Summary` o `Info` de cualquier `Contracts`.

Entonces:

- un `*Response` en la puerta da error, y eso hace ejecutable "Response solo para HTTP";
- `AppIdentityUser` en la puerta da error;
- una entidad del `Domain` en la puerta da error.

Los tipos genéricos (`Task<>`, `ErrorOr<>`, `IReadOnlyList<>`, nullable) se recorren como en `CrossFeatureAnalyzer.FindForeignInterior` (`CrossFeatureAnalyzer.cs:178-205`). [INFERENCIA]

**Control C, todo contrato lo usa otra feature.** Este control hace ejecutable "un contrato nace solo cuando otra feature lo pide".

- **Qué cuenta:** cada tipo de `Contracts/` tiene que aparecer en un archivo de otra feature (`Features/<G>/` con `G ≠ F`).
- **Qué no cuenta:** el uso desde `Infrastructure/` o desde la propia feature. Por eso `AppIdentityUser` necesita la excepción del control A, y el control C también la respeta.
- **Los métodos de la puerta también cuentan:** cada método de `I<F>Module` tiene que tener una llamada desde otra feature. Así `GetPermissionsAsync` no puede volver a la puerta.

### 4.2 ¿Se puede comprobar al compilar? Sí

Roslyn tiene la herramienta para esto: el análisis al final de la compilación (compilation end action).

- "A compilation start action is invoked once, before any of the analyzer's other kinds of actions are invoked" y "A compilation end action is invoked once, after all other applicable actions of an analyzer have been invoked" ([Roslyn, Analyzer Actions Semantics](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md)).
- **Cómo queda** [INFERENCIA]:
  1. `RegisterCompilationStartAction`: crea dos `ConcurrentDictionary`, una de contratos declarados y otra de contratos usados desde afuera.
  2. `RegisterSymbolAction(SymbolKind.NamedType)`: anota cada tipo declarado en `Contracts/` y corre los controles A y B, que no necesitan esperar.
  3. `RegisterSyntaxNodeAction` sobre `IdentifierName`, como `CrossFeatureAnalyzer.cs:148`: resuelve el símbolo. Si es un tipo, lo usa. Si es un miembro (método, campo, propiedad), usa su `ContainingType`, y para un método de extensión, `ReducedFrom`. Si ese tipo vive en `TemplateApp.Features.<F>.Contracts` y el archivo es de otra feature, lo marca como usado. Para los métodos de la puerta guarda también el `IMethodSymbol.OriginalDefinition`.
  4. `RegisterCompilationEndAction`: reporta cada contrato declarado que nadie marcó, en la ubicación de su declaración.
- **Detalle que importa:** `GetCallerId()` se usa como método de extensión. En Tasks no aparece el nombre `ClaimsPrincipalExtensions`. Si el analyzer contara solo los nombres de tipo, daría un rojo falso sobre `ClaimsPrincipalExtensions`. Por eso cuenta el `ContainingType` de los miembros.
- **Descriptor:** el diagnóstico del final de la compilación lleva `customTags: WellKnownDiagnosticTags.CompilationEnd`. Si no lo lleva, salta RS1037: "Add 'CompilationEnd' custom tag to compilation end diagnostic descriptor" ([RS1037](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/Microsoft.CodeAnalysis.Analyzers.md); [WellKnownDiagnosticTags.CompilationEnd](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.wellknowndiagnostictags.compilationend)).
- **Un ID, dos o tres descriptores,** como DUNE0014, que comparte el ID entre `ErrorOutsideCatalog` y `ErrorCodePrefix` (`DuneDiagnostics.cs:464-480`). [REPO]
- **Costos:**
  - **En el editor, el control C puede no verse.** Según la documentación de vs-threading de Microsoft, esos diagnósticos "will appear in a full build log" pero "may disappear when the document they refer to is opened in the editor", salvo con *full solution analysis* ([vs-threading, FSA](https://vs-threading.readthedocs.io/en/latest/analyzers/fsa.html)). En `dotnet build` y en CI siempre aparecen.
  - **Más memoria y una pasada más:** el analyzer recorre los identificadores del proyecto, igual que DUNE0004 hoy. [INFERENCIA]
  - **El proyecto de tests también corre los analyzers** (`dune/Dune.props:2,11`). Ahí los contratos llegan como metadata, no como código fuente. La acción de símbolo no los anota, entonces no hay reportes falsos. [INFERENCIA, a comprobar con la fixture]
- **Recomendación: sí, con el control C incluido.** Es la parte de la convención que más fácil se rompe, porque nadie mira un tipo que sobra. El costo del editor es aceptable porque el gate real es `dotnet build`. [INFERENCIA]

### 4.3 Cambios que arrastra en Dune [REPO]

- **DUNE0008 hoy rechaza cualquier nombre que termine en `Module`** dentro de una feature (`NamingAnalyzer.cs:72-77`), y eso choca con `IAuthModule`. Cambio propuesto:
  - acepta solo `I<F>Module` en `Features/<F>/Contracts/`;
  - acepta `<F>Module` en la raíz de la feature, solo si implementa `I<F>Module`;
  - todo otro `*Module` sigue en rojo.
  - Los tests de hoy `ModuleOutsideFeatureRootIsRejected` (`NamingAndQueryAnalyzerTests.cs:27`) y `SharedModuleAtFeatureRootIsRejected` (`SliceShapeAnalyzerTests.cs:54`, una `partial class TasksModule` sin interfaz) siguen en rojo: el módulo parcial que descartó ADR-0004 sigue prohibido. Por la regla 9, este cambio lleva su línea en `decisions.tsv`.
- **`Dune.props:5`:** sumar `DUNE0015` a `WarningsAsErrors`. Si no, el build no se corta.
- **`AnalyzerHost.All()`** (`AnalyzerHost.cs:44-58`): sumar el analyzer nuevo.
- **Archivos nuevos:** `ContractsAnalyzer.cs` y `ContractsAnalyzerTests.cs`, con el patrón de `ErrorCatalogAnalyzerTests`: rutas `backend/Features/...`, stubs de `ErrorOr`, `AnalyzerHost.Only<T>()` y `Locations()` ordenadas.

### 4.4 Fixture válida y violaciones deliberadas

**Fixture válida (cero diagnósticos):**

- `backend/Features/Auth/Contracts/IAuthModule.cs`, con un método que devuelve `Task<UserAccountDetails?>` y otro que recibe `IOwnedResource`.
- `backend/Features/Auth/Contracts/UserAccountDetails.cs` (record).
- `backend/Features/Auth/Contracts/ClaimsPrincipalExtensions.cs` y `AppIdentityUser.cs`.
- `backend/Features/Users/Contracts/UserResponse.cs`, `UserPermissions.cs` y `UserEndpointNames.cs`.
- `backend/Features/Auth/AuthModule.cs : IAuthModule`.
- `backend/Features/Users/GetUser/GetUser.cs`, que usa `IAuthModule.FindAccountAsync`, `UserAccountDetails`, `GetCallerId()` y `UserPermissions.Read`.
- `backend/Features/Auth/RegisterUser/RegisterUser.cs`, que usa `UserResponse` y `UserEndpointNames`.
- `backend/Features/Tasks/TaskResponse.cs`, fuera de `Contracts`: el analyzer no lo mira.

**Violaciones deliberadas (una línea esperada por cada una):**

| Archivo | Qué rompe |
|---|---|
| `Auth/Contracts/IAccessPolicy.cs` | interfaz que no es `IAuthModule` (control A) |
| `Users/Contracts/UserProfileSnapshot.cs` | record con sufijo fuera de la lista (A) |
| `Tasks/Contracts/TaskStatus.cs` (enum) | clase de tipo no admitida (A) |
| `Auth/Contracts/SessionUser.cs` (clase común) | clase no admitida, aunque herede de algo de Identity (A, la excepción no se extiende) |
| `Auth/Contracts/IAuthModule.cs` con un método que devuelve `UserResponse` | `Response` en la puerta (B) |
| `Tasks/Contracts/TaskDetails.cs` que solo usa Tasks | contrato sin uso afuera (C) |
| `IAuthModule.GetPermissionsAsync` llamado solo desde `Auth/` | método de la puerta sin uso afuera (C) |
| `Tasks/CreateTask/TasksModule.cs` | `*Module` fuera de lugar (DUNE0008, sigue en rojo) |

### 4.5 El rojo que se espera sobre el código de hoy [REPO]

Con los controles A, B y C, el analyzer reporta **8 errores**:

| # | Archivo | Control | Motivo |
|---|---|---|---|
| 1 | `Auth/Contracts/IAccessPolicy.cs:5` | A | interfaz que no es `IAuthModule` |
| 2 | `Auth/Contracts/IUserAccounts.cs:5` | A | interfaz que no es `IAuthModule` |
| 3 | `Auth/Contracts/UserAccount.cs:3` | A | record sin sufijo de la lista |
| 4 | `Users/Contracts/IUserProfiles.cs:5` | A | interfaz que no es `IUsersModule` |
| 5 | `Users/Contracts/UserProfileSnapshot.cs:3` | A | `Snapshot` no está en la lista |
| 6 | `Tasks/Contracts/TaskResponse.cs:3` | C | solo lo usa Tasks |
| 7 | `Tasks/Contracts/TaskListResponse.cs:3` | C | solo lo usa Tasks |
| 8 | `Health/Contracts/HealthStatusResponse.cs:3` | C | solo lo usa Health |

Lo que pasa sin error:

- `AppIdentityUser`, por la excepción.
- `ClaimsPrincipalExtensions`, porque la usan Tasks y Users.
- `TaskPermissions` y `UserPermissions`, porque las usa `Auth/Access/RolePermissions.cs`.
- `UserEndpointNames`, porque la usa `RegisterUserEndpoint.cs:25`.
- `UserResponse`, porque la usa Auth.

El control B no tiene nada que revisar hoy, porque todavía no existe ninguna puerta `I<F>Module`.

## 5. Deletion test sobre `UserProfiles.FindAsync`

- **El test** (skill `codebase-design`, [SKILL.md](https://raw.githubusercontent.com/mattpocock/skills/main/skills/engineering/codebase-design/SKILL.md)): "Imagine deleting the module. If complexity vanishes, it was a pass-through. If complexity reappears across N callers, it was earning its keep."
- **Qué hace** (`Users/UserProfiles.cs:35-42`): busca el perfil por `AuthId` con `AsNoTracking`, y si existe, copia `DisplayName` y `Timezone`. [REPO]
- **Quién lo llama:** solo `Auth/OpenSession/OpenSession.cs:54`. Si no hay perfil, devuelve `AuthErrors.ProfileMissing`. Las slices de Users no lo usan: leen `_db.UserProfiles` directo (`GetUser.cs:25-27`, `UpdateProfile.cs:30-31`). [REPO]
- **Si lo borrás:**
  - OpenSession tendría que leer `AppDbContext.UserProfiles`, y conocer la entidad `UserProfile`, la clave `AuthId` y el modo sin tracking.
  - DUNE0004 corta la compilación, porque el tipo de esa expresión es `Users.Domain.UserProfile` (`CrossFeatureAnalyzer.cs:162-165`).
  - Entonces la complejidad no desaparece: vuelve en el llamador y además rompe el límite.
- **No es un pasamanos.** Un pasamanos reenvía a otro módulo sin agregar nada. Este método esconde la persistencia de Users detrás de la puerta. Es corto porque el perfil es chico, no porque no haga nada. [INFERENCIA]
- **¿Se podría eliminar la necesidad?** Por ejemplo, si `login` no devolviera el perfil y el frontend pidiera `GET /users/me`. Eso cambia el contrato HTTP de `login`, y el paso dice "No cambia el JSON de la API". Queda fuera. [INFERENCIA]
- **Recomendación: mantenerlo**, como `IUsersModule.FindProfileAsync`.

## 6. Tamaño del paso

El paso junta siete cosas:

1. las puertas y los renombres, con sus llamadores;
2. sacar las responses propias de `Contracts/`;
3. el cambio de DUNE0008;
4. DUNE0015 controles A y B;
5. DUNE0015 control C, al final de la compilación;
6. el deletion test;
7. los docs.

Es grande para "un paso = un cambio observable" (regla 1). Un corte posible, sin decidirlo:

- **6.3a. Las puertas se reconocen por su nombre.** Puertas, renombres, DUNE0008 y DUNE0015 A y B, deletion test, `conventions.md`. Lo que ves: Dune exige `I<F>Module` y los sufijos. Rojo: los errores 1 a 5.
- **6.3b. Un contrato nace solo cuando otra feature lo pide.** DUNE0015 control C y la mudanza de las tres responses. Lo que ves: un contrato sin uso afuera corta el build. Rojo: los errores 6 a 8.

Las dos mitades no cambian el JSON. Cada una tiene su propio rojo y su propio concepto.

## 7. Choques con los docs del repo

1. **`conventions.md:26`:** "No se usan `*Module` ni `*Endpoints` en features. DUNE0008 lo hace cumplir" (`NamingAnalyzer.cs:72-77`). Esto choca con la fila 131 de `decisions.tsv` (`I<Feature>Module`). La fila es posterior y la decidió Tobi, y el plan ya dice que `conventions.md` cambia. Hay que reescribir la frase y ajustar DUNE0008 con su línea en `decisions.tsv` (regla 9).
2. **ADR-0004, punto 5:** "Lo que usa otra feature va a `Contracts/`, igual que las responses HTTP que comparten varias slices". Lo mismo dicen `conventions.md:14` y `architecture.md:18`. Esto choca con la fila 132 ("separa lo que ve otra feature de las responses HTTP propias"). La fila 132 nombra solo `conventions.md`, pero el ADR dice lo mismo. Falta una entrada en "Cambios" de ADR-0004, decidida por Tobi.
3. **ADR-0004, "Alternativas descartadas":** descarta "Un módulo parcial por feature (ADR-0003)" porque "todas las slices comparten el constructor". La puerta `AuthModule` no es eso: ninguna slice de otra feature la ve por dentro y las slices propias no se apoyan en ella. Pero el nombre es el mismo, y un lector o un agente puede confundirlos. Conviene aclararlo en la misma entrada de "Cambios". [INFERENCIA]
4. **`architecture.md:51`** nombra `IUserProfiles` e `IUserAccounts`. Se actualiza en el paso.
5. **`plan.md:318` (paso 11)** dice "`IAccessPolicy` en `Features/Auth`". Después de este paso, esa pieza es `AccessPolicy` (interna) detrás de `IAuthModule`. Se actualiza la línea o se anota como pendiente en `progreso.md` (regla 7).
6. **No es un choque del repo, pero las fuentes no coinciden:**
   - Milan usa `ProductResponse` como dato entre módulos ([step by step](https://www.milanjovanovic.tech/blog/build-modular-monolith-dotnet-step-by-step)). La decisión del repo ("Response solo para HTTP") sigue a su otro artículo, el de `OrderShippingInfo`.
   - El repo de Grzybek no permite llamadas directas entre módulos ("Modules communicate each other only asynchronously using Events Bus", [README](https://github.com/kgrzybek/modular-monolith-with-ddd)). En Grzybek, `IUserAccessModule` la usa la API, no otro módulo. La forma de la puerta sale de Grzybek; la llamada directa entre módulos, de Milan.

## Decisiones abiertas para Tobi

1. **Implementación de `IAuthModule`:**
   - A: `AuthModule` reemplaza a `UserAccounts` y delega permisos en `AccessPolicy`.
   - B: una sola clase que absorbe también `AccessPolicy`.
   - Recomiendo A, porque cada clase sigue con un trabajo y el paso 11 toca solo `AccessPolicy`.
2. **Nombre de la clase que implementa la puerta:** `AuthModule` / `UsersModule` (Microsoft y las dos fuentes), con DUNE0008 aceptando solo ese par. La otra opción es un nombre sin `Module` (`AuthDoor`?), que deja DUNE0008 como está pero sin respaldo de ninguna fuente. Recomiendo `<F>Module`.
3. **Lista de sufijos:**
   - las tres de las fuentes (`Details`, `Summary`, `Info`), que dejan el vocabulario listo;
   - o solo `Details`, que es la única que se usa hoy, y sumar las otras cuando aparezca su caso.
   - Recomiendo las tres, porque la lista es vocabulario y no código que sobra. [INFERENCIA]
4. **Nombres de los datos:** `UserAccountDetails` y `UserProfileDetails` (recomendado), o `AccountDetails` y `ProfileDetails`, sin `User`.
5. **El control C de DUNE0015 en este paso o no:** gana que la regla "nace cuando otra feature lo pide" se vuelva ejecutable; pierde que el error no se ve en el editor sin full solution analysis.
6. **Partir el paso en 6.3a y 6.3b** (sección 6), o hacerlo entero.
7. **ADR-0004:** sumar una entrada en "Cambios" para el punto 5 (responses fuera de `Contracts/`) y para aclarar que `<F>Module` no es el módulo parcial descartado.

## Qué quedó sin comprobar

- No compilé ni corrí nada: el rojo de la sección 4.5 es una predicción hecha leyendo el código y con `grep`.
- Que el analyzer no dé falsos positivos en el proyecto `TemplateApp.Tests` (contratos como metadata): es una inferencia que tiene que confirmar la fixture.
- Cómo se comportan los diagnósticos del final de la compilación en Rider o en VS Code: solo leí la documentación de Visual Studio (vs-threading).
- Las citas salen del resumen de WebFetch, no del texto original letra por letra.
- La implementación de `IOrdersModule` de Milan: su artículo no la muestra. Que la implementación delegue o consulte directo sale de `CatalogModule`, en otro artículo suyo.
- Que ningún documento de OpenAPI del repo se compare en un test: busqué "openapi" en los tests y en `smoke-api.mjs` y no encontré comparaciones, pero no corrí el documento antes y después.
