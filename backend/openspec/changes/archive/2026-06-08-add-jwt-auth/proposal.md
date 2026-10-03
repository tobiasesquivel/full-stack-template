# Proposal: Add JWT Authentication

## Intent

Add production-ready JWT authentication with refresh token rotation to the ASP.NET Core backend template. This change delivers secure, token-based authentication that:
- Integrates seamlessly with existing Identity infrastructure
- Follows established template conventions (partial controllers, Options pattern, Fluent API)
- Supplies refresh tokens for long-lived sessions with rotation
- Implements token theft detection via revocation cascading
- Provides immediate architectural foundation for future Features like Users/Register, Users/Login, etc.

This is a necessary capability for any production API and aligns with the existence of JWT configuration in `appsettings.example.json` and commented-out Auth methods in `DependencyInjection.cs`.


## Scope

### In Scope
- JWT access token generation (15 min expiration)
- Refresh token generation (7 day expiration, random string)
- Refresh token entity with rotation and revocation detection
- Token theft detection via revocation cascading
- Existing Identity integration (SignInManager, UserManager)
- Transactional user registration (creates both Identity user and domain User concurrently)
- Options pattern integration for JWT configuration
- Partial controller pattern for Auth endpoints
- All configuration updates and conventions documentation

### Out of Scope
- Password reset flows (future work)
- Email verification (future work)
- Multi-factor authentication (future work)
- Social authentication providers (future work)
- Token blacklisting for global revocation (alternative to re-all revocation)

## Capabilities

> Contract for sdd-spec: These become the spec files to implement.

### New Capabilities
- `auth-register`: User registration endpoint and validation
- `auth-login`: User authentication and access token issuance
- `auth-refresh`: Refresh token endpoint with rotation and theft detection
- `auth-jwt-generation`: JWT token generation service and interface
- `auth-tokens`: Token management with refresh token storage

### Modified Capabilities
None

## Approach

### Architecture Decisions

1. **Token Format**
   - Access tokens: JWT (short-lived, 15 min)
   - Refresh tokens: Cryptographically random string (stored hashed in DB)
   - Access token contains `sub` (user ID), `jti` (token ID), and `scope` claims

2. **Refresh Token Entity**
   ```csharp
   public class RefreshToken
   {
       public string Id { get; set; }
       public string Token { get; set; } // Stored hashed
       public string JwtId { get; set; }
       public string AppIdentityUserId { get; set; } // FK to Identity user
       public DateTime ExpiresAt { get; set; }
       public DateTime CreatedAt { get; set; }
       public DateTime? RevokedAt { get; set; }
       public string? ReplacedByToken { get; set; }
   }
   ```

3. **Token Rotation & Theft Detection**
   - On refresh: Old refresh token is revoked, new one issued
   - If reused refresh token detected: ALL user refresh tokens revoked (token theft mitigation)
   - Revoked tokens are not deleted for audit trail and replay detection

4. **User Registration Flow**
   - Single transaction creates both Identity user (via UserManager) and domain User
   - Ensures consistency between Auth and Domain models
   - Uses existing UserManager/RoleManager for Identity operations

5. **Login Flow**
   - Validates credentials via SignInManager
   - Returns tokens if successful
   - Returns 401 if authentication fails

6. **Integration Patterns**
   - JWT options bound via IOptions pattern from `Jwt` section
   - Services registered in `AddInfrastructure()` extension
   - Auth middleware ordered: HTTPS → Auth → Authorization → Controllers
   - Controllers use partial class pattern (minimal base file, action files in use-case folders)

7. **Security**
   - JWT signing key configured in appsettings (rotate in production)
   - Tokens signed with HMAC-SHA256
   - All refresh tokens hashed before storage using SHA256
   - No plaintext tokens in logs or errors

### Design Patterns Used
- **Options Pattern**: `JwtOptions` bound from configuration, registered via DI
- **Repository Pattern Avoidance**: Direct DbContext usage in handlers/services
- **Partial Controllers**: One endpoint per file, minimal base controller
- **Fluent API**: Existing pattern used for RefreshTokenConfiguration
- **Entity Framework Core**: Existing provider for MySQL, migrations support

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Program.cs` | Modified | Add `UseAuthentication()` before `UseAuthorization()` |
| `Infrastructure/DI/DependencyInjection.cs` | Modified | Add `AddAppJwt()` for JWT service registration |
| `Infrastructure/Persistence/AppDbContext.cs` | Modified | Add `DbSet<RefreshToken>` |
| `Domain/Entities/User.cs` | Unchanged | Uses existing pattern (AuthId FK to Identity) |
| `Infrastructure/Persistence/AppIdentityUser.cs` | Unchanged | Standard IdentityUser implementation |
| `Infrastructure/Persistence/Configurations/UserConfiguration.cs` | Unchanged | Existing Fluent API configuration |
| `appsettings.example.json` | Modified | Add `Jwt` section with configuration template |
| `appsettings.json` | Modified | Add `Jwt` section populated for TemplateApp |
| `appsettings.Development.json` | Modified | Add `Jwt` section with test values |
| `AspNetConventions.md` | Modified | Document auth conventions and patterns |
| **New files:** | **New** | Three Auth features (Register/Login/RefreshToken) with DTOs, validators, endpoints |
| **New files:** | **New** | JwtService, ITokenService, RefreshToken entity and config |

## Risk Assessment

### Technical Risks (Likelihood × Impact → Mitigation)

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Security vulnerabilities in token generation | Low | High | Use established JWT libraries, hash all refresh tokens, validate all inputs |
| Database size growth from RefreshToken records | Medium | Medium | Add cleanup job (future iteration), implement TTL indexes |
| Breaking existing controllers with auth middleware | Low | High | Add auth to specific controllers only initially |
| Integration issues with SignInManager | Medium | Medium | Test thoroughly, mock Identity dependencies in unit tests (future) |
| Transaction failures in user registration | Medium | High | Use existing EF Core transaction handling, explicit error messages |

### Overall Risk Level: **Medium**

Focused on security-sensitive areas (token validation, refresh logic, transaction safety) but leverages existing Identity infrastructure which is already installed and configured (verified in `packages.lock.json`).

## Rollback Plan

1. **If issue detected in Auth tokens**:
   ```bash
   git revert --no-commit <commit-hash>
   dotnet ef migrations remove
   ```

2. **If deployment causes issues**:
   - Rollback using application version tag
   - Users' refresh tokens invalidated (invalidation is automatic on missing issuer/key)

3. **If database migration causes issues**:
   ```bash
   dotnet ef database update PreAuthMigration
   ```

4. **If logout cascading doesn't work**:
   - Service blocks will remain but tokens become unusable after 15 min
   - Manual user deletion possible via database

## Dependencies

- **Runtime**: ASP.NET Core 10 identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`)
- **Database**: MySQL with EF Core (existing)
- **Cryptography**: .NET Core built-in `System.IdentityModel.Tokens.Jwt`
- **DI**: Existing Options pattern for configuration
- **Logging**: Existing Serilog integration

## Success Criteria

- [ ] Access tokens issued with 15 min expiration
- [ ] Refresh tokens issued with 7 day expiration
- [ ] Token rotation functional (old refresh token revoked when new issued)
- [ ] Token theft detection implemented (all refresh tokens revoked on reuse)
- [ ] User registration creates both Identity user and domain User in same transaction
- [ ] Login validates credentials and returns tokens on success, 401 on failure
- [ ] All refresh tokens stored hashed in database
- [ ] appsettings populated with JWT configuration
- [ ] JWT middleware integrated into pipeline
- [ ] Feature tests pass (Register endpoint, Login endpoint, Refresh endpoint)
- [ ] OpenAPI docs reflect new auth endpoints
- [ ] Scalar UI accessible at `/docs` for auth endpoints

## Migration Path

1. Create update migration:
   ```bash
   dotnet ef migrations add AddJwtAuth -o Infrastructure/Persistence/Migrations
   ```

2. Apply migration:
   ```bash
   dotnet ef database update
   ```

3. Deploy changes Gradually (if preferred):
   - Deploy backend without auth endpoints first (backwards compatible)
   - Deploy frontend change to use auth endpoints
   - Enable auth middleware via configuration switch

## Time Estimate

- Exploration/Research: 1 hour (completed in proposal phase
- Implementation (specs, design, apply): 6-8 hours code
- Testing: 2 hours
- Documentation: 1 hour
- **Total**: 10-12 hours including verification

Previously estimated line count: ~500 lines across all new/changed files (within risk assessment range).
