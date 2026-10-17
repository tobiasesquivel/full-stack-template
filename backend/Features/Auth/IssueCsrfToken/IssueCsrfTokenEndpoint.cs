namespace TemplateApp.Features.Auth.IssueCsrfToken;

public static class IssueCsrfTokenEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) => auth.MapGet("/csrf", Handle).AllowAnonymous();

    private static IResult Handle(HttpContext http, SessionHttpPolicy policy)
    {
        policy.IssueCsrf(http);
        return Results.Ok();
    }
}
