using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Infrastructure.Persistence.Ownership;
using TemplateApp.Tests.Support.Hosts;

namespace TemplateApp.Tests.Support.Rows;

internal static class DatabaseRows
{
    public static async Task<long> CountAsync(this IServiceProvider services, FormattableString sql)
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database.SqlQuery<long>(sql).SingleAsync();
    }

    public static async Task<string?> TextAsync(this IServiceProvider services, FormattableString sql)
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database.SqlQuery<string>(sql).SingleOrDefaultAsync();
    }

    public static Task<OwnedRows> OwnedRowsAsync(this ApiPair api, string authId) =>
        OwnedRowsAsync([api.Monolith.Services, api.Tasks.Services], authId);

    public static Task<OwnedRows> OwnedRowsAsync(this ApiFixture fixture, string authId) =>
        OwnedRowsAsync([fixture.Services, fixture.TasksServices], authId);

    public static Task<OwnedRows> OwnedRowsAsync(this IServiceProvider host, string authId) =>
        OwnedRowsAsync([host], authId);

    private static async Task<OwnedRows> OwnedRowsAsync(IServiceProvider[] hosts, string authId)
    {
        Dictionary<string, long> byTable = [];
        foreach (IServiceProvider host in hosts)
        {
            using IServiceScope scope = host.CreateScope();
            foreach (DbContext context in scope.ServiceProvider.MigratedContexts())
            {
                ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
                foreach (OwnedTable table in context.Model.TablesErasedWithOwner().Where(table => !byTable.ContainsKey(table.Name)))
                {
                    byTable[table.Name] = await context.Database
                        .SqlQuery<long>(FormattableStringFactory.Create(
                            $$"""SELECT count(*) AS "Value" FROM {{sql.DelimitIdentifier(table.Table, table.Schema)}} WHERE {{sql.DelimitIdentifier(table.OwnerColumn)}} = {0}""",
                            authId))
                        .SingleAsync();
                }
            }
        }

        return new OwnedRows(byTable);
    }
}
