# Spec: Feature Boundary Reorganization

## Purpose

Move `DeleteUser` from `Features/Auth/` to `Features/Users/` so Auth (identity/tokens) and User management (profile, CRUD) are properly separated. Update route and controller references accordingly.

## Requirements

### Requirement: Move DeleteUser feature to Users

`Features/Auth/DeleteUser/` (4 files) MUST move to `Features/Users/Delete/`. Namespace MUST change from `TemplateApp.Features.Auth.DeleteUser` to `TemplateApp.Features.Users.Delete`.

#### Scenario: Files moved with correct namespace

- GIVEN `DeleteUserCommand.cs`, `DeleteUserCommandHandler.cs`, `DeleteUserCommandValidator.cs`, `DeleteUserEndpoint.cs` in `Features/Auth/DeleteUser/`
- WHEN moved to `Features/Users/Delete/`
- THEN `Features/Auth/DeleteUser/` no longer exists
- AND namespace in all 4 files reads `TemplateApp.Features.Users.Delete`

### Requirement: Route moves from AuthController to UsersController

The HTTP route MUST change from `DELETE /api/v1/auth/users/{userId}` on `AuthController` to `DELETE /api/v1/users/{userId}` on `UsersController`. Behavior MUST remain identical — same authorization, same handler, same response.

#### Scenario: Delete endpoint on UsersController

- GIVEN the `DeleteUserEndpoint.cs` moves to `Features/Users/Delete/`
- WHEN the code is compiled
- THEN the endpoint is a partial method of `UsersController` (not `AuthController`)
- AND the route attribute reads `[HttpDelete("{userId}")]`
- AND parent controller route is `[Route("api/v1/users")]`
- AND `[HasPermission(AppPermissions.Users.Manage)]` is preserved
- AND `[Authorize]` is inherited from `UsersController` class attribute

#### Scenario: Old route removed

- GIVEN the move is complete
- WHEN `Features/Auth/DeleteUser/` is deleted
- THEN `AuthController` has no `DeleteAccount` method
- AND no route exists at `DELETE /api/v1/auth/users/{userId}`

#### Scenario: Ownership check preserved

- GIVEN an authenticated user
- WHEN calling `DELETE /api/v1/users/{userId}`
- THEN `User.FindFirstValue(ClaimTypes.NameIdentifier)` is compared with `userId`
- AND a non-matching user receives 403 Forbidden — same as before

#### Scenario: Handler unchanged

- GIVEN the `DeleteUserCommandHandler` moves to the new namespace
- WHEN `dotnet build` runs
- THEN the handler's logic is identical — no business logic changes

### Requirement: RegisterEndpoint uses route-name-based URL generation

`RegisterEndpoint.cs` MUST replace `CreatedAtAction(nameof(UsersController.GetUserById), ...)` with `CreatedAtRoute("GetUserById", ...)`. The `GetUserById` endpoint MUST define a named route.

#### Scenario: GetUserById defines route name

- GIVEN `GetByIdEndpoint.cs`
- WHEN the route attribute is updated
- THEN `[HttpGet("{authId}", Name = "GetUserById")]` is set
- AND no behavior change — response and authorization remain identical

#### Scenario: Register uses CreatedAtRoute

- GIVEN `RegisterEndpoint.cs`
- WHEN the response mapping is updated
- THEN `CreatedAtAction` is replaced by `CreatedAtRoute("GetUserById", new { authId = authResponse.User.Id }, authResponse)`
- AND the `Location` header still points to `GET /api/v1/users/{authId}`
- AND no other response behavior changes

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | `Features/Auth/DeleteUser/` deleted | File review |
| AC-2 | `Features/Users/Delete/` exists with 4 files | File review |
| AC-3 | `DELETE /api/v1/users/{userId}` responds 204 for authorized owner | Integration test |
| AC-4 | `DELETE /api/v1/auth/users/{userId}` returns 404 or no matching route | Integration test |
| AC-5 | Register response `Location` header points to correct user URL | Integration test |
| AC-6 | `dotnet build` + `dotnet test` pass | CI |
