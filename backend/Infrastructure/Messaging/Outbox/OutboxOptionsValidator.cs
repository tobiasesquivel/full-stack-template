using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Messaging.Outbox;

[OptionsValidator]
public sealed partial class OutboxOptionsValidator : IValidateOptions<OutboxOptions>;
