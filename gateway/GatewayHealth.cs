using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TemplateApp.Gateway;

internal static class GatewayHealth
{
    public const string ReadyTag = "ready";

    public static bool IsReadyCheck(HealthCheckRegistration registration) => registration.Tags.Contains(ReadyTag);
}
