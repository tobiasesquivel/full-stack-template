using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.DependencyInjection;

[OptionsValidator]
public sealed partial class DataProtectionKeysOptionsValidator : IValidateOptions<DataProtectionKeysOptions>;
