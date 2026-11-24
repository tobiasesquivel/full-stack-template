using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Http.RateLimiting;

[OptionsValidator]
public sealed partial class ApiRateLimitOptionsValidator : IValidateOptions<ApiRateLimitOptions>;
