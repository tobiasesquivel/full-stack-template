using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Domain.Authorization;
using TemplateApp.Features.Auth.Common;
using TemplateApp.Infrastructure.Auth;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth.Login;

public sealed class LoginUserCommandHandler
{
    private readonly UserManager<AppIdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;

    public LoginUserCommandHandler(
        UserManager<AppIdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        AppDbContext db,
        ITokenService tokenService)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<ErrorOr<AuthResponse>> Handle(LoginUserCommand command, CancellationToken ct)
    {
        AppIdentityUser? identityUser = await _userManager.FindByNameAsync(command.Username);
        if (identityUser is null)
            return AuthErrors.InvalidCredentials;

        bool isPasswordValid = await _userManager
            .CheckPasswordAsync(identityUser, command.Password);

        if (!isPasswordValid)
            return AuthErrors.InvalidCredentials;

        UserProjection? userData = await _db.Users
            .Where(u => u.AuthId == identityUser.Id)
            .Select(u => new UserProjection(u.AuthId, u.Username))
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (userData is null)
            return AuthErrors.UserNotFound;

        IList<string> roles = await _userManager.GetRolesAsync(identityUser);
        IReadOnlyList<string> permissions = await GetPermissionsAsync(identityUser);

        ErrorOr<TokenPair> tokensOrError = await _tokenService.GenerateTokensAsync(identityUser, ct);
        if (tokensOrError.IsError)
            return tokensOrError.Errors;

        TokenPair tokens = tokensOrError.Value;

        AuthResponse response = new(
            new UserInfo(userData.AuthId, identityUser.Email!, userData.Username, roles.ToList(), permissions),
            tokens.AccessToken,
            tokens.RefreshToken);

        return response;
    }

    private async Task<IReadOnlyList<string>> GetPermissionsAsync(AppIdentityUser user)
    {
        IList<string> roles = await _userManager.GetRolesAsync(user);
        List<string> permissions = [];

        foreach (string roleName in roles)
        {
            IdentityRole? role = await _roleManager.FindByNameAsync(roleName);
            if (role is null) continue;

            IList<Claim> claims = await _roleManager.GetClaimsAsync(role);
            permissions.AddRange(claims
                .Where(c => c.Type == AppPermissions.ClaimType)
                .Select(c => c.Value));
        }

        return permissions;
    }

    private sealed record UserProjection(string AuthId, string Username);
}
