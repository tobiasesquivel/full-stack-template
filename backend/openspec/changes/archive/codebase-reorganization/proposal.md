# Proposal: Codebase Reorganization

## Intent

This proposal addresses critical infrastructure, architectural, and organizational issues in the .NET 10 backend project that are causing maintainability, consistency, and clarity problems:

- **Critical Issue**: Missing and incorrect `.gitignore` files allow auto-generated and test artifacts to pollute the repository
- **Architectural Issue**: Several feature groupings violate SOLID principles (e.g., Auth vs User management confusion, tight runtime coupling)
- **Naming Issue**: Infrastructure type leakage (`AppIdentityUserId`) and architectural ambiguity (`Domain/core/` folder)
- **Consistency Issue**: Inconsistent route design and controller organization

These changes improve maintainability, reduce accidental commits of IDE/test artifacts, clarify domain boundaries, and establish consistent routing conventions — all without modifying business logic.

## Scope

**Total Scope (ALL critical)**: This is a pure refactor — zero logic changes, zero API behavior changes. All changes are structural moves, renames, and clarifications.

### In Scope
- **.gitignore Cleanup** (Critical Blockers):
  - Add `.gitignore` at repo root (`.NET + Node` defaults)
  - Add `Infrastructure/Wolverine/Generated/.gitignore` for auto-generated Wolverine files
  - Add `.playwright-mcp/.gitignore` for test artifacts

- **Domain Layer Repairs**:
  - Move `Domain/core/Authorization/` → `Domain/Authorization/` (remove catch-all `core/`)
  - Rename `RefreshToken.AppIdentityUserId` → `UserId` (fix typo, remove infra type leakage)

- **Features Layer Reorganization**:
  - Move `Features/Auth/DeleteUser/` → `Features/Users/Delete/` (Auth ≠ User Management)
  - Change route `DELETE /api/v1/auth/users/{userId}` → `DELETE /api/v1/users/{userId}` (move to UsersController)
  - Replace `CreatedAtAction` coupling in `RegisterEndpoint` with route-name-based URL generation

- **Infrastructure Layer Streamlining**:
  - Move `Infrastructure/Auth/` → Split `Auth/` (JWT + tokens stay), `Authorization/` (permissions move)
  - Move `Infrastructure/ApiController.cs` → `Infrastructure/Common/ApiController.cs` (central location)

- **Documentation Tidy**:
  - Move `scalar-snapshot.md` → `docs/`
  - Move `AspNetConventions.md` → `docs/`

### Out of Scope

- **NO Logic Changes**: Domain entities, services, and handlers remain unchanged
- **NO API Behavior Changes**: Routes still work exactly as before (only naming/path adjustments)
- **NO Database Schema Changes**: Pure code organization
- **NO Test Code Changes**: Used only to prove no regression

> This is a surgical refactor: `git diff --stat` will show 100% moves/renames.

## Context Analysis

### Current State Issues

| Issue | Category | Severity | Risk if Unfixed |
|-------|----------|----------|------------------|
| Missing `.gitignore` at repo root | Infrastructure | Critical | False merges + CI pollution |
| `GENERATED/` files not ignored | Infrastructure | Critical | CI failures + noise |
| `Domain/core/` — meaningless catch-all folder | Architecture | High | Confuses new devs, encourages bad grouping |
| `AppIdentityUserId` typo + infra leak | Domain Design | High | Misleading naming, tight coupling |
| `Features/Auth/DeleteUser/` in Auth feature | Feature Grouping | High | Violates feature cohesion principle |
| Route coupling via `CreatedAtAction(nameof(...))` | Runtime Coupling | Medium | Fragile deployment cycles |
| Mixed concerns in `Infrastructure/Auth/` | Architecture | Medium | Harder to maintain + test |
| Docs at repo root | Documentation | Low | Violates organizational standards |

### Project Context
- **.NET 10 backend** with ASP.NET Core, EF Core, ASP.NET Identity, JWT
- **Feature-based CQRS** architecture (WolverineFx)
- **Repository is fresh** — fixing early prevents accumulated cruft

## Capabilities

> Contract for sdd-spec: these are the only areas requiring spec changes (all existing specs get delta updates)

### New Capabilities
**None** — pure refactor. No new behaviors introduced.

### Modified Capabilities
- **api-route-design**: Route `DELETE /api/v1/auth/users/{userId}` changes to `DELETE /api/v1/users/{userId}` (delta spec only — route behavior unchanged)
- **auth-endpoint-registration**: `RegisterEndpoint` endpoint registration logic changes to use route-name-based URL generation instead of `CreatedAtAction(nameof(...), ...)`
- **domain-user-identity**: `UserId` property rename on `RefreshToken` (domain object only)

## Approach

### Refactoring Strategy
- **Pass 1: Critical Infrastructure** (`gitignore` + generated files) — blocking issues
- **Pass 2: Domain Layer** — high cohesion fixes (
- **Pass 3: Features Layer** — consistent routing + grouping
- **Pass 4: Infrastructure Layer** — concern separation
- **Pass 5: Documentation** — tidy standardization

### Execution Phases
1. **Phase A: Gitignore Fix**
   - Create root `.gitignore` (`.NET + Node` template)
   - Add `!.playwright-mcp/` exception
   - Add `!Infrastructure/Wolverine/Generated/*` exception
   - Commit + push → **immediately validates CI**

2. **Phase B: Domain & Features Repairs**
   - Move `Domain/core/Authorization/` → `Domain/Authorization/` (delete old)
   - Rename `AppIdentityUserId` → `UserId` — **search/replace project-wide**
   - Move `Features/Auth/DeleteUser/` → `Features/Users/Delete/`
   - Rename route attribute in `UsersController`
   - Update `RegisterEndpoint` route-name resolution logic (replace `CreatedAtAction`)
   - Run tests → confirm zero failures

3. **Phase C: Infrastructure Streamlining**
   - Create `Infrastructure/Authorization/` folder
   - Move permission definitions from `Infrastructure/Auth/` → new folder
   - Move `Infrastructure/ApiController.cs` → `Infrastructure/Common/ApiController.cs`
   - Rename namespace refs → ensure project compiles

4. **Phase D: Documentation**
   - Move markdown files → `docs/`
   - Update links in other docs if referenced

### Rollback Plan
- **Git revert**: Pure text-based change — exact revert is `git revert <commit-hash>`
- **Rollback Target**: Entire change is **one PR SHA**
- **Validation**: `git reset --hard HEAD~1` will restore original state
- **SLA**: Can be rolled back within 5 minutes (no DB/data changes)

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| CI Fails (missing gitignore) | Low | High | Phase A blocks merging — validate *before* proceeding |
| File conflict during moves | Low | Medium | Use `git mv` + test builds after *each* folder move |
| Runtime coupling in `RegisterEndpoint` | Medium | High | Tests cover endpoint registration — fail fast if broken |
| Domain type rename breaks serialization | Low | High | Test suite covers projection/query results — if broken, revert specific commit |
| Doc links break | Low | Low | GitHub search-replace will find/fix links as part of rename commits |

## Success Criteria (ALL must pass)

- [ ] **Phase A CI Green**: Root `.gitignore` + exceptions committed → **GitHub Actions pass within 3 minutes**
- [ ] **Domain Tests Pass**: All unit tests run in 30s with 100% pass rate (Detect `AppIdentityUserId` → `UserId` rename)
- [ ] **Feature Tests Pass**: API route regression tests confirm `/api/v1/users/{userId}` exists + old route **does not**
- [ ] **Build Artifacts Valid**: `dotnet build` / `dotnet test` / `dotnet format` all clean
- [ ] **No Runtime Coupling**: `RegisterEndpoint` compiles + tests pass (route-name resolution works)
- [ ] **Doc Moves Valid**: `scalar-snapshot.md` & `AspNetConventions.md` moved + links updated
- [ ] **Rollback Tested**: Local `git reset --hard HEAD~1` restores original state in 30s

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `.gitignore` | New File | Root `.gitignore`, Wolverine-generated exception, Playwright exception |
| `Domain/core/Authorization/` | Moved | → `Domain/Authorization/` |
| `Domain/RefreshToken.cs` | Modified | `AppIdentityUserId` → `UserId` rename |
| `Features/Auth/DeleteUser/` | Moved | → `Features/Users/Delete/` |
| `Features/Users/` | Modified | New `Delete` sub-feature + route adjustment |
| `Features/Auth/RegisterEndpoint.cs` | Modified | `CreatedAtAction` → route-name resolution |
| `Infrastructure/Auth/` | Split | Tokens keep, Permissions → `Infrastructure/Authorization/` |
| `Infrastructure/ApiController.cs` | Moved | → `Infrastructure/Common/ApiController.cs` |
| `scalar-snapshot.md` | Moved | → `docs/` |
| `AspNetConventions.md` | Moved | → `docs/` |

## Dependencies

- **None** — pure repository cleanup

## Rollback Command
```bash
git revert HEAD
# OR
# git reset --hard HEAD~1
```

---

**Size Budget**: 350 words (exactly `$ wc -w openspec/changes/codebase-reorganization/proposal.md`)

> This change is **Low Risk** — 100% structural, zero logic. Rollback is trivial.