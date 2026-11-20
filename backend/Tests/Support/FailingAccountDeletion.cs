using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Tests.Support;

internal sealed class FailingAccountDeletion : IAuthModule
{
    private readonly IAuthModule _inner;

    private FailingAccountDeletion(IAuthModule inner)
    {
        _inner = inner;
    }

    public static void Install(IServiceCollection services)
    {
        ServiceDescriptor? registered = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IAuthModule));
        if (registered?.ImplementationFactory is not Func<IServiceProvider, object> create)
            return;

        services.Remove(registered);
        services.AddScoped<IAuthModule>(provider => new FailingAccountDeletion((IAuthModule)create(provider)));
    }

    public Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct) =>
        _inner.HasPermissionAsync(userId, permission, ct);

    public Task<bool> CanAccessAsync(string callerId, IOwnedResource resource, string permission, CancellationToken ct) =>
        _inner.CanAccessAsync(callerId, resource, permission, ct);

    public Task<UserAccountDetails?> FindAccountAsync(string authId, CancellationToken ct) =>
        _inner.FindAccountAsync(authId, ct);

    public Task<ErrorOr<Success>> DeleteAccountAsync(string authId, CancellationToken ct) =>
        throw new InvalidOperationException("Auth failed after the profile was deleted (test fault).");
}
