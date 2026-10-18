using TemplateApp.Features.Users.Contracts;
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
        LinkGenerator links,
        CancellationToken ct)
    {
        var result = await slice.ExecuteAsync(request, ct);
        if (result.IsError)
            return ApiResults.ToResult(result, _ => Results.Created());

        await policy.SignInAsync(http, result.Value.Session);
        return Results.Created(
            links.GetPathByName(http, UserEndpointNames.GetUser, new { authId = result.Value.User.AuthId }),
            result.Value.User);
    }
}
