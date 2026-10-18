# Paso 6: catálogo de errores estable (investigación)

- Fecha: 2026-10-18.
- Alcance: el paso 6 del [plan](../plan.md). El contrato de error pasa a ser parte de la API: cada error tiene un `code` estable, sale de un catálogo y lo arma un solo mapeo.
- Decisiones que ya están tomadas y que este informe no reabre: `ErrorOr` 2.1.1 y FluentValidation 12.1.1 se quedan (`backend/TemplateApp.csproj:25-26`). Los errores salen como ProblemDetails. La forma vertical slices + módulos profundos de [ADR-0004](../../adr/0004-vertical-slices-deep-modules.md).
- Método: solo fuentes primarias. Son el [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html), Microsoft Learn, el código de ASP.NET Core 10 (rama `release/10.0`), el README y el código de ErrorOr, la documentación de FluentValidation, las [Azure REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md) y [Google AIP-193](https://google.aip.dev/193).
- Evidencia propia: un experimento descartable fuera del repo (`/tmp/paso06-exp`). Usó el SDK 10.0.401, `ErrorOr` 2.1.1, `Asp.Versioning.Http` 10.2.3 y autenticación por cookie, con `ASPNETCORE_ENVIRONMENT=Production`. Cada vez que este informe dice "experimento", habla de esa corrida. No se tocó código del repo.

## Conclusión

- **El `code` es el identificador estable.** El `type` queda con el valor por defecto de ASP.NET Core (la sección del RFC 9110 de cada status). Los clientes deciden por `code`, nunca por `title` ni por `detail`. Los códigos actuales (`Task.NotFound`, `User.NotFound`, `Auth.*`) no cambian: ya están publicados y el smoke afirma uno (`Auth.AntiforgeryFailed`).
- **Formato del código:** `<Prefijo>.<Motivo>` en PascalCase. El prefijo es el de la clase del catálogo: `TaskErrors` → `Task.`, `UserErrors` → `User.`, `AuthErrors` → `Auth.`. Hay una excepción: los errores de validación usan como `Code` el nombre del campo (`"Title"`, `"Page"`, `"DisplayName"`), porque ese valor es la clave del diccionario `errors`.
- **Dónde vive el catálogo:** un `<Feature>Errors.cs` en la raíz de cada feature, como ya están `TaskErrors`, `UserErrors` y `AuthErrors`. Lo transversal (fallos de validación en general, ruta inexistente, error inesperado) va a `Shared/Http/HttpErrors.cs`: no nombra ninguna feature. Lo piden ADR-0004 (puntos 5 y 6) y `decisions.tsv:27`.
- **Un solo mapeo:** `ApiResults` pasa a tener un `StatusFor(ErrorType)` y un único `Results.Problem(...)`. Hoy repite `Results.Problem` 5 veces (`ApiResults.cs:28-32`). El `traceId` **ya sale hoy** en todo ProblemDetails: lo agrega `DefaultProblemDetailsWriter` porque el repo llama a `AddProblemDetails()`. No hace falta código para eso.
- **500:** hoy el `GlobalExceptionHandler` publica `exception.Message` en `detail` y el nombre de la excepción en `type` (`GlobalExceptionHandler.cs:27-29`). En el experimento salió `"detail":"secret connection string Host=db;Password=x"`. Choca con `conventions.md:66` ("Un 500 no publica detalles internos") y con el RFC 9457 §5. El frontend muestra ese `detail` en pantalla (`http-error.ts:49`). Se arregla en este paso: `code = "Http.Unexpected"`, sin `detail`, y la excepción va al log.
- **400 de validación:** se mantiene el diccionario `errors` por campo (`ValidationProblemDetails`), porque el frontend lo lee (`http-error.ts:20`). Se agrega un `code` de nivel superior (`Validation.Failed`). No se agregan códigos por campo: ninguna fuente lo exige y nadie los consumiría hoy.
- **401/403 fuera de los handlers:** los eventos de la cookie escriben un ProblemDetails con `code` (`Auth.SessionRequired`, `Auth.AccessDenied`). Se reutiliza el mismo mapeo: `ApiResults.Problem(error).ExecuteAsync(http)`. `Results.Problem` pasa por `IProblemDetailsService`, así que sale igual que el resto (con `traceId`). El antiforgery deja de escribir `"Auth.AntiforgeryFailed"` en línea y usa `AuthErrors.AntiforgeryFailed`.
- **404 de ruta y de versión:** `app.UseStatusCodePages()` les da cuerpo ProblemDetails. Un `CustomizeProblemDetails` en Infrastructure completa el `code` que falte (`Http.NotFound`, `Http.MethodNotAllowed`…). Esto cambia el contrato de toda respuesta de error sin cuerpo: ⏸ **DECISIÓN DE TOBI** (regla 3 de AGENTS.md y `decisions.tsv:115`).
- **`title` y `detail`:** hoy la descripción variable ("Task with ID '…' not found.") va en `title`. El RFC 9457 §3.1.3 pide un `title` que no cambie entre casos. Moverla a `detail` cambia lo que ve el usuario: el frontend muestra `detail` en 401, 409 y 500 (`http-error.ts:41-49`), y pasaría de "Ya existe un registro con esos datos." a "Email is already registered.". Recomendación: **no moverla en el paso 6**. Queda como pendiente, junto con que el frontend traduzca por `code`. ⏸ **DECISIÓN DE TOBI.**
- **Regla de Dune:** DUNE0013 nueva: "`Error.<Factory>(...)` de ErrorOr solo en un `*Errors.cs`". Es factible con la misma técnica de DUNE0011/0012 (operaciones de invocación y símbolo del método). Obliga a mover 13 llamadas en línea a los catálogos. Opcional en la misma regla: el prefijo del código coincide con la clase (`TaskErrors` → `Task.`).
- **Pruebas:** un test por status con `code` exacto (400, 401, 403, 404, 409 y 500) y el smoke con aserciones de `code`. Hoy solo un test afirma un `code` (`IssueCsrfTokenTests.cs:128`).

## 1. Qué hace estable a un contrato de error

**Respuesta:** un identificador legible por máquina que nunca cambia para la misma condición. El texto para humanos puede cambiar. En este repo ese identificador es la extensión `code`.

### Lo que dicen las fuentes

- **`type` es el identificador primario según el RFC.** "Consumers MUST use the "type" URI (after resolution, if necessary) as the problem type's primary identifier" ([RFC 9457 §3.1.1](https://www.rfc-editor.org/rfc/rfc9457.html#section-3.1.1)). Puede ser una URI que no resuelve, como `tag:example@example.org,2021-09-17:OutOfLuck` (misma sección). Se recomienda absoluta.
- **`title` no cambia entre casos.** "SHOULD NOT change from occurrence to occurrence of the problem, except for localization" ([RFC 9457 §3.1.3](https://www.rfc-editor.org/rfc/rfc9457.html#section-3.1.3)).
- **`detail` ayuda a corregir, y no se parsea.** "Consumers SHOULD NOT parse the "detail" member for information" ([RFC 9457 §3.1.4](https://www.rfc-editor.org/rfc/rfc9457.html#section-3.1.4)).
- **Las extensiones están permitidas** y el cliente ignora las que no conoce ([RFC 9457 §3.2](https://www.rfc-editor.org/rfc/rfc9457.html#section-3.2)). El nombre empieza con letra y tiene tres caracteres o más ([RFC 9457 §4](https://www.rfc-editor.org/rfc/rfc9457.html#section-4)). `code` cumple.
- **Los problemas genéricos no necesitan tipo propio.** "a 403 Forbidden status code in response to a PUT request is self-explanatory" ([RFC 9457 §4](https://www.rfc-editor.org/rfc/rfc9457.html#section-4)).
- **Azure: el código es contrato.** "`x-ms-error-code` values are part of your API contract" y el `code` del cuerpo es idéntico al del header ([#rest-error-code-header](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md#rest-error-code-header), [#rest-error-code-header-and-body-match](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md#rest-error-code-header-and-body-match)). Los demás campos no son contrato ([#rest-error-non-api-contract-fields](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md#rest-error-non-api-contract-fields)).
- **Azure: qué rompe.** "it's only a breaking change if the same conditions result in a _different_ top-level error code" ([#rest-error-code-enum](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md#rest-error-code-enum)). Y "YOU SHOULD NOT add new top-level error codes to an existing API without bumping the service version" ([#rest-add-codes-in-new-api-version](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md#rest-add-codes-in-new-api-version)).
- **Azure: documentar los códigos.** "DO document the service's top-level error code strings" ([#rest-document-error-code-values](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md#rest-document-error-code-values)). En OpenAPI, no listar cada status salvo que la respuesta `default` no alcance ([#rest-error-use-default-response](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md#rest-error-use-default-response)).
- **Google: un par estable por error.** "Services must use the same (reason, domain) pair for the same error, and must not use the same (reason, domain) pair for logically different errors" ([AIP-193, ErrorInfo](https://google.aip.dev/193)). El `domain` cumple el papel del prefijo: dice quién emite el error.

### `type` URI o extensión `code`

| Opción | Qué pide | Costo en este repo |
|---|---|---|
| A. `type` por defecto + `code` (recomendada) | Nada nuevo. ASP.NET Core ya pone `type` por status ([ProblemDetailsDefaults.cs](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Shared/ProblemDetails/ProblemDetailsDefaults.cs)) | El `type` no distingue `Task.NotFound` de `User.NotFound`. Se documenta que el identificador es `code` |
| B. `type` propio por código (`tag:` o URL) | Una URI por código y, si resuelve, una página por código ([RFC 9457 §4](https://www.rfc-editor.org/rfc/rfc9457.html#section-4): "SHOULD resolve to HTML") | Cambia el `type` de todos los errores ya publicados. Hace falta un host de documentación o URIs `tag:` |

Se recomienda A. Es lo que ya hace el repo, lo que hace Azure (un `code` string) y lo que no rompe nada. B queda como opción si algún día la API es pública.

### Espacio de nombres

- ErrorOr trae códigos genéricos por defecto: `"General.NotFound"`, `"General.Forbidden"`, `"General.Validation"` ([Error.cs](https://github.com/amantinband/error-or/blob/main/src/Errors/Error.cs)). Su README propone una clase estática por tema con códigos `Division.CannotDivideByZero` ([README, Organizing Errors](https://github.com/amantinband/error-or#organizing-errors)). El formato `<Prefijo>.<Motivo>` sale de ahí.
- El repo ya lo usa: `Task.NotFound` (`TaskErrors.cs:8`), `User.NotFound` (`UserErrors.cs:9`), `Auth.EmailAlreadyExists` (`AuthErrors.cs:8`). El prefijo es el singular del agregado (`Task`, `User`), no el nombre de la carpeta (`Tasks`, `Users`). Cambiarlo a `Tasks.NotFound` cambiaría códigos publicados: no conviene.
- Regla propuesta: el prefijo es el nombre de la clase sin `Errors`. `TaskErrors` → `Task.`. Se puede verificar en Dune (sección 6).

### Versionado

- Agregar `code` donde hoy no hay (400 de validación, 401/403 de la cookie, 404 de ruta, 500) es un agregado. Según Azure, solo rompe si la misma condición devuelve otro código. Pero Azure también pide no agregar códigos sin subir versión. Este repo tiene una sola versión (v1) y un solo cliente (el frontend del mismo repo). Agregar en v1 es razonable, pero lo decide Tobi.
- Cambiar un código existente sí rompe. Por eso `Task.NotFound`, `User.NotFound` y los `Auth.*` se quedan como están.

### Documentar los códigos

- OpenAPI: `ProducesProblem(status)` y `ProducesValidationProblem()` describen respuestas de error. Funcionan también sobre un `RouteGroupBuilder`: "a common set of error responses to be defined for all operations in a group" ([Learn, Set responses for ProblemDetails](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0#set-responses-for-problemdetails)). Los endpoints usan `Results.*`, que no aporta metadata. Solo los tipos con `IEndpointMetadataProvider` crean un `responses` ([Learn, Describe response types](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0#describe-response-types)).
- El esquema `ProblemDetails` de OpenAPI no lista los valores posibles de `code`. Documentarlos ahí pide un transformer de OpenAPI. El documento solo se publica en Development (`Program.cs`). Recomendación: el catálogo en código es la documentación, y el test de la sección 6 lo fija. `ProducesProblem` por grupo queda como pendiente.

## 2. Dónde vive el catálogo

**Respuesta:** un `<Feature>Errors.cs` en la raíz de cada feature, más un `HttpErrors.cs` chico en `Shared/Http/` para lo transversal. No un catálogo único.

- ADR-0004, punto 5: "Lo interno que comparten varias slices (errores, servicios como `Sessions`, mapeos) queda en la raíz de la feature". `decisions.tsv:27` lo decidió Tobi en el paso 0.4. `architecture.md:17` lo dibuja: `TaskErrors.cs` = "piezas internas que comparten varias slices".
- ADR-0004, punto 6: "`Shared/` queda como núcleo mínimo que no nombra ninguna feature". Un catálogo único en `Shared/` tendría `Task.NotFound` y `Auth.InvalidCredentials` juntos: nombraría features. Choca con el ADR.
- Un catálogo único también rompe la profundidad del módulo: cada feature nueva tocaría un archivo compartido. El criterio de `informe-vsa-deep-modules.md` es que el conocimiento de una feature no salga de ella.
- ¿Va a `Contracts/`? No. Los errores no se usan entre features hoy. `DeleteUser` recibe `Auth.AccountNotFound` de `IUserAccounts` y lo traduce a `User.NotFound` (`DeleteUser.cs:34-35`). La traducción es correcta: el cliente de `DELETE /users/{id}` ve un error de Users. Si otra feature necesitara reconocer un error de Auth, compararía `ErrorType`, como ya hace `DeleteUser`. No necesita el código.
- `Shared/Http/HttpErrors.cs` (nuevo) junta lo que no es de ninguna feature: `Validation.Failed` (400 con `errors`), `Http.NotFound`, `Http.MethodNotAllowed`, `Http.Unauthorized`, `Http.Forbidden`, `Http.Unexpected`. Son los códigos de respaldo para respuestas que no vienen de un handler.

### Estado actual frente a la propuesta

`grep "Error\." backend/Features` fuera de los `*Errors.cs` da 13 errores escritos en línea:

| Archivo | Hoy | Propuesta |
|---|---|---|
| `Tasks/CreateTask/CreateTask.cs:32`, `DeleteTask.cs:27`, `GetTask.cs:27`, `UpdateTask.cs:37` | `Error.Forbidden(code: "Task.Forbidden", description: "Access denied.")` ×4 | `TaskErrors.Forbidden` |
| `Tasks/ListTasks/ListTasks.cs:20,22` | `Error.Validation(code: "Page", …)`, `(code: "PageSize", …)` | `TaskErrors.InvalidPage`, `TaskErrors.InvalidPageSize` (código = campo) |
| `Users/GetUser/GetUser.cs:32`, `DeleteUser.cs:31` | `Error.Forbidden(code: "User.Forbidden", …)` ×2 | `UserErrors.Forbidden` |
| `Auth/RegisterUser/RegisterUser.cs:39,41,43` | `Error.Validation(code: "UserName" / "Email" / "Password", …)` | `AuthErrors.InvalidUserName`, `InvalidEmail`, `InvalidPassword` (código = campo) |
| `Auth/RegisterUser/RegisterUser.cs:82` | `Error.Validation(code: e.Code, …)` con el código de Identity | `AuthErrors.FromIdentity(IdentityError)` |
| `Auth/UserAccounts.cs:46` | `Error.Failure(code: e.Code, …)` con el código de Identity | `AuthErrors.AccountDeleteFailed(IdentityError)` o similar |

Además:

- `"Auth.AntiforgeryFailed"` está en línea en `SessionHttpPolicy.cs:54`. Pasa a `AuthErrors.AntiforgeryFailed`.
- `UserErrors.InvalidDisplayName` ya usa el campo como código (`"DisplayName"`). Es el precedente para los errores de validación del catálogo.
- `AuthErrors.InvalidSession` y `AuthErrors.SessionRevoked` no se usan en ningún lado (grep sin resultados fuera de `AuthErrors.cs`). Son código muerto. Se pueden reutilizar para la cookie (sección 5) o borrar.
- `Shared/Http/ValidationErrors.cs` traduce FluentValidation a ErrorOr con `code: f.PropertyName`. Es una traducción de borde y se queda en `Shared/Http`. La regla de Dune la exceptúa por nombre de archivo (`*Errors.cs`).

## 3. Un solo mapeo de `Error` a ProblemDetails

**Respuesta:** `ApiResults` decide el status con una tabla `ErrorType → status` y arma **un** `Results.Problem`. El `traceId` ya lo pone ASP.NET Core. El 500 deja de publicar el mensaje de la excepción.

### Lo que hace ASP.NET Core hoy

- `Results.Problem` pasa por `IProblemDetailsService` cuando está registrado ([ProblemHttpResult.cs, `ExecuteAsync`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Http/Http.Results/src/ProblemHttpResult.cs)). Si no puede escribir, cae a `WriteResultAsJsonAsync`.
- El escritor por defecto hace tres cosas en orden ([DefaultProblemDetailsWriter.cs, `WriteAsync`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Http/Http.Extensions/src/DefaultProblemDetailsWriter.cs)):
  1. `ProblemDetailsDefaults.Apply`: completa `type` y `title` según el status si faltan.
  2. `traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier` en `Extensions`.
  3. `CustomizeProblemDetails`, si está configurado.
- Experimento, la forma actual de `ApiResults` (404):

  ```json
  {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Task with ID 'x' not found.","status":404,"code":"Task.NotFound","traceId":"00-06d4b6bd…-75554fcb…-00"}
  ```

  El `traceId` tiene formato W3C `traceparent`. Es el mismo id que exporta OpenTelemetry: sirve para buscar el log de esa request. **No hace falta agregarlo.**
- Un `CustomizeProblemDetails` se aplica también a `Results.Problem` y `Results.ValidationProblem`. En el experimento, todas las respuestas salieron con la marca `"customized":true`.

### Forma propuesta de `ApiResults`

5 a 10 renglones sobre el archivo real (`backend/Shared/Http/ApiResults.cs`):

```csharp
public static IResult ToResult<T>(ErrorOr<T> result, Func<T, IResult> success) =>
    result.IsError ? Problem(result.Errors) : success(result.Value);

public static IResult Problem(List<Error> errors) =>
    errors.All(e => e.Type == ErrorType.Validation)
        ? Results.ValidationProblem(FieldErrors(errors), extensions: Code(HttpErrors.ValidationFailed))
        : Problem(errors[0]);

public static IResult Problem(Error error) =>
    Results.Problem(statusCode: StatusFor(error.Type), title: error.Description, extensions: Code(error.Code));
```

- `StatusFor` es la tabla de `conventions.md:66`: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, Failure y Unexpected 500.
- `Results.ValidationProblem` acepta `extensions`. El experimento lo confirmó: salió `"errors":{"Title":["x"]},"code":"Validation.Failed","traceId":"…"`.
- `title: error.Description` mantiene el contrato de hoy (ver "title y detail" en la Conclusión).
- `Problem(Error)` es público para que lo usen la cookie y el antiforgery (sección 5). Así hay un solo lugar que convierte un `Error` en HTTP.
- Trampa: un `Error.Validation` suelto que pasa por `Problem(Error)` sale como 400 sin `errors`. Eso es lo que quiere el antiforgery (no es un error de campo). Un error de validación de una slice siempre pasa por `Problem(List<Error>)` y sale con `errors`, aunque sea uno solo (como `Page` hoy).
- Con errores mezclados (por ejemplo `RegisterUser` con un Conflict y un Validation de Identity), gana el primero. Es lo que hace hoy. El RFC lo respalda: "the most relevant or urgent problem be represented in the response" ([RFC 9457 §3](https://www.rfc-editor.org/rfc/rfc9457.html#section-3)).

### 500

- Hoy (`GlobalExceptionHandler.cs:27-29`): `Type = exception.GetType().Name`, `Detail = exception.Message`. Experimento: `{"type":"InvalidOperationException","title":"An error ocurred","status":500,"detail":"secret connection string Host=db;Password=x",…}`.
- Choca con `conventions.md:66` ("Un 500 no publica detalles internos"). El RFC dice: evitar exponer "implementation details such as a stack dump" ([RFC 9457 §5](https://www.rfc-editor.org/rfc/rfc9457.html#section-5)). Learn dice: "Don't share detailed exception information publicly when the app runs in production" ([Learn, Developer Exception Page](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0#developer-exception-page)).
- El frontend lo muestra: `default: return detail ?? 'Error inesperado del servidor…'` (`http-error.ts:49`). Hoy, ante una excepción, el usuario ve su mensaje interno.
- Propuesta: el handler deja `Type` y `Title` a los valores por defecto, no pone `Detail` y agrega `code = HttpErrors.Unexpected` ("Http.Unexpected"). La excepción ya llega al log: `ExceptionHandlerMiddleware` la registra antes de llamar al handler [INFERENCIA; sin verificar en el código de ASP.NET Core para este informe]. El `traceId` une la respuesta con el log.
- Un `Error.Failure` de una slice (por ejemplo `AuthErrors.ProfileMissing`) también da 500. Ese sí lleva su `code` (`Auth.ProfileMissing`): es un fallo conocido, no una excepción.

## 4. Errores de validación (400)

**Respuesta:** mantener el diccionario `errors` por campo y sumar un `code` de nivel superior. No agregar códigos por campo en este paso.

- ASP.NET Core arma `HttpValidationProblemDetails` con `errors` como diccionario campo → mensajes. Es lo que devuelve `Results.ValidationProblem` (experimento: `"errors":{"Title":["Title is required."]}`).
- El RFC muestra otra forma en su ejemplo: un arreglo `errors` con `detail` y `pointer` (JSON Pointer al campo) ([RFC 9457 §3](https://www.rfc-editor.org/rfc/rfc9457.html#section-3)). Es un ejemplo, no una regla.
- Azure propone `details` como arreglo de errores con su `code` y su `target` ([#rest-error-response-body-structure](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md#rest-error-response-body-structure)). Google pide un `ErrorInfo` con `reason` en toda respuesta de error ([AIP-193](https://google.aip.dev/193)).
- FluentValidation ya tiene un código por regla: `ErrorCode`, por defecto el nombre del validador (`NotNullValidator`), y se cambia con `WithErrorCode` ([FluentValidation, Error Codes](https://docs.fluentvalidation.net/en/latest/error-codes.html)). `ValidationErrors.From` hoy lo descarta y usa `PropertyName`.

Comparación:

| Opción | Contrato | Quién lo usa |
|---|---|---|
| A. `errors` por campo + `code: "Validation.Failed"` (recomendada) | Agrega un campo. `errors` no cambia | El frontend lee `Object.values(problem.errors)` (`http-error.ts:20`); el test de registro afirma `errors.DisplayName` (`RegisterUserTests.cs:58`) |
| B. Además, códigos por campo (otro diccionario o arreglo) | Agrega un campo nuevo con forma propia | Nadie hoy. El frontend valida en el cliente con Signal Forms y muestra los mensajes del servidor tal cual |
| C. Cambiar `errors` a arreglo con `pointer` (forma del RFC) | Rompe `errors` | Rompe el frontend y el test |

Se recomienda A: cumple el "Verifica" del plan (un `code` estable en el 400) sin tocar lo que ya se consume. B tiene sentido cuando el frontend traduzca por código. C no aporta nada que alguien use.

Detalles:

- Las claves de `errors` salen en PascalCase (`"Title"`, `"DisplayName"`), aunque el JSON es camelCase (`conventions.md:67`). Cambiarlas rompe el contrato. Se anota como pendiente, fuera de este paso.
- `RegisterUser.cs:82` usa el código de Identity como clave de `errors` (por ejemplo `PasswordTooShort`). Hoy no se ve: el largo se valida antes, y `AuthFeature.cs:39-43` relaja las demás reglas de contraseña. Al moverlo a `AuthErrors.FromIdentity`, conviene usar el campo como clave (`Password`, `UserName`) para que siga la regla "código = campo".

## 5. 401/403 fuera de los handlers, y 404 sin cuerpo

**Respuesta:** sí, deben ser ProblemDetails con `code`. Se puede de dos formas, y conviene usar las dos: la cookie escribe su ProblemDetails con un código de Auth, y `UseStatusCodePages` cubre todo lo demás que hoy sale vacío.

### Lo que sale hoy

Experimento, misma configuración del repo (cookie con `Respond` que solo pone el status, versionado por URL):

| Request | Status | Body |
|---|---|---|
| `GET /api/v1/secure` sin cookie | 401 | vacío (`Content-Length: 0`) |
| `GET /api/v9/nf` | 404 | vacío |
| `GET /api/nf` | 404 | vacío |
| `GET /nope` | 404 | vacío |

En el repo: `SessionHttpPolicy.cs:32-33` y `Respond` (`:110`); `ApiVersioningTests.UnknownOrMissingVersion_Returns404_WithoutBody` afirma el cuerpo vacío (`ApiVersioningTests.cs:34-41`); `decisions.tsv:115` lo dejó para este paso.

### Opción 1: la cookie escribe el ProblemDetails

- .NET 10 ya responde 401/403 en vez de redirigir para endpoints de API. Pero "Only the status code changes": no agrega cuerpo ([Learn, breaking change de .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/10/cookie-authentication-api-endpoints)). Los eventos `OnRedirectToLogin` y `OnRedirectToAccessDenied` siguen siendo el lugar para escribirlo.
- Forma propuesta en `SessionHttpPolicy`:

  ```csharp
  options.Events.OnRedirectToLogin = context => ApiResults.Problem(AuthErrors.SessionRequired).ExecuteAsync(context.HttpContext);
  options.Events.OnRedirectToAccessDenied = context => ApiResults.Problem(AuthErrors.AccessDenied).ExecuteAsync(context.HttpContext);
  ```

  `Results.Problem` usa `IProblemDetailsService` por dentro (sección 3), así que no hace falta inyectarlo a mano.
- Experimento con el evento escribiendo por `IProblemDetailsService`:

  ```json
  {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Authentication required.","status":401,"code":"Auth.Unauthenticated","traceId":"00-776b7e91…-79c8c3fa…-00"}
  ```

- La cookie no distingue "sin cookie" de "sesión revocada o vencida": `ValidatePrincipalAsync` rechaza el principal y después llega el mismo challenge. Por eso un solo código para el 401: `Auth.SessionRequired`. Se puede reutilizar `AuthErrors.InvalidSession`, pero su nombre dice otra cosa. El nombre final es de Tobi.
- El antiforgery (`SessionHttpPolicy.cs:51-55`) pasa a `ApiResults.Problem(AuthErrors.AntiforgeryFailed).ExecuteAsync(http)`. `AntiforgeryFailed` es un `Error.Validation` con código `Auth.AntiforgeryFailed`. Sale igual que hoy: 400, sin `errors`, con el mismo `code`.

### Opción 2: `UseStatusCodePages`

- Con `AddProblemDetails()`, `StatusCodePagesMiddleware` "generates a problem details response by default" para respuestas 400-599 sin cuerpo ([Learn, Problem details](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0#problem-details)).
- Experimento con `app.UseStatusCodePages()` después de `UseExceptionHandler()`:

| Request | Status | Body |
|---|---|---|
| `GET /api/v1/secure` sin cookie | 401 | ProblemDetails, `title: "Unauthorized"`, sin `code` |
| `GET /api/v9/nf` | 404 | ProblemDetails, `title: "Not Found"`, sin `code`, sin `api-supported-versions` |
| `GET /api/nf`, `GET /nope` | 404 | ProblemDetails, igual |
| Respuestas con cuerpo (`Task.NotFound`, validación, 409) | igual que antes | sin cambios |

- Combinada con la opción 1, la cookie gana: su respuesta ya tiene cuerpo y `UseStatusCodePages` no la toca (experimento, modo `pdcookie`).
- El `code` que falta lo pone un `CustomizeProblemDetails` en `BackendInfrastructure.AddProblemDetails(...)`: si `Extensions` no tiene `code`, usa el de `HttpErrors` según el status (404 → `Http.NotFound`, 405 → `Http.MethodNotAllowed`, 401 → `Http.Unauthorized`…). Cubre también futuros 4xx vacíos (por ejemplo, un 415 por `Content-Type`).
- Trampa: `UseStatusCodePages` cambia **todos** los errores sin cuerpo, no solo `/api`. El frontend se sirve con nginx en otro contenedor (`frontend/nginx.conf`), así que la API no sirve páginas. [INFERENCIA por la estructura del repo]
- El rechazo del rate limiter de `/auth` (`AuthFeature.cs:82-90`) tampoco escribe cuerpo hoy [sin verificar en el experimento]. Con `UseStatusCodePages` pasaría a ProblemDetails. El status del rechazo es otro tema: queda como pendiente.

### Recomendación

Las dos. La opción 1 da un código de dominio a lo que el frontend sí mira (el 401 de sesión). La opción 2 cierra el hueco de `decisions.tsv:115` y garantiza que ningún error salga sin cuerpo. La opción 2 cambia el contrato de toda respuesta vacía y obliga a cambiar `ApiVersioningTests` a propósito: ⏸ **DECISIÓN DE TOBI**, con su línea en `decisions.tsv` (regla 9).

## 6. Cómo probar el contrato y la regla de Dune

### Tests

Hoy un solo test afirma un `code`: `IssueCsrfTokenTests.cs:128` (`Auth.AntiforgeryFailed`). Los demás afirman solo el status (`GetTaskTests.cs:51`, `DeleteTaskTests.cs:50`, `GetUserTests.cs:72`, `RegisterUserTests.cs:35`, `OpenSessionTests.cs:32`…).

Propuesta: un test transversal nuevo en `backend/Tests/Errors/ErrorContractTests.cs`, como `Tests/Versioning/ApiVersioningTests.cs`. Una fila por status, con el `code` exacto y la forma del cuerpo:

| Status | Request | `code` esperado | Además |
|---|---|---|---|
| 400 | `POST /api/v1/tasks` con título vacío | `Validation.Failed` | `errors.Title` existe |
| 400 | `POST` sin token antiforgery | `Auth.AntiforgeryFailed` | sin `errors` |
| 401 | `POST /api/v1/auth/login` con clave mala | `Auth.InvalidCredentials` | |
| 401 | `GET /api/v1/tasks` sin cookie | `Auth.SessionRequired` | antes: cuerpo vacío |
| 403 | `GET /api/v1/tasks/{id}` de otro usuario | `Task.Forbidden` | |
| 403 | `GET /api/v1/users/{id}` de otro usuario | `User.Forbidden` | |
| 404 | `GET /api/v1/tasks/{id}` inexistente | `Task.NotFound` | |
| 404 | `GET /api/v9/health/live` | `Http.NotFound` | solo si Tobi elige `UseStatusCodePages` |
| 409 | registro duplicado | `Auth.EmailAlreadyExists` o `Auth.UserNameAlreadyExists` | |
| 500 | endpoint que lanza (solo en el test) | `Http.Unexpected` | sin `detail`; `type` no es el nombre de la excepción |

- Todas las filas afirman `Content-Type: application/problem+json` y que existe `traceId`.
- El 500 necesita un endpoint que lance. Se puede agregar solo en la `WebApplicationFactory` del test con un `IStartupFilter`, sin tocar producción. [INFERENCIA; sin probar en el repo]
- Orden TDD: primero las filas que ya pasan (caracterización: `Task.NotFound`, `Auth.InvalidCredentials`, `Auth.EmailAlreadyExists`, `Task.Forbidden`). Después las que fallan en rojo hasta el cambio (`Validation.Failed`, `Auth.SessionRequired`, `Http.Unexpected`).
- Smoke (`.agents/skills/verify-template/scripts/smoke-api.mjs`): ya guarda `code` en lo observado (`:64`) y afirma `Auth.AntiforgeryFailed` (`:149`, `:158`). Se suman aserciones en las líneas que hoy solo esperan el status: 401 (`:146`), 409 (`:155`), 400 (`:169`, `:172`), 403 (`:170`, `:192-193`), 404 (`:195`) y, si se elige, `../v9` (`:144`).

### Regla de Dune: DUNE0013 "Error fuera del catálogo"

**Factible.** Usa la misma técnica que DUNE0011 y DUNE0012: `RegisterOperationAction(OperationKind.Invocation)`, el símbolo del método invocado y la ruta del archivo (`DunePaths`). La próxima id libre es DUNE0013 (`DuneDiagnostics.cs:107` es la 0012).

Qué detecta:

1. Una invocación a un método estático de `ErrorOr.Error` (`Validation`, `NotFound`, `Forbidden`, `Unauthorized`, `Conflict`, `Failure`, `Unexpected`, `Custom`) en un archivo del backend que no termina en `Errors.cs`. Se excluyen tests y migraciones, como en `ApiRouteAnalyzer.cs:38-41`.
2. Opcional: en un `<X>Errors.cs` de `backend/Features/`, si el argumento `code` es una constante, debe empezar con `<X>.`. La excepción son los `Error.Validation`, cuyo código es el nombre del campo. Si el código no es constante (las traducciones de Identity), la regla 2 no lo mira.
3. Opcional: un `ExtensionData` con clave `"code"` escrito como literal fuera de `Shared/Http` (cubre `new Dictionary<string, object?> { ["code"] = "Auth.AntiforgeryFailed" }`). Con la regla 1 y `ApiResults.Problem(Error)`, este caso ya no tiene motivo para existir.

Por qué "solo en `*Errors.cs`" y no "sin literal en `code:`": la versión con literal no ve `Error.Validation(code: e.Code, …)` (`RegisterUser.cs:82`) ni `Error.Failure(code: e.Code, …)` (`UserAccounts.cs:46`). Esos también arman códigos dentro del caso de uso. La versión por archivo es más simple y no tiene huecos.

Costo: 13 llamadas en línea y el literal del antiforgery se mueven a los catálogos (tabla de la sección 2). `Shared/Http/ValidationErrors.cs` pasa sin cambios: su nombre termina en `Errors.cs`.

Stubs del test: `Dune.Analyzers.Tests` no referencia ErrorOr (`Dune.Analyzers.Tests.csproj:12-26`). Hay dos caminos: un stub como en `VersionedApiAnalyzerTests.cs:11-37`, o agregar la referencia al paquete. El stub alcanza:

```csharp
namespace ErrorOr
{
    using System.Collections.Generic;
    public readonly record struct Error
    {
        public string Code { get; init; }
        public static Error Forbidden(string code = "General.Forbidden", string description = "", Dictionary<string, object>? metadata = null) => default;
        public static Error Validation(string code = "General.Validation", string description = "", Dictionary<string, object>? metadata = null) => default;
    }
}
```

Fixture válida (cero diagnósticos): `backend/Features/Tasks/TaskErrors.cs` con `public static readonly Error Forbidden = Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");` y `backend/Features/Tasks/GetTask/GetTask.cs` que devuelve `TaskErrors.Forbidden`.

Violación deliberada (un diagnóstico por línea marcada):

- `backend/Features/Tasks/GetTask/GetTask.cs`: `return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");` → regla 1.
- `backend/Features/Auth/RegisterUser/RegisterUser.cs`: `errors.Add(Error.Validation(code: e.Code, description: e.Description));` → regla 1, aunque el código no sea literal.
- `backend/Features/Tasks/TaskErrors.cs`: `Error.NotFound(code: "User.NotFound", …)` → regla 2 (prefijo ajeno).

Va con su línea en `decisions.tsv` (reglas 4 y 9 de AGENTS.md).

Lo que la regla no cubre: que dos errores distintos compartan código, o que alguien cambie el texto de un código. Lo primero se puede probar con un test de reflexión sobre las propiedades `static readonly Error` de los `*Errors` (los métodos con parámetros, como `TaskErrors.NotFound(Guid)`, se invocan con un valor cualquiera). Lo segundo lo cubre el test de contrato: cambiar `Task.NotFound` lo pone en rojo.

## 7. El frontend y el `code`

**Respuesta:** confirmado, el frontend no lee `code`. Lee `status`, `detail` y `errors`.

- `frontend/src/app/core/problem-details.ts`: la interfaz tiene `title`, `detail` y `errors`. No tiene `code`.
- `frontend/src/app/core/http-error.ts`: decide por `error.status`. Muestra los mensajes de `errors` en 400 (`:20`), `detail` o un texto fijo en 401, 409 y 500 (`:41-49`), y siempre un texto fijo en 403 y 404 (`:43-45`).
- `grep "code"` en `frontend/src` sin specs: sin resultados que lean el campo.
- e2e: solo `frontend/e2e/session.spec.ts:82` afirma un texto de error ("El recurso solicitado no existe o ya fue eliminado.", 404, texto fijo). No cambia.

Consecuencias para este paso:

- Agregar `code` y `traceId` no cambia nada en el frontend.
- Quitar `detail` del 500 cambia lo que ve el usuario, para bien: pasa del mensaje interno al texto fijo "Error inesperado del servidor. Inténtalo más tarde.". Conviene un spec de Vitest en `http-error.spec.ts` que fije ese caso (ya existe uno parecido: "prefers the detail for 401, 409 and 500").
- Mover la descripción de `title` a `detail` sí cambia el texto visible en 401, 409 y 500 (sección "title y detail"). Por eso no se recomienda en este paso.

## Cambios de contrato HTTP (regla 3 de AGENTS.md)

| Cambio | ¿Rompe? | Backend | Frontend | Tests |
|---|---|---|---|---|
| `code: "Validation.Failed"` en el 400 de validación | No, agrega un campo | `ApiResults` | sin cambios | test de contrato, smoke |
| 401/403 de la cookie con ProblemDetails y `code` | Agrega cuerpo donde no había | `SessionHttpPolicy` | sin cambios (decide por status) | test de contrato, smoke |
| 500 sin `detail`, sin nombre de excepción, con `code` | Quita información filtrada | `GlobalExceptionHandler` | el usuario ve el texto fijo; spec de Vitest | test de contrato |
| `UseStatusCodePages` (404 de ruta y de versión, otros vacíos) | Agrega cuerpo a todo error vacío | `Program.cs`, `BackendInfrastructure` | sin cambios | `ApiVersioningTests` cambia a propósito, smoke `:144` |
| Descripción de `title` a `detail` | Sí, cambia el texto visible | `ApiResults` | `http-error.ts` y sus specs | todos los que lean `title` |
| Códigos existentes (`Task.NotFound`, `Auth.*`…) | No cambian | | | |

## Choques con los docs del repo

Se listan sin resolver. La regla 10 de AGENTS.md pide preguntarle a Tobi.

1. **`conventions.md:66`** dice "Un 500 no publica detalles internos". El código lo viola hoy (`GlobalExceptionHandler.cs:27-29`). No choca con la propuesta: la propuesta cumple el doc. Se anota porque el doc ya era falso.
2. **`decisions.tsv:115`** dice que `/api/v9/...` responde 404 sin cuerpo, "va en el catálogo de errores del paso 6". Adoptar `UseStatusCodePages` reemplaza esa decisión: necesita una línea nueva en `decisions.tsv` y cambiar `ApiVersioningTests` a propósito (regla 9).
3. **`plan.md:254`** dice "Cambia: `Shared/Http`, catálogos por feature, tests de estados y payloads". No nombra `Infrastructure` (`GlobalExceptionHandler`, `AddProblemDetails`), `Program.cs` (`UseStatusCodePages`), `Features/Auth/SessionHttpPolicy.cs` ni Dune. El arreglo del 500 y la cookie caben en "payloads"; Dune lo pide la regla 4 de AGENTS.md. `UseStatusCodePages` es más dudoso: puede ir en este paso o en uno propio.
4. **RFC 9457 §3.1.3 frente al contrato actual.** El `title` lleva hoy texto variable por caso. Cumplir el RFC cambia lo que ve el usuario. El plan no lo pide y la regla 3 obligaría a tocar el frontend.
5. **Azure `#rest-add-codes-in-new-api-version`** pide no agregar códigos sin subir de versión. Este paso agrega códigos en v1. Es una guía para servicios públicos con muchos clientes; este repo tiene un cliente propio. Lo decide Tobi.
6. **`conventions.md:66`** mapea "Unexpected, 500" pero no nombra `Failure`. El código manda `Failure` a 500 por la rama `_` (`ApiResults.cs:32`), y `AuthErrors.ProfileMissing` es un `Failure`. Conviene que el doc lo diga.

## Decisiones abiertas para Tobi

1. ⏸ **`UseStatusCodePages`:** ¿todo error sin cuerpo pasa a ProblemDetails con `code` (`Http.NotFound`…), en este paso? Cambia el contrato de las respuestas vacías y `decisions.tsv:115`.
2. ⏸ **`title` y `detail`:** ¿se deja la descripción en `title` (recomendado para este paso) o se mueve a `detail` junto con el frontend?
3. **Nombres de los códigos nuevos:** `Validation.Failed`, `Auth.SessionRequired`, `Auth.AccessDenied`, `Http.NotFound`, `Http.Unexpected`. ¿Se reutiliza `AuthErrors.InvalidSession` para el 401 de la cookie o se borran `InvalidSession` y `SessionRevoked`, que no se usan?
4. **`type` propio por código** (opción B de la sección 1): ¿ahora, después o nunca?
5. **Alcance de DUNE0013:** ¿solo la regla 1 (factories solo en `*Errors.cs`) o también la 2 (prefijo = clase)?

## Fuentes

Especificaciones y guías:

- https://www.rfc-editor.org/rfc/rfc9457.html (Problem Details; §3, §3.1.1, §3.1.3, §3.1.4, §3.2, §4, §4.2.1, §5)
- https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md (anclas `#rest-error-code-header`, `#rest-error-code-header-and-body-match`, `#rest-error-code-enum`, `#rest-add-codes-in-new-api-version`, `#rest-document-error-code-values`, `#rest-error-response-body-structure`, `#rest-error-non-api-contract-fields`, `#rest-error-use-default-response`)
- https://google.aip.dev/193 (ErrorInfo, reason y domain)

Microsoft Learn:

- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/10/cookie-authentication-api-endpoints

Código de ASP.NET Core (rama `release/10.0`):

- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Http/Http.Results/src/ProblemHttpResult.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Http/Http.Extensions/src/DefaultProblemDetailsWriter.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Shared/ProblemDetails/ProblemDetailsDefaults.cs

Librerías:

- https://github.com/amantinband/error-or#organizing-errors (README de ErrorOr)
- https://github.com/amantinband/error-or/blob/main/src/Errors/Error.cs (factories y códigos por defecto)
- https://docs.fluentvalidation.net/en/latest/error-codes.html

Repo (leídos para fijar el estado actual): `backend/Shared/Http/ApiResults.cs`, `backend/Shared/Http/ValidationErrors.cs`, `backend/Infrastructure/Middlewares/GlobalExceptionHandler.cs`, `backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs`, `backend/Program.cs`, `backend/Features/Auth/SessionHttpPolicy.cs`, `backend/Features/Auth/AuthFeature.cs`, `backend/Features/Auth/AuthErrors.cs`, `backend/Features/Auth/UserAccounts.cs`, `backend/Features/Auth/RegisterUser/RegisterUser.cs`, `backend/Features/Tasks/TaskErrors.cs`, `backend/Features/Tasks/*/*.cs`, `backend/Features/Users/UserErrors.cs`, `backend/Features/Users/*/*.cs`, `backend/Tests/Versioning/ApiVersioningTests.cs`, `backend/Features/**/*Tests.cs`, `.agents/skills/verify-template/scripts/smoke-api.mjs`, `frontend/src/app/core/http-error.ts`, `frontend/src/app/core/http-error.spec.ts`, `frontend/src/app/core/problem-details.ts`, `frontend/src/app/features/auth/login/login.ts`, `frontend/e2e/session.spec.ts`, `dune/Dune.Analyzers/ApiRouteAnalyzer.cs`, `dune/Dune.Analyzers/DunePaths.cs`, `dune/Dune.Analyzers/DuneDiagnostics.cs`, `dune/Dune.Analyzers.Tests/VersionedApiAnalyzerTests.cs`, `docs/adr/0004-vertical-slices-deep-modules.md`, `docs/architecture.md`, `docs/conventions.md`, `docs/decisions.tsv`, `docs/evolucion/plan.md`, `docs/evolucion/progreso.md`.
