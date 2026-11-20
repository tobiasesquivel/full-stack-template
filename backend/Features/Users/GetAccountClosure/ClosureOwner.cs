using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users.GetAccountClosure;

internal sealed record ClosureOwner(string OwnerId) : IOwnedResource;
