namespace TemplateApp.Infrastructure.Persistence;

public sealed record MigratedContext(Type ContextType, string Schema, bool AdoptsLegacyTables, bool AppliesMigrations = true);
