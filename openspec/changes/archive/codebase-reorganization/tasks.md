# Tasks: Codebase Reorganization

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~300–360 |
| 400-line budget risk | Low |
| Chained PRs recommended | No |
| Suggested split | Single PR |
| Delivery strategy | single-pr |
| Chain strategy | pending |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | All phases (A–D) in sequence | PR 1 | Single atomic refactor — each phase gates on the previous |

## Phase A: Critical Infrastructure (.gitignore)

- [x] A1 Create root `.gitignore` — .NET + Node defaults, Wolverine exception (`!Infrastructure/Wolverine/Generated/*`), Playwright exception (`!.playwright-mcp/`)
- [x] A2 Create `.playwright-mcp/.gitignore` — ignore all artifacts inside `.playwright-mcp/`
- [x] A3 Create `Infrastructure/Wolverine/Generated/.gitignore` — `*` ignore, keep dir tracked
- [x] A4 Build + verify: `dotnet build` succeeds; `git status` shows no build/test artifacts

## Phase B: Domain Layer

- [x] B1 `git mv Domain/core/Authorization/*.cs Domain/Authorization/` — update namespaces to `TemplateApp.Domain.Authorization`; delete `core/` dir
- [x] B2 `Domain/Entities/RefreshToken.cs` — rename `AppIdentityUserId` → `UserId`
- [x] B3 `RefreshTokenConfiguration.cs` — update `.Property(rt => rt.UserId)`, `HasIndex(rt => new { rt.UserId, ... })`, `HasForeignKey(rt => rt.UserId)`
- [x] B4 `JwtService.cs` — update all 3 `AppIdentityUserId` refs → `UserId`
- [x] B5 `DeleteUserCommandHandler.cs` — update `.Where(rt => rt.UserId == ...)`
- [x] B6 Update all migration files (`.Designer.cs`, `.cs`, `ModelSnapshot.cs`) — replace `AppIdentityUserId` with `UserId` in shadow property, FK, and index definitions
- [x] B7 Generate new migration: `dotnet ef migrations add RenameRefreshTokenUserId -o Infrastructure/Persistence/Migrations` for column rename
- [x] B8 Update all 13 `using TemplateApp.Domain.core.Authorization` → `TemplateApp.Domain.Authorization` project-wide
- [x] B9 Build + verify: `dotnet build` zero warnings, `dotnet test` passes, grep confirms zero `AppIdentityUserId` / `TemplateApp.Domain.core` refs remain

## Phase C: Features + Infrastructure

- [x] C1 `git mv Features/Auth/DeleteUser/ Features/Users/Delete/` — update namespaces to `TemplateApp.Features.Users.Delete`
- [x] C2 Rewrite `DeleteUserEndpoint.cs` as partial `UsersController` — route `[HttpDelete("{userId}")]`, method `DeleteUser`, keep handler + permission logic identical
- [x] C3 `AuthController.cs` — remove `DeleteAccount()` partial (moved to `UsersController`)
- [x] C4 `GetByIdEndpoint.cs` — add `Name = "GetUserById"` to `[HttpGet("{authId}")]`
- [x] C5 `RegisterEndpoint.cs` — replace `CreatedAtAction(nameof(...), ...)` with `CreatedAtRoute("GetUserById", ...)`
- [x] C6 `git mv Infrastructure/Auth/PermissionRequirement.cs Infrastructure/Authorization/` — namespace `TemplateApp.Infrastructure.Authorization`
- [x] C7 `git mv Infrastructure/Auth/PermissionPolicyProvider.cs Infrastructure/Authorization/` — namespace update
- [x] C8 `git mv Infrastructure/Auth/PermissionAuthorizationHandler.cs Infrastructure/Authorization/` — namespace update; also update its `Domain.core.Authorization` import to `Domain.Authorization`
- [x] C9 `git mv Infrastructure/Auth/HasPermissionAttribute.cs Infrastructure/Authorization/` — namespace update
- [x] C10 `Infrastructure/DependencyInjection/DependencyInjection.cs` — update using + registration to new `Authorization` namespace
- [x] C11 Update all remaining `using TemplateApp.Infrastructure.Auth` — permission users get `using TemplateApp.Infrastructure.Authorization`; token users keep `Auth`
- [x] C12 `git mv Infrastructure/ApiController.cs Infrastructure/Common/` — namespace `TemplateApp.Infrastructure.Common`
- [x] C13 Update all 4 `: ApiController` controller imports from `TemplateApp.Infrastructure` → `TemplateApp.Infrastructure.Common`
- [x] C14 Build + verify: `dotnet build` succeeds, `dotnet test` passes, `dotnet format` clean

## Phase D: Documentation

- [x] D1 `git mv scalar-snapshot.md docs/scalar-snapshot.md`
- [x] D2 `git mv AspNetConventions.md docs/AspNetConventions.md`
- [x] D3 Update cross-references to `AspNetConventions.md` outside of `docs/` — use `docs/AspNetConventions.md`
- [x] D4 Verify: both files exist in `docs/`, no stale root copies, `grep` shows no un-prefixed refs outside `docs/`
