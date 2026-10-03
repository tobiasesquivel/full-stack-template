# Delta for user-entity

## ADDED Requirements

### Requirement: User entity — AuthId is primary key

The `User` entity MUST use `AuthId` (string) as its primary key. `User.Id` (int auto-increment) MUST be removed.

**Entity changes:**
- Remove `Id` property (`int`, auto-increment PK)
- `AuthId` becomes the PK via data annotations or Fluent API
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

## REMOVED Requirements

### Requirement: User.Id as integer primary key

(Reason: `User.Id` (int auto-increment PK) is replaced by `User.AuthId` (string PK + FK to AspNetUsers). This ensures the domain entity identifier is the same as the Identity user ID, eliminating the need to map between `int` and `string` identifiers.)
