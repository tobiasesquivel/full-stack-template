using FluentValidation;

namespace TemplateApp.Features.Tasks.UpdateTask;

internal sealed class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskCommandValidator()
    {
        RuleFor(x => x.Title).TaskTitle();
        RuleFor(x => x.Version).NotNull();
    }
}
