using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Http.RateLimiting;

public static class ApiRateLimiting
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<ApiRateLimitOptions, ApiRateLimitOptionsValidator>()
            .BindConfiguration(ApiRateLimitOptions.Section);

        services.AddRateLimiter();
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<ApiRateLimitOptions>>((limiter, limits) =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.OnRejected = WriteRetryAfter;
                limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => Partition(http, limits.Value));
            });

        return services;
    }

    public static string ClientAddressOf(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static FixedWindowRateLimiterOptions FixedWindow(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits,
        Window = window,
        QueueLimit = 0,
    };

    private static RateLimitPartition<string> Partition(HttpContext http, ApiRateLimitOptions limits)
    {
        string? userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        (string key, int permits) = userId is null
            ? ($"anonymous:{ClientAddressOf(http)}", limits.AnonymousRequests)
            : IsRead(http.Request.Method)
                ? ($"read:{userId}", limits.UserReads)
                : ($"write:{userId}", limits.UserWrites);

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => FixedWindow(permits, limits.Window));
    }

    private static bool IsRead(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static ValueTask WriteRetryAfter(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            long seconds = (long)Math.Ceiling(retryAfter.TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    }
}
