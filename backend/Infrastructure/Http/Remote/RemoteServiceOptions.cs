namespace TemplateApp.Infrastructure.Http.Remote;

public sealed class RemoteServiceOptions
{
    public const string Section = "Services";

    public Uri? BaseAddress { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int Retries { get; set; } = 2;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(5);

    public int BreakerMinimumThroughput { get; set; } = 10;

    public int MaxConcurrentRequests { get; set; } = 100;
}
