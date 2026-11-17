using FluentValidation;

namespace TemplateApp.Features.Tasks.CreateTask;

internal sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).TaskTitle();
    }
}
