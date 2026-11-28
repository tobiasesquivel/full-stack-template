using System.Net;
using Microsoft.AspNetCore.DataProtection;
using TemplateApp.Infrastructure.Access;
using TemplateApp.Infrastructure.Http.Remote;

namespace TemplateApp.Features.Tasks.Access;

internal sealed class AuthPermissionsClient
{
    private readonly HttpClient _http;
    private readonly IDataProtectionProvider _keys;

    public AuthPermissionsClient(HttpClient http, IDataProtectionProvider keys)
    {
        _http = http;
        _keys = keys;
    }

    public async Task<bool> DeclareAsync(FeaturePermissions declaration, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, $"internal/v1/feature-permissions/{Uri.EscapeDataString(declaration.Feature)}")
        {
            Content = JsonContent.Create(new PermissionDeclarationRequest(PermissionDeclarationSeal.Seal(_keys, declaration))),
        };
        using HttpResponseMessage response = await _http.SendRemoteAsync(request, ct, HttpStatusCode.NoContent, HttpStatusCode.NotFound);
        return response.StatusCode == HttpStatusCode.NoContent;
    }
}
