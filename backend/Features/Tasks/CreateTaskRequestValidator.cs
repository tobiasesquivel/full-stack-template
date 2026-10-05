using FluentValidation;
using TemplateApp.Features.Tasks.Contracts;

namespace TemplateApp.Features.Tasks;

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Title must not be null.")
            .Must(t => t.Trim().Length is >= 1 and <= 200)
            .WithMessage("Title must be between 1 and 200 characters.");
    }
}
