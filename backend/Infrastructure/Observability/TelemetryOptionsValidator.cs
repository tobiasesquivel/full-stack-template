using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Observability;

[OptionsValidator]
public sealed partial class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>;
