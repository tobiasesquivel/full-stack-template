using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Infrastructure.Messaging.Outbox;

public static class OutboxRegistration
{
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddOptionsWithValidateOnStart<OutboxOptions, OutboxOptionsValidator>()
            .BindConfiguration(OutboxOptions.Section);
        services.TryAddSingleton<OutboxEvents>();
        services.TryAddSingleton<OutboxInterceptor>();
        services.TryAddSingleton<OutboxMetrics>();
        services.ConfigureDbContext<TContext>((provider, options) => options.AddInterceptors(provider.GetRequiredService<OutboxInterceptor>()));
        services.AddKeyedSingleton<OutboxRelay>(typeof(TContext), (provider, _) => ActivatorUtilities.CreateInstance<OutboxRelay>(provider, typeof(TContext)));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredKeyedService<OutboxRelay>(typeof(TContext)));
        return services;
    }

    public static IServiceCollection AddOutboxEvent<TEvent>(this IServiceCollection services, string name, int version)
        where TEvent : class, IDomainEvent =>
        services.AddSingleton(new OutboxEventType(typeof(TEvent), name, version));

    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IDomainEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.TryAddScoped<THandler>();
        return services.AddSingleton(new EventHandlerRegistration(
            typeof(TEvent),
            typeof(THandler).FullName ?? typeof(THandler).Name,
            OutboxRelay.DispatchAsync<TEvent, THandler>));
    }
}
