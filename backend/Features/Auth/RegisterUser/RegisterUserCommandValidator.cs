using FluentValidation;

namespace TemplateApp.Features.Auth.RegisterUser;

internal sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.UserName)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("UserName must not be null.")
            .Must(userName => userName.Trim().Length is >= 1 and <= 100)
            .WithMessage("UserName must be between 1 and 100 characters.");

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Email must not be null.")
            .Must(BeValidEmail)
            .WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Password must not be null.")
            .Length(6, 128)
            .WithMessage("Password must be between 6 and 128 characters.");
    }

    private static bool BeValidEmail(string email)
    {
        string trimmed = email.Trim();
        return trimmed.Length is >= 3 and <= 256 && trimmed.Contains('@');
    }
}
