using FluentValidation;

namespace TemplateApp.Features.Projects.CreateProject;

internal sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).ProjectName();
    }
}
