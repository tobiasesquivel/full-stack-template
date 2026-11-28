using System.Reflection;
using TemplateApp.Infrastructure.Access;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Http.Remote;

namespace TemplateApp.Infrastructure.Hosting;

public static class FeatureHosting
{
    public static IServiceCollection AddFeatures(this IServiceCollection services, Assembly host)
    {
        HostFeatures features = HostFeatures.In(host);
        foreach (IFeature feature in features.All)
            feature.AddServices(services);

        services.AddSingleton(features);
        services.AddSingleton(HostPermissions.In(host));
        return services;
    }

    public static IEndpointRouteBuilder MapFeatures(this IEndpointRouteBuilder app)
    {
        HostFeatures features = app.ServiceProvider.GetRequiredService<HostFeatures>();
        RouteGroupBuilder api = app.MapVersionedApi();
        RouteGroupBuilder internalApi = app.MapInternalApi();
        foreach (IFeature feature in features.All)
        {
            feature.MapEndpoints(api);
            feature.MapInternalEndpoints(internalApi);
        }

        return app;
    }
}
