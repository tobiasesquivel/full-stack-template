using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TemplateApp.Tests.Support;

public sealed class FailAfterSaving<TEntity> : SaveChangesInterceptor
    where TEntity : class
{
    private readonly ConditionalWeakTable<DbContext, object> _armed = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        DbContext? context = eventData.Context;
        if (context is not null && context.ChangeTracker.Entries<TEntity>().Any(e => e.State == EntityState.Added))
            _armed.AddOrUpdate(context, this);

        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null && _armed.Remove(eventData.Context))
            throw new InvalidOperationException($"{typeof(TEntity).Name} was saved, then the operation failed.");

        return ValueTask.FromResult(result);
    }
}
