namespace TemplateApp.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    public const string Section = "Messaging";

    public Uri? RabbitMqUri { get; set; }

    public string Prefix { get; set; } = string.Empty;
}
