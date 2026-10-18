namespace TemplateApp.Infrastructure.Persistence;

public sealed class DatabaseInitialization : IHostedService
{
    private readonly IServiceProvider _services;

    public DatabaseInitialization(IServiceProvider services)
    {
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        DatabaseInitializer.InitializeAsync(_services, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
