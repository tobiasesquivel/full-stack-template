using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure;

[OptionsValidator]
public sealed partial class DataProtectionKeysOptionsValidator : IValidateOptions<DataProtectionKeysOptions>;
