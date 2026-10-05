# Delta Spec: implement-db-conventions

**Base spec**: `openspec/specs/auth/spec.md`

## ADDED — database-transactions

### Requirement: Transaction Boundaries for Multi-Phase Writes

Operations with two or more sequential `SaveChangesAsync` calls MUST be wrapped in an explicit EF Core transaction via `_db.Database.BeginTransactionAsync()`. The transaction MUST commit only after all phases succeed and MUST roll back on any exception.

| Operation | Phases | Escalation Risk |
|-----------|--------|----------------|
| Register (handler) | IdentityUser.CreateAsync + User.Add + token gen (RefreshToken write) | UserManager.CreateAsync may auto-commit — tested, documented if split |
| RedeemTokenAsync | Read token + batch revoke old + GenerateTokensAsync + Revoke old | All share `AppDbContext` — same transaction scope |

#### Scenario: Register commits all three writes atomically
- GIVEN a valid registration creating IdentityUser, domain User, and RefreshToken
- WHEN the handler executes
- THEN all writes succeed within `BeginTransactionAsync` / `CommitAsync`
- AND a 201 response is returned

#### Scenario: Register rolls back on mid-phase failure
- GIVEN a registration where token generation fails after IdentityUser + User are created
- WHEN an exception is thrown
- THEN `transaction.RollbackAsync()` executes
- AND no partial state (orphan IdentityUser without domain User) persists in the database
- AND an error response is returned

#### Scenario: RedeemTokenAsync atomic rotation
- GIVEN a valid unexpired refresh token
- WHEN RedeemTokenAsync completes the rotation
- THEN all writes (revoke batch, generate new, mark old as replaced) commit atomically
- AND a 200 response with new token pair is returned

### Requirement: Projection + AsNoTracking for Reads

Read-only queries MUST use `.Select()` to project only required columns and `.AsNoTracking()` to skip change tracking.

#### Scenario: Login reads User with projection
- GIVEN a login query that needs User.Id and User.Username
- WHEN the handler queries `_db.Set<User>()`
- THEN `.Select(u => new { u.Id, u.Username }).AsNoTracking().FirstOrDefaultAsync()` is used
- AND no full User entity is loaded into the change tracker

---

## MODIFIED — auth-register

### Register User Endpoint — Transaction Enforcement

The existing register scenario "Register a new user successfully" (auth spec §auth-register) specifies a single EF Core transaction. The handler now **enforces** this contract explicitly.
(Previously: spec said transaction but code had no `BeginTransactionAsync` — IdentityUser create, User.Add, and token gen were independent saves)

#### Scenario: Register with existing email rolls back cleanly
- GIVEN a duplicate email
- WHEN the handler returns the conflict error before the transaction starts
- THEN no transaction is opened
- AND no database writes occur — behavior unchanged from existing spec

---

## MODIFIED — auth-jwt

### RedeemTokenAsync — Transaction Scope

`RedeemTokenAsync` now wraps its multi-phase writes in a transaction. The existing scenarios "Refresh with a valid token" and "Reused token triggers theft cascade" (auth spec §auth-refresh) now execute atomically.
(Previously: batch revoke via `ExecuteUpdateAsync` and `storedToken.Revoke()` via `SaveChangesAsync` were independent commits)

#### Scenario: Theft cascade revokes all tokens atomically
- GIVEN a reused (already replaced) refresh token
- WHEN the theft cascade batch-revokes all user tokens
- THEN the `ExecuteUpdateAsync` runs within the transaction
- AND if subsequent `GenerateTokensAsync` fails, the batch revoke is rolled back
- AND the system returns 401 with no partial revocation

---

## MODIFIED — auth-login

### Login User Query — Projection + AsNoTracking

The User query in `LoginCommandHandler.Handle` now uses projection and `AsNoTracking`. The existing login scenarios (auth spec §auth-login) are unaffected in behavior — only the query pattern changes.
(Previously: `_db.Set<User>().FirstOrDefaultAsync(...)` — full entity loaded with change tracking)

#### Scenario: Login returns correct UserInfo with projection
- GIVEN a registered user
- WHEN Login executes
- THEN the User query selects only the needed fields
- AND the response still matches the existing `AuthResponse` format

---

## REMOVED REQUIREMENTS

None.

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | Register transaction spans IdentityUser + User + token | Code review: `BeginTransactionAsync` before step 2, `CommitAsync` after step 4 |
| AC-2 | RedeemTokenAsync writes wrapped in transaction | Code review: single transaction scopes all writes in method |
| AC-3 | Login User query uses Select + AsNoTracking + null check | Code review: `.Select()` + `.AsNoTracking()` + `if (null) return Error` present |
| AC-4 | AspNetConventions.md updated | File review: Database & Persistence section has transaction, projection, null check, and nested record conventions |
| AC-5 | AppDbContext has `DbSet<User> Users` property | File review: property exists in AppDbContext |
| AC-6 | UserInfo nested inside AuthResponse, UserInfo.cs deleted | File review: AuthResponse contains nested UserInfo record; Common/UserInfo.cs removed |
| AC-7 | Build passes | `dotnet build` succeeds with no warnings |
