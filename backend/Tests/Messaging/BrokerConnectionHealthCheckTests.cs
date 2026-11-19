using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class BrokerConnectionHealthCheckTests
{
    private readonly RabbitMqFixture _rabbit;

    public BrokerConnectionHealthCheckTests(RabbitMqFixture rabbit) => _rabbit = rabbit;

    [Fact]
    public async Task WithTheBrokerUp_TheCheckIsHealthy()
    {
        Assert.Equal(HealthStatus.Healthy, await CheckAsync(_rabbit.Uri));
    }

    [Fact]
    public async Task WithTheBrokerUnreachable_TheCheckIsDegraded()
    {
        Assert.Equal(HealthStatus.Degraded, await CheckAsync(new Uri("amqp://templateapp:templateapp@127.0.0.1:1/")));
    }

    private static async Task<HealthStatus> CheckAsync(Uri broker)
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddOptions<MessagingOptions>().Configure(options => options.RabbitMqUri = broker);
        services.AddHealthChecks()
            .AddCheck<BrokerConnectionHealthCheck>("rabbitmq", HealthStatus.Degraded, timeout: TimeSpan.FromSeconds(2));
        await using ServiceProvider provider = services.BuildServiceProvider();

        HealthReport report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);
        return report.Entries["rabbitmq"].Status;
    }
}
