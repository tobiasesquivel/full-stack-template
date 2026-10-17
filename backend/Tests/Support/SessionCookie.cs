namespace TemplateApp.Tests.Support;

internal sealed class SessionCookie
{
    public string Value { get; init; } = string.Empty;
    public string Path { get; init; } = "/";
    public bool HttpOnly { get; init; }
}
