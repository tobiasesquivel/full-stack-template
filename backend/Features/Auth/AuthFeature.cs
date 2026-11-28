using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain.Events;
using TemplateApp.Features.Auth.MessageNames;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Features.Auth.Telemetry;
using TemplateApp.Infrastructure.Hosting;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Http.RateLimiting;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Mediator;
using TemplateApp.Infrastructure.Messaging.Outbox;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Infrastructure.Persistence.Initialization;

namespace TemplateApp.Features.Auth;

internal sealed class AuthFeature : IFeature
{
    public const string RateLimitPolicy = "auth";

    public void AddServices(IServiceCollection services)
    {
        services.AddMigratedDbContext<AuthDbContext>(AuthDbContext.Schema, adoptsLegacyTables: true);
        services.AddOutbox<AuthDbContext>();
        services.AddOutboxEvent<AccountDeleted>(AuthEvents.AccountDeleted, AuthEvents.Version);
        services.AddOutboxEvent<AccountClosureRejected>(AuthEvents.AccountClosureRejected, AuthEvents.Version);
        services.AddIntegrationEventPublication<AccountDeletedInfo>(AuthEvents.AccountDeleted, AuthEvents.Version, AuthEvents.Exchange);
        services.AddIntegrationEventPublication<AccountClosureRejectedInfo>(AuthEvents.AccountClosureRejected, AuthEvents.Version, AuthEvents.Exchange);
        services.AddEventHandler<AccountDeleted, PublishAuthEvents.AuthEventPublisher>();
        services.AddEventHandler<AccountClosureRejected, PublishAuthEvents.AuthEventPublisher>();
        services.AddIntegrationEventHandler<DecideAccountClosureInfo, DecideAccountClosure.DecideAccountClosureCommandsConsumer>(AuthCommands.DecideAccountClosure, AuthCommands.Version, AuthCommands.Exchange, DecideAccountClosure.DecideAccountClosure.Consumer);
        services.AddRetiredBinding(TaskEvents.Exchange, TaskEvents.OwnerTasksHeld, DecideAccountClosure.DecideAccountClosure.Consumer);
        AddAuthIdentity(services);
        AddAuthSession(services);
        AddAuthHttpPolicy(services);

        services.AddSingleton<AuthMetrics>();
        services.AddScoped<AccessPolicy>();
        services.AddScoped<AuthModule>();
        services.AddScoped<IAuthModule>(sp => sp.GetRequiredService<AuthModule>());
        services.AddScoped<IDatabaseSeeder, RoleSeeder>();
        services.AddScoped<Sessions>();
        services.AddScoped<SessionHttpPolicy>();
        services.AddMediatorHandler<RegisterUser.RegisterUserCommand.Handler>(MediatorTransaction.Atomic);
        services.AddSingleton<IValidator<RegisterUser.RegisterUserCommand>, RegisterUser.RegisterUserCommandValidator>();
        services.AddMediatorHandler<OpenSession.OpenSessionCommand.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<CloseSession.CloseSessionCommand.Handler>(MediatorTransaction.None);
        services.AddMediatorHandler<CheckSession.CheckSessionCommand.Handler>(MediatorTransaction.None);
        services.AddScoped<DecideAccountClosure.DecideAccountClosure>();
    }

    private static void AddAuthIdentity(IServiceCollection services)
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
    }

    private static void AddAuthSession(IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<Access.SessionOptions, SessionOptionsValidator>()
            .BindConfiguration(Access.SessionOptions.Section);

        services.AddSessionCookie();
        services.AddOptions<CookieAuthenticationOptions>(SessionCookie.Scheme)
            .Configure<IOptions<Access.SessionOptions>>((cookie, session) => SessionHttpPolicy.ConfigureCookie(cookie, session.Value));
    }

    private static void AddAuthHttpPolicy(IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<AuthRateLimitOptions, AuthRateLimitOptionsValidator>()
            .BindConfiguration(AuthRateLimitOptions.Section);
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<AuthRateLimitOptions>>((limiter, limits) =>
                limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                    ApiRateLimiting.ClientAddressOf(http),
                    _ => ApiRateLimiting.FixedWindow(limits.Value.PermitLimit, limits.Value.Window))));
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        RouteGroupBuilder auth = api.MapGroup("/auth");

        IssueCsrfToken.IssueCsrfTokenEndpoint.Map(auth);
        RegisterUser.RegisterUserEndpoint.Map(auth);
        OpenSession.OpenSessionEndpoint.Map(auth);
        CloseSession.CloseSessionEndpoint.Map(auth);
    }

    public void MapInternalEndpoints(RouteGroupBuilder internalApi)
    {
        CheckSession.CheckSessionEndpoint.Map(internalApi);
    }
}
