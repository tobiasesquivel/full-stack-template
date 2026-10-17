using ErrorOr;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Auth.OpenSession;

public static class OpenSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) =>
        auth.MapPost("/login", HandleAsync).AllowAnonymous().RequireRateLimiting("auth");

    private static async Task<IResult> HandleAsync(
        OpenSessionRequest request,
        HttpContext http,
        OpenSession slice,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(request, ct);
        if (result.IsError)
        {
            if (result.Errors.Any(e => e.Type == ErrorType.Unauthorized))
                policy.ClearRefresh(http);
            return ApiResults.ToResult(result, _ => Results.Ok());
        }

        (AuthResponse response, string refresh) = result.Value;
        policy.AttachRefresh(http, refresh);
        return Results.Ok(response);
    }
}
