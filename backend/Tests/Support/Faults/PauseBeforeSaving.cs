using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TemplateApp.Tests.Support.Faults;

public sealed class PauseBeforeSaving<TEntity> : SaveChangesInterceptor
    where TEntity : class
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Reached => _reached.Task;

    public void Release() => _released.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context?.ChangeTracker.Entries<TEntity>().Any(e => e.State == EntityState.Added) == true)
        {
            _reached.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}
