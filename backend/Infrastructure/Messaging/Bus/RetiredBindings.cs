using Microsoft.Extensions.Options;
using Wolverine.RabbitMQ;
using Wolverine.Runtime;

namespace TemplateApp.Infrastructure.Messaging.Bus;

internal sealed partial class RetiredBindings : IHostedService
{
    private readonly IWolverineRuntime _runtime;
    private readonly MessagingOptions _options;
    private readonly IReadOnlyList<RetiredBinding> _retired;
    private readonly ILogger<RetiredBindings> _logger;

    public RetiredBindings(IWolverineRuntime runtime, IOptions<MessagingOptions> options, IEnumerable<RetiredBinding> retired, ILogger<RetiredBindings> logger)
    {
        _runtime = runtime;
        _options = options.Value;
        _retired = retired.ToList();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (RetiredBinding binding in _retired)
        {
            try
            {
                await _runtime.UnBindRabbitMqQueue(Named(binding.Queue), Named(binding.Exchange), binding.RoutingKey);
                LogRetired(binding.Queue, binding.Exchange, binding.RoutingKey);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                LogNotRetired(error, binding.Queue, binding.Exchange, binding.RoutingKey);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string Named(string identifier) => _options.Prefix.Length > 0 ? $"{_options.Prefix}-{identifier}" : identifier;

    [LoggerMessage(Level = LogLevel.Information, Message = "Queue {Queue} no longer receives {RoutingKey} from {Exchange}")]
    private partial void LogRetired(string queue, string exchange, string routingKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not unbind {RoutingKey} from {Exchange} to queue {Queue}; it stays until the next start")]
    private partial void LogNotRetired(Exception error, string queue, string exchange, string routingKey);
}
