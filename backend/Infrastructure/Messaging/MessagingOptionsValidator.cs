using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Messaging;

public sealed partial class MessagingOptionsValidator : IValidateOptions<MessagingOptions>
{
    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        if (options.RabbitMqUri is not { IsAbsoluteUri: true } uri || (uri.Scheme != "amqp" && uri.Scheme != "amqps"))
            failures.Add($"{MessagingOptions.Section}:RabbitMqUri must be an absolute amqp or amqps URL.");

        if (!PrefixPattern().IsMatch(options.Prefix))
            failures.Add($"{MessagingOptions.Section}:Prefix may only use lowercase letters, digits and hyphens.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex("^[a-z0-9-]*$")]
    private static partial Regex PrefixPattern();
}
