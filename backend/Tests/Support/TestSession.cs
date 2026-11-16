using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TemplateApp.Tests.Support;

internal sealed class TestSession
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _client;
    public Dictionary<string, SessionCookie> Cookies { get; } = new(StringComparer.Ordinal);
    public string UserName { get; set; } = string.Empty;
    public string AuthId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public TestSession(HttpClient client)
    {
        _client = client;
    }

    public Task<SessionResponse> SendAsync(
        HttpMethod method,
        string route,
        object? body = null,
        bool csrf = true,
        IReadOnlyDictionary<string, string>? headers = null) =>
        SendCoreAsync(
            method,
            route,
            body is null ? null : new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
            csrf,
            headers);

    public Task<SessionResponse> SendRawAsync(HttpMethod method, string route, string body, string mediaType) =>
        SendCoreAsync(method, route, new StringContent(body, Encoding.UTF8, mediaType), csrf: true, headers: null);

    private async Task<SessionResponse> SendCoreAsync(
        HttpMethod method,
        string route,
        HttpContent? content,
        bool csrf,
        IReadOnlyDictionary<string, string>? headers)
    {
        using HttpRequestMessage request = new(method, route);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        string path = "/" + route.Split('?')[0];
        string[] sent = Cookies
            .Where(kv => path.StartsWith(kv.Value.Path, StringComparison.Ordinal))
            .Select(kv => $"{kv.Key}={kv.Value.Value}")
            .ToArray();
        if (sent.Length > 0)
            request.Headers.Add("Cookie", string.Join("; ", sent));

        if (csrf && method != HttpMethod.Get && Cookies.TryGetValue("XSRF-TOKEN", out SessionCookie? xsrf))
            request.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(xsrf.Value));

        foreach ((string name, string value) in headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(name, value);

        request.Content = content;

        using HttpResponseMessage httpResponse = await _client.SendAsync(request);

        List<string> setCookies = [];
        if (httpResponse.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
        {
            foreach (string raw in values)
            {
                setCookies.Add(raw);
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
                Cookies[name] = new SessionCookie
                {
                    Value = value,
                    Path = Attribute(parts, "Path") ?? "/",
                    HttpOnly = parts.Any(p => p.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase)),
                    Secure = parts.Any(p => p.Equals("Secure", StringComparison.OrdinalIgnoreCase)),
                    SameSite = Attribute(parts, "SameSite"),
                };
            }
        }

        string text = await httpResponse.Content.ReadAsStringAsync();
        return new SessionResponse
        {
            Status = httpResponse.StatusCode,
            Payload = string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text),
            Text = text,
            SetCookies = setCookies,
            Location = httpResponse.Headers.Location?.OriginalString,
            Headers = httpResponse.Headers
                .Concat(httpResponse.Content.Headers)
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase),
        };
    }

    public TestSession CopyTo(HttpClient client)
    {
        TestSession copy = new(client)
        {
            UserName = UserName,
            AuthId = AuthId,
            Password = Password,
        };
        foreach (KeyValuePair<string, SessionCookie> kv in Cookies)
            copy.Cookies[kv.Key] = kv.Value;
        return copy;
    }

    private static string? Attribute(string[] parts, string name) =>
        parts.FirstOrDefault(p => p.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..];
}
