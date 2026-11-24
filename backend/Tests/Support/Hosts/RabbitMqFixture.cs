using Testcontainers.RabbitMq;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

[assembly: AssemblyFixture(typeof(RabbitMqFixture))]

namespace TemplateApp.Tests.Support.Hosts;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    public const string Image = "rabbitmq:4.4.1-management";
    public const int ManagementPort = 15672;

    private RabbitMqContainer? _rabbit;

    public Uri Uri { get; private set; } = null!;

    public Uri ManagementUri { get; private set; } = null!;

    public static RabbitMqContainer Build() =>
        new RabbitMqBuilder(Image)
            .WithUsername("templateapp")
            .WithPassword("templateapp")
            .WithPortBinding(ManagementPort, true)
            .Build();

    public async ValueTask InitializeAsync()
    {
        _rabbit = Build();
        await _rabbit.StartAsync();
        Uri = new Uri(_rabbit.GetConnectionString());
        ManagementUri = new Uri($"http://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(ManagementPort)}/");
    }

    public async ValueTask DisposeAsync()
    {
        if (_rabbit is not null)
            await _rabbit.DisposeAsync();
    }
}
