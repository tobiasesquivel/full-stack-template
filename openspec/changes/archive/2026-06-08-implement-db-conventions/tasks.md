# Tasks: Implement Database Conventions

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~85–100 |
| 400-line budget risk | Low |
| Chained PRs recommended | No |
| Suggested split | Single PR |
| Delivery strategy | single-pr |
| Chain strategy | pending |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

## Phase 1: Foundation

- [x] 1.1 **AppDbContext.cs** — Add `public DbSet<User> Users => Set<User>();` property below existing `RefreshTokens` property
- [x] 1.2 **AuthResponse.cs** — Nest `UserInfo` record inside `AuthResponse`: add body with `public sealed record UserInfo(int Id, string Email, string UserName);`
- [x] 1.3 **AspNetConventions.md** — Add new Database & Persistence conventions: explicit transaction scope pattern, projection + AsNoTracking for reads, null check → ErrorOr pattern, nested records for single-use types, no ConfigureAwait(false) in app code

## Phase 2: Implementation

- [x] 2.1 **RegisterCommandHandler.cs** — Wrap IdentityUser creation + `_db.Users.Add(user)` + token generation in `BeginTransactionAsync`/`CommitAsync`/`RollbackAsync` try-catch; change `_db.Set<User>().Add(user)` to `_db.Users.Add(user)`; change `new UserInfo(...)` to `new AuthResponse.UserInfo(...)`
- [x] 2.2 **LoginCommandHandler.cs** — Replace `_db.Set<User>().FirstOrDefaultAsync(...)` with `.Where().Select().AsNoTracking().FirstOrDefaultAsync(ct)` projecting only `Id` and `Username`; add null check returning `Error.NotFound("Auth.UserNotFound")`; remove `?.`/`??` fallbacks; change `new UserInfo(...)` to `new AuthResponse.UserInfo(...)`
- [x] 2.3 **JwtService.cs** — Replace all `_db.Set<RefreshToken>()` calls (lines 77, 89, 98) with `_db.RefreshTokens`; keep `_db.Set<AppIdentityUser>()` on line 106 as-is

## Phase 3: Cleanup

- [x] 3.1 **UserInfo.cs** — Delete `Features/Auth/Common/UserInfo.cs` (contents merged into AuthResponse, all references updated to `AuthResponse.UserInfo`)
- [x] 3.2 **Build verification** — Run `dotnet build` and confirm zero warnings
