using Microsoft.AspNetCore.WebUtilities;

namespace TemplateApp.Gateway;

internal static class GatewayProblemDetails
{
    public static IServiceCollection AddGatewayProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions.TryAdd(
                "code",
                CodeFor(context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode)));

    private static string CodeFor(int status)
    {
        string reason = ReasonPhrases.GetReasonPhrase(status).Replace(" ", string.Empty, StringComparison.Ordinal);
        return reason.Length > 0 ? $"Http.{reason}" : $"Http.Status{status}";
    }
}
