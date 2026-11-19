using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TemplateApp.Infrastructure.Messaging;

internal sealed class BrokerConnectionHealthCheck : IHealthCheck
{
    private readonly MessagingOptions _options;

    public BrokerConnectionHealthCheck(IOptions<MessagingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ConnectionFactory factory = new()
        {
            Uri = _options.RabbitMqUri!,
            RequestedConnectionTimeout = context.Registration.Timeout,
            AutomaticRecoveryEnabled = false,
            ClientProvidedName = "templateapp-health",
        };
        await using IConnection connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.CloseAsync(cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy();
    }
}
