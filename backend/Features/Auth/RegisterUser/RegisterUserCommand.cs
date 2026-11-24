namespace TemplateApp.Features.Auth.RegisterUser;

internal sealed record RegisterUserCommand(string UserName, string Email, string Password, string DisplayName);
