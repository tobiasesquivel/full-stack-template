using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Infrastructure.Identity;
using TemplateApp.Infrastructure.Options;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth;

public sealed record IssuedSession(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshPlaintext);

public sealed class TokenService
{
    private readonly JwtOptions _options;
    private readonly AppDbContext _db;
    private readonly UserManager<AppIdentityUser> _users;
    private readonly TimeProvider _clock;

    public TokenService(IOptions<JwtOptions> options, AppDbContext db, UserManager<AppIdentityUser> users, TimeProvider clock)
    {
        _options = options.Value;
        _db = db;
        _users = users;
        _clock = clock;
    }

    public string BuildAccessToken(AppIdentityUser user, Guid familyId, DateTimeOffset now, out DateTimeOffset expiresAt)
    {
        expiresAt = now.AddMinutes(_options.AccessTokenExpirationMinutes);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Sid, familyId.ToString("D")),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        ];

        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_options.Key));
        SigningCredentials creds = new(key, SecurityAlgorithms.HmacSha256);
        JwtSecurityTokenHandler handler = new();

        JwtSecurityToken token = new(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        return handler.WriteToken(token);
    }

    public async Task<IssuedSession> IssueNewFamilyAsync(AppIdentityUser user, CancellationToken ct)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        Guid familyId = Guid.NewGuid();
        string plaintext = RefreshToken.NewPlaintext();

        RefreshToken entity = new()
        {
            TokenHash = RefreshToken.Hash(plaintext),
            FamilyId = familyId,
            UserId = user.Id,
            ExpiresAt = now.AddDays(_options.RefreshTokenExpirationDays),
            CreatedAt = now,
        };

        _db.RefreshTokens.Add(entity);

        string accessToken = BuildAccessToken(user, familyId, now, out DateTimeOffset expiresAt);

        return new IssuedSession(accessToken, expiresAt, plaintext);
    }

    public async Task<ErrorOr<IssuedSession>> RotateAsync(string plaintext, CancellationToken ct)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        string hash = RefreshToken.Hash(plaintext);

        RefreshToken? presented = await _db.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.TokenHash == hash, ct);
        if (presented is null)
            return AuthErrors.InvalidSession;

        if (presented.RevokedAt is not null || presented.ExpiresAt <= now)
        {
            await RevokeFamilyAsync(presented.FamilyId, now, ct);
            await _db.SaveChangesAsync(ct);
            return AuthErrors.SessionRevoked;
        }

        int revokedSiblings = await _db.RefreshTokens
            .Where(rt => rt.FamilyId == presented.FamilyId && rt.Id != presented.Id && rt.RevokedAt != null)
            .CountAsync(ct);
        if (revokedSiblings > 0)
        {
            await RevokeFamilyAsync(presented.FamilyId, now, ct);
            await _db.SaveChangesAsync(ct);
            return AuthErrors.SessionRevoked;
        }

        AppIdentityUser? user = await _users.FindByIdAsync(presented.UserId);
        if (user is null)
            return AuthErrors.InvalidSession;

        string nextPlaintext = RefreshToken.NewPlaintext();
        DateTimeOffset nextExpiry = presented.ExpiresAt <= now.AddDays(_options.RefreshTokenExpirationDays)
            ? presented.ExpiresAt
            : now.AddDays(_options.RefreshTokenExpirationDays);

        RefreshToken next = new()
        {
            TokenHash = RefreshToken.Hash(nextPlaintext),
            FamilyId = presented.FamilyId,
            UserId = presented.UserId,
            ExpiresAt = nextExpiry,
            CreatedAt = now,
        };

        presented.Revoke(now, next.TokenHash);
        _db.RefreshTokens.Add(next);
        await _db.SaveChangesAsync(ct);

        string accessToken = BuildAccessToken(user, presented.FamilyId, now, out DateTimeOffset expiresAt);

        return new IssuedSession(accessToken, expiresAt, nextPlaintext);
    }

    public async Task RevokePresentedAsync(string plaintext, CancellationToken ct)
    {
        string hash = RefreshToken.Hash(plaintext);
        RefreshToken? presented = await _db.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.TokenHash == hash, ct);

        if (presented is null)
            return;

        DateTimeOffset now = _clock.GetUtcNow();
        await RevokeFamilyAsync(presented.FamilyId, now, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        List<RefreshToken> active = await _db.RefreshTokens
            .Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null)
            .ToListAsync(ct);

        foreach (RefreshToken token in active)
            token.Revoke(now);
    }

    public Task<bool> IsFamilyActiveAsync(Guid familyId, string userId, CancellationToken ct) =>
        _db.RefreshTokens.AnyAsync(
            rt => rt.FamilyId == familyId && rt.UserId == userId && rt.RevokedAt == null,
            ct);

    public static string CookieHeader(string plaintext, IHostEnvironment environment)
    {
        bool secure = !environment.IsDevelopment();
        return $"template_refresh={Uri.EscapeDataString(plaintext)}; Path=/api/v1/auth; HttpOnly; SameSite=Strict{(secure ? "; Secure" : string.Empty)}";
    }

    public static string ClearedCookieHeader(IHostEnvironment environment)
    {
        bool secure = !environment.IsDevelopment();
        return $"template_refresh=; Path=/api/v1/auth; HttpOnly; Max-Age=0; SameSite=Strict{(secure ? "; Secure" : string.Empty)}";
    }

    public static bool TryGetRefreshPlaintext(HttpRequest request, out string? plaintext)
    {
        plaintext = null;
        if (!request.Cookies.TryGetValue("template_refresh", out string? raw) || string.IsNullOrEmpty(raw))
            return false;

        plaintext = raw;
        return true;
    }

    public AuthResponse ToAuthResponse(AppIdentityUser user, UserProfileSnapshot profile, IList<string> roles, IReadOnlySet<string> permissions, IssuedSession session) =>
        new(
            new TemplateApp.Features.Users.Contracts.UserResponse(
                user.Id,
                user.UserName,
                user.Email,
                profile.DisplayName,
                profile.Timezone,
                new List<string>(roles).AsReadOnly(),
                permissions.ToList().AsReadOnly()),
            session.AccessToken,
            session.AccessTokenExpiresAt);

    public TokenResponse ToTokenResponse(IssuedSession session) =>
        new(session.AccessToken, session.AccessTokenExpiresAt);
}

public sealed record UserProfileSnapshot(string DisplayName, string Timezone);
