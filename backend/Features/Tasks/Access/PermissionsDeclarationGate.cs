using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.Access;

internal static class PermissionsDeclarationGate
{
    public static async ValueTask<object?> RequireDeclaredAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.RequestServices.GetRequiredService<PermissionsDeclaration>().Declared.IsCompletedSuccessfully
            ? await next(context)
            : ApiResults.Problem(HttpErrors.ServiceUnavailable);
}
