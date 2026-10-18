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
            return ApiResults.ToResult(result, _ => Results.Ok());

        await policy.SignInAsync(http, result.Value.Session);
        return Results.Ok(result.Value.User);
    }
}
