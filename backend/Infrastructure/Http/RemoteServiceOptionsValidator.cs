using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Http;

[OptionsValidator]
public sealed partial class RemoteServiceOptionsValidator : IValidateOptions<RemoteServiceOptions>;
