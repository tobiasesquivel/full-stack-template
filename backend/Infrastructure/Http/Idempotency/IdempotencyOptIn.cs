namespace TemplateApp.Infrastructure.Http.Idempotency;

public static class IdempotencyOptIn
{
    public static RouteHandlerBuilder WithIdempotency<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : notnull =>
        builder.AddEndpointFilter<RouteHandlerBuilder, IdempotencyFilter<TRequest>>();
}
