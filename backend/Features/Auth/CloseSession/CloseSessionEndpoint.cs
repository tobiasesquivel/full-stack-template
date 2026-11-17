namespace TemplateApp.Features.Auth.CloseSession;

public static class CloseSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) =>
        auth.MapPost("/logout", HandleAsync).AllowAnonymous().RequireRateLimiting(AuthFeature.RateLimitPolicy);

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        CloseSession slice,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        await slice.ExecuteAsync(http.User, ct);
        await policy.SignOutAsync(http);
        return Results.NoContent();
    }
}
