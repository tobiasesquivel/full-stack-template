using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Auth.RefreshSession;

public static class RefreshSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) =>
        auth.MapPost("/refresh", HandleAsync).AllowAnonymous().RequireRateLimiting("auth");

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        RefreshSession slice,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        if (!policy.TryGetRefresh(http.Request, out string? plaintext) || string.IsNullOrEmpty(plaintext))
        {
            policy.ClearRefresh(http);
            return ApiResults.ToResult<TokenResponse>(AuthErrors.InvalidSession, _ => Results.Ok());
        }

        var result = await slice.ExecuteAsync(plaintext, ct);
        if (result.IsError)
        {
            policy.ClearRefresh(http);
            return ApiResults.ToResult(result, _ => Results.Ok());
        }

        (TokenResponse response, string next) = result.Value;
        policy.AttachRefresh(http, next);
        return Results.Ok(response);
    }
}
