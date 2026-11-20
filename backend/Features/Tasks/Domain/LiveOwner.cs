namespace TemplateApp.Features.Tasks.Domain;

internal sealed class LiveOwner
{
    private LiveOwner(string id)
    {
        Id = id;
    }

    public string Id { get; }

    internal abstract class Issuer
    {
        protected static LiveOwner Issue(string ownerId) => new(ownerId);
    }
}
