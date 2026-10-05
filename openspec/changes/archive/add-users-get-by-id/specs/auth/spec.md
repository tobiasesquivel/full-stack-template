# Delta for auth

## MODIFIED Requirements

### Requirement: Register User Endpoint

Full replacement of the existing register requirement covering all prior scenarios PLUS the Location header change. Previously: `UserInfo.Id` is now `UserInfo.AuthId`, Location header now points to `UsersController.GetUserById`.

**Endpoint:** `POST /api/v1/auth/register` → `201 Created`

**Request:**
| Field | Type | Req | Constraints | Error msg |
|-------|------|-----|-------------|-----------|
| Email | string | Yes | Email format, max 256 | `'Email' is not a valid email address.` |
| Password | string | Yes | 6–128 chars | `'Password' must be at least 6 characters.` |
| UserName | string | Yes | 1–50 chars, no leading/trailing space | `'UserName' must be between 1 and 50 characters.` |

**Response (201):**
```json
{
  "user": { "authId": "string", "email": "string", "userName": "string" },
  "accessToken": "string",
  "refreshToken": "string"
}
```

**Error (400):** Standard ProblemDetails with `errors` dictionary per field.

#### Scenario: Register a new user successfully
- GIVEN a request with valid Email, Password, and UserName
- WHEN the endpoint receives the POST request
- THEN an IdentityUser and domain `User` are created in a single EF Core transaction
- AND the Location header points to `GET /api/v1/users/{authId}`
- AND `User.AuthId` equals the created IdentityUser's Id
- AND a signed JWT access token (15 min) and refresh token (7d) are returned
- AND the response status is 201

#### Scenario: Register with existing email rolls back cleanly
- GIVEN a duplicate email
- WHEN the handler returns the conflict error before the transaction starts
- THEN no transaction is opened
- AND no database writes occur

#### Scenario: Register with existing email
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
- GIVEN a Password shorter than 6 characters
- WHEN the request is validated
- THEN the system returns 400 with a Password validation error
- AND no Identity operation is attempted

### Requirement: Login User Endpoint

Full replacement of the existing login requirement with updated UserInfo DTO. Previously: `UserInfo` had `int Id`, now uses `string AuthId`.

**Endpoint:** `POST /api/v1/auth/login` → `200 OK`

**Request:**
| Field | Type | Req | Constraints | Error msg |
|-------|------|-----|-------------|-----------|
| Email | string | Yes | Valid email format | `'Email' is not a valid email address.` |
| Password | string | Yes | Non-empty | `'Password' must not be empty.` |

**Response (200):**
```json
{
  "user": { "authId": "string", "email": "string", "userName": "string" },
  "accessToken": "string",
  "refreshToken": "string"
}
```

**Error (401):** ProblemDetails with title `"Invalid credentials"`.

#### Scenario: Login with correct credentials
- GIVEN a registered user with email and password
- WHEN a POST request contains the correct Email and Password
- THEN SignInManager validates the credentials
- AND the system returns 200 with tokens
- AND the response `UserInfo` contains `AuthId` (string) instead of `Id` (int)
- AND the handler projects `User.AuthId` directly

#### Scenario: Login with incorrect password
- GIVEN a registered user
- WHEN the request has correct email but wrong password
- THEN the system returns 401 with `"Invalid credentials"`
- AND no tokens are generated

#### Scenario: Login for non-existent email
- GIVEN no user exists for the given email
- WHEN a POST request is sent
- THEN the system returns 401 — same response as wrong password

## REMOVED Requirements

### Requirement: UserInfo.Id field in AuthResponse

(Reason: `User.Id` (int PK) is removed. `User.AuthId` (string) replaces it as the user identifier in `AuthResponse.UserInfo`. The nested record `UserInfo` now uses `string AuthId` instead of `int Id`.)
