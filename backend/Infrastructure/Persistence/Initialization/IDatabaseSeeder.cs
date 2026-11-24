namespace TemplateApp.Infrastructure.Persistence.Initialization;

public interface IDatabaseSeeder
{
    Task SeedAsync(CancellationToken ct);
}
