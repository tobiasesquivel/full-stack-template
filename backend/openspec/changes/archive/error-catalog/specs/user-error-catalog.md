# User Error Catalog Specification

## Purpose

Centralize user-domain ErrorOr errors in the static `UserErrors` class with a factory method for parameterized user-ID errors, replacing inline `Error.NotFound()` calls and fixing the semantic misuse of `Auth.UserNotFound` in user handlers.

## Requirements

### Requirement: Factory Method

`UserErrors` MUST provide a factory method accepting a `string authId` parameter:

```
public static Error NotFound(string authId)
```

It MUST return `Error.NotFound(code: "User.NotFound", description: $"User with ID '{authId}' not found.")`.

#### Scenario: `NotFound` returns parameterized error

- GIVEN a string value representing a user's AuthId
- WHEN `UserErrors.NotFound(authId)` is called
- THEN the returned `Error` has code `User.NotFound`
- AND the description includes the formatted auth ID

### Requirement: Handler Integration

User query and command handlers MUST call `UserErrors.NotFound(authId)` at the following sites:

| File | Call Site | Before | After |
|------|-----------|--------|-------|
| `GetUserByIdQueryHandler.cs` | User not found by AuthId | inline `Error.NotFound()` | `UserErrors.NotFound(query.AuthId)` |
| `DeleteUserCommandHandler.cs` | User to delete not found | `Auth.UserNotFound` (wrong domain) | `UserErrors.NotFound(command.UserId)` |

#### Scenario: GetById returns `UserErrors.NotFound` for missing user

- GIVEN an AuthId that does not match any domain User
- WHEN `GetUserByIdQueryHandler` queries for it
- THEN it returns `UserErrors.NotFound(query.AuthId)`
- AND the response code is `User.NotFound`
- AND the description contains the queried AuthId

#### Scenario: Delete uses `UserErrors.NotFound` instead of `Auth.UserNotFound` (semantic fix)

- GIVEN an AuthId with no corresponding identity user to delete
- WHEN `DeleteUserCommandHandler` looks up the user
- THEN it returns `UserErrors.NotFound(command.UserId)`
- AND the error code is `User.NotFound` — not `Auth.UserNotFound`
- AND the response correctly scopes the error to the user domain
