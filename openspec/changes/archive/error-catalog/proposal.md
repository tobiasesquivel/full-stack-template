# Proposal: Introduce Static Error Catalogs with Factory Methods for .NET 10 Backend

## Intent
Centralize all ErrorOr error definitions into static error catalogs per feature area, replacing hardcoded inline error creation. This improves:

- **Consistency**: Uniform error codes and descriptions across the codebase
- **Maintainability**: Single source of truth for error definitions instead of scattered inline errors
- **Correctness**: Fix existing inconsistencies (e.g., `Auth.UserNotFound` used where `User.UserNotFound` should be used)
- **Context**: Clear semantic domains for errors (Auth vs User vs Task entities)
- **Testability**: Centralized error catalogs enable easy testing of error scenarios

**Problem**: Currently ALL errors are hardcoded inline in handlers like:
```csharp
return Error.NotFound(description: "Task not found", code: "Task.NotFound");
```

This leads to:
- Inconsistent error codes across similar error types
- Duplicate error descriptions that drift over time
- No central place to verify error codes and types
- Visual clutter in handlers, reducing signal-to-noise ratio

## Scope

### In Scope
- Create static error catalog classes per feature domain:
  - `Features/Auth/Common/AuthErrors.cs` — static readonly error definitions for auth-related errors
  - `Features/Users/Common/UserErrors.cs` — factory methods for user-related errors (factory for dynamic IDs)
  - `Features/Tasks/Common/TaskErrors.cs` — factory methods for task-related errors (factory for dynamic IDs)
- Create `Common` folders in Auth, Users, and Tasks features if they don't exist
- Update ALL existing error sites to use the catalogs:
  - JwtService (4 error returns)
  - LoginCommandHandler (3 error returns)
  - RegisterCommandHandler (1 error return)
  - RefreshTokenCommandHandler (3 error returns)
  - GetById handlers for Task and User (2 error returns)
  - Update/Toggle/Delete handlers for Task and User (3+ error returns)
- Fix semantic error correctness: Change `Auth.UserNotFound` → `User.UserNotFound` in DeleteUserCommandHandler where user identity doesn't exist becomes user profile doesn't exist

### Out of Scope
- FluentValidation middleware changes (already centralized)
- ApiController changes (not affected by error catalogs)
- Any behavior changes to the application (pure refactoring)
- Test files (tests should be updated by applying the same changes)

## Capabilities

### New Capabilities
- `error-catalogs`: Central static error definitions per feature domain with factory methods for parameterized errors
  - Enables uniform error handling across the entire backend
  - Provides compile-time safety when reusing error definitions
  - Supports factory pattern for errors requiring dynamic values (IDs, formats, etc.)

### Modified Capabilities
None. This is a pure refactoring that does not change any behavioral requirements or specs.

## Approach

### Design Principles
1. **Static Errors** for errors that are truly domain-agnostic or consistently reusable
2. **Factory Methods** for errors requiring entity IDs or dynamic context (`.WithId()`, `.ForUser()`, `.ForTask()`)
3. **Feature Scoping** - Each feature has its own `Common` namespace for shared infrastructure
4. **Error Code Convention**: Follow `Domain.ErrorType` pattern (e.g., `Auth.InvalidToken`, `User.NotFound`, `Task.ValidationFailed`)

### Implementation Strategy

1. **Create Directory Structure**
   ```
   Features/
   ├── Auth/
   │   └── Common/
   │       └── AuthErrors.cs
   ├── Users/
   │   └── Common/
   │       └── UserErrors.cs
   └── Tasks/
       └── Common/
           └── TaskErrors.cs
   ```

2. **Define Static Catalog Pattern**
   ```csharp
   // AuthErrors.cs
   public static class AuthErrors
   {
       public static Error EmailAlreadyExists => Error.Conflict(
           code: "Auth.EmailAlreadyExists",
           description: "Email is already registered");
       
       public static Error InvalidCredentials => Error.Unauthorized(
           code: "Auth.InvalidCredentials",
           description: "Invalid credentials provided");
   }
   ```

   ```csharp
   // UserErrors.cs
   public static class UserErrors
   {
       public static Error NotFound(string userId) => Error.NotFound(
           code: "User.NotFound",
           description: $"User with ID '{userId}' not found");
   }
   ```

   ```csharp
   // TaskErrors.cs
   public static class TaskErrors
   {
       public static Error NotFound(string taskId) => Error.NotFound(
           code: "Task.NotFound",
           description: $"Task with ID '{taskId}' not found");
   }
   ```

3. **Migration Plan**
   - Create catalog classes first
   - Update one file at a time to avoid merge conflicts
   - Use "Find Usages" to locate all error return sites
   - Update error codes from inline to catalog-based
   - Fix discovered inconsistencies (e.g., `Auth.UserNotFound` → `User.UserNotFound`)

4. **Error Code Normalization**
   - Replace scattered `"Task.NotFound"` with centralized static definition
   - Replace scattered `"User.NotFound"` with parameterized factory
   - Fix `Auth.UserNotFound` where it incorrectly refers to user profile absence

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Features/Auth/` | Modified | All auth handlers, JwtService updated to use AuthErrors catalog |
| `Features/Users/` | Modified | GetUserByIdQueryHandler, DeleteUserCommandHandler updated to use UserErrors catalog |
| `Features/Tasks/` | Modified | GetTaskByIdQueryHandler, UpdateTaskCommandHandler, ToggleTaskCommandHandler, DeleteTaskCommandHandler updated to use TaskErrors catalog |
| `Infrastructure/Auth/JwtService.cs` | Modified | Updated to use AuthErrors catalog for all 4 error returns |
| Total files changed: ~15-20 source files |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| **Incomplete coverage** | Low | Use IDE "Find Usages" on Error class to locate all error return sites. Implement a systematic approach: handle one feature per commit/PR. |
| **Merge conflicts** | Medium | Schedule the changes feature-by-feature and coordinate with team. Use feature branches for isolation. |
| **Hidden dependencies** | Low | Ensure all catalog classes are public static and include XML documentation for discoverability |
| **Copy-paste errors** | Low | Follow the systematic file-by-file update plan. Use IDE refactoring to rename error codes globally once initial catalogs are in place |
| **Breaking changes** | Very Low | This refactoring only centralizes existing error definitions; no behavior changes. Tests should continue to pass as they test the same errors. |

## Rollback Plan
1. Revert the PR to restore inline error definitions
2. All changes are isolated to error return statements;
3. Simple rollback with `git revert` should complete within minutes

## Dependencies
- None. Pure refactoring within the same project.
- Build and test pipeline remains unchanged (same assertions and behaviors)

## Success Criteria
- [ ] All auth errors centralized in `AuthErrors` catalog
- [ ] All task errors centralized in `TaskErrors` catalog using factory methods
- [ ] All user errors centralized in `UserErrors` catalog using factory methods
- [ ] Existing handlers and services compile and behave identically
- [ ] Error code consistency verified (no duplicates, consistent conventions)
- [ ] Semantic correctness: `User.UserNotFound` used where user profile doesn't exist, not `Auth.UserNotFound`
- [ ] No behavioral changes detected by tests
- [ ] PR line count remains under 800 lines (single PR strategy)
