using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TemplateApp.Domain.Entities;
using TemplateApp.Features.Auth.Common;
using TemplateApp.Infrastructure.Options;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Infrastructure.Auth;

public sealed class JwtService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly AppDbContext _db;
    private readonly UserManager<AppIdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public JwtService(IOptions<JwtOptions> options, AppDbContext db, UserManager<AppIdentityUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _options = options.Value;
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;

    }

    public async Task<ErrorOr<TokenPair>> GenerateTokensAsync(AppIdentityUser user, CancellationToken ct = default)
    {
        string jwtId = Guid.NewGuid().ToString("N");
        DateTime now = DateTime.UtcNow;

        Task<IList<string>> rolesTask = _userManager.GetRolesAsync(user);
        Task<IList<Claim>> claimsTask = _userManager.GetClaimsAsync(user);

        await Task.WhenAll(rolesTask, claimsTask);

        IList<string> roles = rolesTask.Result;
        List<Claim> userClaims = claimsTask.Result.ToList();

        List<Claim> roleClaims = await GetRoleClaimsAsync(roles);
        userClaims.AddRange(roleClaims);

        string accessToken = GenerateAccessToken(user, roles, userClaims, jwtId, now);
        string refreshTokenPlain = await GenerateRefreshTokenAsync(jwtId, user.Id, now, ct);

        return new TokenPair(accessToken, refreshTokenPlain);
    }

    private string GenerateAccessToken(AppIdentityUser user, IEnumerable<string> roles, IEnumerable<Claim> userClaims, string jwtId, DateTime now)
    {
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, jwtId),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Iss, _options.Issuer),
        ];

        foreach (string role in roles)
        {
            claims.Add(new Claim("role", role));
        }

        claims.AddRange(userClaims);

        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_options.Key));
        SigningCredentials creds = new(key, SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.AccessTokenExpirationMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }


    private async Task<string> GenerateRefreshTokenAsync(string jwtId, string userId, DateTime now, CancellationToken ct)
    {
        byte[] refreshTokenBytes = RandomNumberGenerator.GetBytes(32);
        string refreshTokenPlain = Convert.ToBase64String(refreshTokenBytes);
        string refreshTokenHash = HashToken(refreshTokenPlain);

        RefreshToken refreshTokenEntity = new()
        {
            Token = refreshTokenHash,
            JwtId = jwtId,
            UserId = userId,
            ExpiresAt = now.AddDays(_options.RefreshTokenExpirationDays),
            CreatedAt = now,
        };

        _db.RefreshTokens.Add(refreshTokenEntity);

        return refreshTokenPlain;
    }

    public async Task<ErrorOr<TokenPair>> RedeemTokenAsync(string refreshTokenPlain, CancellationToken ct = default)
    {
        string hash = HashToken(refreshTokenPlain);
        DateTime now = DateTime.UtcNow;

        RefreshToken? storedToken = await _db
            .RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == hash, ct);

        if (storedToken is null || storedToken.RevokedAt is not null || storedToken.ExpiresAt < now)
            return AuthErrors.InvalidToken;

        if (storedToken.ReplacedByToken is not null)
        {
            await _db
                .RefreshTokens
                .Where(rt => rt.UserId == storedToken.UserId && rt.RevokedAt == null)
                .ExecuteUpdateAsync(setter => setter.SetProperty(rt => rt.RevokedAt, now), ct);

            return AuthErrors.TokenTheft;
        }

        AppIdentityUser? identityUser = await _db
            .Set<AppIdentityUser>()
            .FindAsync([storedToken.UserId], ct);

        if (identityUser is null)
            return AuthErrors.InvalidToken;

        ErrorOr<TokenPair> newTokensOrError = await GenerateTokensAsync(identityUser, ct);
        if (newTokensOrError.IsError)
            return newTokensOrError.Errors;

        TokenPair newTokens = newTokensOrError.Value;

        storedToken.Revoke(HashToken(newTokens.RefreshToken));

        return newTokens;
    }

    private async Task<List<Claim>> GetRoleClaimsAsync(IEnumerable<string> roles)
    {
        IdentityRole?[] identityRoles = await Task.WhenAll(
            roles.Select(roleName => _roleManager.FindByNameAsync(roleName))
        );

        IList<Claim>[] roleClaimLists = await Task.WhenAll(
            identityRoles
                .Where(role => role is not null)
                .Select(role => _roleManager.GetClaimsAsync(role!))
        );

        return roleClaimLists.SelectMany(claims => claims).ToList();
    }

    private static string HashToken(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
