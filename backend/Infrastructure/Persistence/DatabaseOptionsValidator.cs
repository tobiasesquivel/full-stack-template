using Microsoft.Extensions.Options;

namespace TemplateApp.Infrastructure.Persistence;

[OptionsValidator]
public sealed partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>;
