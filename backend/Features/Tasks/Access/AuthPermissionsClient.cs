using System.Net;
using TemplateApp.Infrastructure.Access;
using TemplateApp.Infrastructure.Http.Remote;

namespace TemplateApp.Features.Tasks.Access;

internal sealed class AuthPermissionsClient
{
    private readonly HttpClient _http;

    public AuthPermissionsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task DeclareAsync(FeaturePermissions declaration, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, $"internal/v1/feature-permissions/{Uri.EscapeDataString(declaration.Feature)}")
        {
            Content = JsonContent.Create(new PermissionDeclarationRequest(declaration.Grants)),
        };
        using HttpResponseMessage response = await _http.SendRemoteAsync(request, ct, HttpStatusCode.NoContent);
    }
}
