namespace TemplateApp.Features.Auth.Contracts;

public sealed record UserAccount(
    string AuthId,
    string? UserName,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
