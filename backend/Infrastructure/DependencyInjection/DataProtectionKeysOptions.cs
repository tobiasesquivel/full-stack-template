using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Infrastructure.DependencyInjection;

public sealed record DataProtectionKeysOptions : IValidatableObject
{
    public const string Section = "DataProtection";

    public string? KeysDirectory { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        string.IsNullOrWhiteSpace(KeysDirectory) || Path.IsPathFullyQualified(KeysDirectory)
            ? []
            : [new ValidationResult("DataProtection:KeysDirectory must be an absolute path.")];
}
