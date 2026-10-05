# Auth Error Catalog Specification

## Purpose

Centralize auth-domain ErrorOr definitions in the static `AuthErrors` class, replacing inline `Error.*()` calls with consistent singleton errors across all auth handlers and services.

## Requirements

### Requirement: Static Error Definitions

`AuthErrors` MUST define each error as `public static Error X => Error.Y(code: "...", description: "...")` — no factory methods, no dynamic parameters.

| Member | Error Type | Code | Description |
|--------|-----------|------|-------------|
| `EmailAlreadyExists` | Conflict | `Auth.EmailAlreadyExists` | Email is already registered. |
| `InvalidCredentials` | Unauthorized | `Auth.InvalidCredentials` | Invalid credentials. |
| `UserNotFound` | NotFound | `Auth.UserNotFound` | User not found. |
| `InvalidToken` | Unauthorized | `Auth.InvalidToken` | Invalid refresh token. |
| `TokenTheft` | Unauthorized | `Auth.TokenTheft` | Token theft detected. All sessions revoked. |

#### Scenario: All errors accessible via static member access

- GIVEN any handler or service that imports `AuthErrors`
- WHEN it accesses `AuthErrors.{Member}`
- THEN the returned `Error` matches the type, code, and description in the table
- AND each access returns the same singleton instance

### Requirement: Handler Integration

Handlers MUST reference `AuthErrors` members at the following sites:

| File | Call Site | Error |
|------|-----------|-------|
| `RegisterCommandHandler.cs` | Duplicate email check | `AuthErrors.EmailAlreadyExists` |
| `LoginCommandHandler.cs` | Wrong password | `AuthErrors.InvalidCredentials` |
| `LoginCommandHandler.cs` | User not found | `AuthErrors.UserNotFound` |
| `LoginCommandHandler.cs` | Sign-in failed | `AuthErrors.InvalidCredentials` |
| `JwtService.cs` | Invalid token (×2) | `AuthErrors.InvalidToken` |
| `JwtService.cs` | Token reuse detected | `AuthErrors.TokenTheft` |

#### Scenario: Register with duplicate email returns `AuthErrors.EmailAlreadyExists`

- GIVEN an email already registered
- WHEN `RegisterCommandHandler` detects the conflict
- THEN it returns `AuthErrors.EmailAlreadyExists`
- AND the response code is `Auth.EmailAlreadyExists`

#### Scenario: Login with incorrect credentials returns `InvalidCredentials`

- GIVEN a registered user
- WHEN `LoginCommandHandler` receives wrong password OR username does not exist
- THEN it returns `AuthErrors.InvalidCredentials`
- AND the error does not distinguish between "wrong password" and "user not found"

#### Scenario: Login when user projection is missing returns `UserNotFound`

- GIVEN an identity user exists but no domain `User` projection
- WHEN `LoginCommandHandler` queries the projection
- THEN it returns `AuthErrors.UserNotFound`
- AND the response code is `Auth.UserNotFound` (auth-scoped, not user-scoped)

#### Scenario: JWT service rejects invalid tokens

- GIVEN an expired, malformed, or invalid refresh token
- WHEN `JwtService` validates it
- THEN it returns `AuthErrors.InvalidToken`
- AND no cascade occurs

#### Scenario: JWT service detects token reuse

- GIVEN a refresh token that was previously rotated
- WHEN that token value is presented again
- THEN `JwtService` returns `AuthErrors.TokenTheft`
- AND all non-revoked tokens for the user are batch-revoked
