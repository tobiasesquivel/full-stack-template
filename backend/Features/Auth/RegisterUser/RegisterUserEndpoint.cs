using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Auth.RegisterUser;

public static class RegisterUserEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) =>
        auth.MapPost("/register", HandleAsync).AllowAnonymous().RequireRateLimiting("auth");

    private static async Task<IResult> HandleAsync(
        RegisterUserRequest request,
        HttpContext http,
        RegisterUser slice,
        SessionHttpPolicy policy,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(request, ct);
        if (result.IsError)
            return ApiResults.ToResult(result, _ => Results.Created());

        (AuthResponse response, string refresh) = result.Value;
        policy.AttachRefresh(http, refresh);
        return Results.Created($"/api/v1/users/{response.User.AuthId}", response);
    }
}
