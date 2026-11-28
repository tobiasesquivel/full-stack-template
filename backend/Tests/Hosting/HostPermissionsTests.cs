using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Access;
using TemplateApp.Infrastructure.Hosting;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Hosting;

public sealed class HostPermissionsTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public HostPermissionsTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public void TheMonolith_GathersTheDeclarationsOfItsOwnFeatures_WithoutTasks()
    {
        List<string> declared = Declared(_fixture.Services);

        Assert.NotEmpty(declared);
        Assert.Subset(_fixture.Services.GetRequiredService<HostFeatures>().Names.ToHashSet(StringComparer.Ordinal), declared.ToHashSet(StringComparer.Ordinal));
        Assert.DoesNotContain("Tasks", declared);
    }

    [Fact]
    public void TheTasksHost_DeclaresOnlyTasks() =>
        Assert.Equal(["Tasks"], Declared(_fixture.TasksServices));

    private static List<string> Declared(IServiceProvider services) =>
        services.GetRequiredService<HostPermissions>().All.Select(declaration => declaration.Feature).ToList();
}
