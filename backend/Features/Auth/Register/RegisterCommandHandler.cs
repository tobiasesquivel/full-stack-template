using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using TemplateApp.Domain.Authorization;
using TemplateApp.Domain.Entities;
using TemplateApp.Features.Auth.Common;
using TemplateApp.Infrastructure.Auth;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth.Register;

public sealed class RegisterUserCommandHandler
{
    private readonly UserManager<AppIdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;

    public RegisterUserCommandHandler(
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


    public async Task<ErrorOr<AuthResponse>> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        AppIdentityUser? existingUser = await _userManager.FindByEmailAsync(command.Email);
        if (existingUser is not null)
            return AuthErrors.EmailAlreadyExists;

        AppIdentityUser identityUser = new() { UserName = command.UserName, Email = command.Email, };

        IdentityResult createOutcome = await _userManager.CreateAsync(identityUser, command.Password);
        if (!createOutcome.Succeeded)
        {
            List<Error> errors = createOutcome.Errors
                .Select(e => Error.Validation(code: e.Code, description: e.Description))
                .ToList();
            return errors;
        }

        string newRol = AppRoles.User;
        IdentityResult roleResult = await _userManager.AddToRoleAsync(identityUser, newRol);

        if (!roleResult.Succeeded)
        {
            List<Error> errors = roleResult.Errors
                .Select(e => Error.Validation(code: e.Code, description: e.Description))
                .ToList();
            return errors;
        }

        User user = new() { AuthId = identityUser.Id, Username = command.UserName, };
        _db.Users.Add(user);

        ErrorOr<TokenPair> tokensOrError = await _tokenService.GenerateTokensAsync(identityUser, ct);
        if (tokensOrError.IsError)
            return tokensOrError.Errors;

        TokenPair tokens = tokensOrError.Value;

        IReadOnlyList<string> permissions = await GetPermissionsAsync(identityUser);

        AuthResponse response = new(
            new UserInfo(user.AuthId, identityUser.Email!, user.Username, [newRol], permissions),
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
}
