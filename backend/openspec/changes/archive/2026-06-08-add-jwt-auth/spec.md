# Spec: Add JWT Authentication

## ADDED Requirements — auth-register

### Requirement: Register User Endpoint

**Endpoint:** `POST /api/v1/auth/register` → `201 Created`

**Request:**
| Field | Type | Req | Constraints | Error msg |
|-------|------|-----|-------------|-----------|
| Email | string | Yes | Email format, max 256 | `'Email' is not a valid email address.` |
| Password | string | Yes | 8–128 chars | `'Password' must be at least 8 characters.` |
| UserName | string | Yes | 1–50 chars, no leading/trailing space | `'UserName' must be between 1 and 50 characters.` |

**Response (201):**
```json
{
  "user": { "id": "guid", "email": "string", "userName": "string" },
  "accessToken": "string",
  "refreshToken": "string"
}
```

**Error (400):** Standard ProblemDetails with `errors` dictionary per field.

#### Scenario: Register a new user successfully
- GIVEN a request with valid Email, Password, and UserName
- WHEN the endpoint receives the POST request
- THEN an IdentityUser and domain User are created in a single EF Core transaction
- AND a signed JWT access token (15 min) and refresh token (7d) are returned
- AND the response status is 201

#### Scenario: Register with an existing email
- GIVEN a user already registered with `user@example.com`
- WHEN a POST request uses the same email
- THEN the system returns 400 with a duplicate-email error
- AND no IdentityUser or domain User is created

#### Scenario: Register with invalid email format
- GIVEN a request with `"not-an-email"` as Email
- WHEN the request is validated
- THEN the system returns 400 with validation error for the Email field
- AND no database write occurs

#### Scenario: Register with weak password
- GIVEN a Password shorter than 8 characters
- WHEN the request is validated
- THEN the system returns 400 with a Password validation error
- AND no Identity operation is attempted

---

## ADDED Requirements — auth-login

### Requirement: Login User Endpoint

**Endpoint:** `POST /api/v1/auth/login` → `200 OK`

**Request:**
| Field | Type | Req | Constraints | Error msg |
|-------|------|-----|-------------|-----------|
| Email | string | Yes | Valid email format | `'Email' is not a valid email address.` |
| Password | string | Yes | Non-empty | `'Password' must not be empty.` |

**Response (200):**
```json
{
  "user": { "id": "guid", "email": "string", "userName": "string" },
  "accessToken": "string",
  "refreshToken": "string"
}
```

**Error (401):** ProblemDetails with title `"Invalid credentials"`. No distinction between "user not found" and "wrong password" — same response for both.

#### Scenario: Login with correct credentials
- GIVEN a registered user with email and password
- WHEN a POST request contains the correct Email and Password
- THEN SignInManager validates the credentials
- AND the system returns 200 with a JWT access token and a refresh token
- AND the refresh token is persisted as a SHA256 hash in the database

#### Scenario: Login with incorrect password
- GIVEN a registered user
- WHEN the request contains the correct email but wrong password
- THEN the system returns 401 with `"Invalid credentials"` — no further detail
- AND no tokens are generated

#### Scenario: Login for non-existent email
- GIVEN no user exists for the given email
- WHEN a POST request is sent
- THEN the system returns 401 — same response as wrong password
- AND the response does not reveal whether the email exists

---

## ADDED Requirements — auth-refresh

### Requirement: Refresh Token Endpoint

**Endpoint:** `POST /api/v1/auth/refresh` → `200 OK`

**Request:**
| Field | Type | Req | Constraints | Error msg |
|-------|------|-----|-------------|-----------|
| RefreshToken | string | Yes | Non-empty | `'RefreshToken' must not be empty.` |

**Response (200):**
```json
{
  "accessToken": "string",
  "refreshToken": "string"
}
```

**Error (401):** ProblemDetails. Single generic error — never reveals whether the token exists, was revoked, or expired individually.

#### Scenario: Refresh with a valid, unexpired token
- GIVEN a valid unexpired refresh token for a registered user
- WHEN a POST request includes that token
- THEN the system validates the token hash against the database
- AND the old token is marked as revoked with `RevokedAt` set
- AND a new access token and refresh token are returned
- AND `ReplacedByToken` on the old row is set to the new token hash

#### Scenario: Reused token triggers theft cascade
- GIVEN a refresh token that was already revoked by rotation
- WHEN that same token value is used again
- THEN the system detects the old token in the database with a `ReplacedByToken` value
- AND ALL non-revoked refresh tokens for that user are revoked immediately
- AND the system returns 401 Unauthorized
- AND the user must re-authenticate with their password

#### Scenario: Expired refresh token is rejected
- GIVEN a refresh token whose `ExpiresAt` is in the past
- WHEN that token is presented
- THEN the system returns 401 Unauthorized
- AND no token rotation occurs

#### Scenario: Concurrent refresh race condition
- GIVEN two simultaneous refresh requests using the same valid token
- WHEN both requests pass the validation phase concurrently
- THEN the first request to commit succeeds and rotates the token
- AND the second request finds the token revoked and returns 401
- AND no theft cascade is triggered (single revocation, not reuse — detected by DB concurrency)

---

## ADDED Requirements — auth-jwt-generation

### Requirement: JwtService

**Interface:** `IJwtService` — `GenerateTokensAsync(AppIdentityUser user, CancellationToken ct) → TokenResult`

**TokenResult:** `{ AccessToken: string, RefreshToken: string, ExpiresAt: DateTime }`

**JWT Claims:**
| Claim | Key | Value |
|-------|-----|-------|
| Subject | sub | `user.Id` |
| JWT ID | jti | `Guid.NewGuid().ToString()` |
| Issued At | iat | Current UTC unix timestamp |
| Not Before | nbf | Same as `iat` |
| Expiration | exp | `iat + AccessTokenExpirationMinutes` |
| Issuer | iss | `JwtOptions.Issuer` |
| Audience | aud | `JwtOptions.Audience` |

**Algorithm:** HMAC-SHA256 (`SecurityAlgorithms.HmacSha256`)

**Lifetime:** Access token = 15 min. Refresh token = 7 days (random 256-bit string, not JWT).

#### Scenario: GenerateTokensAsync returns both tokens
- GIVEN an authenticated AppIdentityUser
- WHEN `GenerateTokensAsync` is called
- THEN a signed JWT access token is returned with all standard claims
- AND a cryptographically random refresh token string is returned
- AND the refresh token is NOT a JWT — it is a 256-bit random value

#### Scenario: JWT is signed and verifiable
- GIVEN a generated JWT access token
- WHEN the token is validated by ASP.NET Core's JWT bearer middleware
- THEN the signature matches using the configured HMAC-SHA256 key
- AND the token is accepted for authenticated requests

#### Scenario: Refresh token is generated as secure random
- GIVEN a call to generate a refresh token
- WHEN the token is created
- THEN it uses a cryptographically secure random number generator
- AND the result is a base64-encoded 256-bit value
- AND the system stores only its SHA256 hash in the database

---

## ADDED Requirements — auth-tokens

### Requirement: RefreshToken Entity

**Columns:**
| Column | Type | Constraints |
|--------|------|-------------|
| Id | Guid | PK, `IsNewGuid()` on create |
| Token | string(256) | Required, stores SHA256 hash |
| JwtId | string(128) | Required, links to the associated access token |
| AppIdentityUserId | string | Required, FK → `AspNetUsers.Id` (Cascade delete) |
| ExpiresAt | DateTime (UTC) | Required |
| CreatedAt | DateTime (UTC) | Required, set to `UtcNow` on create |
| RevokedAt | DateTime? (UTC) | Nullable, set when token is rotated or revoked |
| ReplacedByToken | string(256)? | Nullable, set when token is rotated |

**Indexes:**
- `IX_RefreshTokens_AppIdentityUserId_RevokedAt` — composite on `(AppIdentityUserId, RevokedAt)` for efficient user-scoped lookups and active-token queries
- `IX_RefreshTokens_Token` — unique index on `Token` for fast lookup by hash

**Fluent API configuration:** `Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs` — auto-discovered via `ApplyConfigurationsFromAssembly`.

### Requirement: JwtOptions (Configuration)

**Section:** `"Jwt"` in `appsettings.json`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Issuer | string | `""` | `iss` claim value |
| Audience | string | `""` | `aud` claim value |
| Key | string | `""` | HMAC-SHA256 signing key (min 32 chars enforced at startup) |
| AccessTokenExpirationMinutes | int | 15 | Lifetime of access tokens in minutes |
| RefreshTokenExpirationDays | int | 7 | Lifetime of refresh tokens in days |

**Binding:** `services.Configure<JwtOptions>(configuration.GetSection("Jwt"))` in `DependencyInjection.cs`.

#### Scenario: JwtOptions validated at startup
- GIVEN a misconfigured `Key` shorter than 32 characters
- WHEN the application starts
- THEN a startup validation exception is thrown
- AND the application fails to start, preventing insecure token signing

#### Scenario: Active tokens queryable by user
- GIVEN a user with multiple refresh tokens (some revoked, some active)
- WHEN querying active tokens for that user
- THEN the composite index on `(AppIdentityUserId, RevokedAt)` is used
- AND only tokens where `RevokedAt IS NULL` and `ExpiresAt > UtcNow` are returned
