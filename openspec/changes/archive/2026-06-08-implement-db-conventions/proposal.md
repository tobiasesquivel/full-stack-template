# Proposal: implement-db-conventions

## Intent
Standardize database access patterns by:
- Wrapping multi-phase DB operations in transactions to ensure atomicity
- Using projections (`Select`) instead of loading full entities for reads
- Enforcing `AsNoTracking` for read-only queries
- Documenting how transactions interact with ASP.NET Identity's UserManager operations

This addresses atomicity gaps where user creation, domain entity creation, and token generation can get out of sync during failures.

## Scope

### In Scope
- **AspNetConventions.md**: Add new database transaction conventions to Database & Persistence section
- **RegisterCommandHandler.cs**: Wrap steps 2-4 (create IdentityUser, create domain User, generate tokens) in a transaction
- **JwtService.cs**: 
  - `GenerateTokensAsync`: Wrap RefreshToken save in transaction when called independently
  - `RedeemTokenAsync`: Wrap multi-phase operation (read token, batch revoke, generate new tokens, revoke old) in transaction
- **LoginCommandHandler.cs**: Use projection + AsNoTracking when querying User (line 42-44)
- **RegisterCommandHandler.cs**: Verify projection usage pattern (no read query present, but ensure consistency)

### Out of Scope
- Refactoring ASP.NET Identity itself (UserManager, SignInManager) — we only wrap their operations in transactions
- Changing the execution strategy from the application-wide retry mechanism to simple transactions
- Adding new transaction management infrastructure — using existing EF Core transaction APIs
- Changing business logic or authorization rules — only transaction boundaries and read patterns

## Capabilities
> Contract for the sdd-spec phase: which spec files should exist after this change?

### New Capabilities
- `database-transactions`: Transaction management conventions and patterns for consistent multi-write operations

### Modified Capabilities
- `auth-jwt`: JwtService now uses transaction boundaries for RefreshToken operations
- `auth-register`: RegisterCommandHandler now wraps Identity + domain writes in a transaction  
- `auth-login`: LoginCommandHandler now uses optimized read patterns with projections and AsNoTracking

## Approach

### Transaction Strategy (Execution Flow)
1. **Use EF Core's `CreateExecutionStrategy()` philosophy**: While we won't replace it, the transaction behavior follows the same pattern — if the operation fails mid-transaction, all steps rollback atomically
2. **DbContext transaction scope**: Since AppIdentityUser, User, and RefreshToken all share the same `AppDbContext`, their writes participate in the same transaction via `_db.Database.BeginTransactionAsync()`
3. **Identity operations**: UserManager.CreateAsync and SignInManager.CheckPasswordSignInAsync use the same DbContext, so they share the same connection and can be part of the same transaction. However, they may internally call `SaveChangesAsync()` creating implicit commits when we don't explicitly wrap them.

### Solution Pattern
```csharp
// For RegisterCommandHandler: Explicit transaction spanning all writes
await using var transaction = await _db.Database.BeginTransactionAsync(ct);
try {
    // Step 1: Identity user creation (may internally SaveChanges)
    IdentityResult createOutcome = await _userManager.CreateAsync(...);
    
    // Step 2-3: Domain writes
    _db.Set<User>().Add(user);
    await _db.SaveChangesAsync(ct);
    
    // Step 4: Token generation (saves RefreshToken)
    var tokensOrError = await _tokenService.GenerateTokensAsync(...);
    
    await transaction.CommitAsync(ct);
} catch {
    await transaction.RollbackAsync(ct);
    throw;
}
```

For reads, use projection patterns:
```csharp
// Bad: Loads full entity
var user = await _db.Set<User>().FindAsync(id, ct);

// Good: Projection with AsNoTracking
var userInfo = await _db.Set<User>()
    .Where(u => u.AuthId == identityUser.Id)
    .Select(u => new { u.Id, u.Username })
    .AsNoTracking()
    .FirstOrDefaultAsync(ct);
```

### Architecture Notes
- **Transaction boundary design**: We explicitly wrap multi-phase writes. Identity operations may auto-commit, but since they share connection, explicit transaction ensures consistency
- **Projection enforcement**: Read queries will fail CI checks if they load full entities unnecessarily
- **No repository abstraction**: Using `_db.Set<T>()` directly with transaction boundaries — keeps convention consistent with existing patterns

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `AspNetConventions.md` | Modified | Add new conventions to Database & Persistence section |
| `Infrastructure/Persistence/AppDbContext.cs` | Modified | Add `DbSet<User> Users` property |
| `Features/Auth/Register/RegisterCommandHandler.cs` | Modified | Add explicit transaction around Identity + domain writes; use `_db.Users` |
| `Features/Auth/Login/LoginCommandHandler.cs` | Modified | Use projection + AsNoTracking + null check with ErrorOr when querying User |
| `Features/Auth/Common/AuthResponse.cs` | Modified | Nest UserInfo record inside AuthResponse |
| `Features/Auth/Common/UserInfo.cs` | Deleted | Contents merged into AuthResponse as nested record |
| `Infrastructure/Auth/JwtService.cs` | Modified | Use `_db.RefreshTokens` instead of `_db.Set<RefreshToken>()` where applicable |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| **Identity auto-commit**: `UserManager.CreateAsync` may internally call `SaveChangesAsync`, splitting our transaction into two commits | Low | Test thoroughly; if discovered, reconsider approach or document as known limitation |
| **Transaction deadlocks**: Implicit escalation from row to table locks on concurrent token operations | Medium | Monitor post-deployment; use retry in exceptional cases rather than laxer consistency |
| **Performance impact**: Transactions may briefly hold locks longer | Medium | Keep transaction scope limited to essential writes; validate with performance benchmarks |
| **Migration conflicts**: AspNetConventions.md modification requires team awareness | Low | Add PR description with clear convention additions; pair with team review |

## Rollback Plan
1. **Revert changes**: `git revert HEAD~1` or checkout previous commit
2. **Feature flag**: None needed — changes are pure consistency improvements
3. **Database impact**: None — no schema changes, only multi-row updates rolled back on error
4. **User impact**: Degraded consistency — some registrations may create partial state without rollback, but functionality remains

## Dependencies
- **None** — uses existing EF Core transaction APIs and conventions

## Success Criteria
- [ ] aspnet-conventions: Database & Persistence section includes transaction, projection, null check, nested record, and AsNoTracking conventions
- [ ] AppDbContext: `DbSet<User> Users` property added
- [ ] RegisterCommandHandler: Three writes (Identity + User + RefreshToken) are atomic per registration test; uses `_db.Users`
- [ ] LoginCommandHandler: User query uses projection + AsNoTracking + null check with ErrorOr; no full entity loads in read path
- [ ] AuthResponse: UserInfo nested inside AuthResponse; UserInfo.cs deleted
- [ ] JwtService.RedeemTokenAsync: Multi-phase operations (read + revoke + generate + revoke) are atomic; uses `_db.RefreshTokens`
- [ ] CI pipeline passes: `dotnet build` succeeds with no warnings