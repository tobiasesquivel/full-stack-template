using ErrorOr;
using TemplateApp.Features.Auth.Contracts;

namespace TemplateApp.Features.Auth.RefreshSession;

public sealed class RefreshSession
{
    private readonly TokenService _tokens;

    public RefreshSession(TokenService tokens)
    {
        _tokens = tokens;
    }

    public async Task<ErrorOr<(TokenResponse Response, string RefreshPlaintext)>> ExecuteAsync(string refreshPlaintext, CancellationToken ct)
    {
        ErrorOr<IssuedSession> rotated = await _tokens.RotateAsync(refreshPlaintext, ct);
        if (rotated.IsError)
            return rotated.Errors;

        return (_tokens.ToTokenResponse(rotated.Value), rotated.Value.RefreshPlaintext);
    }
}
