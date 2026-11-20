using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wolverine;
using Wolverine.HealthChecks;

namespace TemplateApp.Infrastructure.Messaging;

public static class MessagingRegistration
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(2);

    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<MessagingOptions, MessagingOptionsValidator>()
            .BindConfiguration(MessagingOptions.Section);
        IntegrationEventNaming.Install();
        services.AddWolverine(ExtensionDiscovery.ManualOnly, options => options.ApplicationAssembly = typeof(MessagingRegistration).Assembly);
        services.AddSingleton<IWolverineExtension, MessagingExtension>();
        services.TryAddScoped<IntegrationEventPublisher>();
        services.AddHealthChecks()
            .AddCheck<BrokerConnectionHealthCheck>("rabbitmq", HealthStatus.Degraded, timeout: HealthCheckTimeout)
            .AddWolverine("broker", HealthStatus.Degraded, timeout: HealthCheckTimeout)
            .AddWolverineListeners("broker-listeners", failureStatus: HealthStatus.Degraded, timeout: HealthCheckTimeout);
        return services;
    }

    public static IServiceCollection AddIntegrationEventPublication<TEvent>(this IServiceCollection services, string name, int version, string exchange)
        where TEvent : class =>
        services.AddSingleton(new IntegrationEventType(typeof(TEvent), name, version, exchange));

    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services, string name, int version, string exchange, string queue)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.TryAddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return services.AddSingleton(new IntegrationEventSubscription(new IntegrationEventType(typeof(TEvent), name, version, exchange), queue));
    }
}
