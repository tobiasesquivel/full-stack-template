using FluentValidation;

namespace TemplateApp.Features.Projects.CreateProject;

internal sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Name).ProjectName();
    }
}
