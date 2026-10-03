namespace TemplateApp.Infrastructure.Seeders;

internal static class DatabaseSeederExtensions
{
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        using IServiceScope scope = app.Services.CreateScope();
        IServiceProvider serviceProvider = scope.ServiceProvider;

        try { await RoleSeeder.SeedRolesAsync(serviceProvider); }
        catch (Exception ex)
        {
            ILogger<WebApplication> logger = serviceProvider.GetRequiredService<ILogger<WebApplication>>();
            logger.LogError(ex, "Error seeding the database.");
        }
    }
}
