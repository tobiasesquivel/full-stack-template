using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Access;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.GetUser;

internal sealed partial record GetUserQuery
{
    public sealed class Handler
    {
        private readonly UsersDbContext _db;
        private readonly IAuthModule _auth;

        public Handler(UsersDbContext db, IAuthModule auth)
        {
            _db = db;
            _auth = auth;
        }

        public async Task<ErrorOr<UserResponse>> Handle(GetUserQuery query, CancellationToken ct)
        {
            UserProfile? profile = await _db.UserProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.AuthId == query.AuthId, ct);
            if (profile is null)
                return UserErrors.NotFound(query.AuthId);

            if (!await _auth.CanAccessAsync(query.CallerId, profile, UsersPermissions.Read, ct))
                return UserErrors.Forbidden;

            UserAccountDetails? account = await _auth.FindAccountAsync(query.AuthId, ct);
            if (account is null)
                return UserErrors.NotFound(query.AuthId);

            return UserResponse.From(account, UsersModule.Describe(profile));
        }
    }
}
