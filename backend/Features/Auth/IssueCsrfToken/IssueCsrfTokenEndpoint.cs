using TemplateApp.Features.Auth.Access;
using TemplateApp.Shared.Architecture;

namespace TemplateApp.Features.Auth.IssueCsrfToken;

[DuneExemption(DuneCheck.MediatorEndpoint, "Solo escribe la cookie de antiforgery: no hay caso de uso que despachar.")]
internal static class IssueCsrfTokenEndpoint
{
    public static void Map(IEndpointRouteBuilder auth) => auth.MapGet("/csrf", Handle).AllowAnonymous();

    private static IResult Handle(HttpContext http, SessionHttpPolicy policy)
    {
        policy.IssueCsrf(http);
        return Results.Ok();
    }
}
