namespace TemplateApp.Features.Auth.CloseSession;

public static class CloseSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) =>
        auth.MapPost("/logout", HandleAsync).AllowAnonymous().RequireRateLimiting("auth");

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        CloseSession slice,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        policy.TryGetRefresh(http.Request, out string? plaintext);
        await slice.ExecuteAsync(plaintext, ct);
        policy.ClearRefresh(http);
        return Results.NoContent();
    }
}
