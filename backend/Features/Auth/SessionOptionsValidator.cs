using Microsoft.Extensions.Options;

namespace TemplateApp.Features.Auth;

[OptionsValidator]
public sealed partial class SessionOptionsValidator : IValidateOptions<SessionOptions>;
