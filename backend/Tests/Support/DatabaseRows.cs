using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Tests.Support;

internal static class DatabaseRows
{
    public static async Task<long> CountAsync(this IServiceProvider services, FormattableString sql)
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database.SqlQuery<long>(sql).SingleAsync();
    }

    public static async Task<OwnedRows> OwnedRowsAsync(this IServiceProvider services, string authId) =>
        new(
            await services.CountAsync($"""SELECT count(*) AS "Value" FROM "AspNetUsers" WHERE "Id" = {authId}"""),
            await services.CountAsync($"""SELECT count(*) AS "Value" FROM "AspNetUserRoles" WHERE "UserId" = {authId}"""),
            await services.CountAsync($"""SELECT count(*) AS "Value" FROM "Sessions" WHERE "UserId" = {authId}"""),
            await services.CountAsync($"""SELECT count(*) AS "Value" FROM "UserProfiles" WHERE "AuthId" = {authId}"""),
            await services.CountAsync($"""SELECT count(*) AS "Value" FROM "TaskItems" WHERE "OwnerId" = {authId}"""),
            await services.CountAsync($"""SELECT count(*) AS "Value" FROM "Projects" WHERE "OwnerId" = {authId}"""),
            await services.CountAsync($"""SELECT count(*) AS "Value" FROM "IdempotencyKeys" WHERE "OwnerId" = {authId}"""));
}
