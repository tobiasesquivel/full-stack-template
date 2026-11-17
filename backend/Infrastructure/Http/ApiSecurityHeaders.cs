using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace TemplateApp.Infrastructure.Http;

public static class ApiSecurityHeaders
{
    public static IServiceCollection AddApiSecurityHeaders(this IServiceCollection services)
    {
        services.Configure<KestrelServerOptions>(kestrel => kestrel.AddServerHeader = false);
        services.AddSecurityHeaderPolicies(policies => policies.SetDefaultPolicy(ApiPolicy));
        return services;
    }

    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.UseSecurityHeaders();

    private static void ApiPolicy(HeaderPolicyCollection headers) => headers
        .AddContentTypeOptionsNoSniff()
        .AddContentSecurityPolicy(csp =>
        {
            csp.AddDefaultSrc().None();
            csp.AddFrameAncestors().None();
        })
        .AddFrameOptionsDeny()
        .AddReferrerPolicyNoReferrer()
        .AddCrossOriginResourcePolicy(corp => corp.SameOrigin())
        .AddCustomHeader("Cache-Control", "no-store");
}
