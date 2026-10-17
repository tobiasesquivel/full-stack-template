using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Auth.Jwt;

public sealed class SessionValidator
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;

    public SessionValidator(AppDbContext db, TokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    public static Task OnTokenValidatedAsync(TokenValidatedContext context) =>
        context.HttpContext.RequestServices.GetRequiredService<SessionValidator>().ValidateAsync(context);

    public async Task ValidateAsync(TokenValidatedContext context)
    {
        string? sub = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        string? sid = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sid)?.Value;

        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sid, out Guid familyId))
        {
            context.Fail("Session is missing.");
            return;
        }

        bool userExists = await _db.Set<AppIdentityUser>()
            .AsNoTracking()
            .AnyAsync(u => u.Id == sub);

        if (!userExists)
        {
            context.Fail("Session is missing.");
            return;
        }

        bool active = await _tokens.IsFamilyActiveAsync(familyId, sub, context.HttpContext.RequestAborted);
        if (!active)
            context.Fail("Session has been revoked.");
    }
}
