namespace TemplateApp.Features.Auth;

public sealed record IssuedSession(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshPlaintext);
