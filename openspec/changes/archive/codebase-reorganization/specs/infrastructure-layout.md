# Spec: Infrastructure Layout Reorganization

## Purpose

Separate mixed concerns in `Infrastructure/Auth/` — JWT token services stay in `Auth/`, permission/authorization classes move to `Infrastructure/Authorization/`. The shared `ApiController` base moves to `Infrastructure/Common/`. All namespace references updated.

## Requirements

### Requirement: Split Auth into Auth + Authorization

Permission-related classes MUST move from `Infrastructure/Auth/` to `Infrastructure/Authorization/`. Their namespace MUST change from `TemplateApp.Infrastructure.Auth` to `TemplateApp.Infrastructure.Authorization`.

| Move to `Authorization/` | Stays in `Auth/` |
|--------------------------|-------------------|
| `PermissionRequirement.cs` | `ITokenService.cs` |
| `PermissionPolicyProvider.cs` | `JwtService.cs` |
| `PermissionAuthorizationHandler.cs` | `TokenPair.cs` |
| `HasPermissionAttribute.cs` | |

#### Scenario: Permission files relocated

- GIVEN `PermissionRequirement.cs`, `PermissionPolicyProvider.cs`, `PermissionAuthorizationHandler.cs`, `HasPermissionAttribute.cs` in `Infrastructure/Auth/`
- WHEN moved to `Infrastructure/Authorization/`
- THEN each file's namespace reads `TemplateApp.Infrastructure.Authorization`
- AND `Infrastructure/Auth/` contains only token-related files

#### Scenario: All references updated

- GIVEN controllers and handlers that import `TemplateApp.Infrastructure.Auth` for permission attributes
- WHEN the split is applied
- THEN all `using TemplateApp.Infrastructure.Auth` that import permission types MUST change to `using TemplateApp.Infrastructure.Authorization`
- AND `dotnet build` succeeds

### Requirement: DI registration stays intact

The permission-type namespace change MUST NOT break dependency injection registration. `PermissionPolicyProvider` and `PermissionAuthorizationHandler` MUST still be registered in `DependencyInjection.cs`.

#### Scenario: DI resolves correctly

- GIVEN `DependencyInjection.cs` registers permission types
- WHEN the namespace changes
- THEN the registration uses the new namespace
- AND `dotnet build` succeeds
- AND runtime resolves `IAuthorizationPolicyProvider` to `PermissionPolicyProvider`

### Requirement: Move ApiController to Common

`Infrastructure/ApiController.cs` MUST move to `Infrastructure/Common/ApiController.cs`. Namespace MUST change from `TemplateApp.Infrastructure` to `TemplateApp.Infrastructure.Common`.

#### Scenario: ApiController relocated

- GIVEN `Infrastructure/ApiController.cs`
- WHEN moved to `Infrastructure/Common/ApiController.cs`
- THEN namespace reads `TemplateApp.Infrastructure.Common`
- AND `Infrastructure/ApiController.cs` no longer exists

#### Scenario: All controller references updated

- GIVEN controllers inherit `ApiController` via `using TemplateApp.Infrastructure`
- WHEN the move is applied
- THEN all `using TemplateApp.Infrastructure` in controllers that depend on `ApiController` MUST update to `using TemplateApp.Infrastructure.Common`
- AND `dotnet build` succeeds

#### Scenario: Problem() mapping preserved

- GIVEN the moved `ApiController`
- WHEN `Problem(List<Error> errors)` is called
- THEN the error-to-HTTP-status mapping is identical — no behavior change

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | `Infrastructure/Authorization/` exists with 4 permission files | File review |
| AC-2 | `Infrastructure/Common/ApiController.cs` exists | File review |
| AC-3 | All permission `using` directives updated project-wide | `dotnet build` |
| AC-4 | All `ApiController` `using` directives updated project-wide | `dotnet build` |
| AC-5 | `dotnet test` passes — auth and permission tests unchanged | CI |
