namespace TemplateApp.Features.Users.Contracts;

public sealed record UserResponse(
    string AuthId,
    string? UserName,
    string? Email,
    string DisplayName,
    string Timezone,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
