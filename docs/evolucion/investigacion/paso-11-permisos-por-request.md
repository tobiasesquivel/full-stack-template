# Paso 11: permisos memorizados por request (investigación)

- Fecha: 2026-11-16.
- Alcance: el paso 11 del [plan](../plan.md) (`plan.md:316-319`). Hoy cada comprobación de permisos vuelve a leer Identity. El paso guarda el resultado mientras dura el request, y solo ese tiempo. El plan pide cambiar `AccessPolicy` en `Features/Auth/Access` (que vive detrás de `IAuthModule` desde el paso 6.3) y sumar tests. El test del plan cuenta las consultas de permisos en un request con varias comprobaciones, y comprueba que un permiso cambiado se aplica en el request siguiente.
- Decisiones que ya están tomadas y que este informe no reabre: ASP.NET Core Identity con `AddIdentityCore` + `AddRoles` + `AddEntityFrameworkStores<AppDbContext>` (`Features/Auth/AuthFeature.cs:35-49`); los permisos son claims `permission` de los roles (`Features/Auth/Access/RolePermissions.cs:8`); `IAuthModule` es la única puerta y `AccessPolicy` queda interno, sin interfaz (`docs/decisions.tsv:138`); la sesión lleva solo identidad, y roles y permisos "se consultan en cada request" (`docs/architecture.md:72`, `docs/conventions.md:97`, `docs/decisions.tsv:89`); la caché entre requests entra recién en el paso 33 (`docs/conventions.md:92`, `plan.md:439-441`); un solo `AppDbContext` hasta el paso 22.
- Método: solo fuentes primarias. Son Microsoft Learn (ASP.NET Core 10, EF Core 10 y la inyección de dependencias de .NET), el código de `dotnet/aspnetcore` en la rama `release/10.0` (Identity) y de `dotnet/runtime` en `release/10.0` (Hosting), la documentación de xUnit y el OWASP Authorization Cheat Sheet. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA].
- Evidencia propia: un experimento descartable fuera del repo.
  - "Experimento": una app .NET 10 de un solo archivo (`dotnet run perms.cs`, SDK 10.0.401) en el directorio temporal, con `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12 y `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (las versiones del repo, `backend/TemplateApp.csproj:30,35`), contra un contenedor `postgres:18` descartable. Registra Identity igual que el repo (`AddIdentityCore().AddRoles().AddEntityFrameworkStores()`), copia el `GetPermissionsAsync` de hoy línea por línea y suma una versión con memo. Cuenta los comandos SQL con un `DbCommandInterceptor`. Corre 9 casos (E1 a E9). Lo corrí dos veces con el mismo resultado. Después borré el contenedor.
  - No se tocó código del repo.

## Glosario corto

- **Memo** (memoización): guardar el resultado de una función la primera vez que se calcula y devolver ese resultado guardado en las llamadas siguientes con los mismos datos de entrada. Acá = una caché que vive solo lo que vive el request.
- **Caché entre requests**: un almacén que sobrevive al request, como `IMemoryCache` o `HybridCache`. Es lo que el paso 33 trae y lo que este paso **no** usa.
- **Scope** (alcance de DI): el contenedor de servicios crea una instancia nueva de cada servicio *scoped* por scope. ASP.NET Core abre un scope por request.
- **Captive dependency** (dependencia cautiva): un servicio de vida larga (singleton) que guarda dentro uno de vida corta (scoped). El corto pasa a vivir lo que vive el largo.
- **Identity**: ASP.NET Core Identity. `UserManager` y `RoleManager` son sus fachadas; `UserStore` y `RoleStore` son los que hablan con EF Core.
- **Comando SQL**: cada consulta o escritura que EF manda a PostgreSQL. Un `DbCommandInterceptor` (interceptor de comandos) es una clase que EF llama antes y después de cada uno ([Learn, Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)).
- **Probe** (sonda): un endpoint que existe solo en los tests, como `ErrorProbe` (`backend/Tests/Support/ErrorProbe.cs:8-19`).

## Conclusión

- **Dónde vive el memo:** en un campo del propio `AccessPolicy`, que ya es scoped (`AuthFeature.cs:19`). ASP.NET Core crea un scope por request y lo descarta al terminar, entonces el memo muere con el request sin código extra. `HttpContext.Items` da el mismo alcance pero pide `IHttpContextAccessor` y no existe fuera de HTTP. `IClaimsTransformation` puede correr varias veces por request y solo ve al usuario que llama. `IMemoryCache`/`HybridCache` sobreviven al request: el plan los prohíbe para esto y el paso 33 los trae.
- **Qué se memoriza y con qué clave:** roles **y** permisos, por `userId` (comparación ordinal). Así `DescribeAsync` deja de leer los roles dos veces (`AuthModule.cs:34-35`). También se memoriza el resultado vacío de un usuario que no existe: dentro de un request ese dato no cambia salvo que el mismo request lo borre.
- **Cuántos comandos hoy:** `GetPermissionsAsync` manda `2 + 2N` comandos para un usuario con N roles (experimento E1: 2, 4, 6 y 8 comandos para 0, 1, 2 y 3 roles; 1 si el usuario no existe). Identity no cachea nada: cada llamada vuelve a la base. Con tres comprobaciones en el mismo request, un usuario con 2 roles pasa de 18 comandos a 6 (E3).
- **Hallazgo para el plan:** hoy **ningún endpoint** comprueba dos veces los permisos del mismo usuario en un request. Cada slice hace una sola comprobación (`CreateTask.cs:31`, `GetTask.cs:26`, `UpdateTask.cs:36`, `DeleteTask.cs:26`, `GetUser.cs:29`, `DeleteUser.cs:28`). La única repetición real es la de roles en `DescribeAsync`. Por eso el test de "varias comprobaciones" necesita una sonda en los tests, como `ErrorProbe`. El beneficio de hoy es chico; el paso deja el mecanismo listo para los endpoints que comprueben más de una vez.
- **Seguridad del alcance:** un scoped es uno por request y uno por `CreateScope()` en los seeders y en los tests. `DbContext` no admite operaciones en paralelo (E6 lo confirmó), así que un `Dictionary` común alcanza; no hace falta `ConcurrentDictionary`.
- **Desactualización dentro del request:** si un request cambia roles y después vuelve a preguntar, ve el valor viejo (E4). Hoy ningún request hace eso: `RegisterUser` agrega el rol **antes** de la primera lectura (`RegisterUser.cs:53`, `:65`) y `DeleteUser` no vuelve a leer después de borrar (`DeleteUser.cs:28-31`). Recomiendo documentarlo, sin método de invalidación.
- **Cómo contar en el test:** un `DbCommandInterceptor` de test, sumado con `ConfigureDbContext<AppDbContext>` solo en una fábrica aislada (`CreateIsolatedClient`). Cuenta los comandos que tocan tablas `AspNet*` entre el inicio y el fin del request medido. No hace falta `AsyncLocal`: la fábrica aislada solo atiende a ese test. Sin paquetes nuevos.
- **"Se aplica en el request siguiente":** ya lo prueba `GetTaskTests.ForeignTask_IsForbidden_Then_AdminSeesIt_Then_RevokeHidesItAgain` (`Features/Tasks/GetTask/GetTaskTests.cs:35-70`): 403, se da `Admin`, 200, se quita, 403. Tiene que seguir en verde. Recomiendo un test nuevo con la sonda: cuenta, cambia el rol en otro scope y vuelve a preguntar en un request nuevo.
- **Herramientas:** ninguna. Todo está en EF Core y en ASP.NET Core, que el repo ya usa. No hay decisión de Tobi por regla 5.
- **Dune:** no recomiendo un analyzer para "`AccessPolicy` es scoped". Ya lo cuidan dos redes: la validación de scopes del host en Development y el test de revocación. Si el paso escribe en los docs la regla "los permisos no se guardan entre requests", la forma ejecutable es extender `check-structure.mjs` para rechazar las APIs de caché (`Microsoft.Extensions.Caching.*`, `AddMemoryCache`, `AddHybridCache`…) hasta el paso 33, como ya hace con bus y mediator. Hoy no tiene violaciones.
- **Fuera de alcance:** una sola consulta con join (`AspNetUserRoles` + `AspNetRoleClaims`) baja todo a 1 comando (E5), pero es otro concepto (forma de la consulta). Queda como pendiente.

## 1. Dónde vive el memo

**Respuesta:** en un campo de `AccessPolicy`. Es la única opción que da exactamente el alcance del request, funciona igual fuera de HTTP y no agrega piezas.

**Qué garantiza un servicio scoped**

- "For web applications, a *scoped* lifetime indicates that services are created once per client request (connection). In apps that process requests, scoped services are disposed at the end of the request" ([Learn, Service lifetimes](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes)).
- "The framework creates a scope per request, and `RequestServices` exposes the scoped service provider. All scoped services are valid for as long as the request is active" ([Learn, DI en ASP.NET Core, Request Services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)).
- En el repo, todo el request comparte una sola instancia: `AccessPolicy`, `AuthModule` e `IAuthModule` son scoped, y la puerta reusa la instancia de `AuthModule` (`AuthFeature.cs:19-21`). `UserManager` y `RoleManager` también son scoped ([IdentityServiceCollectionExtensions.cs:49](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/IdentityServiceCollectionExtensions.cs), [IdentityBuilder.cs:199](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/IdentityBuilder.cs)), igual que los stores ([IdentityEntityFrameworkBuilderExtensions.cs:71-72](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/IdentityEntityFrameworkBuilderExtensions.cs)) y el `AppDbContext` ([Learn, Service lifetimes](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes), nota sobre `AddDbContext`).
- Experimento E8: dentro de un scope, `GetRequiredService<MemoPolicy>()` dos veces dio la misma instancia; entre dos scopes, instancias distintas.

**Las cuatro opciones**

- `HttpContext.Items`: "used to store data while processing a single request. The collection's contents are discarded after a request is processed" ([Learn, Session and state, HttpContext.Items](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/app-state?view=aspnetcore-10.0)). Mismo alcance que el scoped. Pero `AccessPolicy` tendría que leer `IHttpContextAccessor`, y Microsoft dice "Avoid static access to HttpContext (for example, IHttpContextAccessor.HttpContext)" ([Learn, DI en ASP.NET Core, Recommendations](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)). Además `HttpContext` es `null` fuera de un request ([Learn, Use HttpContext](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/use-http-context?view=aspnetcore-10.0)): un seeder o un test con `CreateScope()` necesitaría otro camino.
- `IClaimsTransformation`: "`TransformAsync` can be called multiple times for a single request when authentication is evaluated more than once" y "Ensure transformations are idempotent" ([Learn, Claims](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/claims?view=aspnetcore-10.0)). Entonces igual necesita un memo por request para no repetir consultas. Corre en **todo** request autenticado, también en los que no comprueban nada, como `ListTasks`. Solo agrega claims del usuario que llama; `GetUser` también pide los permisos del usuario leído (`GetUser.cs:32`). Y pone los permisos dentro del `ClaimsPrincipal`, cerca de lo que `conventions.md:97` quiere evitar ("Roles y permisos no van en la sesión"). [INFERENCIA sobre el costo en `ListTasks`]
- `IMemoryCache` o `HybridCache` con un tiempo de vida: el valor sobrevive al request. Después de quitar un rol, el permiso viejo sigue hasta que vence la entrada o hasta que alguien la borra (`RemoveAsync`), y con varias instancias "the in-memory cache in other servers isn't affected" ([Learn, HybridCache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0)). `HybridCache` pide el paquete `Microsoft.Extensions.Caching.Hybrid` (misma fuente). OWASP pide "Permission should be validated correctly on every request" ([OWASP Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html), "Validate the Permissions on Every Request"). El plan lo prohíbe ("el alcance permitido es *solo* dentro del request") y `conventions.md:92` deja la caché para el paso 33.

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Campo en `AccessPolicy` (scoped) | Alcance exacto del request sin código extra. Funciona igual en seeders y tests con `CreateScope()`. Queda dentro de Auth, detrás de `IAuthModule`. | Si alguien registra `AccessPolicy` como singleton, el memo vive toda la app (sección 2). |
| B. `HttpContext.Items` | Mismo alcance. | Estado ambiental (`IHttpContextAccessor`), `null` fuera de HTTP, claves sin tipo. |
| C. `IClaimsTransformation` | Los permisos quedan en `User` para `[Authorize(Policy=…)]`. | Puede correr varias veces por request; corre aunque no haga falta; solo cubre al que llama; acerca permisos a la sesión. |
| D. `IMemoryCache`/`HybridCache` con TTL | Ahorra consultas entre requests. | Permisos revocados que siguen vigentes; invalidación entre instancias; el paso 33 y, para `HybridCache`, un paquete (regla 5). |

Recomendación: **A**.

## 2. ¿Un scoped es siempre uno por request?

**Respuesta:** sí en HTTP, y uno por `CreateScope()` fuera de HTTP. El riesgo real es registrar `AccessPolicy` como singleton; dos redes que ya existen lo frenan.

- Fuera de HTTP: "A scoped service should always be used from within a scope–either an implicit scope (such as ASP.NET Core's per-request scope) or an explicit scope created with IServiceScopeFactory.CreateScope()" ([Learn, Service lifetimes](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes)). En el repo, los tests abren un scope por operación (`TestAccounts.SetRoleAsync`, `backend/Tests/Support/TestAccounts.cs:86-96`; `AuditLog`, `AuditLog.cs:9-11`). El arranque usa un scope propio para migrar y sembrar (`Infrastructure/Persistence/DatabaseInitialization.cs:12-13`, que llama a `DatabaseInitializer.InitializeAsync`).
- Singleton por error: "Do ***not*** resolve a scoped service directly from a singleton [...] Doing so causes the scoped service to behave like a singleton, which can lead to incorrect state when processing subsequent requests" (misma fuente).
- Primera red: en Development el host valida scopes. El código del host pone `ValidateScopes = isDevelopment` y `ValidateOnBuild = isDevelopment` ([HostingHostBuilderExtensions.cs:336-341](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/Microsoft.Extensions.Hosting/src/HostingHostBuilderExtensions.cs)). `AccessPolicy` depende de `UserManager` y `RoleManager`, que son scoped (`AccessPolicy.cs:14`), entonces registrarlo como singleton haría fallar el arranque en Development. E8 lo mostró: con `ValidateScopes`, resolverlo desde la raíz lanza "Cannot resolve scoped service 'MemoPolicy' from root provider"; sin validación, la raíz devuelve siempre la misma instancia. `verify-template` arranca en Development (`.agents/skills/verify-template/scripts/verify.mjs:1148`). [INFERENCIA: no registré `AccessPolicy` como singleton en el repo]
- Segunda red: los tests corren en el entorno `Testing` (`backend/Tests/Support/ApiFixture.cs:68`), **sin** esa validación. Ahí el singleton pasaría el arranque, pero el memo viviría toda la app y `GetTaskTests.cs:57-58` (200 después de dar `Admin`) fallaría porque `bob` ya quedó memorizado sin permiso. [INFERENCIA]
- Paralelismo: "Entity Framework Core does not support multiple parallel operations being run on the same DbContext instance" y "always `await` async calls immediately" ([Learn, DbContext Lifetime](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/), "Avoiding DbContext threading issues"). La misma página explica que en ASP.NET Core "there is only one thread executing each client request at a given time". E6: dos `GetPermissionsAsync` en paralelo en el mismo scope dieron `InvalidOperationException: A second operation was started on this context instance before a previous operation completed`. Como el memo comparte scope con ese `DbContext`, dos lecturas simultáneas ya fallan antes de tocar el diccionario. Por eso alcanza un `Dictionary<string, …>` simple. [INFERENCIA]

## 3. Qué hace Identity hoy y cuántos comandos manda

**Respuesta:** Identity no cachea. `GetPermissionsAsync` manda `2 + 2N` comandos; `DescribeAsync` suma uno más por leer los roles otra vez.

**El camino de `GetPermissionsAsync`** (`AccessPolicy.cs:20-46`)

| Línea del repo | Llamada | Qué hace Identity | Comandos |
|---|---|---|---|
| `AccessPolicy.cs:22-24` | `_users.Users.AsNoTracking().SingleOrDefaultAsync(...)` | Consulta `AspNetUsers` | 1 |
| `AccessPolicy.cs:28` | `UserManager.GetRolesAsync(user)` | `UserStore.GetRolesAsync`: join de `UserRoles` con `Roles` filtrado por `user.Id`, `ToListAsync` ([UserStore.cs:408-419](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/UserStore.cs)) | 1 |
| `AccessPolicy.cs:33` | `RoleManager.FindByNameAsync(roleName)` por rol | Normaliza el nombre ([RoleManager.cs:306-312](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/RoleManager.cs)) y hace `Roles.FirstOrDefaultAsync(r => r.NormalizedName == ...)` ([RoleStore.cs:280-284](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/RoleStore.cs)) | N |
| `AccessPolicy.cs:37` | `RoleManager.GetClaimsAsync(role)` por rol | `RoleClaims.Where(rc => rc.RoleId.Equals(role.Id))...ToListAsync` ([RoleStore.cs:336-341](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/RoleStore.cs)) | N |

- Busqué "cache" en `UserManager.cs`, `RoleManager.cs`, `UserStore.cs` y `RoleStore.cs` de `release/10.0`: cero resultados. El único reuso posible es el del `DbContext`: `UserStore.FindByIdAsync` usa `FindAsync` ([UserStore.cs:264-269](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/UserStore.cs)), que devuelve la entidad ya rastreada "without making a request to the database" ([Learn, DbSet.FindAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbset-1.findasync?view=efcore-10.0)). `AccessPolicy` usa `AsNoTracking` y una consulta propia, entonces no aprovecha ese reuso.
- Cancelación: `UserManager` y `RoleManager` no reciben el `CancellationToken` del request; usan `CancellationToken.None` ([UserManager.cs:57](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/UserManager.cs), [RoleManager.cs:28](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/RoleManager.cs)). Por eso `AccessPolicy.cs:28,33,37` no pasa `ct`. El memo reduce esas llamadas que no se pueden cancelar.

**Experimento** (mismo código que `AccessPolicy.cs:20-46`)

| Caso | Qué medí | Resultado |
|---|---|---|
| E1 | `GetPermissionsAsync` para usuarios con 0, 1, 2 y 3 roles, y para un id inexistente | 2, 4, 6 y 8 comandos; 1 para el inexistente |
| E2 | Lo que hace `FindAccountAsync` + `DescribeAsync` (`AuthModule.cs:26-43`): `FindByIdAsync`, `GetRolesAsync` y `GetPermissionsAsync` | 6 comandos con 1 rol; 8 con 2 |
| E3 | Tres comprobaciones del mismo usuario (2 roles) en un scope | sin memo 18; con memo 6. Dos usuarios alternados: 10 (6 + 4). Inexistente dos veces: 1 |
| E5 | Una sola consulta con join `UserRoles` ⋈ `RoleClaims` | 1 comando, mismos permisos que E1 |

**Lo que pasa hoy por endpoint, con el rol `User`** [INFERENCIA a partir de E1 y E2 y del código de cada slice]

| Request | Comprobaciones | Comandos de Identity hoy | Con memo de roles y permisos |
|---|---|---|---|
| `POST /tasks` (`CreateTask.cs:31`) | 1 | 4 | 4 |
| `GET /tasks/{id}` propia (`GetTask.cs:26`) | 0 (dueño, `AccessPolicy.cs:60-61`) | 0 | 0 |
| `GET /tasks/{id}` ajena | 1 | 4 | 4 |
| `GET /users/{id}` propio (`GetUser.cs:29-32`) | 0 + `FindAccountAsync` | 6 | 5 |
| `POST /auth/login` y `/auth/register` (`DescribeAsync`, `OpenSession.cs:61`, `RegisterUser.cs:65`) | `DescribeAsync` | 5 | 4 |
| Sonda de test con 3 comprobaciones | 3 | 12 | 4 |

**Una sola consulta: ¿entra?**

- E5 muestra que un join directo sobre `AppDbContext` (que hereda de `IdentityDbContext`, `Infrastructure/Persistence/AppDbContext.cs:10`) baja de `2 + 2N` a 1.
- No lo recomiendo en este paso. El concepto del paso es el alcance de la caché, no la forma de la consulta (regla 1). Además saltea `RoleManager` y duplica en LINQ lo que hoy decide Identity. Queda como pendiente.

## 4. Qué memorizar, con qué clave y cuándo se desactualiza

**Respuesta:** roles y permisos juntos, por `userId` ordinal, también el resultado vacío. Sin invalidación; se documenta el límite.

**Qué y con qué clave**

- La clave es el `userId`, porque un request puede preguntar por dos usuarios distintos: `GetUser` comprueba al que llama (`GetUser.cs:29`) y después describe al leído (`GetUser.cs:32`). E3 lo confirmó: dos usuarios alternados dan 10 comandos, uno por cada carga.
- Guardar roles y permisos juntos deja que `DescribeAsync` deje de llamar a `GetRolesAsync` por su cuenta (`AuthModule.cs:34`). La forma más simple [INFERENCIA]: un método interno `AccessPolicy` que devuelve roles y permisos desde una carga memorizada; `GetPermissionsAsync`, `HasPermissionAsync` y `CanAccessAsync` leen de ahí. Si el resultado es un tipo nuevo, va en su propio archivo (DUNE0010, `docs/conventions.md:12`) o como tipo privado anidado. `IAuthModule` no cambia, entonces DUNE0015 no se mueve (`docs/conventions.md:17`).
- `StringComparer.Ordinal`, el mismo que ya usa `AccessPolicy.cs:26,29,31`.
- Resultado vacío: lo memorizo. El que llama ya pasó la validación de sesión del mismo request (`Features/Auth/SessionHttpPolicy.cs:89-93`), y el usuario leído ya se cargó por perfil. Dentro del request, que la cuenta exista solo cambia si el mismo request la borra. E3: el inexistente dos veces costó 1 comando. [INFERENCIA]

**Desactualización dentro del request**

- E4, mismo scope: comprobé (`Tasks.Read` = false), agregué `Admin` con el `UserManager` del mismo scope, volví a comprobar y dio **false**. Después de olvidar la entrada dio true.
- E4, scope nuevo por request: false, se da `Admin`, true, se quita `Admin`, false. Entre requests el cambio se ve siempre.
- Hoy ningún request escribe roles y después los lee:
  - `RegisterUser` agrega el rol `User` (`RegisterUser.cs:53`) **antes** de la primera lectura (`DescribeAsync`, `RegisterUser.cs:65`). Si alguien invierte ese orden, la respuesta del registro mostraría permisos vacíos. [INFERENCIA]
  - `DeleteUser` comprueba al que llama (`DeleteUser.cs:28`), borra la cuenta (`DeleteUser.cs:31`, que llega a `AuthModule.cs:51`) y no vuelve a leer.
  - Los roles solo cambian en el arranque (`RoleSeeder`) y en los tests, siempre en su propio scope (`TestAccounts.cs:86-96`).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| Documentar el límite: "dentro de un request, `AccessPolicy` responde con lo que leyó la primera vez" | Sin API nueva. Cubre todo el código de hoy. | Un endpoint futuro que cambie roles y vuelva a preguntar tiene que acordarse. |
| Método `Forget(userId)` llamado por toda escritura de roles en Auth | El memo nunca miente dentro del request. | API que hoy nadie usa (regla 7). Las escrituras de Identity no pasan por Auth sola: `UserManager.AddToRoleAsync` no avisa a nadie. |

Recomendación: **documentar** en `architecture.md`, junto a `architecture.md:72`. El método de invalidación entra con el primer endpoint que cambie roles.

## 5. Cómo contar las consultas en un test contra PostgreSQL real

**Respuesta:** un `DbCommandInterceptor` de test, sumado con `ConfigureDbContext<AppDbContext>` en una fábrica aislada, que cuenta los comandos sobre tablas `AspNet*` durante el request medido.

**Qué ofrece EF Core**

| Mecanismo | Alcance según Microsoft | En este test |
|---|---|---|
| `DbCommandInterceptor` | "Per context", registrado en la configuración del contexto ([Learn, Overview of logging and interception](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/)) | Ve cada comando y su `CommandText`. Se suma solo a la app del test. |
| `DiagnosticListener` (`"Microsoft.EntityFrameworkCore"`) | "Process", "registered globally"; "not suitable for getting events from a single DbContext instance" ([Learn, Diagnostic listeners](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/diagnostic-listeners)) | Ve los comandos de **todos** los tests del proceso. Mal para tests en paralelo. |
| Logging, categoría `Microsoft.EntityFrameworkCore.Database.Command` | Per context, vía DI ([misma fuente que la primera fila](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/)) | Funciona con un `ILoggerFactory` propio, como `ErrorContractTests.cs:222-228`. Pero `CapturedLogs` hoy guarda solo nivel y excepción (`Tests/Support/CapturedLogs.cs:8`), sin categoría ni texto. |
| Contador dentro de `AccessPolicy` | — | Mide código propio, no consultas reales. Pone código de test en producción. |

- Microsoft avisa que para *registrar* SQL conviene logging, no interceptores (misma fuente). Acá no registramos: contamos y filtramos por tabla, y el interceptor da el `DbCommand` tipado. [INFERENCIA]
- Cómo se suma sin tocar el registro de producción: "Starting with EF Core 9.0, you can use ConfigureDbContext to apply additional configuration to a DbContext either before or after the AddDbContext call. This is particularly useful for composing non-conflicting configuration in reusable components or tests", y para opciones que no chocan, como interceptores, "all configurations are composed together" ([Learn, DbContext configuration, ConfigureDbContext](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)). El repo ya agrega `AuditInterceptor` dentro de `AddDbContext` (`Infrastructure/DependencyInjection/BackendInfrastructure.cs:27-29`). E9 lo comprobó: `AddDbContext` con el interceptor A y después `ConfigureDbContext` con el B; una consulta: A=1, B=1. Los dos corren.
- El lugar: `ApiFixture.CreateIsolatedClient(Action<IServiceCollection>, out IServiceProvider)` (`Tests/Support/ApiFixture.cs:33-34`) ya acepta servicios extra, y lo usan `AuditTrailTests` y `ListTasksTests` (`Tests/Auditing/AuditTrailTests.cs:25`).

**Aislar los comandos del test**

- xUnit: "By default, there is a test collection per test class"; dentro de una colección los tests no corren en paralelo, y colecciones distintas sí ([xUnit, Running tests in parallel](https://xunit.net/docs/running-tests-in-parallel)). Todas las clases comparten la misma base (`[assembly: AssemblyFixture(typeof(PostgresFixture))]`, `Tests/Support/PostgresFixture.cs:5`).
- Una fábrica aislada tiene su propio contenedor de DI y su propio `AppDbContext`. Un interceptor sumado solo a ella ve solo los comandos de esa app, o sea los del test que la creó. Un `DiagnosticListener` vería los de todas las clases. [INFERENCIA]
- `AsyncLocal` no sirve tal cual en un test HTTP: `TestServer.PreserveExecutionContext` "controls if ExecutionContext and AsyncLocal&lt;T&gt; values are preserved from the client to the server. The default value is false" ([Learn API, TestServer.PreserveExecutionContext](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.testhost.testserver.preserveexecutioncontext?view=aspnetcore-10.0)). Un valor que el test pone antes de `SendAsync` no llega al servidor. Con la fábrica aislada no hace falta. `AsyncLocal` sí sirve en un test de scope sin HTTP: E7 corrió 20 scopes en paralelo, cada uno con su contador `AsyncLocal`, y los 20 contaron exactamente sus propios comandos.
- Dentro del request medido también corren otros comandos: la validación de sesión hace un `ExecuteUpdate` sobre `Sessions` (`SessionHttpPolicy.cs:89-93`, `Sessions.cs:28`), y la slice lee su tabla. Por eso el contador filtra por `CommandText` que nombre una tabla `"AspNet…"`, y se pone en cero justo antes del request medido (el arranque de la fábrica migra y siembra antes).

**El request con varias comprobaciones**

Hoy ningún endpoint comprueba dos veces al mismo usuario (Conclusión). Tres formas de armar el test:

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Sonda de test (un `IStartupFilter` como `ErrorProbe`, `Tests/Support/ErrorProbe.cs:8-19`) que resuelve `IAuthModule` de `HttpContext.RequestServices` y llama 3 veces a `HasPermissionAsync` y `CanAccessAsync` | Request HTTP real con el scope real del framework. Mide justo lo que dice el plan. Sigue un patrón que el repo ya usa. | Código de test que imita un endpoint. |
| B. Test de scope: `services.CreateScope()`, resolver `IAuthModule` y llamar 3 veces | Más corto. | Prueba "por scope", no "por request HTTP". |
| C. Endpoint real (`GET /users/{id}` o login) | Sin código de test extra. | No tiene varias comprobaciones del mismo usuario; solo mide la repetición de roles de `DescribeAsync` (6 → 5). |

Recomendación: **A** para el test del plan. Con el rol `User` espero 12 comandos de Identity hoy y 4 con el memo; con `User` + `Admin`, 18 y 6 (E3). El rojo real es ese número: el test falla hoy porque cuenta 12, no 4. Opcional: **C** sobre login como regresión de `DescribeAsync` (5 → 4).

## 6. "Un permiso cambiado se aplica en el request siguiente"

**Respuesta:** ya hay un test que lo cubre y tiene que seguir en verde. Sumo uno con la sonda que cambia un rol entre dos requests y lo comprueba en el segundo.

- `GetTaskTests.cs:35-70`: `bob` recibe 403 en la tarea de `alice` (`:47-53`), se le da `Admin` en otro scope (`:55`), recibe 200 (`:57-58`), se le quita (`:65`) y vuelve a 403 (`:67-69`). Cada paso es un request nuevo, entonces un scope nuevo y un memo vacío. Con una caché entre requests este test fallaría en `:58` o en `:69`. [INFERENCIA]
- No cambiar los claims de los roles compartidos (`User`, `Admin`, `SuperAdmin`) en un test: todas las clases usan la misma base (sección 5), y quitarle `Tasks.Read` a `Admin` rompería otros tests que corren en paralelo. Para probar un cambio de **permiso** y no de membresía, crear en el test un rol con nombre único (`TestAccounts.UniqueName`, `TestAccounts.cs:17-18`), darle el claim y sumarlo al usuario. [INFERENCIA]
- El límite dentro del request (sección 4, E4) se puede dejar escrito como test con la sonda: comprobar, cambiar el rol con el `UserManager` del mismo request y volver a comprobar, que devuelve el valor viejo; después, en un request nuevo, el valor nuevo. Es opcional. Fija un comportamiento que quizá un paso futuro quiera cambiar con `Forget`.

## 7. Herramientas, librerías y paquetes

**Respuesta:** ninguna decisión de herramienta. No hace falta un paquete nuevo.

- El memo es un `Dictionary` dentro de una clase que ya existe.
- `DbCommandInterceptor` y `ConfigureDbContext` vienen con EF Core, que el repo ya trae con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`backend/TemplateApp.csproj:35`; E9 compiló con ese paquete solo).
- El proyecto de tests ya tiene `Microsoft.AspNetCore.Mvc.Testing` y `Testcontainers.PostgreSql` (`backend/Tests/TemplateApp.Tests.csproj`).
- Lo que **sí** sería decisión de Tobi (regla 5), y por eso lo descarto: `HybridCache` (paquete `Microsoft.Extensions.Caching.Hybrid`) o una caché distribuida. Son del paso 33 (`plan.md:439-441`).

## 8. Regla de Dune

**Respuesta:** no hace falta un analyzer para el ciclo de vida de `AccessPolicy`. Si el paso escribe una regla nueva en los docs, la forma ejecutable es prohibir las APIs de caché en `check-structure.mjs` hasta el paso 33.

**Candidatas**

| Regla candidata | ¿La recomiendo? | Por qué |
|---|---|---|
| "`AccessPolicy` se registra como scoped" (analyzer sobre `AddScoped<AccessPolicy>`) | No | Ya la cuidan la validación de scopes en Development y el test de revocación (sección 2). Un analyzer sobre la llamada de registro es frágil: hay `TryAdd`, fábricas y registros genéricos. [INFERENCIA] |
| "Fuera de `Features/Auth` nadie usa `UserManager` ni `RoleManager`" | No en este paso; pendiente | Hoy se cumple (búsqueda en `backend/` fuera de `Features/Auth` y los tests: cero resultados). Dune solo lo prohíbe en endpoints (DUNE0002, `dune/Dune.Analyzers/DuneDiagnostics.cs:15-22`). `UserManager<AppIdentityUser>` usa un tipo de `Auth.Contracts`, así que DUNE0004 no lo frena. Es una regla de límites entre features, no del memo. |
| "No hay caché entre requests hasta el paso 33" (`check-structure.mjs` rechaza `Microsoft.Extensions.Caching.*` en paquetes y `using`, y `AddMemoryCache`, `AddDistributedMemoryCache`, `AddHybridCache`, `AddStackExchangeRedisCache` en el código del producto) | Sí, si el paso agrega una línea a los docs | `conventions.md:92` ya lo dice, pero solo la parte de bus y mediator es ejecutable (`dune/check-structure.mjs:133-179`, `checkNoBus`). Esta regla protege justo el concepto del paso. Copia la forma de `checkNoBus`, con su fixture válida y su violación deliberada en `check-structure.test.mjs` (como `:111-148`). Hoy no tiene violaciones (búsqueda de `IMemoryCache`, `HybridCache` y `IDistributedCache` en `backend/`: cero resultados). |

- Regla 4 de `AGENTS.md`: toda regla nueva se hace ejecutable en Dune. Si el paso solo cambia `AccessPolicy` y documenta el límite de la sección 4, no hay regla nueva para otros: es comportamiento interno de Auth y lo fijan los tests. Si `conventions.md` gana una línea del tipo "los permisos se memorizan solo dentro del request; nada los guarda entre requests", esa línea pide la regla de la tercera fila. [INFERENCIA sobre cómo aplica la regla 4]

## 9. Contrato HTTP

- No cambia: las respuestas y los permisos devueltos son los mismos. No rige la regla 3, y las e2e quedan `NOT_RUN` según `AGENTS.md`.
- `UserResponse.permissions` sale de `DescribeAsync` (`AuthModule.cs:37-42`). El orden de la lista viene de un `HashSet` (`AccessPolicy.cs:29`, `AuthModule.cs:42`) y no cambia con el memo. [INFERENCIA]

## 10. Choques con los docs y con el plan

- `conventions.md:92` dice que la caché entra recién en el paso 33. El plan llama "caché con alcance correcto" al paso 11. No es una contradicción de fondo: lo del paso 11 es un memo dentro del scope, no una librería de caché. Para no confundir, conviene que `conventions.md:92` diga "caché entre requests". No freno por esto.
- `architecture.md:72` y `conventions.md:97` dicen que los permisos "se consultan en cada request". El memo lo mantiene: se consultan una vez por request, nunca se arrastran al siguiente.
- El plan dice "hoy cada comprobación golpea Identity". Es cierto, pero hoy ningún request hace dos comprobaciones del mismo usuario. El ahorro real de hoy es la lectura doble de roles en `DescribeAsync` (1 comando por login, registro y lectura de usuario). El test del plan necesita una sonda (sección 5). Lo anoto por regla 8; no cambia el paso.
- El plan nombra solo `AccessPolicy` en "Cambia". Para quitar la lectura doble de roles también cambia `AuthModule.DescribeAsync` (`AuthModule.cs:34-35`), dentro de la misma feature. Y si entra la regla de Dune de la sección 8, cambian `dune/check-structure.mjs`, su test y `conventions.md`.

## Decisiones que toma el hilo (modo autónomo)

Ninguna es de herramienta, así que no hay que frenar por la excepción del modo autónomo. Cada una con su recomendación:

1. Dónde vive el memo: campo en `AccessPolicy` (sección 1, opción A).
2. Qué se memoriza: roles y permisos por `userId`, incluido el vacío (sección 4).
3. Desactualización dentro del request: se documenta en `architecture.md`, sin `Forget` (sección 4).
4. Test de conteo: sonda de test + `DbCommandInterceptor` con `ConfigureDbContext` en una fábrica aislada, filtrando tablas `AspNet*` (sección 5, opción A).
5. Test del request siguiente: el de `GetTaskTests` sigue; se suma uno con la sonda y, si se prueba un cambio de claim, con un rol de nombre único (sección 6).
6. Dune: la regla de `check-structure.mjs` contra APIs de caché, si el paso agrega la línea a los docs; si no, queda como pendiente (sección 8).

## Pendientes fuera de alcance (para `progreso.md`)

- Una sola consulta con join `AspNetUserRoles` ⋈ `AspNetRoleClaims` para los permisos (E5: de `2 + 2N` comandos a 1).
- `UserManager.FindByIdAsync` en lugar de la consulta `AsNoTracking` de `AccessPolicy.cs:22-24`: reusaría la cuenta ya rastreada en `FindAccountAsync`, `OpenSession` y `RegisterUser` (un comando menos), a cambio de rastrear la cuenta en requests de Tasks.
- Regla de Dune "fuera de `Features/Auth` nadie usa `UserManager` ni `RoleManager`".
- `ValidateScopes` y `ValidateOnBuild` también en el entorno `Testing` de `ApiFixture`, para que los tests detecten dependencias cautivas.
- Un método de invalidación del memo cuando exista un endpoint que cambie roles y vuelva a preguntar en el mismo request.

## Qué quedó sin comprobar

- No corrí nada del repo: ni tests, ni smoke. El experimento usó Identity con `IdentityUser` y un `DbContext` mínimo, sin `AppIdentityUser`, sin `AuditInterceptor` y sin pipeline HTTP.
- La sonda dentro de `WebApplicationFactory`: no la escribí. No comprobé que un `app.Map` agregado por `IStartupFilter` reciba el scope del request igual que las rutas del producto; lo infiero de `ErrorProbe` y de la documentación de `RequestServices`.
- Los conteos por endpoint de la tabla de la sección 3: salen de E1 y E2 y de leer cada slice, no de medir el repo.
- Que registrar `AccessPolicy` como singleton haga fallar `GetTaskTests.cs:58` en el entorno `Testing`: es deducción.
- Que el `ExecuteUpdate` de `Sessions` no nombre tablas `AspNet*`: lo deduzco del código de `Sessions.cs`; no miré el SQL generado.
- La regla de `check-structure.mjs`: no la escribí ni la corrí.

## Contraste con el código (hilo del paso, 2026-11-16)

Lo que el hilo del paso comprobó en el repo después de esta investigación:

- La sonda (`backend/Tests/Support/PermissionProbe.cs`) recibe el scope del request: con el código de antes del paso, 4 comprobaciones en un request mandaron 16 comandos a tablas `AspNet*` (4 por comprobación, `2 + 2N` con N = 1); con el memo, las mismas 4 mandan lo mismo que una sola.
- Abrir sesión leía `AspNetUserRoles` 2 veces; con el memo, 1.
- Dentro de un mismo scope, un rol agregado no se ve; en el request siguiente sí, y al quitarlo vuelve a 403/`false`. El test cambia la pertenencia al rol `Admin`, no los claims de un rol, así que no toca datos que comparten otros tests.
- La regla de `check-structure.mjs` quedó escrita y corrida: 7 violaciones deliberadas fallan con el gate de antes y pasan con el nuevo; un `AddMemoryCache()` plantado en `AuthFeature.cs` rompe el gate.
- Evidencia en `artifacts/verify-template/fase4-11-red/`, `fase4-11-green/`, `fase4-11-rule/` y `fase4-11-check-all/`.

## Fuentes

- https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes
- https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/app-state?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/use-http-context?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/claims?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/
- https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/
- https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors
- https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/diagnostic-listeners
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbset-1.findasync?view=efcore-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.testhost.testserver.preserveexecutioncontext?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/api/system.threading.asynclocal-1
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/UserStore.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/RoleStore.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/UserManager.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/RoleManager.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/IdentityServiceCollectionExtensions.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/IdentityBuilder.cs
- https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/EntityFrameworkCore/src/IdentityEntityFrameworkBuilderExtensions.cs
- https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/Microsoft.Extensions.Hosting/src/HostingHostBuilderExtensions.cs
- https://xunit.net/docs/running-tests-in-parallel
- https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html
