using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Domain.Entities;
using TemplateApp.Features.Users.Common;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.Delete;

public sealed class DeleteUserCommandHandler
{
    private readonly UserManager<AppIdentityUser> _userManager;
    private readonly AppDbContext _db;

    public DeleteUserCommandHandler(
        UserManager<AppIdentityUser> userManager,
        AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<ErrorOr<Success>> Handle(DeleteUserCommand command, CancellationToken ct)
    {
        AppIdentityUser? identityUser = await _userManager.FindByIdAsync(command.UserId);
        if (identityUser is null)
            return UserErrors.NotFound(command.UserId);

        User? userEntity = await _db.Users.FirstOrDefaultAsync(u => u.AuthId == command.UserId, ct);
        if (userEntity is not null)
            _db.Users.Remove(userEntity);

        List<RefreshToken> refreshTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == command.UserId)
            .ToListAsync(ct);
        if (refreshTokens.Count is not 0)
            _db.RefreshTokens.RemoveRange(refreshTokens);

        IdentityResult deleteResult = await _userManager.DeleteAsync(identityUser);
        if (!deleteResult.Succeeded)
        {
            List<Error> errors = deleteResult.Errors
                .Select(e => Error.Failure(code: e.Code, description: e.Description))
                .ToList();
            return errors;
        }

        return Result.Success;
    }
}
