# Users — Get By Id Specification

## Purpose

Secure public-profile lookup by `AuthId` for authenticated clients. No PII exposure.

## Requirements

### Requirement: GetUserById endpoint

**Endpoint:** `GET /api/v1/users/{authId}` → `200 OK`

**Auth:** `[Authorize]` — valid JWT required on the `UsersController`.

**Response (200):**
| Field | Type |
|-------|------|
| authId | string |
| userName | string |

**Error (404):** ProblemDetails — `code: "Entity.NotFound"`, `description: "User not found"`.

#### Scenario: Existing user profile returned

- GIVEN an authenticated client and a registered user with `AuthId = "abc-123"`
- WHEN GET `/api/v1/users/abc-123`
- THEN status 200 with `{ "authId": "abc-123", "userName": "johndoe" }`

#### Scenario: Non-existing authId returns 404

- GIVEN an authenticated client
- WHEN GET `/api/v1/users/nonexistent`
- THEN status 404 with ProblemDetails

#### Scenario: Unauthenticated request rejected

- GIVEN no valid JWT token
- WHEN GET `/api/v1/users/{authId}`
- THEN status 401

#### Scenario: Response excludes email and PII

- GIVEN any successful response
- THEN the JSON MUST contain only `authId` and `userName`
- AND no `email` field present
- AND handler uses dedicated `GetUserByIdQueryResult` DTO

### Requirement: Handler uses AsNoTracking

The query handler MUST project read-only with `AsNoTracking()`.

#### Scenario: Read pattern enforced

- GIVEN `GetUserByIdQueryHandler`
- WHEN querying the database
- THEN `.Select()` projects only `AuthId` + `UserName`
- AND `.AsNoTracking()` is called
- AND `CancellationToken` is forwarded

### Requirement: Controller structure

`UsersController` MUST be a partial class with route + authorization in base file.

#### Scenario: Controller definition

- GIVEN `UsersController` class
- THEN `[Route("api/v1/users")]`, `[Authorize]`
- AND inherits `ApiController`
- AND constructor injects `IMessageBus` only

## DTO Contracts

```csharp
// Features/Users/GetById/GetUserByIdQuery.cs
public sealed record GetUserByIdQuery(string AuthId);

// Features/Users/GetById/GetUserByIdQueryResult.cs
public sealed record GetUserByIdQueryResult(string AuthId, string UserName);
```

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | 200 for existing user | Integration test |
| AC-2 | Response has only authId + userName | Assert fields |
| AC-3 | No email field | Assert absence |
| AC-4 | 404 for non-existent authId | Integration test |
| AC-5 | 401 without valid JWT | Integration test |
| AC-6 | Handler uses AsNoTracking + Select | Code review |
