using System.Security.Claims;
using ErrorOr;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks;

public sealed record UpdateTaskRequest(string Title, bool IsCompleted);

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
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
    private readonly UpdateTaskRequestValidator _updateValidator = new();

    public async Task<ErrorOr<TaskResponse>> UpdateAsync(string callerId, Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var validation = await _updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.Errors
                .Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage))
                .ToList();

        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _access.CanAccessAsync(callerId, item, TaskPermissions.Update, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        item.Title = request.Title.Trim();
        item.IsCompleted = request.IsCompleted;
        item.UpdatedAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(ct);

        return ToResponse(item);
    }
}

public static partial class TasksEndpoints
{
    internal static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateTaskRequest request,
        TasksModule module,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await module.UpdateAsync(user.GetCallerId(), id, request, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
