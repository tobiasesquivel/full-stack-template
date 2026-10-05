# Spec: Gitignore Configuration

## Purpose

Define `.gitignore` rules that prevent auto-generated files, IDE artifacts, and test outputs from polluting the repository.

## Requirements

### Requirement: Root `.gitignore` covers .NET + Node defaults

The root `.gitignore` MUST include standard .NET (`bin/`, `obj/`, `*.user`, `*.suo`, `*.DotSettings`) and Node (`node_modules/`, `npm-debug.log*`) patterns. All common IDE/tool artifacts (`.vscode/`, `.idea/`, `*.swp`, `*.swo`, `*.DS_Store`) MUST be covered.

#### Scenario: .NET build outputs ignored

- GIVEN a `dotnet build` output
- WHEN checking `git status`
- THEN `bin/` and `obj/` directories MUST NOT appear

#### Scenario: Node artifacts ignored

- GIVEN a project that uses npm
- WHEN `node_modules/` is present
- THEN `node_modules/` MUST be ignored

#### Scenario: IDE artifacts ignored

- GIVEN `.vscode/` or `.idea/` directories
- WHEN running `git status`
- THEN they MUST NOT appear as untracked files

### Requirement: Wolverine generated code ignored

The `.gitignore` MUST contain an exception pattern for `Infrastructure/Wolverine/Generated/*` to allow Wolverine auto-generated handler code while keeping the directory tracked.

#### Scenario: Generated handler code acknowledged

- GIVEN Wolverine generates handler code to `Infrastructure/Wolverine/Generated/`
- WHEN the directory is empty
- THEN `git status` MUST NOT show the directory as untracked
- WHEN files exist inside
- THEN they MAY be tracked if explicitly added

### Requirement: Playwright MCP artifacts ignored

The `.gitignore` MUST contain an exception pattern for `.playwright-mcp/` to allow this tool to create its output files without polluting the repo.

#### Scenario: Playwright MCP output ignored

- GIVEN `.playwright-mcp/` directory exists
- WHEN running `git status`
- THEN all files within `.playwright-mcp/` MUST be ignored

## Acceptance Criteria

| ID | Check | How |
|----|-------|-----|
| AC-1 | Root `.gitignore` exists with .NET + Node defaults | File review |
| AC-2 | `Infrastructure/Wolverine/Generated/` is gitignored | `git check-ignore` |
| AC-3 | `.playwright-mcp/` is gitignored | `git check-ignore` |
| AC-4 | IDE artifacts (`.vscode/`, `.idea/`) are covered | File review |
