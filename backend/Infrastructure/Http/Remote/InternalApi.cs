namespace TemplateApp.Infrastructure.Http.Remote;

public static class InternalApi
{
    public const string Prefix = "/internal/v1";

    public static RouteGroupBuilder MapInternalApi(this IEndpointRouteBuilder app) =>
        app.MapGroup(Prefix)
            .DisableRateLimiting()
            .ExcludeFromDescription()
            .AddEndpointFilter((context, next) =>
            {
                OnBehalfOf.Accept(context.HttpContext);
                return next(context);
            });
}
