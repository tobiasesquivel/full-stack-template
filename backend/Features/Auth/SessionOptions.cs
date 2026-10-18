namespace TemplateApp.Features.Auth;

public sealed record SessionOptions
{
    public int AbsoluteExpirationDays { get; init; }
    public int IdleTimeoutMinutes { get; init; }

    public TimeSpan Lifetime => TimeSpan.FromDays(AbsoluteExpirationDays);
    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);
}
