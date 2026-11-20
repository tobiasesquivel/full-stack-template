using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TemplateApp.Gateway;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace TemplateApp.Tests.Gateway;

public sealed class GatewayTimeoutTests : IDisposable
{
    private readonly WireMockServer _api = WireMockServer.Start();

    [Fact]
    public async Task ApiSilentLongerThanTheActivityTimeout_TheGatewayAnswers504ProblemDetails_AtTheTimeout()
    {
        _api.Given(Request.Create().WithPath("/api/v1/projects").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithDelay(TimeSpan.FromSeconds(5)));
        await using GatewayFactory gateway = new(_api.Url!, activityTimeout: "00:00:01");
        using HttpClient client = gateway.CreateClient();

        Stopwatch clock = Stopwatch.StartNew();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/projects", UriKind.Relative), TestContext.Current.CancellationToken);
        clock.Stop();

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Http.GatewayTimeout", problem.RootElement.GetProperty("code").GetString());
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task ApiAnswersBeforeTheActivityTimeout_TheGatewayForwardsTheAnswer()
    {
        _api.Given(Request.Create().WithPath("/api/v1/projects").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("[]").WithDelay(TimeSpan.FromMilliseconds(200)));
        await using GatewayFactory gateway = new(_api.Url!, activityTimeout: "00:00:05");
        using HttpClient client = gateway.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/projects", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose() => _api.Dispose();

    private sealed class GatewayFactory(string apiAddress, string activityTimeout) : WebApplicationFactory<GatewayHost>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ReverseProxy:Clusters:monolith:HttpRequest:ActivityTimeout", activityTimeout);
            builder.UseSetting("ReverseProxy:Clusters:monolith:HealthCheck:Active:Enabled", "false");
            builder.UseSetting("ReverseProxy:Clusters:tasks:HealthCheck:Active:Enabled", "false");
            builder.UseSetting("ReverseProxy:Clusters:monolith:Destinations:api:Address", $"{apiAddress}/");
        }
    }
}
