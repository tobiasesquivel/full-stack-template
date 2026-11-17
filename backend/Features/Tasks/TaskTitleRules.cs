using FluentValidation;
using TemplateApp.Features.Tasks.Domain;

namespace TemplateApp.Features.Tasks;

internal static class TaskTitleRules
{
    public static IRuleBuilderOptions<T, string> TaskTitle<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Title must not be null.")
            .Must(TaskItem.IsValidTitle)
            .WithMessage($"Title must be between 1 and {TaskItem.TitleMaxLength} characters.");
}
