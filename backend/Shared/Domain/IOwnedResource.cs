namespace TemplateApp.Shared.Domain;

public interface IOwnedResource
{
    string OwnerId { get; }
}
