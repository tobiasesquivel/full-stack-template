using Microsoft.Extensions.Options;

namespace TemplateApp.Features.Auth.Access;

[OptionsValidator]
internal sealed partial class SessionOptionsValidator : IValidateOptions<SessionOptions>;
