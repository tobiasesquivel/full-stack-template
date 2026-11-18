using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy;

namespace TemplateApp.Gateway;

internal sealed class ProxyConfigHealthCheck(IProxyStateLookup proxyState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var routes = proxyState.GetRoutes().ToList();
        bool everyRouteReachesDestinations = routes.Count > 0
            && routes.All(route => route.Cluster is { } cluster && !cluster.Destinations.IsEmpty);

        return Task.FromResult(everyRouteReachesDestinations
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy());
    }
}
