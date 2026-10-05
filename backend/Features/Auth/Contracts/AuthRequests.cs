namespace TemplateApp.Features.Auth.Contracts;

public sealed record RegisterRequest(string UserName, string Email, string Password, string DisplayName);

public sealed record LoginRequest(string UserName, string Password);
