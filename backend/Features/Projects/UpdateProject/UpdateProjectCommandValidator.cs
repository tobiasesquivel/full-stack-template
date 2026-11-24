using FluentValidation;

namespace TemplateApp.Features.Projects.UpdateProject;

internal sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.Name).ProjectName();
        RuleFor(x => x.Version).NotNull();
    }
}
