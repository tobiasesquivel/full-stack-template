using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy;

namespace TemplateApp.Gateway;

internal sealed class ProxyConfigHealthCheck(IProxyStateLookup proxyState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        bool hasRoutes = proxyState.GetRoutes().Any();
        bool everyClusterHasDestinations = proxyState.GetClusters().All(cluster => !cluster.Destinations.IsEmpty);

        return Task.FromResult(hasRoutes && everyClusterHasDestinations
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy());
    }
}
