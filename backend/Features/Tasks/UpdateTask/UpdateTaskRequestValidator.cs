using FluentValidation;

namespace TemplateApp.Features.Tasks.UpdateTask;

internal sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).TaskTitle();
        RuleFor(x => x.Version).NotNull();
    }
}
