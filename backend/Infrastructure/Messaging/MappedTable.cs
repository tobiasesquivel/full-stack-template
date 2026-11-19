using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace TemplateApp.Infrastructure.Messaging;

internal static class MappedTable
{
    public static string Of<TEntity>(DbContext db)
    {
        IEntityType entity = db.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{db.GetType().Name} does not map {typeof(TEntity).Name}: call MapOutbox() or MapInbox() in its OnModelCreating.");
        return db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
}
