using System.Diagnostics.Metrics;

namespace TemplateApp.Features.Auth;

public sealed class AuthMetrics
{
    private const string MeterName = "TemplateApp.Auth";

    private const string OutcomeTag = "templateapp.auth.login.outcome";

    private readonly Counter<long> _loginAttempts;

    public AuthMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        _loginAttempts = meter.CreateCounter<long>(
            "templateapp.auth.login.attempts",
            unit: "{attempt}",
            description: "Login attempts by outcome.");
    }

    public void LoginAttempted(LoginOutcome outcome) =>
        _loginAttempts.Add(1, new KeyValuePair<string, object?>(OutcomeTag, TagValue(outcome)));

    private static string TagValue(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.Success => "success",
        LoginOutcome.InvalidCredentials => "invalid_credentials",
        LoginOutcome.LockedOut => "locked_out",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown login outcome."),
    };
}
