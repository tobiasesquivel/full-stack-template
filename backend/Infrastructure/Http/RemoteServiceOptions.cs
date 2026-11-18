namespace TemplateApp.Infrastructure.Http;

public sealed class RemoteServiceOptions
{
    public const string Section = "Services";

    public Uri? BaseAddress { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);
}
