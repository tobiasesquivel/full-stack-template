# Task Error Catalog Specification

## Purpose

Centralize task-domain ErrorOr errors in the static `TaskErrors` class with a factory method for parameterized task-ID errors, replacing inline `Error.NotFound()` calls across all task handlers.

## Requirements

### Requirement: Factory Method

`TaskErrors` MUST provide a factory method accepting a `Guid taskId` parameter:

```
public static Error NotFound(Guid taskId)
```

It MUST return `Error.NotFound(code: "Task.NotFound", description: $"Task with ID '{taskId}' not found.")`.

#### Scenario: `NotFound` returns parameterized error

- GIVEN a `Guid` value representing a task ID
- WHEN `TaskErrors.NotFound(taskId)` is called
- THEN the returned `Error` has code `Task.NotFound`
- AND the description includes the formatted task ID

### Requirement: Handler Integration

Task query and command handlers MUST call `TaskErrors.NotFound(taskId)` instead of inline `Error.NotFound()` at the following sites:

| File | Call Site | Error |
|------|-----------|-------|
| `GetTaskByIdQueryHandler.cs` | Task not found by ID | `TaskErrors.NotFound(query.Id)` |
| `UpdateTaskCommandHandler.cs` | Task to update not found | `TaskErrors.NotFound(command.Id)` |
| `ToggleTaskCommandHandler.cs` | Task to toggle not found | `TaskErrors.NotFound(command.Id)` |
| `DeleteTaskCommandHandler.cs` | Task to delete not found | `TaskErrors.NotFound(command.Id)` |

#### Scenario: GetById returns `TaskErrors.NotFound` for missing task

- GIVEN a task ID that does not exist in the database
- WHEN `GetTaskByIdQueryHandler` queries for it
- THEN it returns `TaskErrors.NotFound(query.Id)`
- AND the response description contains the queried ID

#### Scenario: Update returns `TaskErrors.NotFound` for missing task

- GIVEN a non-existent task ID in an update command
- WHEN `UpdateTaskCommandHandler` looks up the task
- THEN it returns `TaskErrors.NotFound(command.Id)`
- AND no database write occurs

#### Scenario: Toggle returns `TaskErrors.NotFound` for missing task

- GIVEN a non-existent task ID in a toggle-completion command
- WHEN `ToggleTaskCommandHandler` looks up the task
- THEN it returns `TaskErrors.NotFound(command.Id)`
- AND the task completion status is not modified

#### Scenario: Delete returns `TaskErrors.NotFound` for missing task

- GIVEN a non-existent task ID in a delete command
- WHEN `DeleteTaskCommandHandler` looks up the task
- THEN it returns `TaskErrors.NotFound(command.Id)`
- AND no deletion occurs
