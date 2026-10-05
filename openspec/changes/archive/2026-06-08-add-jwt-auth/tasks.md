# Tasks: Add JWT Authentication

## Overview

5 tasks, ~450-550 estimated lines. Implementation order follows dependency chain:
T1 (infrastructure) → T2 (data model) → T3 (register) → T4 (login) → T5 (refresh)

---

## Task: T1 — JwtOptions + JwtService infrastructure

**Prerequisites**: none

**Files to create:**
- `Infrastructure/Options/JwtOptions.cs` — sealed record with Issuer, Audience, Key, AccessTokenExpirationMinutes, RefreshTokenExpirationDays
- `Infrastructure/Auth/ITokenService.cs` — interface with `GenerateTokensAsync(AppIdentityUser, CancellationToken) → TokenResult` and `RedeemTokenAsync(string, CancellationToken) → TokenResultOrError`
- `Infrastructure/Auth/JwtService.cs` — implementation with JWT generation (HMAC-SHA256, 15 min), refresh token generation (256-bit secure random), SHA256 hashing for storage

**Files to modify:**
- `Infrastructure/DependencyInjection/DependencyInjection.cs` — add `AddAppJwt(this IServiceCollection, IConfiguration)` extension method:
  - Validate JwtOptions at startup (Key >= 32 chars)
  - Configure `services.Configure<JwtOptions>(config.GetSection("Jwt"))`
  - Add authentication with JwtBearer (ValidateIssuer, ValidateAudience, ValidateLifetime, ClockSkew = Zero)
  - Register `ITokenService` → `JwtService` as Singleton
- `Program.cs` — add `app.UseAuthentication()` before `app.UseAuthorization()` (if not already there)
- `appsettings.json` — add `"Jwt"` section with placeholder values for TemplateApp
- `appsettings.Development.json` — add `"Jwt"` section with dev values
- `appsettings.example.json` — update `"Jwt"` section documented

**Verification:** `dotnet build` succeeds. Application starts with valid JWT config. Fails at startup with key < 32 chars.

**Estimate:** ~150 lines (4 new files, 4 modified)

**Commit message:** `feat(infra): add JwtOptions, ITokenService, JwtService, and JWT middleware`

---

## Task: T2 — RefreshToken entity + EF configuration + migration

**Prerequisites**: T1

**Files to create:**
- `Domain/Entities/RefreshToken.cs` — entity with Id (Guid), Token (string 256), JwtId (string 128), AppIdentityUserId (string 450), ExpiresAt (DateTime), CreatedAt (DateTime), RevokedAt (DateTime?), ReplacedByToken (string 256?)
- `Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs` — Fluent API with:
  - PK on Id
  - Required fields with MaxLength
  - Unique index `IX_RefreshTokens_Token` on Token
  - Composite index `IX_RefreshTokens_AppIdentityUserId_RevokedAt` on (AppIdentityUserId, RevokedAt)
  - FK `FK_RefreshTokens_AspNetUsers_AppIdentityUserId` with Cascade delete

**Files to modify:**
- `Infrastructure/Persistence/AppDbContext.cs` — add `public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();`

**Migration:**
- `dotnet ef migrations add AddJwtAuth -o Infrastructure/Persistence/Migrations`

**Verification:** `dotnet build` succeeds. Migration generates correct SQL with RefreshToken table, indexes, FK.

**Estimate:** ~80 lines (2 new files, 1 modified + migration)

**Commit message:** `feat(db): add RefreshToken entity with Fluent API config and migration`

---

## Task: T3 — Auth base controller + Register endpoint

**Prerequisites**: T1, T2

**Files to create:**
- `Features/Auth/AuthController.cs` — partial base controller:
  ```csharp
  [Route("api/v1/auth")]
  [Tags("Auth")]
  public partial class AuthController : ControllerBase
  {
      protected readonly ITokenService TokenService;
      protected readonly UserManager<AppIdentityUser> UserManager;
      protected readonly AppDbContext Db;

      public AuthController(ITokenService ts, UserManager<AppIdentityUser> um, AppDbContext db)
      {
          TokenService = ts;
          UserManager = um;
          Db = db;
      }
  }
  ```
  - Wait — partial class sharing constructor DI won't work well. Better approach: each endpoint partial file has its own injected dependencies. Let me reconsider.

  **Revised approach:**
- `Features/Auth/AuthController.cs` — minimal base:
  ```csharp
  [Route("api/v1/auth")]
  [Tags("Auth")]
  public partial class AuthController : ControllerBase { }
  ```
- `Features/Auth/Register/RegisterEndpoint.cs` — partial class with injected ITokenService, UserManager<AppIdentityUser>, SignInManager<AppIdentityUser>, AppDbContext. Action method `Register(RegisterRequest request, CancellationToken ct)` returning `IActionResult`.
  - Creates AppIdentityUser via UserManager.CreateAsync
  - Creates domain User via Db.Users.AddAsync
  - Transaction scoped to ensure both succeed or both fail
  - Calls TokenService.GenerateTokensAsync and saves RefreshToken
  - Returns 201 with { user: { id, email, userName }, accessToken, refreshToken }
- `Features/Auth/Register/RegisterRequest.cs` — sealed record with Email, Password, UserName, with data annotations or FluentValidation
- (Validation handled inline or via FluentValidation if package is available)

**Verification:** `dotnet build` succeeds. Sending POST to `/api/v1/auth/register` with valid data returns 201 and tokens.

**Estimate:** ~100 lines (3 new files)

**Commit message:** `feat(auth): add Register endpoint with transactional user creation`

---

## Task: T4 — Login endpoint

**Prerequisites**: T1, T2

**Files to create:**
- `Features/Auth/Login/LoginEndpoint.cs` — partial controller method `Login(LoginRequest request, CancellationToken ct)`:
  - Validates credentials via SignInManager.PasswordSignInAsync
  - On success: generates tokens via ITokenService, returns 200
  - On failure: returns 401 with generic "Invalid credentials" (no info leak)
- `Features/Auth/Login/LoginRequest.cs` — sealed record with Email, Password

**Verification:** `dotnet build` succeeds. Login with correct creds returns 200 + tokens. Wrong password returns 401. Non-existent email returns 401 (same message).

**Estimate:** ~70 lines (2 new files)

**Commit message:** `feat(auth): add Login endpoint with credential validation`

---

## Task: T5 — RefreshToken endpoint with rotation + theft detection

**Prerequisites**: T1, T2, T3, T4

**Files to create:**
- `Features/Auth/RefreshToken/RefreshTokenEndpoint.cs` — partial controller method `RefreshToken(RefreshTokenRequest request, CancellationToken ct)`:
  - Hash incoming refresh token with SHA256
  - Look up by hash in RefreshTokens table
  - Validate: exists, not revoked, not expired
  - If ReplacedByToken != null → **theft detected**: revoke ALL active tokens for this user, return 401
  - If valid: revoke old token (set RevokedAt, ReplacedByToken = new hash), generate new pair, save new RefreshToken, return 200
  - Single generic 401 for all failure cases (no info leak)
- `Features/Auth/RefreshToken/RefreshTokenRequest.cs` — sealed record with RefreshToken string

**Critical edge case — concurrent refresh race:**
  - Use EF Core's concurrency handling or a transaction with RepeatableRead isolation
  - First commit wins, second finds token already revoked → returns 401 (NOT theft — single revocation, not reuse pattern)
  - Theft detection only triggers when a token's ReplacedByToken is non-null AND it's used again

**Verification:** `dotnet build` succeeds. Refresh flow: valid token returns 200 + new tokens, old token revoked. Expired/revoked tokens return 401. Theft cascade works.

**Estimate:** ~100 lines (2 new files)

**Commit message:** `feat(auth): add RefreshToken endpoint with rotation and theft detection`

---

## Review Workload Forecast

- **Estimated total changed lines:** ~500
- **File count:** ~17 (12 new, 5 modified + migration)
- **400-line budget risk:** High
- **Chained PRs recommended:** No (change is cohesive — auth is one feature)
- **Decision needed before apply:** Yes — estimate exceeds 400-line review budget

## Implementation Order

```
T1 (infra) ──→ T2 (data) ──→ T3 (register) ──→ T4 (login) ──→ T5 (refresh)
                    ↓
                migration
```

T1 and T2 are foundational. T3/T4 are independent of each other (both depend on T1+T2). T5 depends on all prior tasks.
