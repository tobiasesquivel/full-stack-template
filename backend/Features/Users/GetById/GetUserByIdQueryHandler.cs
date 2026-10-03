using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Users.Common;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.GetById;

public sealed class GetUserByIdQueryHandler(AppDbContext _db)
{
    public async Task<ErrorOr<GetUserByIdQueryResult>> Handle(GetUserByIdQuery query, CancellationToken ct)
    {
        GetUserByIdQueryResult? result = await _db.Users
            .Where(u => u.AuthId == query.AuthId)
            .Select(u => new GetUserByIdQueryResult(u.AuthId, u.Username))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (result is null)
            return UserErrors.NotFound(query.AuthId);

        return result;
    }
}
