namespace TemplateApp.Features.Auth.RegisterUser;

internal sealed record RegisterUserRequest(string UserName, string Email, string Password, string DisplayName);
