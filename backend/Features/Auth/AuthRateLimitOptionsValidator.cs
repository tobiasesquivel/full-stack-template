using Microsoft.Extensions.Options;

namespace TemplateApp.Features.Auth;

[OptionsValidator]
internal sealed partial class AuthRateLimitOptionsValidator : IValidateOptions<AuthRateLimitOptions>;
