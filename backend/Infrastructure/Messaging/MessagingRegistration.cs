using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.HealthChecks;
using TemplateApp.Infrastructure.Messaging.Bus;
using TemplateApp.Infrastructure.Messaging.Mediator;
using TemplateApp.Infrastructure.Messaging.Sagas;

namespace TemplateApp.Infrastructure.Messaging;

public static class MessagingRegistration
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(2);

    public static IServiceCollection AddMessaging(this IServiceCollection services, Assembly? application = null, bool storesSagas = false)
    {
        services.AddOptionsWithValidateOnStart<MessagingOptions, MessagingOptionsValidator>()
            .BindConfiguration(MessagingOptions.Section);
        IntegrationEventNaming.Install();
        services.AddWolverine(ExtensionDiscovery.ManualOnly, options =>
        {
            options.ApplicationAssembly = application ?? typeof(MessagingRegistration).Assembly;
            options.UseFluentValidation(RegistrationBehavior.ExplicitRegistration);
            if (storesSagas)
                SagaStorage.Configure(options);
        });
        services.AddSingleton<IWolverineExtension, MessagingExtension>();
        services.TryAddScoped<IntegrationEventPublisher>();
        services.AddHostedService<RetiredBindings>();
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

    public static IServiceCollection AddRetiredBinding(this IServiceCollection services, string exchange, string routingKey, string queue) =>
        services.AddSingleton(new RetiredBinding(exchange, routingKey, queue));

    public static IServiceCollection AddIntegrationCommand<TCommand>(this IServiceCollection services, string name, int version, string exchange)
        where TCommand : class =>
        services.AddSingleton(new IntegrationCommandType(new IntegrationEventType(typeof(TCommand), name, version, exchange)));

    public static IServiceCollection AddSaga<TSaga, TContext>(this IServiceCollection services)
        where TSaga : Saga
        where TContext : DbContext
    {
        services.TryAddScoped<SagaOutbox<TContext>>();
        return services.AddSingleton(new SagaType(typeof(TSaga)));
    }

    public static IServiceCollection AddMediatorHandler<THandler>(this IServiceCollection services, MediatorTransaction transaction)
        where THandler : class =>
        services.AddSingleton(new MediatorHandlerType(typeof(THandler), transaction));

    public static IServiceCollection AddSagaMessage<TMessage>(this IServiceCollection services, string name, int version, string exchange, string queue)
        where TMessage : class =>
        services.AddSingleton(new SagaSubscription(new IntegrationEventType(typeof(TMessage), name, version, exchange), queue));
}
