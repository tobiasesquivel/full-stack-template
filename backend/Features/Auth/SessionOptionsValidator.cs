using Microsoft.Extensions.Options;

namespace TemplateApp.Features.Auth;

[OptionsValidator]
internal sealed partial class SessionOptionsValidator : IValidateOptions<SessionOptions>;
