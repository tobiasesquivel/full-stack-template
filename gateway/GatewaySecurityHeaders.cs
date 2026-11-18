using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace TemplateApp.Gateway;

internal static class GatewaySecurityHeaders
{
    public static IServiceCollection AddGatewaySecurityHeaders(this IServiceCollection services)
    {
        services.Configure<KestrelServerOptions>(kestrel => kestrel.AddServerHeader = false);
        services.AddSecurityHeaderPolicies(policies => policies.SetDefaultPolicy(ApiPolicy));
        return services;
    }

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
