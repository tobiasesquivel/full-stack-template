namespace TemplateApp.Infrastructure.Http;

public static class InternalApi
{
    public const string Prefix = "/internal/v1";

    public static RouteGroupBuilder MapInternalApi(this IEndpointRouteBuilder app) =>
        app.MapGroup(Prefix)
            .DisableRateLimiting()
            .ExcludeFromDescription();
}
