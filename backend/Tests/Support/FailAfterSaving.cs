using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TemplateApp.Tests.Support;

public sealed class FailAfterSaving<TEntity> : SaveChangesInterceptor
    where TEntity : class
{
    private readonly ConditionalWeakTable<DbContext, object> _armed = new();
    private readonly EntityState _state;
    private readonly Func<Exception> _error;
    private int _remaining;
    private int _failures;

    public FailAfterSaving(EntityState state = EntityState.Added, int times = int.MaxValue, Func<Exception>? error = null)
    {
        _state = state;
        _remaining = times;
        _error = error ?? (() => new InvalidOperationException($"{typeof(TEntity).Name} was saved, then the operation failed."));
    }

    public int Failures => Volatile.Read(ref _failures);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        DbContext? context = eventData.Context;
        if (context is not null && context.ChangeTracker.Entries<TEntity>().Any(e => e.State == _state))
            _armed.AddOrUpdate(context, this);

        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null && _armed.Remove(eventData.Context) && Interlocked.Decrement(ref _remaining) >= 0)
        {
            Interlocked.Increment(ref _failures);
            throw _error();
        }

        return ValueTask.FromResult(result);
    }
}
