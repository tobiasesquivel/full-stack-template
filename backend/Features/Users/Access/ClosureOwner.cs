using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users.Access;

internal sealed record ClosureOwner(string OwnerId) : IOwnedResource;
