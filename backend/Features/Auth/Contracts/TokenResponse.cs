namespace TemplateApp.Features.Auth.Contracts;

public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt);
