namespace TemplateApp.Features.Auth.Common;

public sealed record AuthResponse(UserInfo User, string AccessToken, string RefreshToken);

public sealed record UserInfo(string AuthId, string Email, string UserName, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
