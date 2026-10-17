using FluentValidation;

namespace TemplateApp.Features.Tasks.UpdateTask;

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).TaskTitle();
    }
}
