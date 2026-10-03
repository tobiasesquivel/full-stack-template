namespace TemplateApp.Infrastructure.Options;

public sealed record JwtOptions
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; init; } = 0;
    public int RefreshTokenExpirationDays { get; init; } = 0;
}
