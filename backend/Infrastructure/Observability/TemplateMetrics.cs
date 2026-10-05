using System.Diagnostics.Metrics;

namespace TemplateApp.Infrastructure.Observability;

public sealed class TemplateMetrics : ITemplateMetrics
{
    private readonly Counter<long> _authRegistrations;
    private readonly Counter<long> _authLogins;
    private readonly Counter<long> _tasksCreated;
    private readonly Counter<long> _tasksUpdated;

    public TemplateMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create("TemplateApp.Business", "1.0.0");
        _authRegistrations = meter.CreateCounter<long>(
            "templateapp.auth.registrations", "{registration}", "User registrations by outcome.");
        _authLogins = meter.CreateCounter<long>(
            "templateapp.auth.logins", "{login}", "User logins by outcome.");
        _tasksCreated = meter.CreateCounter<long>(
            "templateapp.tasks.created", "{task}", "Tasks created.");
        _tasksUpdated = meter.CreateCounter<long>(
            "templateapp.tasks.updated", "{task}", "Tasks updated by outcome.");
    }

    public void AuthRegistered(string outcome) =>
        _authRegistrations.Add(1, new KeyValuePair<string, object?>("outcome", NormalizeOutcome(outcome)));

    public void AuthLoggedIn(string outcome) =>
        _authLogins.Add(1, new KeyValuePair<string, object?>("outcome", NormalizeOutcome(outcome)));

    public void TaskCreated() => _tasksCreated.Add(1);

    public void TaskUpdated(string outcome) =>
        _tasksUpdated.Add(1, new KeyValuePair<string, object?>("outcome", NormalizeOutcome(outcome)));

    private static string NormalizeOutcome(string outcome) =>
        string.Equals(outcome, "success", StringComparison.OrdinalIgnoreCase) ? "success"
        : string.Equals(outcome, "failure", StringComparison.OrdinalIgnoreCase) ? "failure" : "unknown";
}
