using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth;

public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthIdentity();
        services.AddAuthSession(configuration);
        services.AddAuthHttpPolicy();

        services.AddScoped<IAccessPolicy, AccessPolicy>();
        services.AddScoped<UserAccounts>();
        services.AddScoped<IUserAccounts>(sp => sp.GetRequiredService<UserAccounts>());
        services.AddScoped<IDatabaseSeeder, RoleSeeder>();
        services.AddScoped<Sessions>();
        services.AddScoped<SessionHttpPolicy>();
        services.AddScoped<RegisterUser.RegisterUser>();
        services.AddScoped<OpenSession.OpenSession>();
        services.AddScoped<CloseSession.CloseSession>();

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
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    private static IServiceCollection AddAuthSession(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Session");
        SessionOptions options = section.Get<SessionOptions>()
            ?? throw new InvalidOperationException("Session configuration section is missing.");

        if (options.AbsoluteExpirationDays <= 0)
            throw new InvalidOperationException("Session:AbsoluteExpirationDays must be positive.");
        if (options.IdleTimeoutMinutes <= 0)
            throw new InvalidOperationException("Session:IdleTimeoutMinutes must be positive.");

        services.Configure<SessionOptions>(section);

        services.AddAuthentication(SessionHttpPolicy.Scheme)
            .AddCookie(SessionHttpPolicy.Scheme, cookie => SessionHttpPolicy.ConfigureCookie(cookie, options));

        services.AddAuthorization();

        return services;
    }

    private static IServiceCollection AddAuthHttpPolicy(this IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
        });

        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("auth", limiter =>
            {
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.PermitLimit = 20;
                limiter.QueueLimit = 0;
            });
        });

        return services;
    }

    public static IApplicationBuilder UseAuthAntiforgery(this IApplicationBuilder app) =>
        app.Use(SessionHttpPolicy.RequireAntiforgeryAsync);

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder auth = api.MapGroup("/auth");

        IssueCsrfToken.IssueCsrfTokenEndpoint.Map(auth);
        RegisterUser.RegisterUserEndpoint.Map(auth);
        OpenSession.OpenSessionEndpoint.Map(auth);
        CloseSession.CloseSessionEndpoint.Map(auth);

        return api;
    }
}
