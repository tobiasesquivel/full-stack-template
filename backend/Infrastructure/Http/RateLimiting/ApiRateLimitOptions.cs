using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Infrastructure.Http.RateLimiting;

public sealed record ApiRateLimitOptions
{
    public const string Section = "RateLimiting";

    [Range(1, 86400)]
    public int WindowSeconds { get; init; } = 60;

    [Range(1, 100000)]
    public int UserReads { get; init; } = 300;

    [Range(1, 100000)]
    public int UserWrites { get; init; } = 60;

    [Range(1, 100000)]
    public int AnonymousRequests { get; init; } = 120;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
