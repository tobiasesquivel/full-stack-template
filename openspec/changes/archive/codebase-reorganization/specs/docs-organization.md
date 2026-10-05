# Spec: Documentation Organization

## Purpose

Move loose documentation files from the project root to a `docs/` directory for consistency with project structure conventions. No content changes.

## Requirements

### Requirement: Move scalar-snapshot.md to docs/

`scalar-snapshot.md` MUST move from the project root to `docs/scalar-snapshot.md`.

#### Scenario: File moved

- GIVEN `scalar-snapshot.md` at project root
- WHEN moved to `docs/scalar-snapshot.md`
- THEN the file exists at `docs/scalar-snapshot.md`
- AND the file no longer exists at the project root
- AND file content is identical — no modifications

### Requirement: Move AspNetConventions.md to docs/

`AspNetConventions.md` MUST move from the project root to `docs/AspNetConventions.md`.

#### Scenario: File moved

- GIVEN `AspNetConventions.md` at project root
- WHEN moved to `docs/AspNetConventions.md`
- THEN the file exists at `docs/AspNetConventions.md`
- AND the file no longer exists at the project root
- AND file content is identical — no modifications

### Requirement: Update cross-references to AspNetConventions.md

Any internal cross-reference pointing to the root `AspNetConventions.md` MUST be updated to point to `docs/AspNetConventions.md`.

#### Scenario: Spec cross-references updated

- GIVEN `openspec/specs/database-transactions/spec.md` references `AspNetConventions.md`
- WHEN the file moves to `docs/AspNetConventions.md`
- THEN the reference updates from `AspNetConventions.md` to `docs/AspNetConventions.md`

#### Scenario: No broken links remain

- GIVEN all internal markdown files
- WHEN searched for `AspNetConventions.md` without `docs/` prefix
- THEN no matches exist except within `docs/` itself

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | `docs/scalar-snapshot.md` exists | File review |
| AC-2 | `docs/AspNetConventions.md` exists | File review |
| AC-3 | Project root has neither file | File review |
| AC-4 | All cross-references point to `docs/AspNetConventions.md` | Grep |
