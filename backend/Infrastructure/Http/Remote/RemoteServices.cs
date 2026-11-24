using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace TemplateApp.Infrastructure.Http.Remote;

public static class RemoteServices
{
    public const string ResilienceHandler = "remote";
    public const double BreakerFailureRatio = 0.5;
    public static readonly TimeSpan BreakerSamplingDuration = TimeSpan.FromSeconds(30);

    public static IHttpClientBuilder AddRemoteClient<TClient>(this IServiceCollection services, string service, RemoteRetry retry)
        where TClient : class
    {
        services.AddOptionsWithValidateOnStart<RemoteServiceOptions, RemoteServiceOptionsValidator>(service)
            .BindConfiguration($"{RemoteServiceOptions.Section}:{service}");
        services.AddTransient<OnBehalfOf.ForwardingHandler>();
        services.AddTransient<BufferedBodyHandler>();

        IHttpClientBuilder client = services.AddHttpClient<TClient>((provider, http) =>
            {
                http.BaseAddress = provider.GetRequiredService<IOptionsMonitor<RemoteServiceOptions>>().Get(service).BaseAddress;
                http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<OnBehalfOf.ForwardingHandler>();
        client.AddResilienceHandler(ResilienceHandler, (pipeline, context) =>
            BuildPipeline(pipeline, context.ServiceProvider.GetRequiredService<IOptionsMonitor<RemoteServiceOptions>>().Get(service), retry));
        return client.AddHttpMessageHandler<BufferedBodyHandler>();
    }

    private static void BuildPipeline(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, RemoteServiceOptions remote, RemoteRetry retry)
    {
        pipeline.TimeProvider = TimeProvider.System;
        pipeline
            .AddConcurrencyLimiter(remote.MaxConcurrentRequests, queueLimit: 0)
            .AddTimeout(remote.TotalTimeout);
        if (retry == RemoteRetry.Idempotent && remote.Retries > 0)
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = remote.Retries,
                Delay = remote.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldRetryAfterHeader = false,
            });
        }

        pipeline
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = BreakerFailureRatio,
                SamplingDuration = BreakerSamplingDuration,
                MinimumThroughput = remote.BreakerMinimumThroughput,
                BreakDuration = remote.BreakDuration,
            })
            .AddTimeout(remote.Timeout);
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
        catch (ExecutionRejectedException ex)
        {
            throw new RemoteServiceUnavailableException(Describe(client, request, Rejection(ex)), ex);
        }

        if (expected.Contains(response.StatusCode))
            return response;

        HttpStatusCode status = response.StatusCode;
        response.Dispose();
        string answered = $"answered {(int)status}";
        if ((int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
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

    private static string Rejection(ExecutionRejectedException rejection) => rejection switch
    {
        TimeoutRejectedException timeout => $"did not answer within {timeout.Timeout.TotalSeconds} s",
        BrokenCircuitException => "is behind an open circuit after repeated failures",
        RateLimiterRejectedException => "already has too many calls in flight",
        _ => "was rejected by the resilience pipeline",
    };

    private static string Describe(HttpClient client, HttpRequestMessage request, string outcome) =>
        $"{request.Method} {new Uri(client.BaseAddress!, request.RequestUri!).AbsolutePath} {outcome}.";
}
