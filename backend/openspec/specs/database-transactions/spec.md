# Spec: Database Transaction & Read Optimization Conventions

Cross-cutting conventions for database access patterns. Applies to all handlers and services that perform multi-phase writes or read-only queries against `AppDbContext`.

## Transaction Model

Wolverine manages transactions automatically via `AutoApplyTransactions()` + `UseEntityFrameworkCoreTransactions()`:

- Every handler method is wrapped in an **ambient database transaction**
- Wolverine calls `SaveChangesAsync` automatically before the transaction commits
- If the handler throws, Wolverine rolls back the transaction
- Handlers do NOT call `SaveChangesAsync`, `BeginTransactionAsync`, or `CommitAsync` explicitly

### Register handler flow
- `UserManager.CreateAsync` and any `_db.Users.Add()` share the same `AppDbContext` instance
- All writes commit atomically via Wolverine's ambient transaction

### RedeemTokenAsync flow
- Refresh token rotation and revocation are within the handler's ambient transaction
- No explicit transaction wrapping needed

## Read Operations — Projection + AsNoTracking

Read-only queries MUST use `.Select()` to project only required columns and `.AsNoTracking()` to skip change tracking.

```csharp
var userData = await _db.Users
    .Where(u => u.AuthId == id)
    .Select(u => new { u.Id, u.Username })
    .AsNoTracking()
    .FirstOrDefaultAsync(ct);
```

## Null Check → ErrorOr Pattern

When fetching data that is expected to exist, check for null and return an `ErrorOr` error immediately.

```csharp
if (userData is null)
    return Error.NotFound(description: "User not found", code: "Auth.UserNotFound");
```

## Nested Records for Single-Use Types

When a type is ONLY used within a single parent type, nest it inside that parent.

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | Handlers do NOT call SaveChangesAsync, BeginTransactionAsync, or CommitAsync | Code review |
| AC-2 | Login User query uses Select + AsNoTracking + null check | Code review |
| AC-3 | docs/AspNetConventions.md documents AutoApplyTransactions pattern | File review |
