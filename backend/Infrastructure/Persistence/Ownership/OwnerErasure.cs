using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TemplateApp.Infrastructure.Persistence.Ownership;

public static class OwnerErasure
{
    private const string Annotation = "TemplateApp:ErasedWithOwner";

    public static EntityTypeBuilder<TEntity> ErasedWithOwner<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, string>> owner)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(Annotation, builder.Property(owner).Metadata.Name);
    }

    public static IReadOnlyList<OwnedTable> TablesErasedWithOwner(this IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetEntityTypes()
            .Where(entity => entity.FindAnnotation(Annotation) is not null)
            .Select(entity =>
            {
                string table = entity.GetTableName()
                    ?? throw new InvalidOperationException($"{entity.DisplayName()} is erased with its owner but is not mapped to a table.");
                string? schema = entity.GetSchema();
                IProperty owner = entity.GetProperty((string)entity[Annotation]!);
                return new OwnedTable(schema, table, owner.GetColumnName(StoreObjectIdentifier.Table(table, schema))!);
            })
            .ToList();
    }
}
