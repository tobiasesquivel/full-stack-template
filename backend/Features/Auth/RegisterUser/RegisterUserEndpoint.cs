using ErrorOr;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Auth.RegisterUser;

internal static class RegisterUserEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) =>
        auth.MapPost("/register", HandleAsync).AllowAnonymous().RequireRateLimiting(AuthFeature.RateLimitPolicy);

    private static async Task<IResult> HandleAsync(
        RegisterUserRequest request,
        HttpContext http,
        IMessageBus bus,
        SessionHttpPolicy policy,
        LinkGenerator links,
        CancellationToken ct)
    {
        var command = new RegisterUserCommand(request.UserName, request.Email, request.Password, request.DisplayName);
        var result = await bus.InvokeAsync<ErrorOr<(UserResponse User, Session Session)>>(command, ct);
        if (result.IsError)
            return ApiResults.ToResult(result, _ => Results.Created());

        await policy.SignInAsync(http, result.Value.Session);
        return Results.Created(
            links.GetPathByName(http, UserEndpointNames.GetUser, new { authId = result.Value.User.AuthId }),
            result.Value.User);
    }
}
