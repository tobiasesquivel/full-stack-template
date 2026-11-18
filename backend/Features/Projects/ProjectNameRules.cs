using FluentValidation;
using TemplateApp.Features.Projects.Domain;

namespace TemplateApp.Features.Projects;

internal static class ProjectNameRules
{
    public static IRuleBuilderOptions<T, string> ProjectName<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Name must not be null.")
            .Must(Project.IsValidName)
            .WithMessage($"Name must be between 1 and {Project.NameMaxLength} characters.");
}
