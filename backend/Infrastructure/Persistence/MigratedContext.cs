namespace TemplateApp.Infrastructure.Persistence;

public sealed record MigratedContext(Type ContextType, bool AdoptsLegacyTables, bool AppliesMigrations = true);
