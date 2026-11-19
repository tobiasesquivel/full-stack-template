using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Infrastructure.Messaging;

public static class OutboxRegistration
{
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddSingleton<OutboxEvents>();
        services.TryAddSingleton<OutboxInterceptor>();
        services.ConfigureDbContext<TContext>((provider, options) => options.AddInterceptors(provider.GetRequiredService<OutboxInterceptor>()));
        return services;
    }

    public static IServiceCollection AddOutboxEvent<TEvent>(this IServiceCollection services, string name, int version)
        where TEvent : class, IDomainEvent =>
        services.AddSingleton(new OutboxEventType(typeof(TEvent), name, version));
}
