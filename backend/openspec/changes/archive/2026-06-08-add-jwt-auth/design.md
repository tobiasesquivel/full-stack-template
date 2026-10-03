# Design: Add JWT Authentication

## Technical Approach

This design implements production-ready JWT authentication with refresh token rotation and theft detection for the ASP.NET Core backend template. The solution integrates natively with the existing Identity infrastructure while following established template conventions:

- **Repository pattern avoidance**: Direct DbContext usage in handlers (template convention)
- **Partial controllers**: One endpoint file per action file for clear ownership (template convention)
- **Options pattern**: JWT configuration via `JwtOptions` class bound from appsettings
- **Fluent API**: Entity configuration auto-discovered via assembly scan
- **CQRS-inspired**: Dedicated handler/validator/command files per use case

The design prioritizes security with:
- Opaque refresh tokens (not JWTs) enabling revocation
- SHA256 hashing before storage
- Token rotation on each refresh
- Theft detection with global revocation cascade
- Constant-time comparison and fail-secure error handling

## Architecture Decisions

### Decision: Entity Structure and Relationships
**Choice**: Create `RefreshToken` as a dedicated domain entity with FK to `AppIdentityUser.Id`
**Alternatives**: Storing refresh tokens in Identity's `UserToken` table or using JWTs for refresh tokens.
**Rationale**: Separate table provides full control over rotation/revocation/theft detection while matching template's pattern of dedicated domain entities.

### Decision: Refresh Token Storage Format  
**Choice**: Store only SHA256 hashes of refresh tokens in database
**Alternatives**: Plaintext storage or storing JWTs with expiry in claims
**Rationale**: Prevents DB compromise from direct token exposure while enabling efficient lookup and maintaining revocation capability.

### Decision: Services Lifetime Configuration
**Choice**: `JwtService` as Singleton, `AppDbContext` as Scoped
**Rationale**: Signature generation is stateless; depends only on thread-safe `IOptionsSnapshot`. DbContext follows EF Core convention per HTTP request.

### Decision: Theft Detection Strategy  
**Choice**: Cascading revocation of ALL non-revoked tokens for user on theft detection
**Alternatives**: Incremental revocation or rolling validity windows
**Rationale**: Forces attacker to re-authenticate with password immediately, providing strongest security posture with acceptable UX tradeoff.

### Decision: JWT Algorithm and Configuration
**Choice**: HMAC-SHA256 signing key with minimum 32-character validation at startup
**Alternatives**: RS256 (certificate management) or ECDSA (overhead)
**Rationale**: HMAC-SHA256 sufficient for internal API. Key rotation via config prevents operational complexity.

### Decision: Upgrade and Backward Compatibility
**Choice**: Deploy backend first (no auth middleware), frontend changes, finally enable auth
**Rationale**: Zero-breaking change deployment minimizes risk and supports gradual rollout.

---

## Data Flow

```
Register/Login Request ────┐
     ↓                 │
UserManager/SignInManager validates credentials ───┐
     ↓                 │
JwtService.GenerateTokensAsync() creates tokens     │
     ↓                 │
TokenService.SaveTokenAsync() persists refresh token ───┘
     ↓
RefreshToken Entity (Token=SHA256, JwtId, UserId, ExpiresAt, …)
     ↓
Auth Endpoint Returns → {accessToken: JWT, refreshToken: plaintext, expiresAt}
     ↓
Client caches tokens

Access Token Usage ──────────────────────────────────────┐
     ↓                                           │
JWT Bearer Middleware validates signature+expiry    │
     ↓ Success → Accept Request                     │
Validation Failed/Tampered → 401                   │

Refresh Token Request ──────────────────┐
     ↓                            │
Hash incoming token → Db Lookup      │
t != null → continue; null → 401    │
RevokedAt != null → rotate already  │→ 401
ExpiresAt < now → 401              │
ReplacedByToken != null →           │
  Theft detected! Cascade revoke    │→ 401
  Revoke ALL user tokens → force re-auth │
Token valid → Rotate               │
  Mark old as revoked + link new hash\n  Generate new token pair            │
  Save new RefreshToken row         │
  Return new {accessToken, refreshToken, expiresAt}
     ↓
Client updates tokens
```

---

## Data Model

### RefreshToken Entity

**PK**: `Id (Guid)`

**Columns:**

| Property | Type/Size | Constraints | Description |
|----------|-----------|-------------|-----------|
| `Token` | `string(256)` | Required, unique | SHA256 hash of plaintext refresh token (base64) |
| `JwtId` | `string(128)` | Required | Links refresh token to associated JWT (`jti` claim) |
| `AppIdentityUserId` | `string(450)` | Required, FK | Identity user primary key |
| `ExpiresAt` | `DateTime` | Required | UTC expiration (separate from access token) |
| `CreatedAt` | `DateTime` | Required | UTC creation timestamp |
| `RevokedAt` | `DateTime?` | Nullable | Set when rotated; preserves audit trail |
| `ReplacedByToken` | `string(256)?` | Nullable | Hash of new refresh token on rotation (theft detection) |

```csharp
// Domain/Entities/RefreshToken.cs
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public required string Token { get; set; }
    [MaxLength(128)] public required string JwtId { get; set; } = string.Empty;
    [MaxLength(450)] public required string AppIdentityUserId { get; set; } = string.Empty;
    public required DateTime ExpiresAt { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    [MaxLength(256)] public string? ReplacedByToken { get; set; }
}
```

**Table Configuration:**
- Index `IX_RefreshTokens_Token` (unique) for fast lookup
- Index `IX_RefreshTokens_AppIdentityUserId_RevokedAt` (composite) for user-scoped active-token queries
- FK `FK_RefreshTokens_AspNetUsers_AppIdentityUserId` ON DELETE CASCADE
- No changes to existing `AspNetUsers`, `User`, or other entities

---

## Token Service Architecture

### Interface: ITokenService
```csharp
public interface ITokenService
{
    Task<TokenResult> GenerateTokensAsync(
        AppIdentityUser user,
        CancellationToken ct = default);
    
    Task<TokenResultOrError> RedeemTokenAsync(
        string refreshTokenString,
        CancellationToken ct = default);
}
```

### Implementation: JwtService
```csharp
public sealed class JwtService : ITokenService
{
    public JwtService(IOptionsSnapshot<JwtOptions> options, AppDbContext db, IDateTimeProvider? dtp=null)
    {
        // Thread-safe via IOptionsSnapshot
    }
    
    public async Task<TokenResult> GenerateTokensAsync(AppIdentityUser user, CancellationToken ct)
    {
        if (_options.Key.Length < 32) throw new InvalidOperationException(
            "JWT key must be ≥32 chars for HMAC-SHA256 security.");
        
        var jwtId = Guid.NewGuid().ToString("N");
        var now = _dateTimeProvider.UtcNow;
        var accessExp = now.AddMinutes(_options.AccessTokenExpirationMinutes);
        var refreshExp = now.AddDays(_options.RefreshTokenExpirationDays);
        
        // JWT claims: sub(jwtId), iat, nbf, exp, iss(_issuer), aud(_audience)
        var token = new JwtSecurityToken(…);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        
        // Refresh token: 256-bit secure random + base64 encode
        var refreshTokenPlain = RandomNumberGenerator.GetBytes(32).Base64Url();
        var refreshTokenHash = SimpleHmacSha256(refreshTokenPlain);
        
        var entity = new RefreshToken
        {
            Token = refreshTokenHash,
            JwtId = jwtId,
            AppIdentityUserId = user.Id ?? throw new InvalidOperationException("No Identity Id"),
            ExpiresAt = refreshExp,
            CreatedAt = now,
            RevokedAt = null,
            ReplacedByToken = null
        };
        
        await _db.RefreshTokens.AddAsync(entity, ct); // Entity uses normal EF cascade/identity columns
        await _db.SaveChangesAsync(ct);
        
        return new TokenResult{ AccessToken=accessToken, RefreshToken=refreshTokenPlain, ExpiresAt=accessExp };
    }
    
    public async Task<TokenResultOrError> RedeemTokenAsync(string refreshTokenPlain, CancellationToken ct)
    {
        var hash = SimpleHmacSha256(refreshTokenPlain);
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token==hash && rt.ReplacedByToken==null, ct);
        if (token==null) return Error.Unauthorized("Invalid refresh token");
        
        var now = _dateTimeProvider.UtcNow;
        if (token.RevokedAt!=null) return Error.Unauthorized("Token revoked");
        if (token.ExpiresAt<now) return Error.Unauthorized("Token expired");
        
        if (token.ReplacedByToken!=null)
        {
            // Theft detected — previous rotation occurred
            await _db.RefreshTokens
                .Where(rt => rt.AppIdentityUserId==token.AppIdentityUserId && rt.RevokedAt==null)
                .ExecuteUpdateAsync(set => set.SetProperty(rt => rt.RevokedAt, now), ct);
            return Error.Unauthorized("Token theft detected. All sessions revoked.");
        }
        
        // Rotate: create new tokens, revoke old
        var user = await _db.Users.FirstOrDefaultAsync(u => u.AuthId==token.AppIdentityUserId, ct);
        var newTokens = await GenerateTokensAsyncFor(user, ct);
        token.RevokedAt = now;
        token.ReplacedByToken = SimpleHmacSha256(newTokens.RefreshToken);
        await _db.SaveChangesAsync(ct);
        
        return newTokens;
    }
    
    private static string SimpleHmacSha256(string data)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(bytes);
    }
}
```

**Implementation Notes:**
- SHA256 hashing for storage and validation only; plaintext returned to client (required for rotation)
- `IOptionsSnapshot` enables safe key rotation without restarting pods
- CancellationToken forwarded to all async calls

---

## Refresh Token Rotation Algorithm (Pseudocode)

```csharp
async Task<TokenResultOrError> RedeemTokenAsync(string rtPlain, Cancellation ct)
  hash = SHA256(rtPlain)                      // Client token → server hash
  token = Db.RefreshToken.Find(token==hash && ReplacedByToken==null)

  if !token      → 401 "Invalid"                // Not found OR rotated before
  if token.Revoke → 401 "Revoked"               // Already used
  if expired     → 401 "Expired"                // Expired by design
  if token.ReplacedByToken != null
      Db.BlockAllUserTokens(token.UserId)        // Theft: revoke all active
      → 401 "Theft det - all sessions revoked"
  
  newTokens = JwtService.GenerateTokensAsync(user) // New pair
  token.RevokedAt       = now                    // Audit trail
  token.ReplacedByToken = SHA256(newTokens.RefreshToken)
  Db.Save()                                   // Atomic with rotation

  return newTokens
```

---

## Theft Detection Algorithm (Pseudocode)

```csharp
on RedeemToken(tokenPlain):
  hash = SHA256(tokenPlain)
  token = Db.RefreshToken.find(hash)

  if token==null or token.RevokedAt!=null or token.ExpiresAt < now:
      return 401  // Standard failure (no info leak)
  
  if token.ReplacedByToken != null:
      // This token used once before → evidence of compromise
      Db.RefreshTokens.updateAll(
          where UserId==token.UserId and RevokedAt==null
          set RevokedAt=now
      )
      return 401 "Token theft detected - all sessions revoked"

  proceed with rotation
```

---

## Endpoint Design

**Base Namespace**: `TemplateApp.Features.Auth`

**Base Controller**: `AuthController.cs`
```csharp
// Features/Auth/AuthController.cs
[Route("api/v1/auth")][Tags("Auth")]public partial class AuthController : ControllerBase {}
```

| Use Case | Controller Partial | Route | Method | Auth | Purpose |
|----------|------------------|-------|--------|------|---------|
| Register | RegisterEndpoint | POST api/v1/auth/register | Register | None | Create user + issue tokens |
| Login | LoginEndpoint | POST api/v1/auth/login | Login | None | Validate creds + issue tokens |
| Refresh | RefreshEndpoint | POST api/v1/auth/refresh | RefreshToken | None | Rotate tokens |

**Pattern Applied:** All partial methods leverage template's CQRS structure:
- Request DTO as `sealed record`
- Command/Query record
- FluentValidation validator exposing `ErrorOr<T>`
- Handler class with single `Handle` method
- Endpoint as partial controller action returning `IResult`
- CancellationToken throughout async chain

---

## Security Controls

| Control | Implementation | Purpose |
|---------|----------------|---------|
| **Token Hashing** | SHA256 `ComputeHash(Encoding.UTF8.GetBytes(tokenPlain))` | Prevent DB compromise from direct token exposure |
| **Signing Alg** | `SecurityAlgorithms.HmacSha256` | Industry-standard symmetric HMAC for internal APIs |
| **Key Min Length** | Validation at DI registration (≥32 chars) | Enforce minimum entropy |
| **Error Handling** | Single 401 message for all invalid cases | Prevent information leakage |
| **Cookie vs Body** | JSON request/response body only | API-first, no cookies |
| **Expose Unused Fields** | None returned in errors | Limit surface for enumeration |

---

## DI Registration

### Extension: AddAppJwt()
File: `Infrastructure/DependencyInjection/DependencyInjection.cs`
```csharp
internal static class JwtAuthExtensions
{
    public static IServiceCollection AddAppJwt(
        this IServiceCollection services, IConfiguration config)
    {
        // 1. Validate JWT configuration
        var jwt = config.GetRequiredSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("Missing Jwt config.");
        if (jwt.Key.Length<32) throw new InvalidOperationException(
            "JWT Key must be ≥32 chars.");
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        
        // 2. Bind JwtBearer auth
        services.AddAuthentication(opt =>
        {
            opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(opt =>
        {
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                ValidateAudience = true, ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                ValidateLifetime = true, RequireExpirationTime = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = ClaimTypes.Name,
            };
            opt.Events ??= new JwtBearerEvents(); // Quiet logging defaults
        });
        
        // 3. Register services
        services.AddSingleton<ITokenService, JwtService>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddValidatorsFromAssemblyContaining<RegisterValidator>();
        
        return services;
    }
}
```

### Lifetimes Matrix
| Service/Component | Lifetime | Rationale |
|-----------------|----------|-----------|
| `JwtService` | Singleton | Stateless, depends only on thread-safe `IOptionsSnapshot` |
| `ITokenService` | Singleton | Interface bound to `JwtService` Singleton |
| `JwtOptions` | OptionsSnapshot | Config-driven values, reloadable without restart |
| `AppDbContext` | Scoped | EF Core per-request unit of work |
| `SignInManager<T>` | Scoped | AspNetCore.Identity per-request service |
| `UserManager<T>` | Scoped | Identity per-request service |

---

## Configuration

### JwtOptions Class
```csharp
// Infrastructure/Options/JwtOptions.cs
public sealed record JwtOptions
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty; // ≥32 chars enforced at startup
    public int AccessTokenExpirationMinutes { get; init; } = 15;
    public int RefreshTokenExpirationDays { get; init; } = 7;
}
```

### Appsettings Structure
`appsettings.json`:
```json
{
  "Jwt": {
    "Issuer": "https://api.example.com",
    "Audience": "api://default",
    "Key": "your-32-char-or-longer-key-here-change-in-production",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  }
}
```

`appsettings.Development.json`:
```json
{
  "Jwt": {
    "Issuer": "https://localhost:5001",
    "Audience": "api://default",
    "Key": "development-key-32-chars-long!!!",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 1
  }
}
```

`appsettings.example.json`:
```json
{
  "Jwt": {
    "Issuer": "https://api.YOURDOMAIN.com",
    "Audience": "api://default",
    "Key": "your-secure-key-minimum-32-chars",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  }
}
```

Note: In production, the `Key` should be sourced from secure secrets or environment variables.

---

## Middleware Pipeline

### Program.cs Ordering
```csharp
app.UseHttpsRedirection();           // 1. Security baseline
app.UseAuthentication();             // 2. Identity/claims population (JWT Bearer)
app.UseAuthorization();              // 3. [Authorize] attribute enforcement
app.MapControllers();                // 4. Route endpoint execution

// After above changes:
// UseAuthentication must come before UseAuthorization
```

**Why this order:** ASP.NET Core requires `UseAuthentication()` to run before route endpoints so that `HttpContext.User` is populated before endpoint authorization checks.

---

## Migration Plan

### Migration Name
```bash
dotnet ef migrations add AddJwtAuth -o Infrastructure/Persistence/Migrations
```

### Migration Impact
The `AddJwtAuth` migration will create:
- `RefreshToken` table with all columns, PK, and indexes
- FK `FK_RefreshTokens_AspNetUsers_AppIdentityUserId` (ON DELETE CASCADE)
- Unique index `IX_RefreshTokens_Token` for hash lookup
- Composite index `IX_RefreshTokens_AppIdentityUserId_RevokedAt` for active token queries
- No changes to existing `AspNetUsers`, `User`, or other tables

---

## Testing Strategy

| Layer | Focus | Tools | Mocks |
|-------|-------|-------|-------|
| **Unit Tests** | Handler validation, hashing, rotation logic | xUnit | Moq/Substitute |
| **Integration** | DbContext queries, concurrency edge cases | TestContainers/Postgres | None |
| **E2E** | Register/Login/Refresh endpoints, OpenAPI docs | Scalar/postman | Frontend mock |
| **Startup Valid** | DI validation, key length, config binding | Manual/unit | N/A |

### Per-Use-Case Scenarios
| Endpoint | Scenario | Expected Result |
|----------|----------|---------------|
| Register | Valid email/password/name | 201 + tokens |
| Register | Duplicate email | 400 + duplicate error |
| Register | Invalid email | 400 + validation error |
| Register | Weak password | 400 + validation error |
| Login | Correct credentials | 200 + tokens |
| Login | Wrong password | 401 + generic message |
| Login | Non-existent email | 401 + generic message |
| Refresh | Valid token | 200 + new tokens (old revoked) |
| Refresh | Expired token | 401 + generic message |
| Refresh | Already-rotated token | 401 + "theft detected" cascade |
| Refresh | Concurrent rotations | First wins; second becomes "revoked" → 401 |

### Error Design Principles
- Generic 401 messages for all client error categories (no info leakage)
- ProblemDetails standard with `errors` dictionary for validation
- No distinction between "user not found" and "wrong password" in login responses

---

## Open Questions
- [ ] Should refresh tokens use a rolling window with short expiry (e.g., 30min + refresh) vs long-lived (7 days)? → Closed: long-lived chosen for UX; rotation mitigates risk
- [ ] Should token theft revoke only the repeated token vs all active tokens? → Closed: all active chosen for strongest security

---
