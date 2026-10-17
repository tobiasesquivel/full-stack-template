using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Jwt;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth;

public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthIdentity();
        services.AddAuthJwt(configuration);
        services.AddAuthHttpPolicy();

        services.AddScoped<IAccessPolicy, AccessPolicy>();
        services.AddScoped<UserAccounts>();
        services.AddScoped<IUserAccounts>(sp => sp.GetRequiredService<UserAccounts>());
        services.AddScoped<IDatabaseSeeder, RoleSeeder>();
        services.AddScoped<TokenService>();
        services.AddScoped<SessionValidator>();
        services.AddScoped<SessionHttpPolicy>();
        services.AddScoped<RegisterUser.RegisterUser>();
        services.AddScoped<OpenSession.OpenSession>();
        services.AddScoped<RefreshSession.RefreshSession>();
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

    private static IServiceCollection AddAuthJwt(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection jwtSection = configuration.GetSection("Jwt");
        JwtOptions jwtOptions = jwtSection.Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration section is missing.");

        if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key is required and must be at least 32 characters. Set it via environment or secret.");
        if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
            throw new InvalidOperationException("Jwt:Issuer is required.");
        if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
            throw new InvalidOperationException("Jwt:Audience is required.");
        if (jwtOptions.AccessTokenExpirationMinutes <= 0)
            throw new InvalidOperationException("Jwt:AccessTokenExpirationMinutes must be positive.");
        if (jwtOptions.RefreshTokenExpirationDays <= 0)
            throw new InvalidOperationException("Jwt:RefreshTokenExpirationDays must be positive.");

        services.Configure<JwtOptions>(jwtSection);

        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = SessionValidator.OnTokenValidatedAsync,
                };
            });

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

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder auth = app.MapGroup("/api/v1/auth");
        auth.AddEndpointFilter<SessionHttpPolicy>();

        IssueCsrfToken.IssueCsrfTokenEndpoint.Map(auth);
        RegisterUser.RegisterUserEndpoint.Map(auth);
        OpenSession.OpenSessionEndpoint.Map(auth);
        RefreshSession.RefreshSessionEndpoint.Map(auth);
        CloseSession.CloseSessionEndpoint.Map(auth);

        return app;
    }
}
