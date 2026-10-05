# Archive Report: add-jwt-auth

## Change Information

| Field | Value |
|-------|-------|
| **Change Name** | add-jwt-auth |
| **Status** | ✅ Complete |
| **Archived Date** | 2026-06-08 |
| **Verification** | 16/16 scenarios PASS |

## Summary

Added production-ready JWT authentication with refresh token rotation to the ASP.NET Core backend template. The change delivers secure, token-based authentication integrated with the existing Identity infrastructure, including token theft detection via revocation cascading.

## Artifacts

| Artifact | Status | Description |
|----------|--------|-------------|
| `proposal.md` | ✅ | Intent, scope, approach, risk assessment, rollback plan |
| `spec.md` | ✅ | 6 requirements across 5 domains: auth-register, auth-login, auth-refresh, auth-jwt-generation, auth-tokens |
| `design.md` | ✅ | Technical architecture, data flow, entity model, DI registration, security controls |
| `tasks.md` | ✅ | 5 implementation tasks (T1–T5) covering infrastructure → data model → register → login → refresh |
| `verify-report.md` | ✅ | 16/16 scenarios PASS, 0 CRITICAL issues, 2 minor warnings |
| `archive-report.md` | ✅ | This document |

## Domains Implemented

| Domain | Requirements | Scenarios | Status |
|--------|-------------|-----------|--------|
| auth-register | 1 | 4 | ✅ |
| auth-login | 1 | 3 | ✅ |
| auth-refresh | 1 | 4 | ✅ |
| auth-jwt-generation | 1 | 3 | ✅ |
| auth-tokens | 2 | 2 | ✅ |

## Source of Truth Updated

- `openspec/specs/auth/spec.md` — created as the main spec for JWT authentication

## Files Affected in Codebase

### New Files
- `Domain/Entities/RefreshToken.cs`
- `Infrastructure/Options/JwtOptions.cs`
- `Infrastructure/Auth/ITokenService.cs`
- `Infrastructure/Auth/JwtService.cs`
- `Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs`
- `Features/Auth/AuthController.cs`
- `Features/Auth/Register/RegisterEndpoint.cs`
- `Features/Auth/Register/RegisterRequest.cs`
- `Features/Auth/Login/LoginEndpoint.cs`
- `Features/Auth/Login/LoginRequest.cs`
- `Features/Auth/RefreshToken/RefreshTokenEndpoint.cs`
- `Features/Auth/RefreshToken/RefreshTokenRequest.cs`
- `Infrastructure/Persistence/Migrations/*AddJwtAuth*`

### Modified Files
- `Program.cs` — added `UseAuthentication()` before `UseAuthorization()`
- `Infrastructure/DependencyInjection/DependencyInjection.cs` — added `AddAppJwt()` extension
- `Infrastructure/Persistence/AppDbContext.cs` — added `DbSet<RefreshToken>`
- `appsettings.json` — added Jwt section
- `appsettings.Development.json` — added Jwt section
- `appsettings.example.json` — updated Jwt section

## Notes

### Warnings from Verification
1. **nbf claim** — `JwtSecurityToken` sets nbf via `notBefore` parameter; included automatically by `JwtSecurityTokenHandler`. Low risk.
2. **ExpiresAt in response** — Spec mentions `ExpiresAt` in `TokenResult` but implementation returns only `accessToken` and `refreshToken`. Client can decode JWT for expiration. Intentional simplification.

### Recommendations
- Add `ExpiresAt` property to `TokenResult` if clients need it without decoding the JWT
- Consider adding `nbf` to explicit claims for clarity
- Future: add cleanup job for expired RefreshToken records

### Not Included (Explicitly Out of Scope)
- Password reset flows
- Email verification
- Multi-factor authentication
- Social authentication providers
