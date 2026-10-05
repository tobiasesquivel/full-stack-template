# Spec: User Entity — AuthId as Primary Key

## Purpose

Define the User domain entity where `AuthId` (string) is the primary key and foreign key to ASP.NET Identity's `AspNetUsers.Id`. The legacy `User.Id` (int auto-increment) is removed.

## Requirements

### Requirement: User entity — AuthId is primary key

The `User` entity MUST use `AuthId` (string) as its primary key. `User.Id` (int auto-increment) MUST be removed.

**Entity changes:**
- Remove `Id` property (`int`, auto-increment PK)
- `AuthId` becomes the PK via Fluent API (`HasKey(u => u.AuthId)`)
- `AuthId` is FK to `AspNetUsers.Id` (string)

#### Scenario: Schema reflects new PK

- GIVEN the EF Core model
- WHEN migrations are generated
- THEN `User` table MUST have `AuthId` as primary key column of type `text`
- AND there MUST be NO `Id` column (int, serial)
- AND `AuthId` MUST have a foreign key to `AspNetUsers.Id`

### Requirement: UserConfiguration — Fluent API update

`UserConfiguration.cs` MUST configure `AuthId` as the primary key via `HasKey(u => u.AuthId)`. The `HasIndex(u => u.AuthId)` MUST be removed (PK is already unique).

#### Scenario: Fluent configuration

- GIVEN `UserConfiguration` class
- THEN `HasKey(u => u.AuthId)` is present
- AND `HasIndex(u => u.AuthId)` is absent
- AND the FK to `AspNetUsers.Id` is preserved

### Requirement: Register handler — no Id generation needed

The `RegisterCommandHandler` MUST NOT set `User.Id` on creation. `User` is created with `AuthId = identityUser.Id` only.

#### Scenario: User creation on register

- GIVEN a successful IdentityUser creation
- WHEN creating the domain `User`
- THEN `User.AuthId = identityUser.Id`
- AND no `User.Id` property is set

### Requirement: DeleteUser — ownership validation uses AuthId

The DeleteUser handler MUST validate ownership by comparing the request user's `AuthId` with the resource owner's `AuthId`.

#### Scenario: Owner can delete own user

- GIVEN an authenticated user who owns the account
- WHEN deleting their own user
- THEN ownership check compares `user.AuthId` with the authenticated user's ID

#### Scenario: Non-owner cannot delete

- GIVEN an authenticated user who does NOT own the account
- WHEN attempting to delete
- THEN ownership check fails
- AND the system returns Forbidden/Unauthorized

### Requirement: AuthResponse.UserInfo uses AuthId

`AuthResponse.UserInfo` MUST use `string AuthId` instead of `int Id`. The nested record `UserInfo` now has `string AuthId` as the user identifier.

#### Scenario: UserInfo in auth responses

- GIVEN any auth response (register or login)
- THEN the `UserInfo` record contains `string AuthId`
- AND no `int Id` field is present

## DTO Contracts

```csharp
// Domain entity
public sealed class User
{
    public string AuthId { get; } = string.Empty; // PK + FK to AspNetUsers.Id
    public string UserName { get; } = null!;
    public AppIdentityUser? IdentityUser { get; set; }
}

// Auth response
public sealed record AuthResponse(UserInfo User, string AccessToken, string RefreshToken)
{
    public sealed record UserInfo(string AuthId, string Email, string UserName, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
}
```

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | User table has AuthId as PK (text), no Id column | Migration inspection |
| AC-2 | UserConfiguration HasKey(AuthId), no HasIndex(AuthId) | Code review |
| AC-3 | Register handler does not set User.Id | Code review |
| AC-4 | DeleteUser ownership check uses AuthId | Code review |
| AC-5 | AuthResponse.UserInfo uses string AuthId | Code review |
