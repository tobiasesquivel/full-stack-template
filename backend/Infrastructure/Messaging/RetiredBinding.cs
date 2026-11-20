namespace TemplateApp.Infrastructure.Messaging;

public sealed record RetiredBinding(string Exchange, string RoutingKey, string Queue);
