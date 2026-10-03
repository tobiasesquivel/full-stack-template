# Tasks: Error Catalog — Static Error Classes

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~60–80 |
| 400-line budget risk | Low |
| Chained PRs recommended | No |
| Suggested split | Single PR |
| Delivery strategy | single-pr |
| Chain strategy | pending |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | All phases in sequence | PR 1 | Single PR — three independent groups, create-before-replace ordering |

## Phase 1: Create Error Catalog Classes

- [x] 1.1 Create `Features/Auth/Common/AuthErrors.cs` — 5 static errors: `EmailAlreadyExists`, `InvalidCredentials`, `UserNotFound`, `InvalidToken`, `TokenTheft`
- [x] 1.2 Create `Features/Tasks/Common/TaskErrors.cs` — factory method `NotFound(Guid taskId)` returning `Error.NotFound(code: "Task.NotFound", ...)`
- [x] 1.3 Create `Features/Users/Common/UserErrors.cs` — factory method `NotFound(string authId)` returning `Error.NotFound(code: "User.NotFound", ...)` (create `Features/Users/Common/` dir)

## Phase 2: Update Auth Handlers (AuthErrors)

- [x] 2.1 `RegisterCommandHandler.cs` — replace `Error.Conflict(...)` with `AuthErrors.EmailAlreadyExists`
- [x] 2.2 `LoginCommandHandler.cs` — replace 2× `Error.Unauthorized(...)` with `AuthErrors.InvalidCredentials`, replace `Error.NotFound(...)` with `AuthErrors.UserNotFound` (line 35, 41, 50)
- [x] 2.3 `JwtService.cs` — replace 2× `Error.Unauthorized("Invalid refresh token", ...)` with `AuthErrors.InvalidToken`, replace `Error.Unauthorized("Token theft...")` with `AuthErrors.TokenTheft` (lines 116, 125, 133)

## Phase 3: Update Task Handlers (TaskErrors)

- [x] 3.1 `GetTaskByIdQueryHandler.cs` — replace `Error.NotFound(...)` with `TaskErrors.NotFound(query.Id)`
- [x] 3.2 `UpdateTaskCommandHandler.cs` — replace `Error.NotFound(...)` with `TaskErrors.NotFound(command.Id)`
- [x] 3.3 `ToggleTaskCommandHandler.cs` — replace `Error.NotFound(...)` with `TaskErrors.NotFound(command.Id)`
- [x] 3.4 `DeleteTaskCommandHandler.cs` — replace `Error.NotFound(...)` with `TaskErrors.NotFound(command.Id)`

## Phase 4: Update User Handlers (UserErrors)

- [x] 4.1 `GetUserByIdQueryHandler.cs` — replace inline `Error.NotFound(...)` with `UserErrors.NotFound(query.AuthId)` (lines 19–21)
- [x] 4.2 `DeleteUserCommandHandler.cs` — replace `Error.NotFound(..., code: "Auth.UserNotFound")` with `UserErrors.NotFound(command.UserId)` (semantic fix: User domain, not Auth)

## Phase 5: Verify

- [x] 5.1 `dotnet build` succeeds, no compilation errors
- [x] 5.2 Grep confirms zero `Error\.(NotFound|Unauthorized|Conflict|Failure)` calls remain in handler/service files under `Features/` and `Infrastructure/Auth/`
