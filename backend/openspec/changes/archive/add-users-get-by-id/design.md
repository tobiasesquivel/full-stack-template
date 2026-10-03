# Design: Add Users Get By ID Endpoint

## Technical Approach

Introduce a new secure, read-only public profile endpoint under `/api/v1/users/{authId}` to retrieve minimal user metadata (`AuthId`, `UserName`) while **removing** sensitive PII exposure and **refactoring** the domain `User` entity so that `AuthId` (the ASP.NET Identity user ID) becomes the primary key and foreign key, eliminating the legacy `User.Id` (int) surrogate and its index.

All patterns follow the codebase’s conventions: explicit types (`sealed record` DTOs), EF Core projections with `AsNoTracking()`, Wolverine auto-transactions, `ErrorOr` for expected errors, and partial controller composition. No routing, security, or performance regressions are expected due to the inherited `[Authorize]` and read-only design.

---

## Architecture Decisions


### Decision: Make User.AuthId the primary key and foreign key to AspNetUsers
| Choice | Alternatives considered | Rationale |
|---|---|---|
| **`AuthId` (string) becomes PK + FK to AspNetUsers.Id** | Keep `User.Id` (int) and keep `AuthId` only as alternate key | Reduces mapping complexity; Identity `Id` should be the canonical user identifier; improves performance via direct string PK lookups |
| **Remove all legacy `User.Id` column (int auto-increment) + unique index** | Keep `Id` as a shadow property but make it non-PK | Avoids accidental joins on wrong identifier; aligns with Identity design; minimizes schema surface |

**Why this choice over others**: 
- Micro-services and frontends already operate in terms of `AuthId` (the ASP.NET Identity user ID). Repeated mapping from `int` ⇄ `string` is eliminated.
- Primary key behavior: string PK is reasonable for 256-char UUIDs or 36-char GUIDs and avoids accidental table scans.
- EF Core behavior: dropping a PK column may require a destructive migration (`CreateTable` + copy data), but that outcome is acceptable given this is an early-stage project with low likelihood of production data.


---


### Decision: EF Core migration strategy — drop & recreate Users table with new schema
| Choice | Alternatives considered | Rationale |
|---|---|---|
| **Schema generation via `dotnet ef migrations add MakeAuthIdPrimaryKey` after entity refactor** | Write raw SQL `ALTER TABLE ... DROP COLUMN Id, ALTER COLUMN AuthId SET NOT NULL ...` | Migration tool rewrites schema more safely; CI pipeline validates the migration before any PR; also lints DDL for proper quoting |
| **New migration includes data copy** | Keep existing `Users` rows and migrate `AuthId` values | Keeps historical data, but risky & non-atomic; migration already destroys table when PK changed; this change affects early repo; destructive change is preferred now |

**Why this choice over others**: 
- EF Core does not natively support *changing* a primary key of an existing non-empty table; the safest transformation is a `CreateTable` + copy of required rows.
- The repo is in early stage; no production data to preserve.


---


### Decision: Dedicated `GetUserByIdQueryResult` DTO for GET public profile
| Choice | Alternatives considered | Rationale |
|---|---|---|
| **New `GetUserByIdQueryResult` with fields: `string AuthId`, `string UserName`** | Re-use existing `UserInfo` or extend `AuthResponse.User` | Forces separation of concerns; prevents accidental PII leakage when future contributors add email field; follows spec exactly |
| **Place all new DTOs under `Features/Users/GetById/`** | Place DTOs next to `User` entity | Better code ownership per feature; separation from domain aligns with screaming architecture; no accidental reuse |

**Why this choice over others**: 
- External services and frontends depend only on this endpoint schema; stability now avoids breaking future contracts.
- `sealed record` used to enforce immutability and value-equal contract guarantees.


---


### Decision: Wolverine auto-transactions + single `SaveChangesAsync()` call per handler
| Choice | Alternatives considered | Rationale |
|---|---|---|
| **Handler does NOT call `BeginTransactionAsync`, `SaveChangesAsync`, or `CommitAsync`** — relies on `AutoApplyTransactions()` | Explicit manual transaction scope | Wolverine manages ambient transaction via `IMessageBus`; keeps handler concise; cancellation propagated automatically |
| **Read-only query uses `AsNoTracking()` and projects to `GetUserByIdQueryResult`** | Fetch full entity and map | Prevents change-tracker overhead; keeps transaction lightweight; aligns with spec AC-6 |

**Why this choice over others**: 
- Template convention already enforces `AutoApplyTransactions` and `UseEntityFrameworkCoreTransactions()`; custom manual code would diverge from convention and increase risk of leaked transactions.


---


### Decision: Register endpoint Location header refactor
| Choice | Alternatives considered | Rationale |
|---|---|---|
| **`CreatedAtAction("GetUserById", "Users", new { authId = authResponse.User.AuthId }, authResponse)`** | Hard-code raw string `/api/v1/users/{authId}` | Keeps endpoint route self-discoverable via ASP.NET Core routing; renames are caught at compile time; aligns with spec AC-4 |
| **Inject `IUrlHelper` into controller constructor** | Build URL manually | Using `IUrlHelper` gives consistent route generation; no magic strings; spec requires this pattern |

**Why this choice over others**: 
- Auto-tested; route table already maps `authId` to handler; changing route in one place improves maintainability.


---

## Data Flow

```
[Client] ────┐
            JWT Validation + Policy (Authorize)
[GET /api/v1/users/{authId}]
            │
            ↓
[UsersController.GetUserById]────IMessageBus.InvokeAsync<ErrorOr<GetUserByIdQueryResult>>───►
                                                                               │
                                                                               ↓
                                                                    GetUserByIdQueryHandler
                                                                               │
                                                                         DbSet<User>
                                                                               │
                                                               .Where(u => u.AuthId == query.AuthId)
                                                                               │
                                                                               ↓
                                                             .Select(u => new {u.AuthId, u.UserName})
                                                                               │
                                                                               ↓
                                                        .AsNoTracking().SingleOrDefaultAsync()
                                                                               │
                                                                               ↓
                                                    null? Error.NotFound() : result
                                                                               │
                                                                               ↓
                                                                    _bus.Invoke() returns ErrorOr<T>
                                                                               │
                                                                               ↓
                                         Controller: result.Match(Ok, Problem) ────→ [200|Problem] JSON
```

---


## File Changes

| File | Action | Description |
|------|--------|-------------|
| `Domain/Entities/User.cs` | Modify | Remove `Id` (int), ensure `AuthId` is PK (`[Key]`) & required; keep FK `AuthId` mapping to AspNetUsers |
| `Infrastructure/Persistence/Configurations/UserConfiguration.cs` | Modify | Use `HasKey(u => u.AuthId)` instead of composite key via `HasKey(u => u.Id)`; remove `HasIndex(u => u.AuthId)` (PK is unique); keep `HasOne(u => u.IdentityUser).WithMany().HasForeignKey(u => u.AuthId)` |
| `Infrastructure/Persistence/AppDbContext.cs` | Unchanged | DbSet definition unchanged; `User` type unchanged |
| `Infrastructure/Persistence/Migrations/MakeAuthIdPrimaryKey.cs` | Create | EF Core auto-generated migration to drop old `Users` table and recreate with new schema (PK `AuthId` as string, FK to AspNetUsers) |
| `Features/Auth/Common/AuthResponse.cs` | Modify | `UserInfo(int Id, ...)` → `UserInfo(string AuthId, ...)`; rename field internally |
| `Features/Auth/Login/LoginCommandHandler.cs` | Modify | Remove any `Id` field from projection, ensure handler only uses `AuthId`; projection uses `Select(...).AsNoTracking()` |
| `Features/Auth/Register/RegisterEndpoint.cs` | Modify | Change `CreatedAtAction` `Created` to point to new route: `CreatedAtAction("GetUserById", "Users", new { authId = ... }, ...)` |
| `Features/Auth/DeleteUser/DeleteUserCommandHandler.cs` | Modify | Ownership check changes from `user.Id` to `(await _db.Users.FindAsync(...)).AuthId` equal to the authenticated claim |
| `Features/Users/UsersController.cs` | Create | Partial controller: `[Route("api/v1/users")]`, `[Authorize]`, constructor `IMessageBus`; namespace = folder path: `TemplateApp.Features.Users` |
| `Features/Users/GetById/GetUserByIdQuery.cs` | Create | `sealed record GetUserByIdQuery(string AuthId) : IQuery<ErrorOr<GetUserByIdQueryResult>>`; namespace `TemplateApp.Features.Users.GetById` |
| `Features/Users/GetById/GetUserByIdQueryHandler.cs` | Create | `IQueryHandler<GetUserByIdQuery, ErrorOr<GetUserByIdQueryResult>>`; projection: `_db.Users.Where(u => u.AuthId == query.AuthId).Select(u => new GetUserByIdQueryResult(u.AuthId, u.UserName)).AsNoTracking().SingleOrDefaultAsync()`; null? `Error.NotFound()` : result; auto transaction via Wolverine `\[AutoApplyTransactions\]` |
| `Features/Users/GetById/GetUserByIdQueryResult.cs` | Create | `sealed record GetUserByIdQueryResult(string AuthId, string UserName)`; namespace `TemplateApp.Features.Users.GetById` |
| `Features/Users/GetById/GetByIdEndpoint.cs` | Create | Partial `UsersController`; `[HttpGet("{authId}")]`; `public async Task<ActionResult<GetUserByIdQueryResult>> GetUserById(string authId, CancellationToken ct)`; `Bus.InvokeAsync<ErrorOr<GetUserByIdQueryResult>>(new GetUserByIdQuery(authId), ct)`; `result.Match(Ok, Problem)` |

---

## Interfaces / Contracts

### Domain Entity: `User.cs`
```csharp
namespace TemplateApp.Domain.Entities;

public sealed class User
{
    public string AuthId { get; } = string.Empty; // PK + FK to AspNetUsers.Id
    
    public string UserName { get; } = null!;
    
    // No Id (int) anymore — removed
    
    public AppIdentityUser? IdentityUser { get; set; }
    // Note: IdentityUser.Id = AuthId (string) — FK by convention due to AppIdentityUser.Id type
}
```

### AuthResponse.UserInfo Record Refactor
`Features/Auth/Common/AuthResponse.cs`
```csharp
public sealed record AuthResponse(UserInfo User, string AccessToken, string RefreshToken)
{
    public sealed record UserInfo(string AuthId, string Email, string UserName); // int Id → string AuthId
}
```

### GetUserByIdQuery & Result
**Features/Users/GetById/GetUserByIdQuery.cs**
```csharp
namespace TemplateApp.Features.Users.GetById;

public sealed record GetUserByIdQuery(string AuthId) : IQuery<ErrorOr<GetUserByIdQueryResult>>;
```

**Features/Users/GetById/GetUserByIdQueryResult.cs**
```csharp
namespace TemplateApp.Features.Users.GetById;

public sealed record GetUserByIdQueryResult(string AuthId, string UserName);
```

### EF Core Configuration: UserConfiguration.cs
**Infrastructure/Persistence/Configurations/UserConfiguration.cs**
```csharp
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.AuthId); // AuthId is the PK now

        builder.HasOne(u => u.IdentityUser)
            .WithMany()
            .HasForeignKey(u => u.AuthId); // FK to IdentityUser.Id (string)

        // builder.HasIndex(u => u.AuthId); // ← removed — PK is already unique
    }
}
```

### New Endpoint Partial: GetByIdEndpoint.cs
**Features/Users/GetById/GetByIdEndpoint.cs**
```csharp
namespace TemplateApp.Features.Users;

public partial class UsersController
{
    [HttpGet("{authId}")]
    public async Task<ActionResult<GetUserByIdQueryResult>> GetUserById(
        string authId,
        CancellationToken ct)
    {
        ErrorOr<GetUserByIdQueryResult> resultOrError = await _bus.InvokeAsync<
            ErrorOr<GetUserByIdQueryResult>>(
            new GetUserByIdQuery(authId),
            ct
        );

        return resultOrError.Match(Ok, Problem);
    }
}
```

---

## Testing Strategy

| Layer | What to Test | Approach |
|-------|---------------|----------|
| **Unit** | `User` entity new PK & FK mapping | EF Core in-memory test that `HasKey` and `HasOne` compile & apply correctly |
| **Unit** | Query projection (AuthId + UserName) | Verify projection in test handler without DB: `DbContext.Set<User>().Add(...); _ = await Set.Select(...).AsNoTracking().FirstOrDefaultAsync()` |
| **Integration** | `GET /api/v1/users/{authId}` 200, 404, 401, invalid token | ASP.NET test server; JWT auth middleware; verifies route, auth, DTO serialization |
| **Integration** | Register → Location header points to new endpoint & points to existing user | Validate header URL & fetch same user data |
| **End-to-end** | EF Core migration applies cleanly & does NOT drop AspNetUsers table | Integration test that `dotnet ef database update` succeeds in CI pipeline |

Tooling:
- `Microsoft.NET.Test.Sdk`, `xunit`, `NSubstitute`, `Testcontainers.PostgreSql` (pipeline provides real DB).
- `Microsoft.AspNetCore.Mvc.Testing` for controller integration tests.

---

## Migration / Rollout

### Database Migration
1. Refactor `User.cs`, `UserConfiguration.cs` as described.
2. Run:
   ```bash
   dotnet ef migrations add MakeAuthIdPrimaryKey -o Infrastructure/Persistence/Migrations
   ```
   Ensure the generated migration contains:
   - `CreateTable` with `AuthId` as PK string, FK to AspNetUsers, `UserName` column
   - `DropColumn` of `Id` (int) if still present
3. Validate the schema is correct on a throw-away DB or locally:
   ```sql
   \dt "public"."Users"  -- Ensure AuthId text PK, no Id column, FK to auth."aspnetusers"
   ```

### Backwards Compatibility
- No breaking change to existing auth endpoints; only Location header of Register is updated.
- New endpoint is additive; opt-in by route discovery.

### Feature Flags / Phased Rollout
Not required. Rollback plan in case of migration issues:
1. Revert the migration:
   ```bash
   dotnet ef migrations remove
   ```
2. Revert code changes to `User.cs`, `UserConfiguration.cs`, `RegisterEndpoint.cs`
3. Re-apply previous migration if necessary.

### Release Criteria
- [ ] Endpoint compiles with SDD conventions (explicit types, sealed records, namespace=path) — checked in CI `dotnet build`
- [ ] EF Core migration builds and applies without data loss on empty schema
- [ ] All authentication flows (login, register, delete) unchanged except Location header
- [ ] AuthId route parameter accepted; controller extracts route value correctly
- [ ] Authentication `[Authorize]` enforced; unauth returns 401
- [ ] Handler uses projection, `AsNoTracking()`, correct null check → `Error.NotFound()`

---

## Open Questions
- [ ] **PK migration strategy**: Confirm in team that destructive migration (drop table) is acceptable given lack of production data, else design an `INSERT INTO ... FROM SELECT ...` step which verifies existing `AuthId` values are non-null and unique.
- [ ] **Performance test**: Validate that AuthId (string PK) doesn’t cause cluster fragmentation in PostgreSQL. If cluster bloat noticed, plan future `ALTER TABLE CLUSTER ON AuthId` maintenance script.

---

**Design complete — ready for sdd-tasks.**