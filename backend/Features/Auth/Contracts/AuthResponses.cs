using TemplateApp.Features.Users.Contracts;

namespace TemplateApp.Features.Auth.Contracts;

public sealed record AuthResponse(UserResponse User, string AccessToken, DateTimeOffset AccessTokenExpiresAt);

public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt);
