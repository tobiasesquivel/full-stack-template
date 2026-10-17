namespace TemplateApp.Features.Auth.RegisterUser;

public sealed record RegisterUserRequest(string UserName, string Email, string Password, string DisplayName);
