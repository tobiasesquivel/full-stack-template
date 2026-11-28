namespace TemplateApp.Infrastructure.Persistence.Ownership;

public sealed record OwnedTable(string? Schema, string Table, string OwnerColumn)
{
    public string Name => Schema is null ? Table : $"{Schema}.{Table}";
}
