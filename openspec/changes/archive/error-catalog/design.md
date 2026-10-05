# Design: Error Catalog Implementation

## Technical Approach

Introduce three static error catalog classes (`AuthErrors`, `TaskErrors`, `UserErrors`) to centralize all ErrorOr error definitions. This refactoring replaces scattered inline `Error.*` calls with consistent, discoverable singleton error instances organized by domain.

The implementation follows a **static catalog** pattern for fully-qualified domain errors (Auth) and **factory methods** for parameterized errors requiring dynamic values (Guid taskId, string authId).

File changes are organized into **three independent workstreams** that can be executed in any order without merge conflicts:

1. **AuthErrors** - All static errors in `Features/Auth/Common/AuthErrors.cs`
2. **TaskErrors** - Factory method in `Features/Tasks/Common/TaskErrors.cs`
3. **UserErrors** - Factory method in `Features/Users/Common/UserErrors.cs` (creates new Common folder)

## Architecture Decisions

### Decision: Error Catalog Pattern
**Choice**: Static error catalog classes with factory methods for parameterized errors
**Alternatives considered**: 
- Global constants file (rejected: no scoping/organization)
- Scattered inline errors (rejected: inconsistent, hard to maintain)
- Enum-based errors (rejected: lacks ErrorOr framework integration)

**Rationale**: The static catalog pattern provides:
- Centralized single source of truth for error definitions
- Compile-time safety and discoverability via IDE autocomplete
- Consistent error codes and descriptions across the codebase
- Semantic grouping by domain (Auth vs Task vs User)
- Factory methods for dynamic error contexts (IDs)
- Easy testing and verification of error scenarios

### Decision: Static Errors for Auth Domain
**Choice**: `public static Error EmailAlreadyExists => Error.Conflict(...)` for all auth errors
**Alternatives considered**: 
- Factory methods for all errors (rejected: auth errors never need dynamic parameters)
- Enum-based registry (rejected: less discoverable than static members)

**Rationale**: Auth errors are consistently-qualified and never require dynamic parameters. Static members ensure the same Error instance is reused everywhere, maintaining consistency.

### Decision: Factory Methods for Entity Domains
**Choice**: `public static Error NotFound(Guid taskId)` and `public static Error NotFound(string authId)` for NotFound errors requiring IDs
**Alternatives considered**:
- Inline Error creation with parameters (rejected: no centralization)
- Fully static errors with harcoded IDs (rejected: defeats purpose of factory)
- No factory (rejected: duplicates code patterns)

**Rationale**: Task and User domains frequently need to report "not found" errors with the missing entity's ID in the description. Factory methods provide a DRY pattern for this common case while still centralizing the error code and base description.

### Decision: Existing Folder Structure
**Choice**: Reuse `Features/{Domain}/Common/` folders for catalog classes without changing namespace structure
**Alternatives considered**:
- New `ErrorCatalogs/` folder (rejected: violates existing domain layout)
- Top-level `Common/ErrorCatalogs/` (rejected: premature abstraction)
- Feature-less structure (rejected: loses semantic domain context)

**Rationale**: The `Features/{Domain}/Common` folders already exist for shared infrastructure within each feature domain. Placing error catalogs there follows the existing pattern and keeps related code co-located.

### Decision: Sequential Error Return Sites
**Choice**: Replace error return sites in source files AFTER creating catalog classes
**Alternatives considered**:
- Create then immediately replace in same step (rejected: harder to review/test)
- Create catalogs, commit, then replace all files (rejected: two-step but cleaner)

**Rationale**: Creating catalogs first establishes a known-good baseline. Replacing error returns is a systematic refactor that benefits from the catalogs existing, making compilation and testing straightforward.

## Data Flow

```
[Handlers/Services] → [Error Catalog] → ErrorOr Error Definitions
       │                              │
       ├── Auth: EmailAlreadyExists ──┘
       ├── Auth: InvalidCredentials ──┘
       ├── Auth: UserNotFound ───────┘
       └── ... (all error types)
```

Error catalogs act as an **abstraction layer** between error sources and ErrorOr error definitions. Handlers query the catalog (statically) to obtain the canonical error instance, ensuring every caller uses the same error code, type, and description.

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `Features/Auth/Common/AuthErrors.cs` | Create | Static error catalog for Auth domain with 5 error definitions |
| `Features/Tasks/Common/TaskErrors.cs` | Create | Factory method catalog for Task domain errors |
| `Features/Users/Common/UserErrors.cs` | Create | Factory method catalog for User domain errors + semantic domain fix |
| `Features/Auth/Register/RegisterCommandHandler.cs` | Modify | Swap inline auth error for `AuthErrors.EmailAlreadyExists` |
| `Features/Auth/Login/LoginCommandHandler.cs` | Modify | Swap inline auth errors for `AuthErrors.InvalidCredentials` and `AuthErrors.UserNotFound` |
| `Infrastructure/Auth/JwtService.cs` | Modify | Swap 4 inline auth errors for `AuthErrors.InvalidToken` and `AuthErrors.TokenTheft` |
| `Features/Tasks/GetById/GetTaskByIdQueryHandler.cs` | Modify | Swap inline error for `TaskErrors.NotFound(query.Id)` |
| `Features/Tasks/Update/UpdateTaskCommandHandler.cs` | Modify | Swap inline error for `TaskErrors.NotFound(command.Id)` |
| `Features/Tasks/Toggle/ToggleTaskCommandHandler.cs` | Modify | Swap inline error for `TaskErrors.NotFound(command.Id)` |
| `Features/Tasks/Delete/DeleteTaskCommandHandler.cs` | Modify | Swap inline error for `TaskErrors.NotFound(command.Id)` |
| `Features/Users/GetById/GetUserByIdQueryHandler.cs` | Modify | Swap inline error and error code for `UserErrors.NotFound(query.AuthId)` (fixes to User domain instead of Auth) |
| `Features/Users/Delete/DeleteUserCommandHandler.cs` | Modify | Replace `Auth.UserNotFound` (wrong domain) with `UserErrors.NotFound(command.UserId)` |

## Interfaces / Contracts

### AuthErrors Contract
```csharp
// Features/Auth/Common/AuthErrors.cs
public static class AuthErrors
{
    /// <summary>Email is already registered.</summary>
    public static Error EmailAlreadyExists => Error.Conflict(code: "Auth.EmailAlreadyExists", description: "Email is already registered");

    /// <summary>Invalid credentials provided.</summary>
    public static Error InvalidCredentials => Error.Unauthorized(code: "Auth.InvalidCredentials", description: "Invalid credentials");

    /// <summary>User projection not found.</summary>
    public static Error UserNotFound => Error.NotFound(code: "Auth.UserNotFound", description: "User not found");

    /// <summary>Invalid refresh token.</summary>
    public static Error InvalidToken => Error.Unauthorized(code: "Auth.InvalidToken", description: "Invalid refresh token");

    /// <summary>Token theft detected. All sessions revoked.</summary>
    public static Error TokenTheft => Error.Unauthorized(code: "Auth.TokenTheft", description: "Token theft detected. All sessions revoked.");
}
```

**Usage Sites**: `AuthResponse.cs` not modified (only error definitions); `
`RegisterCommandHandler.cs` uses `AuthErrors.EmailAlreadyExists`; \
`LoginCommandHandler.cs` uses `AuthErrors.InvalidCredentials` and `AuthErrors.UserNotFound`; \
`JwtService.cs` uses `AuthErrors.InvalidToken` and `AuthErrors.TokenTheft`;

**Constraints**: All errors are `public static readonly` singletons. Error code format: `Auth.{MemberName}`.


### TaskErrors Contract
```csharp
// Features/Tasks/Common/TaskErrors.cs
public static class TaskErrors
{
    /// <summary>Create a not-found error for the given task ID.</summary>
    /// <param name="taskId">The task ID that wasn't found.</param>
    public static Error NotFound(Guid taskId) => Error.NotFound(
        code: "Task.NotFound",
        description: $"Task with ID '{taskId}' not found.");
}
```

**Usage Sites**: Any task handler (`GetTaskById`, `UpdateTaskCommand`, `ToggleTaskCommand`, `DeleteTaskCommand`) returning `Error.NotFound(...)` with format "Task with ID '{id}' not found." must use this factory.

**Parameters**: `taskId` - the Guid used to query the task
**Returns**: An Error instance with code `Task.NotFound` and parameterized description


### UserErrors Contract
```csharp
// Features/Users/Common/UserErrors.cs
public static class UserErrors
{
    /// <summary>Create a not-found error for the given user Auth ID.</summary>
    /// <param name="authId">The Auth ID that wasn't found.</param>
    public static Error NotFound(string authId) => Error.NotFound(
        code: "User.NotFound",
        description: $"User with ID '{authId}' not found.");
}
```

**Usage Sites**: User handlers (`GetUserByIdQueryHandler`, `DeleteUserCommandHandler`) returning errors with code `User.NotFound` must use this factory.

**Parameters**: `authId` - the string AuthId used to query the user
**Returns**: An Error instance with code `User.NotFound` (NOT `Auth.UserNotFound`) and parameterized description

**Semantic Fix**: Replaces the mis-used `Auth.UserNotFound` in `DeleteUserCommandHandler` with the correct domain-scoped `User.NotFound`.


## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| **Unit Testing** | Catalog class definitions compile and contain expected members | IDE/compiler verification; Ensure `AuthErrors`, `TaskErrors`, `UserErrors` classes compile without warnings |
| **Compilation** | All modified files compile successfully | `dotnet build` target in CI pipeline; No inline error invocation; All error accesses via catalog class |
| **Integration** | Handlers/Services return catalog-based errors without behavior change | Existing test suite passes; Error codes and descriptions match specification tables |
| **Semantic Validation** | `UserErrors` used where user profile missing (not `AuthErrors`) | `DeleteUserCommandHandler` returns `User.NotFound` instead of `Auth.UserNotFound`; Tests validate error code |

**Verification Steps**:
1. Apply changes in any order
2. `dotnet build` ensures catalogs compile and are accessible
3. Run existing test suite: confirms no behavioral changes
4. Check error codes via `grep` for "User.NotFound" in user handlers vs "Auth.UserNotFound"


## Migration / Rollout

### Manifest Strategy
```bash
# 1. Create the three catalog files first (compiles immediately)
dotnet build

# 2. Apply the 9 file updates one-by-one
# Order doesn't matter - each file is independent
dotnet build  # verify after each change

# 3. Run the test suite
dotnet test
```

### Rollback Plan
**Single revert**: `git revert <commit>` restores inline error definitions across all modified files.

**Isolation**: Each PR contains either the 3 catalogs OR the 9 file updates — optional to combine. The catalogs are inert until usages are updated.

### Feature Flags
Not applicable — pure refactoring with no runtime impact.

### Phased Rollout
None — changes are behaviorally identical. Roll out in a single PR for consistency.

## Open Questions
- [ ] **Execution Order**: Should we recommend a specific order beyond "create catalogs first, then replace usages"? Answer: No — files are independent, order arbitrary.
- [ ] **Documentation**: Should we add XML comments to each error member for IntelliSense? Yes — already implemented in the design.
- [ ] **Namespace Consistency**: Should we ensure `UserErrors` is in `TemplateApp.Features.Users.Common` or match existing namespace? Yes — uses existing namespace pattern.

## Next Step
Ready for implementation tasks (`sdd-tasks`):
1. Create `AuthErrors.cs`, `TaskErrors.cs`, `UserErrors.cs`
2. Update all 9 error return sites in the handlers and services
3. Verify compilation and run test suite