namespace TemplateApp.Infrastructure.Http;

public static class Idempotency
{
    public static RouteHandlerBuilder WithIdempotency<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : notnull =>
        builder.AddEndpointFilter<RouteHandlerBuilder, IdempotencyFilter<TRequest>>();
}
