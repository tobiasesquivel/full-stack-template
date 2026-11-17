using FluentValidation;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Features.Users.UpdateProfile;

internal sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("DisplayName must not be null.")
            .Must(UserProfile.IsValidDisplayName)
            .WithMessage($"DisplayName must be between 1 and {UserProfile.DisplayNameMaxLength} characters.");

        RuleFor(x => x.Timezone)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Timezone must not be null.")
            .Must(BeValidTimezone)
            .WithMessage("Timezone must be a valid IANA timezone.");
    }

    private static bool BeValidTimezone(string timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone) || timezone.Length > 100)
            return false;

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
