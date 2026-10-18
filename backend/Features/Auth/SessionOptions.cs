using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Features.Auth;

public sealed record SessionOptions
{
    public const string Section = "Session";

    [Range(1, 365)]
    public int AbsoluteExpirationDays { get; init; }

    [Range(1, 1440)]
    public int IdleTimeoutMinutes { get; init; }

    public TimeSpan Lifetime => TimeSpan.FromDays(AbsoluteExpirationDays);
    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);
}
