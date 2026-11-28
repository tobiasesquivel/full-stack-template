using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TemplateApp.Features.Tasks.Access;

internal sealed class PermissionsDeclarationHealthCheck : IHealthCheck
{
    private readonly PermissionsDeclaration _declaration;

    public PermissionsDeclarationHealthCheck(PermissionsDeclaration declaration)
    {
        _declaration = declaration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_declaration.Declared.IsCompletedSuccessfully
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Auth has not accepted the permissions of this host yet."));
}
