# Tasks: Add Users Get By ID

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~180–220 |
| 400-line budget risk | Low |
| Chained PRs recommended | No |
| Suggested split | Single PR |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

## Phase 1: Foundation — PK Refactor

- [x] 1.1 `Domain/Entities/User.cs`: Remove `Id` (int PK), add `[Key]` on `AuthId`, keep required strings
- [x] 1.2 `UserConfiguration.cs`: `HasKey(u => u.AuthId)`, remove `HasIndex(u => u.AuthId)`, keep FK to AspNetUsers
- [x] 1.3 Generate migration: `dotnet ef migrations add MakeAuthIdPrimaryKey -o Infrastructure/Persistence/Migrations`

## Phase 2: Auth Layer Updates

- [x] 2.1 `Features/Auth/Common/AuthResponse.cs`: `UserInfo(int Id, ...)` → `UserInfo(string AuthId, ...)`
- [x] 2.2 `Features/Auth/Login/LoginCommandHandler.cs`: Update projection to use `AuthId` instead of `Id`
- [x] 2.3 `Features/Auth/DeleteUser/DeleteUserCommandHandler.cs`: Ownership check compares `user.AuthId` instead of `user.Id` (no changes needed — handler already uses `AuthId`)
- [x] 2.4 `Features/Auth/Register/RegisterEndpoint.cs`: `CreatedAtAction("GetUserById", "Users", new { authId = authResponse.User.AuthId }, authResponse)`

## Phase 3: New Users Feature

- [x] 3.1 Create `Features/Users/UsersController.cs` — partial class, `[Route("api/v1/users")]`, `[Authorize]`, `IMessageBus` ctor
- [x] 3.2 Create `Features/Users/GetById/GetUserByIdQuery.cs` — `sealed record GetUserByIdQuery(string AuthId)`
- [x] 3.3 Create `Features/Users/GetById/GetUserByIdQueryResult.cs` — `sealed record GetUserByIdQueryResult(string AuthId, string UserName)`
- [x] 3.4 Create `Features/Users/GetById/GetUserByIdQueryHandler.cs` — projection + AsNoTracking + null check → `Error.NotFound()`
- [x] 3.5 Create `Features/Users/GetById/GetByIdEndpoint.cs` — partial `UsersController`, `[HttpGet("{authId}")]`, `result.Match(Ok, Problem)`

## Phase 4: Testing & Verification

- [ ] 4.1 Integration test: `GET /api/v1/users/{authId}` returns 200 with `authId` + `userName` (skipped — no test project)
- [ ] 4.2 Integration test: response excludes `email` field (no PII leak) (skipped — no test project)
- [ ] 4.3 Integration test: non-existent `authId` returns 404 with ProblemDetails (skipped — no test project)
- [ ] 4.4 Integration test: missing/invalid JWT returns 401 (skipped — no test project)
- [ ] 4.5 Integration test: Register Location header points to `GetUserById` with correct `authId` (skipped — no test project)
- [x] 4.6 Verify handler uses `AsNoTracking()` + `Select()` projection (code review) — confirmed

## Dependencies

- Phase 1 → Phase 2 → Phase 3: sequential (PK refactor must ship first)
- Phase 4 (tests) can start after Phase 3 is stable
- Migration (1.3) runs last within Phase 1, after entity + config are ready
