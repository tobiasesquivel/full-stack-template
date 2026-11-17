using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Http;

[OptionsValidator]
public sealed partial class ApiRateLimitOptionsValidator : IValidateOptions<ApiRateLimitOptions>;
