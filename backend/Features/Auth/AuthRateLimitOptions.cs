using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Features.Auth;

internal sealed record AuthRateLimitOptions
{
    public const string Section = "AuthRateLimiting";

    [Range(1, 100000)]
    public int PermitLimit { get; init; } = 20;

    [Range(1, 86400)]
    public int WindowSeconds { get; init; } = 60;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
