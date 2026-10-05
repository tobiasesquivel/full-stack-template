using Microsoft.EntityFrameworkCore;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Infrastructure.Seeders;

namespace TemplateApp.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken ct = default)
    {
        using IServiceScope scope = app.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;

        AppDbContext db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);

        await RoleSeeder.SeedRolesAsync(services);
    }
}
