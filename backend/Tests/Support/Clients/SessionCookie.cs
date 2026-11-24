namespace TemplateApp.Tests.Support.Clients;

internal sealed class SessionCookie
{
    public string Value { get; init; } = string.Empty;
    public string Path { get; init; } = "/";
    public bool HttpOnly { get; init; }
    public bool Secure { get; init; }
    public string? SameSite { get; init; }
}
