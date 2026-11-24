using FluentValidation;

namespace TemplateApp.Features.Tasks.CreateTask;

internal sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.Title).TaskTitle();
    }
}
