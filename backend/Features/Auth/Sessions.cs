using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth;

internal sealed class Sessions
{
    private readonly AppDbContext _db;
    private readonly SessionOptions _options;
    private readonly TimeProvider _clock;

    public Sessions(AppDbContext db, IOptions<SessionOptions> options, TimeProvider clock)
    {
        _db = db;
        _options = options.Value;
        _clock = clock;
    }

    public Session Open(string userId)
    {
        Session session = Session.Open(userId, _clock.GetUtcNow(), _options.Lifetime);
        _db.Sessions.Add(session);
        return session;
    }

    public async Task<bool> IsActiveAsync(Guid sessionId, string userId, CancellationToken ct)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        Session? session = await _db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
            return false;

        if (!session.IsActive(now, _options.IdleTimeout))
        {
            await CloseAsync(sessionId, ct);
            return false;
        }

        if (session.NeedsTouch(now))
        {
            DateTimeOffset seen = session.LastSeenAt;
            await _db.Sessions
                .Where(s => s.Id == sessionId && s.LastSeenAt == seen)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastSeenAt, now), ct);
        }

        return true;
    }

    public Task CloseAsync(Guid sessionId, CancellationToken ct) =>
        _db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(ct);
}
