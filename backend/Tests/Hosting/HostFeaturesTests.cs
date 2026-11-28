using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Hosting;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Hosting;

public sealed class HostFeaturesTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public HostFeaturesTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public void EachHost_StartsTheFeaturesItCompiles_InOrderOfName()
    {
        Assert.Equal(["Auth", "Health", "Projects", "Users"], _fixture.Services.GetRequiredService<HostFeatures>().Names);
        Assert.Equal(["Health", "Tasks"], _fixture.TasksServices.GetRequiredService<HostFeatures>().Names);
    }
}
