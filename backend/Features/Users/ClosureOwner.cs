using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users;

internal sealed record ClosureOwner(string OwnerId) : IOwnedResource;
