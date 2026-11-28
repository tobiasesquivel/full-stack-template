using TemplateApp.Infrastructure.Access;

namespace TemplateApp.Features.Tasks.Access;

internal sealed partial class PermissionsDeclaration : BackgroundService
{
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LastRetry = TimeSpan.FromSeconds(15);

    private readonly HostPermissions _declarations;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PermissionsDeclaration> _logger;
    private readonly TaskCompletionSource _declared = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PermissionsDeclaration(HostPermissions declarations, IServiceScopeFactory scopes, ILogger<PermissionsDeclaration> logger)
    {
        _declarations = declarations;
        _scopes = scopes;
        _logger = logger;
    }

    public Task Declared => _declared.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan retry = FirstRetry;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (await DeclareAllAsync(stoppingToken))
                    LogDeclared(_logger, _declarations.All.Count, attempt);
                else
                    LogPreviousRelease(_logger);

                _declared.TrySetResult();
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogRetry(_logger, ex, attempt, retry);
            }

            await Task.Delay(retry, stoppingToken);
            retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LastRetry.Ticks));
        }
    }

    private async Task<bool> DeclareAllAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopes.CreateScope();
        AuthPermissionsClient auth = scope.ServiceProvider.GetRequiredService<AuthPermissionsClient>();
        foreach (FeaturePermissions declaration in _declarations.All)
        {
            if (!await auth.DeclareAsync(declaration, ct))
                return false;
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Auth accepted the permissions of {Features} features on attempt {Attempt}")]
    private static partial void LogDeclared(ILogger logger, int features, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth has no route for permission declarations (a release before step T4); this host relies on the permissions that release seeds")]
    private static partial void LogPreviousRelease(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth did not accept the permissions on attempt {Attempt}; retrying in {Retry}")]
    private static partial void LogRetry(ILogger logger, Exception exception, int attempt, TimeSpan retry);
}
