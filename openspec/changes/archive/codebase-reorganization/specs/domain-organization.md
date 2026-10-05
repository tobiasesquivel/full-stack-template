# Spec: Domain Organization

## Purpose

Eliminate the ambiguous `Domain/core/` catch-all folder and fix infrastructure type leakage in the `RefreshToken` entity. All domain logic moves to semantically named folders under `Domain/`.

## Requirements

### Requirement: Move `Domain/core/Authorization/` to `Domain/Authorization/`

`Domain/core/Authorization/` MUST be moved to `Domain/Authorization/`. The `Domain/core/` directory MUST be deleted after the move. All namespaces MUST update from `TemplateApp.Domain.core.Authorization` to `TemplateApp.Domain.Authorization`.

#### Scenario: Files moved with correct namespace

- GIVEN `Domain/core/Authorization/AppRoles.cs` and `AppPermissions.cs`
- WHEN moved to `Domain/Authorization/`
- THEN `Domain/core/Authorization/` no longer exists
- AND `Domain/core/` no longer exists
- AND namespace in both files reads `TemplateApp.Domain.Authorization`

#### Scenario: All project references updated

- GIVEN files referencing `TemplateApp.Domain.core.Authorization`
- WHEN the move and rename complete
- THEN `dotnet build` succeeds
- AND `dotnet test` passes with zero failures

### Requirement: Rename `RefreshToken.AppIdentityUserId` to `UserId`

The `RefreshToken` entity property `AppIdentityUserId` MUST be renamed to `UserId`. All project-wide references (entity, configuration, services, handlers, migrations) MUST be updated.

#### Scenario: Entity property renamed

- GIVEN `Domain/Entities/RefreshToken.cs` with `AppIdentityUserId`
- WHEN the rename is applied
- THEN `AppIdentityUserId` becomes `UserId`
- AND `UserId` retains its `required` modifier

#### Scenario: Fluent configuration updated

- GIVEN `RefreshTokenConfiguration.cs` references `AppIdentityUserId`
- WHEN the rename is applied
- THEN all `.Property()`, `.HasIndex()`, and `.HasForeignKey()` calls use `rt => rt.UserId`

#### Scenario: JwtService references updated

- GIVEN `JwtService.cs` sets and queries `AppIdentityUserId`
- WHEN the rename is applied
- THEN all references use `UserId` instead

#### Scenario: Handler references updated

- GIVEN `DeleteUserCommandHandler.cs` queries `rt.AppIdentityUserId`
- WHEN the rename is applied
- THEN the `Where` clause uses `rt.UserId`

#### Scenario: Migration snapshot updated

- GIVEN migration snapshots reference `AppIdentityUserId`
- WHEN the rename is applied
- THEN all snapshot files (`*.Designer.cs`, `ModelSnapshot.cs`) MUST use the new column name
- AND a new migration MUST be generated to rename the column

#### Scenario: Build and tests pass

- GIVEN all references are updated
- WHEN `dotnet build` runs
- THEN compilation succeeds
- AND `dotnet test` passes with zero failures

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | `Domain/core/` deleted, `Domain/Authorization/` exists | File review |
| AC-2 | `AppRoles.cs` and `AppPermissions.cs` namespace = `TemplateApp.Domain.Authorization` | Code review |
| AC-3 | `RefreshToken.UserId` replaces `AppIdentityUserId` | Code review |
| AC-4 | Configuration, services, handlers all use `UserId` | `dotnet build` |
| AC-5 | New migration renames column | Migration inspection |
