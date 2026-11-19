namespace TemplateApp.Features.Projects.RebuildTaskCounters;

internal sealed partial class RebuildTaskCountersOnStartup : BackgroundService
{
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _services;
    private readonly TimeProvider _clock;
    private readonly ILogger<RebuildTaskCountersOnStartup> _logger;

    public RebuildTaskCountersOnStartup(IServiceProvider services, TimeProvider clock, ILogger<RebuildTaskCountersOnStartup> logger)
    {
        _services = services;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await using AsyncServiceScope scope = _services.CreateAsyncScope();
                RebuildTaskCounters rebuild = scope.ServiceProvider.GetRequiredService<RebuildTaskCounters>();
                if (await rebuild.IsNeededAsync(stoppingToken))
                    await rebuild.ExecuteAsync(stoppingToken);
                return;
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogWillRetry(error, RetryEvery);
            }

            await Task.Delay(RetryEvery, _clock, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rebuilding the task counters failed; next attempt in {RetryEvery}")]
    private partial void LogWillRetry(Exception error, TimeSpan retryEvery);
}
