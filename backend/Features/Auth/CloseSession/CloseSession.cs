namespace TemplateApp.Features.Auth.CloseSession;

public sealed class CloseSession
{
    private readonly TokenService _tokens;

    public CloseSession(TokenService tokens)
    {
        _tokens = tokens;
    }

    public async Task ExecuteAsync(string? refreshPlaintext, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshPlaintext))
            return;

        await _tokens.RevokePresentedAsync(refreshPlaintext, ct);
    }
}
