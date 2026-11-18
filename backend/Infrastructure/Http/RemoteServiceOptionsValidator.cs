using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Http;

public sealed class RemoteServiceOptionsValidator : IValidateOptions<RemoteServiceOptions>
{
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(30);

    public ValidateOptionsResult Validate(string? name, RemoteServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string section = $"{RemoteServiceOptions.Section}:{name}";
        List<string> failures = [];
        if (options.BaseAddress is not { IsAbsoluteUri: true } address
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            failures.Add($"{section}:BaseAddress must be an absolute http or https URL.");

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > MaxTimeout)
            failures.Add($"{section}:Timeout must be greater than zero and at most {MaxTimeout.TotalSeconds} seconds.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
