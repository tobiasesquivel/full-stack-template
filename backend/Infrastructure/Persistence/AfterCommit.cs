namespace TemplateApp.Infrastructure.Persistence;

public sealed class AfterCommit
{
    private readonly List<Action> _pending = [];

    public void Add(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pending.Add(action);
    }

    internal void Run()
    {
        Action[] committed = [.. _pending];
        _pending.Clear();
        foreach (Action action in committed)
            action();
    }

    internal void Discard() => _pending.Clear();
}
