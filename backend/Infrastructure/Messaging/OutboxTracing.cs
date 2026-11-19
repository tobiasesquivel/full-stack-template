using System.Diagnostics;

namespace TemplateApp.Infrastructure.Messaging;

public static class OutboxTracing
{
    public const string SourceName = "TemplateApp.Outbox";

    public static readonly ActivitySource Source = new(SourceName);
}
