using TemplateApp.Features.Auth.Contracts;

namespace TemplateApp.Features.Users.Contracts;

public sealed record UserResponse(
    string AuthId,
    string? UserName,
    string? Email,
    string DisplayName,
    string Timezone,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    public static UserResponse From(UserAccountDetails account, UserProfileDetails profile) =>
        new(
            account.AuthId,
            account.UserName,
            account.Email,
            profile.DisplayName,
            profile.Timezone,
            account.Roles,
            account.Permissions);
}
