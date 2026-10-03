# ASP.NET Conventions — Full-Stack Template

## Dependency Injection

- All service registration lives in `Infrastructure/DependencyInjection/DependencyInjection.cs`.
- Each feature/framework registers its own dependencies via an `AddApp{Name}()` extension method called from `AddInfrastructure()`.
- Methods return `IServiceCollection` to allow chaining.
- Example: `services.AddAppDatabase()`, `services.AddAppIdentity()`, `services.AddAppJwt()`.

## Architecture & Project Structure

- **Domain/** — domain entities, pure business logic. No framework dependencies. No reference to infrastructure types.
- **Features/** — application layer. Feature-based folders: each **feature** (business capability) is a folder under `Features/{FeatureName}/`.
- Inside each feature, **use cases** are subfolders named after the action (e.g., `GetById/`, `Create/`, `Test/`).
- All files for a use case live inside its subfolder — never loose files inside the feature folder.
- **Infrastructure/** — cross-cutting: persistence, DI, middleware, auth.
- No loose files in the project root (except `Program.cs` and conventions/docs).

**Example structure:**

```
Features/
├── Health/
│   └── Test/                    ← use case = Test
│       ├── TestController.cs
│       └── TestResponse.cs
├── Users/
│   ├── Login/
│   ├── Register/
│   └── UsersController.cs       ← base controller (partials) if shared endpoints
```

### Domain Entity Conventions

Entities in `Domain/Entities/`:

- **Public class** (not sealed by default)
- **Primary key**: `Guid` named `Id` (exception: `User` uses `AuthId` as PK)
- **Immutable fields**: `{ get; init; }` — set once at creation
- **Mutable fields**: `{ get; set; }` — values that change over time
- **Tracked state**: `{ get; private set; }` with behavior methods (e.g., `RefreshToken.Revoke(string?)`)
- **Id generation**: `Id = Guid.NewGuid()` assigned at creation
- **Timestamps**: `DateTime.UtcNow` assigned at creation
- **No base entity class**, no `BaseEntity<TId>` abstraction
- **No navigation properties** to other entities (except FK string properties like `AuthId`)

## Database & Persistence

- PostgreSQL with Entity Framework Core.
- Provider: `Npgsql.EntityFrameworkCore.PostgreSQL`.
- DbContext lives in `Infrastructure/Persistence/AppDbContext.cs`.
- Entity configurations use **Fluent API** via `IEntityTypeConfiguration<T>` — no data annotations on domain entities.
- One file per entity, named `{Entity}Configuration.cs`, inside `Infrastructure/Persistence/Configurations/`.
- `OnModelCreating` uses `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)` — all configurations are auto-discovered, no manual registration.
- Migrations go in `Infrastructure/Persistence/Migrations/`.
- New migrations: `dotnet ef migrations add {Name} -o Infrastructure/Persistence/Migrations`.

### Entity Configuration Conventions

Configuration classes in `Infrastructure/Persistence/Configurations/`:

- **Sealed** by default, inherit `IEntityTypeConfiguration<T>`
- Auto-discovered via `ApplyConfigurationsFromAssembly()` — no manual registration
- `HasMaxLength()` values: `AuthId`=450, `Title`=200, `Token`=256, `JwtId`=128, `ReplacedByToken`=256, `Username`=100
- Foreign keys: Entity → `AppIdentityUser` with `.HasOne().WithMany().OnDelete(DeleteBehavior.Cascade)`
- No composite keys, no shadow properties

### Transaction Boundaries & Read Optimizations

#### Database Transactions — AutoApplyTransactions

Wolverine manages database transactions automatically via `AutoApplyTransactions()` + `UseEntityFrameworkCoreTransactions()`:

- Every handler method is **wrapped in an ambient database transaction**.
- Wolverine calls `SaveChangesAsync` automatically **before the transaction commits**.
- If the handler throws an exception, Wolverine **rolls back** the transaction.
- Handlers do **NOT** call `SaveChangesAsync`, `BeginTransactionAsync`, or `CommitAsync` explicitly.

This means multi-phase writes (Identity user creation + domain entity + tokens) commit atomically without any manual transaction code in the handler. The `AppDbContext` instance is shared across the handler and any services it calls (e.g., `JwtService`), so all changes are within the same transaction scope.

```csharp
// Example: Register handler — no explicit SaveChanges needed
public async Task<ErrorOr<AuthResponse>> Handle(RegisterUserCommand command, CancellationToken ct)
{
    AppIdentityUser identityUser = new() { ... };
    IdentityResult createOutcome = await _userManager.CreateAsync(identityUser, command.Password);

    User user = new() { AuthId = identityUser.Id, ... };
    _db.Users.Add(user);  // tracked by EF, persisted by Wolverine

    ErrorOr<TokenPair> tokensOrError = await _tokenService.GenerateTokensAsync(identityUser, ct);
    // Wolverine calls SaveChangesAsync atomically — all or nothing
}
```

**Rules:**
- Use `_db.Set<TEntity>()` or the dedicated `DbSet<TEntity>` property when available (e.g., `_db.Users`, `_db.RefreshTokens`).
- No `ConfigureAwait(false)` in application code — no SynchronizationContext in ASP.NET Core.

#### Read Optimizations — Projection + AsNoTracking

**Always** use projection for read-only queries, especially identity-linked entities. Use the dedicated `DbSet` property when available:

```csharp
var userData = await _db.Users
    .Where(u => u.AuthId == id)
    .Select(u => new { u.Id, u.Username })
    .AsNoTracking()
    .FirstOrDefaultAsync(ct);
```

- **Projection**: ONLY fields required by response.
- **AsNoTracking()**: Prevent EF change tracker overhead.
- **Async**: Forward `CancellationToken` always.

#### Null Check on Fetched Data — Error Pattern

When fetching data that is expected to exist, check for null and return an `ErrorOr` error immediately. Then use the value normally — no null-coalescing or null-conditionals downstream:

```csharp
var userData = await _db.Users
    .Where(u => u.AuthId == id)
    .Select(u => new { u.Id, u.Username })
    .AsNoTracking()
    .FirstOrDefaultAsync(ct);

if (userData is null)
    return Error.NotFound(description: "User not found", code: "Entity.NotFound");

// Use userData directly — no ?. or ?? needed
```

### Parallel Async Calls

When making multiple independent async calls that can run concurrently, use `Task.WhenAll` + `.Result`:

```csharp
Task<IList<string>> rolesTask = _userManager.GetRolesAsync(user);
Task<IList<Claim>> claimsTask = _userManager.GetClaimsAsync(user);

await Task.WhenAll(rolesTask, claimsTask);

IList<string> roles = rolesTask.Result;
List<Claim> userClaims = claimsTask.Result.ToList();
```

**Rules:**
- Only parallelize calls that have **no dependency** on each other's results.
- Use `Task.WhenAll` (not `Task.WhenAny`) to await all concurrently.
- Access the result via `.Result` after `Task.WhenAll` — safe because the task is already completed.
- Do NOT use this pattern for inherently sequential operations (e.g., create user → add role).

#### Nested Records for Single-Use Types

When a type (record/class) is ONLY used within a single parent type, nest it inside that parent instead of creating a separate file. The "one file per type" rule is suspended for these single-use inner types:

```csharp
// ✅ Good: UserInfo is only used inside AuthResponse
public sealed record AuthResponse(UserInfo User, string AccessToken, string RefreshToken)
{
    public sealed record UserInfo(int Id, string Email, string UserName);
}
```

### Seeding

Seeders in `Infrastructure/Seeders/`:

- **Static classes** with **static async methods**
- **Idempotent**: check existence before adding:
  - `roleManager.RoleExistsAsync()` before `roleManager.CreateAsync()`
  - Compare existing claims vs target permissions via HashSet — skip already-assigned
- Called via `app.SeedDatabaseAsync()` extension method in `Program.cs`:
  1. Creates `IServiceScope`
  2. Resolves services manually via scope
  3. Wraps each seeder in try-catch (failure is logged, does NOT crash startup)

**Adding a new seeder**: Add the call in `DatabaseSeederExtensions.SeedDatabaseAsync()`, same try-catch pattern.

## Authentication & Authorization

- ASP.NET Core Identity for user management.
- JWT Bearer tokens for access tokens.
- Refresh tokens with rotation (old refresh token invalidated on each refresh).
- Identity tables stored in the same PostgreSQL database.
- Roles are seeded via `RoleSeeder.SeedRolesAsync()`, called at app startup.

### Permission-Based Authorization

Permissions are checked via a custom `[HasPermission(string)]` attribute:

```csharp
[HasPermission(AppPermissions.Tasks.Read)]
public async Task<IActionResult> GetAll(CancellationToken ct) { ... }
```

The permission infrastructure consists of four files in `Infrastructure/Auth/`:

| File | Purpose |
|------|---------|
| `HasPermissionAttribute.cs` | Translates `[HasPermission(AppPermissions.X)]` into `[Authorize(Policy = "Permissions.X.Y")]` |
| `PermissionPolicyProvider.cs` | Replaces the default `IAuthorizationPolicyProvider` — creates policies on-the-fly for any policy name. Eliminates the need to register each permission in `AddAuthorization()`. |
| `PermissionRequirement.cs` | DTO carrying the permission string to the handler |
| `PermissionAuthorizationHandler.cs` | Checks if the user has a claim of type `"permission"` with the required value |

**Rules:**
- Permissions are stored as **role claims** with `Type = "permission"` and `Value = "Permissions.{Resource}.{Action}"`.
- Permission strings are centralized in `Domain/core/Authorization/AppPermissions.cs` — never hardcode strings in controllers.
- `IAuthorizationPolicyProvider` is overridden so no explicit `AddAuthorization` policy registration is needed.
- The `[Authorize]` attribute at controller class level still uses the default policy (authenticated user). `[HasPermission]` adds the granular permission check at method level.
- When a user logs in or registers, their effective permissions are returned in the `AuthResponse.UserInfo.Permissions` field (`IReadOnlyList<string>`).

### Token Service

Token operations are abstracted behind `ITokenService` (`Infrastructure/Auth/`):
- `GenerateTokensAsync(AppIdentityUser, CancellationToken)` — creates access + refresh token pair
- `RedeemTokenAsync(string, CancellationToken)` — validates and rotates refresh token

**Refresh token rotation flow**:
1. Incoming plaintext is SHA-256 hashed
2. Hashed token looked up in `RefreshTokens` table
3. If `ReplacedByToken` is NOT null → token theft detected → all user sessions revoked (bulk `ExecuteUpdateAsync`)
4. If valid → old token revoked with `Revoke(HashToken(newToken))`, new pair generated
5. Token hash is the DB identifier — plaintext never stored

## Coding Style

- File-scoped namespaces (`namespace X.Y;`).
- Implicit usings enabled.
- Nullable reference types enabled.
- Primary constructors preferred when injecting dependencies.
- Response DTOs are `sealed record` — immutable by default, value equality, concise. (`public sealed record MyResponse(...)`)
- Request DTOs are `sealed record` too when they're simple DTOs; use `class` only when mutation or inheritance is genuinely needed.
- **One class/record/enum per file.** Never define multiple types in the same file. Each file exports exactly one public type. (Exception: private helper types inside a class.)
- **No `var`.** Use explicit types everywhere. This makes the code self-documenting and catches type mismatches at declaration site.
- **No logic in controllers.** Controllers are thin — they deserialize the request, create a command/query, send it via `IMessageBus.InvokeAsync<ErrorOr<T>>()`, and map the result to an HTTP response. Business logic lives in handlers.
- **`Result` suffix is reserved** exclusively for Query Result DTO class names (e.g., `GetUserQueryResult`). Do NOT use `Result` for token responses, handler intermediates, or any non-query type.
- **Use ErrorOr for flow control.** Expected errors (validation, not found, conflict, unauthorized) are `ErrorOr<T>.Errors`, not exceptions. Exceptions are for unexpected failures only.
- **`sealed` by default** — all handlers, DTOs, query results, and infrastructure classes are `sealed`. Only unseal when inheritance is genuinely needed.
- **Primary constructors** for simple classes (1-2 dependencies). Explicit constructors with `_camelCase` fields for 3+ dependencies.
- **Controller field naming** — use `_bus` or `_messageBus` consistently within the same controller. Do NOT mix both.
- **`IReadOnlyList<T>`** for read-only collections in response DTOs. Never expose `List<T>` or arrays.
- **No `[FromRoute]`, `[FromQuery]` on simple types** — ASP.NET Core infers simple types from the route/query string automatically.
- **`CancellationToken` parameter** is named `ct` in all handlers and endpoint methods.
- **No `this.`** — use `_camelCase` for private fields. Never use `this.`.

## Options Pattern

- Configuration sections use the **Options pattern** (`IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>`) whenever possible.
- Each options class ends with `Options` — e.g., `JwtOptions`, `DatabaseOptions`, `SmtpOptions`.
- Registered via `services.Configure<TOptions>(configuration.GetSection("SectionName"))` in `AddInfrastructure()`.
- The corresponding `appsettings.json` section name matches the class name minus the `Options` suffix (e.g., `"Jwt": { ... }` for `JwtOptions`).
- Options classes go next to where they're consumed. If shared across features, put them in `Infrastructure/Options/`.
- Never hardcode configuration keys — always bind to a typed Options class.
- **Options defaults are zero/nul** — do NOT hardcode business values like 15 or 7 as defaults. The source of truth is `appsettings.json`. Defaults should be `0`, `string.Empty`, or `null` so a missing config section fails fast.

### Configuration Sections

| Section | Key | Type | Notes |
|---------|-----|------|-------|
| `ConnectionStrings.DefaultConnection` | string | PostgreSQL connection string |
| `Jwt.Issuer` | string | JWT issuer |
| `Jwt.Audience` | string | JWT audience |
| `Jwt.Key` | string | HMAC-SHA256 key (min 32 chars) |
| `Jwt.AccessTokenExpirationMinutes` | int | Access token lifetime (default: 15) |
| `Jwt.RefreshTokenExpirationDays` | int | Refresh token lifetime (default: 7) |
| `AllowedHosts` | string | CORS allowlist (`"*"` by default) |

**`appsettings.Development.json`** overrides with dev-local values. Short refresh token expiry (1 day) prevents stale tokens during development.

## .NET Template

- The project is a runnable `dotnet new` template.
- `.template.config/template.json` defines metadata and `sourceName: "TemplateApp"`.
- `sourceName` is the placeholder: all occurrences of `TemplateApp` in filenames and file contents are replaced with the user's project name (`dotnet new cleanapi -n MyProject`).
- The `cleanapi` short name is registered in `template.json`.
- `appsettings.example.json` documents the config keys — do NOT commit secrets there.

## Handler Patterns

### Structure

Each handler is a `sealed class` in the use case's folder (`Features/{Feature}/{Action}/`).

- **Simple handlers** (1-2 dependencies): use primary constructors
  ```csharp
  public sealed class CreateTaskCommandHandler(AppDbContext _db)
  {
      public async Task<ErrorOr<TaskItemResponse>> Handle(CreateTaskCommand command, CancellationToken ct) { ... }
  }
  ```

- **Complex handlers** (3+ dependencies): use explicit constructor with `_camelCase` fields
  ```csharp
  public sealed class RegisterUserCommandHandler
  {
      private readonly UserManager<AppIdentityUser> _userManager;
      private readonly RoleManager<IdentityRole> _roleManager;
      private readonly AppDbContext _db;
      private readonly ITokenService _tokenService;

      public RegisterUserCommandHandler(
          UserManager<AppIdentityUser> userManager,
          RoleManager<IdentityRole> roleManager,
          AppDbContext db,
          ITokenService tokenService)
      { ... }
  }
  ```

### Naming

| Element | Pattern | Example |
|---------|---------|---------|
| Command handler | `{Action}{Entity}CommandHandler` | `CreateTaskCommandHandler` |
| Query handler | `{Action}{Entity}QueryHandler` | `GetTaskByIdQueryHandler` |
| Command record | `{Action}{Entity}Command` | `CreateTaskCommand` |
| Query record | `Get{Entity}Query` | `GetTaskByIdQuery` |
| Validator | `{Command}Validator` | `CreateTaskCommandValidator` |
| Request DTO | `{Action}Request` | `CreateRequest` |
| Query result DTO | `{Query}Result` | `GetTaskByIdQueryResult` |
| Endpoint file | `{Action}Endpoint.cs` | `CreateEndpoint.cs` |
| Response DTO (non-query) | `{Action}Response` | `AuthResponse`, `RefreshResponse` |

### Handler Rules

1. **No `SaveChangesAsync`** in handlers — Wolverine's `AutoApplyTransactions` handles it automatically.
2. **No repository pattern** — handlers work directly with `AppDbContext`.
3. **No exceptions for flow control** — use `ErrorOr<T>` in all handler returns. Expected errors (validation, not found, conflict, unauthorized) are `Error`, not exceptions.
4. **CancellationToken** parameter is always named `ct` and forwarded to every async call.
5. **Read-only queries** use `.AsNoTracking()` + projection (`.Select(...)`) — never expose domain entities to clients.
6. **DateTime timestamps** use `DateTime.UtcNow` — never local time, never `DateTime.Now`.
7. **Business rule validation** uses `||` to chain checks that lead to the same error:
   ```csharp
   if (x is null || x.Revoked || x.Expired)
       return Error.Validation(description: "...");
   ```
8. **Handler methods are `public`** and follow the signature `Task<ErrorOr<T>> Handle(Command/Query command, CancellationToken ct)`.

### Controller Response Mapping

Endpoints map `ErrorOr<T>` to HTTP responses consistently:

| Operation | Pattern |
|-----------|---------|
| Query (GET) | `resultOrError.Match(Ok, Problem)` |
| Create (POST) | `resultOrError.Match(response => CreatedAtAction(nameof(GetById), new { id = response.Id }, response), Problem)` |
| Update (PUT/PATCH) | `resultOrError.Match(Ok, Problem)` |
| Delete (DELETE) | `resultOrError.Match(_ => NoContent(), Problem)` |

**CreatedAtAction rules**:
- **Same controller**: `nameof(GetById)` — no controller name argument
- **Cross controller**: `nameof(TargetController.Method)` for actionName, `nameof(TargetController).Replace("Controller", string.Empty, StringComparison.Ordinal)` for controllerName

**AuthId extraction pattern** (all authenticated, scoped-to-user endpoints):
```csharp
string? authId = User.FindFirstValue(ClaimTypes.NameIdentifier);
if (authId is null)
    return Unauthorized();
```

### ApiController Problem() Mapping

`Infrastructure/ApiController` provides `Problem(List<Error> errors)`:

1. **Empty errors** → `Problem()` (500)
2. **ALL errors are Validation** → `ValidationProblem()` (400) — each error's `code` becomes the field name key, `description` becomes the error message
3. **Otherwise** → first error mapped to HTTP status:

| ErrorOr Type | HTTP Status |
|-------------|-------------|
| `ErrorType.Validation` | 400 Bad Request |
| `ErrorType.NotFound` | 404 Not Found |
| `ErrorType.Unauthorized` | 401 Unauthorized |
| `ErrorType.Forbidden` | 403 Forbidden |
| `ErrorType.Conflict` | 409 Conflict |
| _other_ | 500 Internal Server Error |

In the single-error branch: `error.Description` → `ProblemDetails.Title`, `error.Code` → `ProblemDetails.Detail`.

### ErrorOr Error Conventions

**DO NOT hardcode errors inline.** Every error worth returning is defined once in a **static error catalog** per feature domain:

| Feature | Catalog | Type |
|---------|---------|------|
| Auth | `Features/Auth/Common/AuthErrors.cs` | Static `readonly` |
| Tasks | `Features/Tasks/Common/TaskErrors.cs` | Factory `NotFound(Guid)` |
| Users | `Features/Users/Common/UserErrors.cs` | Factory `NotFound(string)` |

#### Static errors (no dynamic data)

```csharp
// Features/Auth/Common/AuthErrors.cs
public static class AuthErrors
{
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        code: "Auth.InvalidCredentials",
        description: "Invalid credentials.");

    public static readonly Error EmailAlreadyExists = Error.Conflict(
        code: "Auth.EmailAlreadyExists",
        description: "Email is already registered.");
}
```

Usage — no inline strings:
```csharp
// ✅
return AuthErrors.InvalidCredentials;

// × NO
return Error.Unauthorized(description: "Invalid credentials", code: "Auth.InvalidCredentials");
```

#### Factory methods (errors with dynamic data)

```csharp
// Features/Tasks/Common/TaskErrors.cs
public static class TaskErrors
{
    public static Error NotFound(Guid taskId) => Error.NotFound(
        code: "Task.NotFound",
        description: $"Task with ID '{taskId}' not found.");
}
```

```csharp
// ✅
return TaskErrors.NotFound(task.Id);

// × NO
return Error.NotFound(description: "Task not found", code: "Task.NotFound");
```

#### When to use static vs factory

| Approach | When |
|----------|------|
| `static readonly Error` | Error is always the same (no variable data). E.g., `InvalidCredentials`, `EmailAlreadyExists` |
| `static Error Factory(Type param)` | Error embedds runtime data like entity IDs. E.g., `NotFound(taskId)`, `NotFound(authId)` |

#### Error code convention

`{Domain}.{SpecificIssue}` — consistent with the catalog class name:

| Catalog | Code example |
|---------|-------------|
| `AuthErrors.InvalidCredentials` | `Auth.InvalidCredentials` |
| `AuthErrors.EmailAlreadyExists` | `Auth.EmailAlreadyExists` |
| `TaskErrors.NotFound(id)` | `Task.NotFound` |
| `UserErrors.NotFound(id)` | `User.NotFound` |

#### Error type to HTTP mapping

| ErrorOr Type | HTTP Status | When to use |
|-------------|-------------|-------------|
| `Error.NotFound()` | 404 | Entity/resource not found |
| `Error.Conflict()` | 409 | Duplicate or state conflict |
| `Error.Unauthorized()` | 401 | Invalid credentials, bad token |
| `Error.Forbidden()` | 403 | Insufficient permissions |
| `Error.Validation()` | 400 | Validation failure |
| `Error.Failure()` | 500 | Unexpected Identity errors (e.g., `IdentityResult.Errors`) |

#### Multi-error responses (Identity framework)

When an Identity operation produces multiple errors, map each to an ErrorOr error and return as `List<Error>`. This is the ONE exception where inline `Error.Validation`/`Error.Failure` is acceptable — Identity error codes come from the framework at runtime:
```csharp
List<Error> errors = identityResult.Errors
    .Select(e => Error.Validation(code: e.Code, description: e.Description))
    .ToList();
return errors;
```

#### Void operations

Operations that succeed with no return value use `Result.Success` (from ErrorOr).

### FluentValidation

**Structure**:
- Validators are `sealed class {CommandName}Validator : AbstractValidator<{CommandName}>`
- Validated at the **command** level (not request DTOs), in the same use-case folder
- All `RuleFor()` calls in the constructor — no separate validation methods

**Registration**: Automatic via Wolverine's `opts.UseFluentValidation()` in `AddAppWolverine()`. No `services.AddValidatorsFromAssembly()` needed — `FluentValidation.DependencyInjectionExtensions` handles auto-discovery.

**Rules**:
- Always validate, even single-property commands
- Prefer simple `RuleFor(x => x.Property).NotEmpty()` chains
- No `Must()`/`When()` unless genuinely needed
- Validation failures surface as 400 automatically (Wolverine catches `ValidationException` via `ValidationExceptionHandler`)

## Exception Handling

Two `IExceptionHandler` implementations in `Infrastructure/Middlewares/`:

1. **`ValidationExceptionHandler`** — catches `FluentValidation.ValidationException`, returns 400 with `ProblemDetails.Extensions["errors"]`
2. **`GlobalExceptionHandler`** — catches ALL other exceptions, returns 500 with generic `ProblemDetails`

Registered via `services.AddExceptionHandler<T>()` + `services.AddProblemDetails()` in `AddAppExceptionMiddlewares()`. Order matters: `ValidationExceptionHandler` first.

Middleware pipeline activation: `app.UseExceptionHandler()` in `Program.cs`.

## Program.cs Structure

```
1. Bootstrap Serilog BEFORE builder
2. Build: create builder, configure services
3. Build app, configure middleware pipeline
4. Seed database, run Wolverine commands

**Middleware order**:
1. `UseHttpsRedirection()`
2. `UseExceptionHandler()` — registers `GlobalExceptionHandler` + `ValidationExceptionHandler`
3. `UseAuthentication()`
4. `UseAuthorization()`
5. `MapControllers()`

**Development-only**: `MapOpenApi()` + `MapScalarApiReference()`

**Wolverine entry point**: `app.RunJasperFxCommands(args)` enables `dotnet run -- codegen` and `dotnet run -- describe`.

**HostAbortedException**: Caught silently before the generic `catch(Exception)` — this is the standard `dotnet new` pattern (thrown when `dotnet run` is cancelled). Must be caught separately to avoid false error logs.
```

## Wolverine Configuration

Configured in `AddAppWolverine()` inside `Infrastructure/DependencyInjection/DependencyInjection.cs`:

```csharp
builder.UseWolverine(opts =>
{
    opts.CodeGeneration.GeneratedCodeOutputPath = "Infrastructure/Wolverine/Generated";
    opts.UseFluentValidation();
    opts.PersistMessagesWithPostgresql(connectionString);
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
    opts.Services.AddResourceSetupOnStartup();

    opts.CodeGeneration.AlwaysUseServiceLocationFor<UserManager<AppIdentityUser>>();
    opts.CodeGeneration.AlwaysUseServiceLocationFor<RoleManager<IdentityRole>>();

    opts.CodeGeneration.TypeLoadMode = environment.IsDevelopment()
        ? TypeLoadMode.Auto
        : TypeLoadMode.Static;
});
```

**Key points**:
- Generated handler code → `Infrastructure/Wolverine/Generated/WolverineHandlers/`
- `UseFluentValidation()` — auto-registers all validators
- `AlwaysUseServiceLocationFor<T>()` — REQUIRED for `UserManager` and `RoleManager` (scoped Identity services)
- `TypeLoadMode.Auto` in dev for hot reload; `TypeLoadMode.Static` in prod for performance
- `dotnet run -- codegen` to regenerate handler code after adding new handlers
- `dotnet run -- describe` to inspect the Wolverine message routing

## Testing

_(To be defined — add test conventions here when added.)_
