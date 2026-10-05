using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TemplateApp.Tests.Support;

internal sealed class SessionCookie
{
    public string Value { get; init; } = string.Empty;
    public string Path { get; init; } = "/";
    public bool HttpOnly { get; init; }
}

internal sealed class SessionResponse
{
    public HttpStatusCode Status { get; init; }
    public JsonDocument? Payload { get; init; }
}

internal sealed class TestSession
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _client;
    public Dictionary<string, SessionCookie> Cookies { get; } = new(StringComparer.Ordinal);
    public string? AccessToken { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string AuthId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public TestSession(HttpClient client)
    {
        _client = client;
    }

    public async Task<SessionResponse> SendAsync(
        HttpMethod method,
        string route,
        object? body = null,
        bool authenticated = true,
        bool csrf = true)
    {
        using HttpRequestMessage request = new(method, route);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (Cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", Cookies.Select(kv => $"{kv.Key}={kv.Value.Value}")));

        if (authenticated && AccessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        if (csrf && method != HttpMethod.Get && Cookies.TryGetValue("XSRF-TOKEN", out SessionCookie? xsrf))
            request.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(xsrf.Value));

        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using HttpResponseMessage httpResponse = await _client.SendAsync(request);

        if (httpResponse.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
        {
            foreach (string raw in setCookies)
            {
                string[] parts = raw.Split(';').Select(p => p.Trim()).ToArray();
                string pair = parts[0];
                int equals = pair.IndexOf('=', StringComparison.Ordinal);
                if (equals <= 0)
                    continue;
                string name = pair[..equals];
                string value = pair[(equals + 1)..];
                bool expired = parts.Any(p => p.Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase))
                    || parts.Where(p => p.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase))
                        .Any(p => DateTimeOffset.TryParse(p["Expires=".Length..], out DateTimeOffset exp) && exp <= DateTimeOffset.UtcNow);
                if (expired || value.Length == 0)
                {
                    Cookies.Remove(name);
                    continue;
                }
                string path = parts.FirstOrDefault(p => p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))?["Path=".Length..] ?? "/";
                bool httpOnly = parts.Any(p => p.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
                Cookies[name] = new SessionCookie { Value = value, Path = path, HttpOnly = httpOnly };
            }
        }

        string text = await httpResponse.Content.ReadAsStringAsync();
        return new SessionResponse
        {
            Status = httpResponse.StatusCode,
            Payload = string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text),
        };
    }
}
