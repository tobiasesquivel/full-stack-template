using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Http;

public static class RemoteServices
{
    public static IHttpClientBuilder AddRemoteClient<TClient>(this IServiceCollection services, string service)
        where TClient : class
    {
        services.AddOptionsWithValidateOnStart<RemoteServiceOptions, RemoteServiceOptionsValidator>(service)
            .BindConfiguration($"{RemoteServiceOptions.Section}:{service}");
        services.AddTransient<OnBehalfOf.ForwardingHandler>();

        return services.AddHttpClient<TClient>((provider, client) =>
            {
                RemoteServiceOptions remote = provider.GetRequiredService<IOptionsMonitor<RemoteServiceOptions>>().Get(service);
                client.BaseAddress = remote.BaseAddress;
                client.Timeout = remote.Timeout;
            })
            .AddHttpMessageHandler<OnBehalfOf.ForwardingHandler>();
    }

    public static async Task<HttpResponseMessage> SendRemoteAsync(
        this HttpClient client,
        HttpRequestMessage request,
        CancellationToken ct,
        params HttpStatusCode[] expected)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new RemoteServiceUnavailableException(Describe(client, request, "is unreachable"), ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new RemoteServiceUnavailableException(Describe(client, request, $"did not answer within {client.Timeout.TotalSeconds} s"), ex);
        }

        if (expected.Contains(response.StatusCode))
            return response;

        HttpStatusCode status = response.StatusCode;
        response.Dispose();
        string answered = $"answered {(int)status}";
        if ((int)status >= 500)
            throw new RemoteServiceUnavailableException(Describe(client, request, answered));

        throw new InvalidOperationException(Describe(client, request, answered + ", outside its contract"));
    }

    public static async Task EnsureProblemCodeAsync(this HttpResponseMessage response, string code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);

        string? answered = null;
        try
        {
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (body.RootElement.ValueKind == JsonValueKind.Object && body.RootElement.TryGetProperty("code", out JsonElement value))
                answered = value.GetString();
        }
        catch (JsonException)
        {
        }

        if (!string.Equals(answered, code, StringComparison.Ordinal))
            throw new RemoteServiceUnavailableException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath} answered {(int)response.StatusCode} with code {answered ?? "none"} instead of {code}: the release that answered may not have this route yet.");
    }

    private static string Describe(HttpClient client, HttpRequestMessage request, string outcome) =>
        $"{request.Method} {new Uri(client.BaseAddress!, request.RequestUri!).AbsolutePath} {outcome}.";
}
