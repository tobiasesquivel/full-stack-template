using System.Security.Claims;
using ErrorOr;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Shared.Authorization;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks;

public sealed record CreateTaskRequest(string Title);

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Title must not be null.")
            .Must(t => t.Trim().Length is >= 1 and <= 200)
            .WithMessage("Title must be between 1 and 200 characters.");
    }
}

public sealed partial class TasksModule
{
    private readonly CreateTaskRequestValidator _createValidator = new();

    public async Task<ErrorOr<TaskResponse>> CreateAsync(string callerId, CreateTaskRequest request, CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.Errors
                .Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage))
                .ToList();

        if (!await _permissions.HasPermissionAsync(callerId, AppPermissions.Tasks.Create, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        var item = new TaskItem
        {
            Id = Guid.NewGuid(),
            OwnerId = callerId,
            Title = request.Title.Trim(),
            IsCompleted = false,
            CreatedAt = _clock.GetUtcNow(),
        };

        _db.TaskItems.Add(item);
        await _db.SaveChangesAsync(ct);

        return ToResponse(item);
    }
}

public static partial class TasksEndpoints
{
    private static async Task<IResult> CreateAsync(
        CreateTaskRequest request,
        TasksModule module,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await module.CreateAsync(user.GetCallerId(), request, ct);
        return ApiResults.ToResult(result, value => Results.Created($"/api/v1/tasks/{value.Id}", value));
    }
}
