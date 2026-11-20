using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain.Events;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth;

internal static class AuthFeature
{
    public const string RateLimitPolicy = "auth";

    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<AuthDbContext>(AuthDbContext.Schema, adoptsLegacyTables: true);
        services.AddOutbox<AuthDbContext>();
        services.AddOutboxEvent<AccountDeleted>(AuthEvents.AccountDeleted, AuthEvents.Version);
        services.AddOutboxEvent<AccountClosureRejected>(AuthEvents.AccountClosureRejected, AuthEvents.Version);
        services.AddIntegrationEventPublication<AccountDeletedInfo>(AuthEvents.AccountDeleted, AuthEvents.Version, AuthEvents.Exchange);
        services.AddIntegrationEventPublication<AccountClosureRejectedInfo>(AuthEvents.AccountClosureRejected, AuthEvents.Version, AuthEvents.Exchange);
        services.AddEventHandler<AccountDeleted, PublishAuthEvents.AuthEventPublisher>();
        services.AddEventHandler<AccountClosureRejected, PublishAuthEvents.AuthEventPublisher>();
        services.AddIntegrationEventHandler<DecideAccountClosure.OwnerTasksHeldIntegrationEvent, DecideAccountClosure.OwnerTasksHeldConsumer>(TaskEvents.OwnerTasksHeld, TaskEvents.Version, TaskEvents.Exchange, DecideAccountClosure.DecideAccountClosure.Consumer);
        services.AddIntegrationEventHandler<DecideAccountClosureInfo, DecideAccountClosure.DecideAccountClosureCommandsConsumer>(AuthCommands.DecideAccountClosure, AuthCommands.Version, AuthCommands.Exchange, DecideAccountClosure.DecideAccountClosure.Consumer);
        services.AddAuthIdentity();
        services.AddAuthSession();
        services.AddAuthHttpPolicy();

        services.AddSingleton<AuthMetrics>();
        services.AddScoped<AccessPolicy>();
        services.AddScoped<AuthModule>();
        services.AddScoped<IAuthModule>(sp => sp.GetRequiredService<AuthModule>());
        services.AddScoped<IDatabaseSeeder, RoleSeeder>();
        services.AddScoped<Sessions>();
        services.AddScoped<SessionHttpPolicy>();
        services.AddScoped<RegisterUser.RegisterUser>();
        services.AddScoped<OpenSession.OpenSession>();
        services.AddScoped<CloseSession.CloseSession>();
        services.AddScoped<CheckSession.CheckSession>();
        services.AddScoped<DecideAccountClosure.DecideAccountClosure>();

        return services;
    }

    private static IServiceCollection AddAuthIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<AppIdentityUser>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequiredUniqueChars = 0;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    private static IServiceCollection AddAuthSession(this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<SessionOptions, SessionOptionsValidator>()
            .BindConfiguration(SessionOptions.Section);

        services.AddSessionCookie();
        services.AddOptions<CookieAuthenticationOptions>(SessionCookie.Scheme)
            .Configure<IOptions<SessionOptions>>((cookie, session) => SessionHttpPolicy.ConfigureCookie(cookie, session.Value));

        return services;
    }

    private static IServiceCollection AddAuthHttpPolicy(this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<AuthRateLimitOptions, AuthRateLimitOptionsValidator>()
            .BindConfiguration(AuthRateLimitOptions.Section);
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<AuthRateLimitOptions>>((limiter, limits) =>
                limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                    ApiRateLimiting.ClientAddressOf(http),
                    _ => ApiRateLimiting.FixedWindow(limits.Value.PermitLimit, limits.Value.Window))));

        return services;
    }

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder auth = api.MapGroup("/auth");

        IssueCsrfToken.IssueCsrfTokenEndpoint.Map(auth);
        RegisterUser.RegisterUserEndpoint.Map(auth);
        OpenSession.OpenSessionEndpoint.Map(auth);
        CloseSession.CloseSessionEndpoint.Map(auth);

        return api;
    }

    public static RouteGroupBuilder MapAuthInternalEndpoints(this RouteGroupBuilder internalApi)
    {
        CheckSession.CheckSessionEndpoint.Map(internalApi);

        return internalApi;
    }
}
