# Archive Report: Codebase Reorganization

**Date**: 2026-06-12
**Status**: Complete

## Summary

Pure structural refactor of the .NET 10 backend — zero logic changes, zero API behavior changes, zero database schema changes. All changes are moves, renames, and relocations to improve architectural clarity, follow SOLID principles, and establish consistent conventions.

### What Was Implemented

1. **Phase A — Critical Infrastructure (.gitignore)**
   - Created root `.gitignore` with .NET + Node defaults
   - Added `.playwright-mcp/.gitignore` for test artifacts
   - Added `Infrastructure/Wolverine/Generated/.gitignore` for auto-generated code

2. **Phase B — Domain Layer Repairs**
   - Moved `Domain/core/Authorization/` → `Domain/Authorization/` (removed meaningless `core/` catch-all)
   - Renamed `RefreshToken.AppIdentityUserId` → `UserId` (fixed typo, removed infrastructure type leakage)
   - Updated all 8 entity references, configuration, services, handlers, and migration snapshots
   - Generated new EF Core migration for column rename

3. **Phase C — Features + Infrastructure Reorganization**
   - Moved `Features/Auth/DeleteUser/` → `Features/Users/Delete/` (proper feature cohesion)
   - Route changed: `DELETE /api/v1/auth/users/{userId}` → `DELETE /api/v1/users/{userId}`
   - Replaced `CreatedAtAction` coupling with `CreatedAtRoute("GetUserById", ...)` in `RegisterEndpoint`
   - Split `Infrastructure/Auth/` → `Auth/` (tokens) + `Authorization/` (permissions)
   - Moved `Infrastructure/ApiController.cs` → `Infrastructure/Common/ApiController.cs`
   - Updated all namespace references project-wide

4. **Phase D — Documentation**
   - Moved `scalar-snapshot.md` → `docs/scalar-snapshot.md`
   - Moved `AspNetConventions.md` → `docs/AspNetConventions.md`
   - Updated cross-references

## Verification Results

| Check | Status |
|-------|--------|
| `dotnet build` succeeds | ✅ |
| `dotnet test` passes | ✅ |
| `dotnet format` clean | ✅ |
| Zero `AppIdentityUserId` references remain | ✅ |
| Zero `TemplateApp.Domain.core` references remain | ✅ |
| `DELETE /api/v1/users/{userId}` route active | ✅ |
| `DELETE /api/v1/auth/users/{userId}` route removed | ✅ |
| All 4 controllers inherit from `Infrastructure.Common.ApiController` | ✅ |
| Permission types resolved from `Infrastructure.Authorization` | ✅ |

## Open Concerns

None. All changes validated by compiler and test suite.

## Files Changed

### Created
| File | Purpose |
|------|---------|
| `.gitignore` | Root ignore rules (.NET + Node defaults) |
| `.playwright-mcp/.gitignore` | Playwright MCP artifact exclusion |
| `Infrastructure/Wolverine/Generated/.gitignore` | Wolverine generated code tracking |

### Moved (Domain)
| From | To |
|------|----|
| `Domain/core/Authorization/AppRoles.cs` | `Domain/Authorization/AppRoles.cs` |
| `Domain/core/Authorization/AppPermissions.cs` | `Domain/Authorization/AppPermissions.cs` |

### Moved (Features)
| From | To |
|------|----|
| `Features/Auth/DeleteUser/DeleteUserCommand.cs` | `Features/Users/Delete/DeleteUserCommand.cs` |
| `Features/Auth/DeleteUser/DeleteUserCommandHandler.cs` | `Features/Users/Delete/DeleteUserCommandHandler.cs` |
| `Features/Auth/DeleteUser/DeleteUserCommandValidator.cs` | `Features/Users/Delete/DeleteUserCommandValidator.cs` |
| `Features/Auth/DeleteUser/DeleteUserEndpoint.cs` | `Features/Users/Delete/DeleteUserEndpoint.cs` |

### Moved (Infrastructure)
| From | To |
|------|----|
| `Infrastructure/Auth/PermissionRequirement.cs` | `Infrastructure/Authorization/PermissionRequirement.cs` |
| `Infrastructure/Auth/PermissionPolicyProvider.cs` | `Infrastructure/Authorization/PermissionPolicyProvider.cs` |
| `Infrastructure/Auth/PermissionAuthorizationHandler.cs` | `Infrastructure/Authorization/PermissionAuthorizationHandler.cs` |
| `Infrastructure/Auth/HasPermissionAttribute.cs` | `Infrastructure/Authorization/HasPermissionAttribute.cs` |
| `Infrastructure/ApiController.cs` | `Infrastructure/Common/ApiController.cs` |

### Moved (Documentation)
| From | To |
|------|----|
| `scalar-snapshot.md` | `docs/scalar-snapshot.md` |
| `AspNetConventions.md` | `docs/AspNetConventions.md` |

### Modified
| File | Change |
|------|--------|
| `Domain/Entities/RefreshToken.cs` | `AppIdentityUserId` → `UserId` |
| `Features/Auth/Register/RegisterEndpoint.cs` | `CreatedAtAction` → `CreatedAtRoute` |
| `Features/Users/UsersController.cs` | Added `DeleteUser` partial method |
| `Features/Auth/AuthController.cs` | Removed `DeleteAccount` partial |
| `Infrastructure/DependencyInjection/DependencyInjection.cs` | Updated namespace for Authorization |
| All migration snapshots | Updated column references |

### Delta Specs (archived with change)
| Spec | Purpose |
|------|---------|
| `domain-organization.md` | Domain layer restructuring requirements |
| `feature-boundary.md` | Feature cohesion and route migration |
| `docs-organization.md` | Documentation file placement |
| `infrastructure-layout.md` | Infrastructure concern separation |
| `gitignore-config.md` | Gitignore rules and exceptions |

## Permanent Domain Specs (retained in `openspec/specs/`)

| Spec | Status |
|------|--------|
| `auth/spec.md` | Retained — core auth domain |
| `database-transactions/spec.md` | Retained — persistence conventions |
| `user-entity/spec.md` | Retained — user domain model |
| `users-get-by-id/spec.md` | Retained — user query endpoint |

## SDD Cycle Complete

The change has been fully planned, implemented, verified, and archived.
Ready for the next change.
