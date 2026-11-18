using FluentValidation;

namespace TemplateApp.Features.Projects.UpdateProject;

internal sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).ProjectName();
        RuleFor(x => x.Version).NotNull();
    }
}
