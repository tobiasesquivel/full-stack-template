using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TemplateApp.Features.Auth;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Users;
using TemplateApp.Infrastructure.Identity;
using TemplateApp.Infrastructure.Middlewares;
using TemplateApp.Infrastructure.Options;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Authorization;

namespace TemplateApp.Infrastructure.DependencyInjection;

public static class BackendInfrastructure
{
    public static IServiceCollection AddBackendInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddAppExceptionHandling();
        services.AddAppDatabase(configuration);
        services.AddAppJwt(configuration);
        services.AddAppAuthPolicy();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ResourceAuthorizer>();
        services.AddScoped<TokenService>();
        services.AddScoped<AuthModule>();
        services.AddScoped<SessionHttpPolicy>();
        services.AddScoped<UsersModule>();
        services.AddScoped<TasksModule>();

        return services;
    }

    private static IServiceCollection AddAppExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    private static IServiceCollection AddAppDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required. Set it via environment or secret.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

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

    private static IServiceCollection AddAppJwt(
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
                    OnTokenValidated = async context =>
                    {
                        string? sub = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                        string? sid = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sid)?.Value;

                        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sid, out Guid familyId))
                        {
                            context.Fail("Session is missing.");
                            return;
                        }

                        TokenService tokens = context.HttpContext.RequestServices.GetRequiredService<TokenService>();

                        AppDbContext db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        bool userExists = await db.Set<AppIdentityUser>()
                            .AsNoTracking()
                            .AnyAsync(u => u.Id == sub);

                        if (!userExists)
                        {
                            context.Fail("Session is missing.");
                            return;
                        }

                        bool active = await tokens.IsFamilyActiveAsync(familyId, sub, context.HttpContext.RequestAborted);
                        if (!active)
                            context.Fail("Session has been revoked.");
                    },
                };
            });

        services.AddAuthorization();

        return services;
    }

    private static IServiceCollection AddAppAuthPolicy(this IServiceCollection services)
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
}
