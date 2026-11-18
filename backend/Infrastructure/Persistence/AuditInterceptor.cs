using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Infrastructure.Persistence;

public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private const string UserActor = "user";
    private const string AnonymousActor = "anonymous";
    private const string SystemActor = "system";

    private readonly IHttpContextAccessor _http;
    private readonly TimeProvider _clock;

    public AuditInterceptor(IHttpContextAccessor http, TimeProvider clock)
    {
        _http = http;
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            Record(eventData.Context);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            Record(eventData.Context);

        return ValueTask.FromResult(result);
    }

    private void Record(DbContext context)
    {
        foreach (EntityEntry<AuditEntry> unsaved in context.ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added).ToList())
            unsaved.State = EntityState.Detached;

        List<EntityEntry> changed = context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (changed.Count == 0)
            return;

        (string actorKind, string? actorId) = Actor();
        DateTimeOffset now = _clock.GetUtcNow();
        string? traceId = Activity.Current?.TraceId.ToHexString();

        context.AddRange(changed.Select(entry => new AuditEntry
        {
            OccurredAt = now,
            ActorKind = actorKind,
            ActorId = actorId,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = KeyOf(entry),
            Operation = entry.State.ToString(),
            ChangedProperties = ChangedPropertiesOf(entry),
            TraceId = traceId,
        }));
    }

    private (string Kind, string? Id) Actor()
    {
        HttpContext? http = _http.HttpContext;
        if (http is null)
            return (SystemActor, null);

        string? id = OnBehalfOf.UserOf(http);
        return string.IsNullOrWhiteSpace(id) ? (AnonymousActor, null) : (UserActor, id);
    }

    private static string KeyOf(EntityEntry entry) =>
        string.Join(
            ',',
            entry.Metadata.FindPrimaryKey()!.Properties
                .Select(p => Convert.ToString(entry.Property(p.Name).CurrentValue, CultureInfo.InvariantCulture)));

    private static string[] ChangedPropertiesOf(EntityEntry entry) =>
        entry.State != EntityState.Modified
            ? []
            : entry.Properties
                .Where(p => p.IsModified)
                .Select(p => p.Metadata.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
}
