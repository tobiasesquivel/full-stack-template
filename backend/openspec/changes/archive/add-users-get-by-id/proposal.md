# Proposal: Add Get User By ID Endpoint

## Intent
Add a secure endpoint to retrieve a user's public profile by their authentication ID without exposing Personally Identifiable Information (PII). This fulfills the need for authenticated services and frontend applications to fetch user metadata without accessing sensitive data like email addresses, while maintaining a clean contract via dedicated specs.

**Why this change is needed:**
- Current `AuthController` is reaching 400+ lines and handles too many concerns (login, register, token refresh)
- No existing route to fetch user public profile independently
- Security requirement to avoid PII leakage through endpoints
- Better separation of concerns and code organization

## Scope

### In Scope
- New partial `UsersController` class under `Features/Users/`
- New `GetUserByIdQuery`, `GetUserByIdQueryHandler`, and `GetUserByIdQueryResult` components
- New `GetByIdEndpoint.cs` view file in the partial controller pattern
- Update `RegisterEndpoint.cs` to use `CreatedAtAction` pointing to the new endpoint
- Public profile DTO without email field
- JWT authorization requirement (`[Authorize]`)
- Location header fix in Register endpoint
- **User entity refactor**: `AuthId` becomes primary key (replaces `int Id`). `User.Id` removed, `User.AuthId` becomes PK + FK to AspNetUsers
- **AuthResponse.UserInfo**: `int Id` → `string AuthId`
- **Login handler**: projection no longer returns `User.Id` — uses `User.AuthId` directly
- **EF Core migration**: update model to reflect PK change

### Out of Scope
- User profile picture URLs or additional metadata fields
- PUT/PATCH/DELETE operations on users
- Email visibility configuration or toggling
- Audit logging for profile access
- Any GraphQL endpoints (future phase)
- Frontend integration or testing

## Capabilities

### New Capabilities
- **users-get-by-id**: Retrieve public user profile matching existing AuthId without exposing PII

### Modified Capabilities
- **auth-register**: Location header now points to new UsersController.GetUserById endpoint

## Approach

Adopt **Single Responsibility Principle** for authentication endpoints by separating user profile retrieval into its own controller. Follow the established architectural patterns:

1. **Feature folder structure**: `Features/Users/` with sub-components in `/GetById/` directory
2. **Partial class pattern**: WolverineFx message handlers + Endpoints in same file, split via partial classes
3. **DTO naming**: Use `GetUserByIdQueryResult` instead of reusing `UserInfo` to prevent accidental PII exposure upgrades
4. **Authorization**: Require valid JWT via `[Authorize]` attribute on controller class
5. **Persistence**: Use `AsNoTracking()` in handler for read-only query efficiency
6. **Error handling**: Return 404 when user doesn't exist (ErrorOr pattern)

**Technical notes:**
- `User.AuthId` becomes PK (string), `User.Id` (int) removed — FK to AspNetUsers stays
- Route: `/api/v1/users/{authId}` (consistente con AuthId como PK)
- Response: 200 OK with `{ AuthId, UserName }`
- Error: 404 Not Found if user doesn't exist
- Response strictly does NOT include email field
- Register flow: `RegisterCommandHandler` crea `User { AuthId = identityUser.Id, ... }` — sin necesidad de generar Id separado
- EF Core migration necesaria por la PK change

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Domain/Entities/User.cs` | Modified | Remove `Id` (int PK), promote `AuthId` to PK |
| `Infrastructure/Persistence/Configurations/UserConfiguration.cs` | Modified | PK becomes `AuthId`, remove `HasIndex` on AuthId (PK is already unique) |
| `Infrastructure/Persistence/AppDbContext.cs` | Unchanged | `Users` DbSet sigue igual (User type no cambia) |
| `Infrastructure/Persistence/Migrations/` | New migration | EF Core migration por la PK change |
| `Features/Auth/Common/AuthResponse.cs` | Modified | `UserInfo.Id` (int) → `UserInfo.AuthId` (string) |
| `Features/Auth/Login/` | Modified | Proyección ya no retorna `User.Id` |
| `Features/Auth/Register/RegisterEndpoint.cs` | Modified | Location header ahora apunta a `UsersController.GetUserById` con `authId` |
| `Features/Auth/DeleteUser/` | Modified | Validación cambia de `User.Id` a `User.AuthId` |
| `Features/Users/` | New | Directorio completo de user profile |
| `Features/Users/UsersController.cs` | New | Partial controller for user operations |
| `Features/Users/GetById/GetUserByIdQuery.cs` | New | Query definition |
| `Features/Users/GetById/GetUserByIdQueryHandler.cs` | New | Handler with projection and null check |
| `Features/Users/GetById/GetUserByIdQueryResult.cs` | New | Result DTO without PII |
| `Features/Users/GetById/GetByIdEndpoint.cs` | New | Endpoint file (view in UsersController partial) |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| **Email accidentally exposed** | Low | Dedicated `GetUserByIdQueryResult` DTO forces compilation errors if email sneaks in. Code review will verify field list. |
| **Breaking Auth Controller refactor** | Medium | All existing auth endpoints preserved. Only location header in Register changes. Maintains backward compatibility. |
| **PK change migration risk** | High | EF Core no soporta cambiar PK de una tabla existente con datos. Estrategia: (1) nueva migración que droppea Users table + recrea con nueva PK, O (2) migration manual SQL para preservar datos. Discutir con el equipo antes de ejecutar. |
| **Performance regression** | Low | Read-only query with `AsNoTracking()`. No writes, just projection. PK = AuthId (clustered). |
| **Incorrect route authorization** | Medium | JWT `[Authorize]` on controller. AuthId validation happens in handler. Double validation ensures security. |
| **Namespace or folder mismatch** | Low | Convention file `AspNetConventions.md` clearly defines namespace = folder path. CI checks will catch compilation errors. |

## Rollback Plan

1. **If deployment breaks auth flow**: Revert the `RegisterEndpoint.cs` location header change to use previous hardcoded URL
2. **If users lose access**: Revert the entire UsersController and its endpoints (no users affected beyond the new endpoint)
3. **If database issues**: Handler uses `AsNoTracking()` and has null check with error result. No writes, read-only operation

Rollback steps:
```bash
# Revert changes in git
git revert --no-commit <commit-sha>
# Or simply comment out the new UsersController in Startup/Program.cs
```

## Dependencies
- No new dependencies required
- Existing dependencies: ASP.NET Core Identity, JWT Bearer, WolverineFx 6.5.1, EF Core 10, PostgreSQL
- Auth stack remains unchanged — only adds new endpoint

## Success Criteria

- [ ] Endpoint responds with 200 OK for existing users
- [ ] Response body contains only `{ "authId": string, "userName": string }`
- [ ] No email field in any response
- [ ] 404 Not Found for non-existent AuthId
- [ ] Register endpoint Location header correctly points to new endpoint
- [ ] Compiles without warnings under SDD conventions (explicit types, sealed records, correct namespaces)
- [ ] No PII leaked in response bodies
- [ ] Follows AuthId route key convention
- [ ] JWT authorization enforced
- [ ] Handler uses AsNoTracking() for read efficiency
- [ ] User entity no longer has `int Id` column
- [ ] User.AuthId is PK + FK to AspNetUsers
- [ ] UserInfo DTO uses `string AuthId` instead of `int Id`
- [ ] EF Core migration generates cleanly (PK change strategy decided)

## Files to Create

```
Features/Users/UsersController.cs
  └── Partial class: `[Route("api/v1/users")]` `[Authorize]`

Features/Users/GetById/GetUserByIdQuery.cs
  └── `sealed record GetUserByIdQuery(string AuthId)`

Features/Users/GetById/GetUserByIdQueryHandler.cs
  └── Project User → GetUserByIdQueryResult with EF Core `AsNoTracking()`

Features/Users/GetById/GetUserByIdQueryResult.cs
  └── `sealed record GetUserByIdQueryResult(string AuthId, string UserName)`

Features/Users/GetById/GetByIdEndpoint.cs
  └── `partial class UsersController` — HTTP endpoint definition
```

## Files to Modify

```
Domain/Entities/User.cs
  └──── Remove `Id` (int PK), add `[Key]` on `AuthId`, update required/nullability

Infrastructure/Persistence/Configurations/UserConfiguration.cs
  └──── `HasKey(u => u.AuthId)`, remove `HasIndex(u => u.AuthId)`, keep FK

Infrastructure/Persistence/Migrations/
  └──── New migration for PK change (strategy: drop + recreate o manual SQL)

Features/Auth/Common/AuthResponse.cs
  └──── `UserInfo(int Id, ...)` → `UserInfo(string AuthId, ...)`

Features/Auth/Login/
  └──── Handler projection: remove `Id` from result, adjust query

Features/Auth/Register/RegisterEndpoint.cs
  └──── CreatedAtAction("GetUserById", "Users", new { authId = ... }, ...)

Features/Auth/DeleteUser/
  └──── Ownership validation: `user.Id` → `user.AuthId`
```
