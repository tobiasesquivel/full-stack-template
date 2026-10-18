using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Infrastructure.Observability;

public sealed class TelemetryOptions : IValidatableObject
{
    public const string Section = "Otel";
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public string Endpoint { get; set; } = "http://localhost:4318";

    [ConfigurationKeyName(EndpointVariable)]
    public string? StandardEndpoint { get; set; }

    public string EffectiveEndpoint => StandardEndpoint ?? Endpoint;

    public Uri SignalEndpoint(string signal) => new($"{EffectiveEndpoint.TrimEnd('/')}/v1/{signal}");

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        bool valid = Uri.TryCreate(EffectiveEndpoint, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        return valid
            ? []
            : [new ValidationResult($"Otel:Endpoint (or {EndpointVariable}) must be an absolute http or https URL.")];
    }
}
