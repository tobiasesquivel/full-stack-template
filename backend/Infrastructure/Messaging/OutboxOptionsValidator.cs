using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Messaging;

[OptionsValidator]
public sealed partial class OutboxOptionsValidator : IValidateOptions<OutboxOptions>;
