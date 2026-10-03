# Archive Report: add-users-get-by-id

## Change Summary

| Field | Value |
|-------|-------|
| Change Name | `add-users-get-by-id` |
| Archive Date | 2026-06-09 |
| Verification Verdict | PASS |
| Status | Completed |

### What Was Implemented

1. **User Entity Refactor** — `User.Id` (int PK) removed; `AuthId` (string) promoted to PK + FK to `AspNetUsers.Id`. EF Core migration `MakeAuthIdPrimaryKey` generated.

2. **GET /api/v1/users/{authId}** — New read-only endpoint returning `{ AuthId, UserName }` only. No PII exposure. Uses `AsNoTracking()`, `Select()` projection, `ErrorOr.NotFound()` pattern.

3. **Auth Layer Updates** — Register Location header points to new `GetUserById` endpoint. Login uses `FindByNameAsync(command.Username)` instead of email. `LoginUserCommand/Request` field renamed `Email` → `Username`. `AuthResponse.UserInfo` changed from `int Id` to `string AuthId`.

4. **New Feature Structure** — `Features/Users/` feature folder with `UsersController` partial class, `GetById/` subfolder containing query, handler, result DTO, and endpoint.

## Specs Synced

| Spec | Action | Details |
|------|--------|---------|
| `openspec/specs/auth/spec.md` | Updated | Register: Location header, `authId` in response JSON. Login: `Username` field, `authId` instead of `int Id`. Removed old `UserInfo.Id` requirement. |
| `openspec/specs/user-entity/spec.md` | Created | New spec from delta. AuthId PK, Fluent API config, Register/DeleteUser ownership, AuthResponse.UserInfo. |
| `openspec/specs/users-get-by-id/spec.md` | No change | Already the final spec (not a delta). |

## Archive Contents

- `proposal.md` ✅
- `design.md` ✅
- `specs/auth/spec.md` ✅ (delta merged into main)
- `specs/user-entity/spec.md` ✅ (created as new main spec)
- `tasks.md` ✅ (12/12 implementation tasks complete, 6 testing tasks waived — no test project)
- `verify-report.md` ✅ (PASS, 1 critical finding fixed)
- `.archived` ✅

## Task Completion

| Phase | Tasks | Completed | Status |
|-------|-------|-----------|--------|
| Foundation (PK Refactor) | 3 | 3 | ✅ 100% |
| Auth Layer Updates | 4 | 4 | ✅ 100% |
| New Users Feature | 5 | 5 | ✅ 100% |
| Testing & Verification | 6 | 0 | ⏭️ Waived (no test project) |
| **TOTAL** | **18** | **12** | **67%** (all implementable tasks done) |

## Post-Verify Notes

- **Login Username Fix**: `LoginUserCommand`/`LoginRequest` field renamed from `Email` to `Username`. Handler now uses `FindByNameAsync(command.Username)` instead of email-based lookup.
- **CreatedAtAction Typed**: Register endpoint uses `nameof(UsersController.GetUserById)` for compile-time safety instead of string literal.
- **CRITICAL Finding Fixed**: `var userData` in `LoginCommandHandler.cs` replaced with explicit type `GetUserByIdQueryResult?` per AspNetConventions.md "No var" rule.

## Source of Truth Updated

The following specs now reflect the new behavior:
- `openspec/specs/auth/spec.md` — auth register + login requirements updated with `AuthId` and Location header
- `openspec/specs/user-entity/spec.md` — new spec defining User entity schema with AuthId as PK
- `openspec/specs/users-get-by-id/spec.md` — GET endpoint spec (unchanged)

## SDD Cycle Complete

The change has been fully planned, implemented, verified, and archived.

**Archived to**: `openspec/changes/add-users-get-by-id/` (marked with `.archived` timestamp)
