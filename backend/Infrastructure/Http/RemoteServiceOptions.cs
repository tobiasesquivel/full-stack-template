using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Infrastructure.Http;

public sealed class RemoteServiceOptions : IValidatableObject
{
    public const string Section = "Services";

    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(30);

    public Uri? BaseAddress { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseAddress is not { IsAbsoluteUri: true } address
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            yield return new ValidationResult($"{Section}:<name>:BaseAddress must be an absolute http or https URL.");

        if (Timeout <= TimeSpan.Zero || Timeout > MaxTimeout)
            yield return new ValidationResult($"{Section}:<name>:Timeout must be greater than zero and at most {MaxTimeout.TotalSeconds} seconds.");
    }
}
