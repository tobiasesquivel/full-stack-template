using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace TemplateApp.Infrastructure.Persistence;

public sealed record DatabaseOptions : IValidatableObject
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = "ConnectionStrings:DefaultConnection is required. Set it via environment or secret.")]
    public string DefaultConnection { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(DefaultConnection))
            return [];

        string? host;
        try
        {
            host = new NpgsqlConnectionStringBuilder(DefaultConnection).Host;
        }
        catch (ArgumentException)
        {
            return [new ValidationResult("ConnectionStrings:DefaultConnection is not a valid PostgreSQL connection string.")];
        }

        return string.IsNullOrWhiteSpace(host)
            ? [new ValidationResult("ConnectionStrings:DefaultConnection must set Host.")]
            : [];
    }
}
