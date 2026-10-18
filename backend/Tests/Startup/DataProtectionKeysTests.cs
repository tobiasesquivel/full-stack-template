using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Startup;

public sealed class DataProtectionKeysTests : IClassFixture<ApiFixture>, IDisposable
{
    private const string KeysDirectorySetting = "DataProtection:KeysDirectory";

    private readonly ApiFixture _fixture;
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"templateapp-keys-{Guid.NewGuid():N}");
    private readonly string _otherKeys = Path.Combine(Path.GetTempPath(), $"templateapp-keys-{Guid.NewGuid():N}");

    public DataProtectionKeysTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SessionCookie_SurvivesANewInstance_WithTheSameKeysDirectory_And_NotWithAnother()
    {
        TestSession alice = new(CreateClient(_keys));
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        Assert.NotEmpty(Directory.GetFiles(_keys, "key-*.xml"));

        TestSession recreated = new(CreateClient(_keys));
        recreated.Cookies[TestAccounts.SessionCookieName] = alice.Cookies[TestAccounts.SessionCookieName];
        SessionResponse restored = await recreated.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        restored.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, restored.Status);

        TestSession otherKeys = new(CreateClient(_otherKeys));
        otherKeys.Cookies[TestAccounts.SessionCookieName] = alice.Cookies[TestAccounts.SessionCookieName];
        SessionResponse denied = await otherKeys.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        denied.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);
    }

    public void Dispose()
    {
        foreach (string directory in new[] { _keys, _otherKeys })
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private HttpClient CreateClient(string keysDirectory) =>
        _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { [KeysDirectorySetting] = keysDirectory },
            out _);
}
