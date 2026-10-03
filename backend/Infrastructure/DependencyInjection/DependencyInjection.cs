using System.Text;
using JasperFx.CodeGeneration;
using JasperFx.Resources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TemplateApp.Infrastructure.Auth;
using TemplateApp.Infrastructure.Authorization;
using TemplateApp.Infrastructure.Middlewares;
using TemplateApp.Infrastructure.Options;
using TemplateApp.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.FluentValidation;
using Wolverine.Postgresql;

namespace TemplateApp.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostBuilder host,
        IHostEnvironment environment)
    {
        services.AddAppExceptionMiddlewares();
        services.AddAppDatabase(configuration);
        services.AddAppJwt(configuration);
        services.AddAppWolverine(host, configuration, environment);

        return services;
    }


    private static IServiceCollection AddAppDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services
            .AddIdentityCore<AppIdentityUser>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
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

        if (jwtOptions.Key.Length < 32)
            throw new InvalidOperationException("JWT Key must be at least 32 characters long.");

        services.Configure<JwtOptions>(jwtSection);

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
                };
            });

        services.AddScoped<ITokenService, JwtService>();

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    private static IServiceCollection AddAppWolverine(
                this IServiceCollection services,
                IHostBuilder builder,
                IConfiguration configuration,
                IHostEnvironment environment)
    {
        string connectionString = configuration.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("DB Connection string not found.");

        builder.UseWolverine(opts =>
        {
            opts.CodeGeneration.GeneratedCodeOutputPath = "Infrastructure/Wolverine/Generated";
            opts.UseFluentValidation();

            if (environment.IsDevelopment())
            {
                opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
            }
            else
            {
                opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
            }

            opts.PersistMessagesWithPostgresql(connectionString);
            opts.CodeGeneration.AlwaysUseServiceLocationFor<UserManager<AppIdentityUser>>();
            opts.CodeGeneration.AlwaysUseServiceLocationFor<RoleManager<IdentityRole>>();

            opts.UseEntityFrameworkCoreTransactions();

            opts.Policies.AutoApplyTransactions();

            opts.Services.AddResourceSetupOnStartup();
        });

        return services;
    }

    private static IServiceCollection AddAppExceptionMiddlewares(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
