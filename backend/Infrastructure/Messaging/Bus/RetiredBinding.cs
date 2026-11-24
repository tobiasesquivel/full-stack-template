namespace TemplateApp.Infrastructure.Messaging.Bus;

public sealed record RetiredBinding(string Exchange, string RoutingKey, string Queue);
