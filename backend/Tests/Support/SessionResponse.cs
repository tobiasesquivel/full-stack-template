using System.Net;
using System.Text.Json;

namespace TemplateApp.Tests.Support;

internal sealed class SessionResponse
{
    public HttpStatusCode Status { get; init; }
    public JsonDocument? Payload { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<string> SetCookies { get; init; } = [];
}
