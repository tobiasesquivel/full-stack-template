namespace TemplateApp.Features.Auth.RegisterUser;

internal sealed partial record RegisterUserCommand(string UserName, string Email, string Password, string DisplayName);
