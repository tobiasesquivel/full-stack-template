using TemplateApp.Shared.Domain;

namespace TemplateApp.Tests.Support.Contexts;

internal sealed record ProbeItemCreated(Guid ItemId) : IDomainEvent;
