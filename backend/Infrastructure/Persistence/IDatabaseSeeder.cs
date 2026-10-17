namespace TemplateApp.Infrastructure.Persistence;

public interface IDatabaseSeeder
{
    Task SeedAsync(CancellationToken ct);
}
