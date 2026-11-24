using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Http.Remote;

public sealed class RemoteServiceOptionsValidator : IValidateOptions<RemoteServiceOptions>
{
    private static readonly TimeSpan MinTimeout = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaxTimeout = RemoteServices.BreakerSamplingDuration / 2;
    private static readonly TimeSpan MaxTotalTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinBreakDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxBreakDuration = TimeSpan.FromSeconds(60);
    private const int MaxRetries = 5;

    public ValidateOptionsResult Validate(string? name, RemoteServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string section = $"{RemoteServiceOptions.Section}:{name}";
        List<string> failures = [];
        if (options.BaseAddress is not { IsAbsoluteUri: true } address
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            failures.Add($"{section}:BaseAddress must be an absolute http or https URL.");

        if (options.Timeout < MinTimeout || options.Timeout > MaxTimeout)
            failures.Add($"{section}:Timeout must be between {MinTimeout.TotalMilliseconds} ms and {MaxTimeout.TotalSeconds} seconds.");

        if (options.TotalTimeout <= options.Timeout || options.TotalTimeout > MaxTotalTimeout)
            failures.Add($"{section}:TotalTimeout must be greater than Timeout and at most {MaxTotalTimeout.TotalSeconds} seconds.");

        if (options.Retries is < 0 or > MaxRetries)
            failures.Add($"{section}:Retries must be between 0 and {MaxRetries}.");

        if (options.RetryDelay <= TimeSpan.Zero || options.RetryDelay >= options.TotalTimeout)
            failures.Add($"{section}:RetryDelay must be greater than zero and shorter than TotalTimeout.");

        if (options.BreakDuration < MinBreakDuration || options.BreakDuration > MaxBreakDuration)
            failures.Add($"{section}:BreakDuration must be between {MinBreakDuration.TotalSeconds} and {MaxBreakDuration.TotalSeconds} seconds.");

        if (options.BreakerMinimumThroughput < 2)
            failures.Add($"{section}:BreakerMinimumThroughput must be at least 2.");

        if (options.MaxConcurrentRequests < 1)
            failures.Add($"{section}:MaxConcurrentRequests must be at least 1.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
