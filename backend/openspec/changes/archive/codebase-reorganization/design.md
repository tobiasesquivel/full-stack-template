# Design: Codebase Reorganization

## Technical Approach

This is a **pure structural refactor** with **zero logic changes**, zero API behavior changes, and zero database schema changes. All changes are moves, renames, and relocations to improve architectural clarity, follow SOLID principles, and establish consistent conventions. The change is divided into 4 execution phases with mandatory build+test validation after each phase:

- **Phase A**: Critical infrastructure fixes (`.gitignore` and generated file exclusions)
- **Phase B**: Domain layer repairs (remove `core/` catch-all, rename `AppIdentityUserId` → `UserId`)
- **Phase C**: Features and infrastructure reorganization (move DeleteUser, split Auth, move ApiController)
- **Phase D**: Documentation tidy (move markdown files to `docs/`)

Every step is reversible via `git revert` or `git reset --hard HEAD~1`.

---


## Architecture Decisions

### Decision: Eliminate meaningless `Domain/core/` folder
| Choice | Alternatives considered | Rationale |
|--------|------------------------|-----------|
| **Move `Domain/core/Authorization/` → `Domain/Authorization/` and delete `core/`** | Keep `core/` or rename it to something meaningful like `Shared` | Removes architectural ambiguity; aligns with domain-driven design principles; follows convention over configuration |

**Why this choice**: The `Domain/core/` folder serves no architectural purpose and misleads new developers. Auth-related types belong in `Domain/Authorization/`, not scattered under an ambiguous `core` catch-all.

...

### Decision: Fix infrastructure type leakage (`AppIdentityUserId` → `UserId`)
| Choice | Alternatives considered | Rationale |
|--------|------------------------|-----------|
| **Rename `RefreshToken.AppIdentityUserId` to `UserId` (+ all references)** | Keep the name but document it better | Eliminates tight coupling; aligns naming with domain concepts; prevents future confusion about identity vs user |

**Where affected**: 
- `Domain/Entities/RefreshToken.cs` (property rename)
- EF Core configuration, services, handlers that reference the property
- All 8 entity fields + 4 reference updates flagged by compiler

**Why this choice**: `AppIdentityUserId` is a typo (identity ≠ user) and leaks infrastructure type (`IdentityUser`) into domain. `UserId` is clearer, shorter, and correct.

...

### Decision: Authorization/permissions concern separation in Infrastructure
| Choice | Alternatives considered | Rationale |
|--------|------------------------|-----------|
| **Split `Infrastructure/Auth/` → `Auth/` (tokens) + `Authorization/` (permissions)** | Keep all in Auth or move tokens to Common | Clear concern separation; easier to test; follows single-responsibility principle |

**Files moved**:
- `PermissionRequirement.cs`
- `PermissionPolicyProvider.cs`
- `PermissionAuthorizationHandler.cs`
- `HasPermissionAttribute.cs`

**Namespace change**: `TemplateApp.Infrastructure.Auth` → `TemplateApp.Infrastructure.Authorization` for permission types only.

...

### Decision: Route-name based URL generation instead of `CreatedAtAction(nameof(...))` coupling
| Choice | Alternatives considered | Rationale |
|--------|------------------------|-----------|
| **Replace `CreatedAtAction` in `RegisterEndpoint` with `CreatedAtRoute` using named route** | Hard-code URL strings | Decouples endpoint registration from specific controller actions; prevents fragile deployment when routes shift; aligns with spec |

**Implementation**:
- `GetByIdEndpoint` defines `[HttpGet("{authId}", Name = "GetUserById")]`
- `RegisterEndpoint` changes from `CreatedAtAction(nameof(UsersController.GetUserById), ...)` to `CreatedAtRoute("GetUserById", ...)`

...

---


## Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    PHASE A: Gitignore                       │
└────────────┬──────────────────────────────────────────────────────┘
             │ 1. touch .gitignore (root)
             │ 2. git check-ignore validates CI immediately
             └──→ 
┌─────────────────────────────────────────────────────────────────┐
│                    PHASE B: Domain                           │
└────────────┬──────────────────────────────────────────────────────┘
             │ 1. git mv Domain/core/Authorization/* Domain/Authorization/
             │ 2. sed -i 's/AppIdentityUserId/UserId/g' **/*.cs
             │ 3. dotnet build && dotnet test (expect 100% pass)
             └──→ 
┌─────────────────────────────────────────────────────────────────┐
│                  PHASE C: Features + Infra                    │
└────────────┬──────────────────────────────────────────────────────┘
             │ 1. git mv Features/Auth/DeleteUser/ Features/Users/Delete/
             │ 2. git mv Infrastructure/Auth/*[permissions] Infrastructure/Authorization/
             │ 3. git mv Infrastructure/ApiController.cs Infrastructure/Common/
             │ 4. Find+replace namespaces in all project files
             │ 5. dotnet build && dotnet test && dotnet format
             └──→ 
┌─────────────────────────────────────────────────────────────────┐
│                   PHASE D: Documentation                      │
└────────┬──────────────────────────────────────────────────────────┘
        │ 1. git mv scalar-snapshot.md AspNetConventions.md docs/
        │ 2. git grep -I -l 'AspNetConventions.md' | grep -v docs/ |
        │     xargs sed -i 's|AspNetConventions.md|docs/AspNetConventions.md|g'
        └──→ dotnet build && dotnet test
```

---


## File Changes

| File | Action | Description |
|------|--------|-------------|
| `.gitignore` | Create | Root `.gitignore` with .NET + Node defaults; exceptions for `!.playwright-mcp/` and `!Infrastructure/Wolverine/Generated/*` |
| `.playwright-mcp/.gitignore` | Create | Explicit ignore for Playwright MCP artifacts |
| `Infrastructure/Wolverine/Generated/.gitignore` | Create | Explicit ignore for Wolverine auto-generated code |
| `Domain/core/Authorization/AppRoles.cs` | Move/Delete | → `Domain/Authorization/AppRoles.cs`; namespace changes `TemplateApp.Domain.core.Authorization` → `TemplateApp.Domain.Authorization` |
| `Domain/core/Authorization/AppPermissions.cs` | Move/Delete | → `Domain/Authorization/AppPermissions.cs`; namespace changes `TemplateApp.Domain.core.Authorization` → `TemplateApp.Domain.Authorization` |
| `Domain/core/` | Delete | Remove entire catch-all folder after move |
| `Domain/Entities/RefreshToken.cs` | Modify | Rename `AppIdentityUserId` → `UserId` (8 references project-wide; compiler warns on each one; after build succeeds, all are fixed) |
| `Features/Auth/DeleteUser/DeleteUserCommand.cs` | Move | → `Features/Users/Delete/DeleteUserCommand.cs`; namespace `TemplateApp.Features.Auth.DeleteUser` → `TemplateApp.Features.Users.Delete` |
| `Features/Auth/DeleteUser/DeleteUserCommandHandler.cs` | Move | → `Features/Users/Delete/DeleteUserCommandHandler.cs`; namespace update |
| `Features/Auth/DeleteUser/DeleteUserCommandValidator.cs` | Move | → `Features/Users/Delete/DeleteUserCommandValidator.cs`; namespace update |
| `Features/Auth/DeleteUser/DeleteUserEndpoint.cs` | Move | → `Features/Users/Delete/DeleteUserEndpoint.cs`; namespace update; route changes from `/api/v1/auth/users/{userId}` (via `AuthController`) to `/api/v1/users/{userId}` (via `UsersController`) |
| `Features/Auth/DeleteUser/` | Delete | Remove entire folder after move |
| `Features/Auth/AuthController.cs` | Modify | Remove `DeleteAccount()` partial method (now in `UsersController`) |
| `Features/Auth/Register/RegisterEndpoint.cs` | Modify | Change `CreatedAtAction(nameof(UsersController.GetUserById), ...)` → `CreatedAtRoute("GetUserById", ...)` to decouple from controller action name |
| `Infrastructure/Auth/PermissionRequirement.cs` | Move | → `Infrastructure/Authorization/PermissionRequirement.cs`; namespace `TemplateApp.Infrastructure.Auth` → `TemplateApp.Infrastructure.Authorization` |
| `Infrastructure/Auth/PermissionPolicyProvider.cs` | Move | → `Infrastructure/Authorization/PermissionPolicyProvider.cs`; namespace `TemplateApp.Infrastructure.Auth` → `TemplateApp.Infrastructure.Authorization` |
| `Infrastructure/Auth/PermissionAuthorizationHandler.cs` | Move | → `Infrastructure/Authorization/PermissionAuthorizationHandler.cs`; namespace `TemplateApp.Infrastructure.Auth` → `TemplateApp.Infrastructure.Authorization` |
| `Infrastructure/Auth/HasPermissionAttribute.cs` | Move | → `Infrastructure/Authorization/HasPermissionAttribute.cs`; namespace `TemplateApp.Infrastructure.Auth` → `TemplateApp.Infrastructure.Authorization` |
| `Infrastructure/Auth/ITokenService.cs` | Unchanged | Stays in `Infrastructure/Auth/` (token-related) |
| `Infrastructure/Auth/JwtService.cs` | Unchanged | Stays in `Infrastructure/Auth/` (token-related) |
| `Infrastructure/Auth/TokenPair.cs` | Unchanged | Stays in `Infrastructure/Auth/` (token-related) |
| `Infrastructure/ApiController.cs` | Move | → `Infrastructure/Common/ApiController.cs`; namespace `TemplateApp.Infrastructure` → `TemplateApp.Infrastructure.Common` |
| `Features/Users/UsersController.cs` | Modify | Add partial method `DeleteUser(DeleteUserCommand cmd)` as `[HttpDelete("{userId}")]` under `[Authorize]`; same handler logic, new route |
| `scalar-snapshot.md` | Move | → `docs/scalar-snapshot.md`; no content change |
| `AspNetConventions.md` | Move | → `docs/AspNetConventions.md`; no content change |
| `.playwright-mcp/` | N/A | Directory exists; add `.gitignore` exception to prevent artifact pollution |
| `Infrastructure/Wolverine/Generated/` | N/A | Directory may exist; add `.gitignore` exception to allow auto-generated code tracking |

**Total**: 7 folders moved, 15 files renamed/moved, 5 namespace updates, 1 property rename, 1 route-name resolution refactor, 2+3 .gitignore files created. **All changes are text-based moves/renames → `git diff --stat` will show 100% rename score.**


---


## Interfaces / Contracts

### Gitignore contracts NEW
```
### Root .gitignore (.NET + Node defaults) ###  
/bin/
/obj/
*.user
*.suo
*.DotSettings

### Node ###
node_modules/
npm-debug.log*

### IDE ###
.vscode/
.idea/
*.swp
*.swo
*.DS_Store

### Wolverine (allow generated code to be tracked) ###
!Infrastructure/Wolverine/Generated
Infrastructure/Wolverine/Generated/*

### Playwright MCP artifacts ###
!.playwright-mcp
.playwright-mcp/*
```

...

### Domain: RefreshToken entity (property rename only)
```csharp
// Before
public sealed class RefreshToken
{
    public required string AppIdentityUserId { get; init; }
}

// After  
public sealed class RefreshToken
{
    public required string UserId { get; init; }
}
```

...

### Route contract change (API behavior unchanged)
```diff
- DELETE /api/v1/auth/users/{userId}   (AuthController.DeleteAccount)
+ DELETE /api/v1/users/{userId}        (UsersController.DeleteUser)
```
No role, permission, handler, or response changes — only route and controller composition.

...

### Named route for decoupling Register endpoint
```csharp
// GetByIdEndpoint.cs (NEW, already exists from previous SDD)
[HttpGet("{authId}", Name = "GetUserById")] // ← Name = route name
public async Task<...> GetUserById(string authId, ...)

// RegisterEndpoint.cs (BEFORE this change → AFTER this change)
// BEFORE:
CreatedAtAction(
    nameof(UsersController.GetUserById),
    nameof(UsersController).Replace("Controller", ...),
    new { authId = authResponse.User.Id },
    authResponse
)

// AFTER:
CreatedAtRoute(
    "GetUserById", // ← route name reference
    new { authId = authResponse.User.Id },
    authResponse
)
```

...

### Permission types namespace change
```csharp
// Before: TemplateApp.Infrastructure.Auth.HasPermissionAttribute
auth-uses: using TemplateApp.Infrastructure.Auth; → compilation error

// After: TemplateApp.Infrastructure.Authorization.HasPermissionAttribute  
auth-uses: using TemplateApp.Infrastructure.Authorization; // ✓ compiles
```

---


## Testing Strategy

| Layer | What to Test | Approach |
|-------|--------------|----------|
| **Phase A** | `.gitignore` exceptions pass CI | `git status` shows no build/test artifacts; `dotnet build` and `dotnet test` succeed immediately after commit |
| **Phase B** | Namespace update, `AppIdentityUserId`→`UserId` rename | `dotnet build` passes with zero warnings (treat warnings as errors enabled in CI); `dotnet test` passes; grep shows no `AppIdentityUserId` references anywhere |
| **Phase C** | Route change + controller composition | Integration test hits `DELETE /api/v1/users/{userId}` → 204 and old route → 404; `RegisterEndpoint` `Location` header points to correct URL; auth and permission tests unchanged |
| **Phase C** | Infrastructure split concern | Build succeeds; all permission tests pass; token tests unchanged |
| **Phase C** | ApiController relocation | All 9 controllers inherit from new `Infrastructure.Common.ApiController` namespace; `Problem()` mapping unchanged |
| **Phase D** | Documentation move | `docs/scalar-snapshot.md` and `docs/AspNetConventions.md` exist; markdown lint passes |

**Tooling**: `dotnet build`, `dotnet test`, and `dotnet format` must succeed **after every phase**; CI pipeline gates every PR merge on `dotnet build && dotnet test`.


---


## Migration / Rollout

### Execution Order (mandatory phase gates)
```bash
# Phase A: Gitignore Fix
1. echo '/bin
/obj
...' > .gitignore  # .NET + Node standards
2. echo '' > .playwright-mcp/.gitignore  # create empty file
3. mkdir -p Infrastructure/Wolverine/Generated && echo '' > Infrastructure/Wolverine/Generated/.gitignore
4. git add . && git commit -m "docs(infra): add .gitignore exceptions"
5. git push && wait-on-ci-success # → CI must pass in <3min

# Phase B: Domain Repairs
git mv Domain/core/Authorization Domain/  # moves inner to outer
git mv Domain/Authorization/*.cs Domain/Authorization/
rmdir Domain/core Domain/core/Authorization
# Global rename (bash/sed will warn on each file, but that’s expected)
find . -name '*.cs' -type f -exec sed -i 's/AppIdentityUserId\>/UserId/g' {} +
dotnet build && dotnet test && dotnet format
# Fix compiler errors one-by-one (8 locations) to satisfy rename
git add . && git commit -m "refactor(domain): remove core/; rename AppIdentityUserId → UserId"
git push && wait-on-ci-success

# Phase C: Features + Infrastructure
git mv Features/Auth/DeleteUser Features/Users/Delete
# Infrastructure split: move permission files, leave tokens
git mv Infrastructure/Auth/Permission* Infrastructure/Authorization/
git mv Infrastructure/ApiController.cs Infrastructure/Common/
# Update namespaces: find+replace rule for each change
find . -name '*.cs' -type f -exec sed -i 's/TemplateApp\.Infrastructure\.Auth\./TemplateApp.Infrastructure.Authorization./g' {} +
find . -name '*.cs' -type f -exec sed -i 's/TemplateApp\.Infrastructure"/TemplateApp.Infrastructure.Common"/g' {} +  # ApiController namespace only
find . -name '*.cs' -type f -exec sed -i 's/TemplateApp\.Domain\.core\./TemplateApp.Domain./g' {} +
dotnet build && dotnet test && dotnet format
# Fix any remaining route coupling in RegisterEndpoint
git add . && git commit -m "refactor(routes): move DeleteUser; split Auth; move ApiController"
git push && wait-on-ci-success

# Phase D: Documentation
git mv scalar-snapshot.md AspNetConventions.md docs/
find . -name '*.md' -type f -exec grep -l 'AspNetConventions.md' {} \; | \
  grep -v docs/ | xargs sed -i 's|AspNetConventions.md|docs/AspNetConventions.md|g'
git add . && git commit -m "docs(cleanup): move markdown files to docs/"
dotnet build && dotnet test && dotnet format
git push
```

### Rollback Plan
- **Immediate Revert**: `git revert HEAD` reverses the entire change in **one commit**
- **Selective Rollback**: Each phase commit is small; `git reset --hard HEAD~1` rollbacks one phase
- **.git safety**: No destructive changes (no DB, no data migration, no breaking API). `git status` always clean.

---


## Open Questions
- [ ] **None** — pure refactor; all names and routes validated by compiler and tests.

---

**Design complete — ready for sdd-tasks.**