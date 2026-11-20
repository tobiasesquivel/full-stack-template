using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using TemplateApp.Infrastructure.Http;
using Xunit;

namespace TemplateApp.Tests.Remote;

public sealed partial class TimeoutCascadeTests
{
    private const int SequentialRemoteCallsPerRequest = 2;

    private static readonly TimeSpan NginxDefaultReadTimeout = TimeSpan.FromSeconds(60);

    private static readonly string Root = FindRoot();

    [Theory]
    [InlineData("backend/appsettings.json", "Tasks")]
    [InlineData("backend/Hosts/Tasks/appsettings.json", "Monolith")]
    public void EachLevelGivesUpBeforeTheLevelThatCallsIt(string settings, string service)
    {
        RemoteServiceOptions client = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Root, settings))
            .Build()
            .GetSection($"{RemoteServiceOptions.Section}:{service}")
            .Get<RemoteServiceOptions>()!;
        IReadOnlyDictionary<string, TimeSpan> clusters = GatewayActivityTimeouts();
        TimeSpan nginx = NginxReadTimeout();

        Assert.True(client.Timeout < client.TotalTimeout, $"{service}: attempt {client.Timeout} must be shorter than the total {client.TotalTimeout}");
        Assert.Equal(["monolith", "tasks"], clusters.Keys.Order(StringComparer.Ordinal));
        foreach ((string cluster, TimeSpan timeout) in clusters)
        {
            TimeSpan worstCase = client.TotalTimeout * SequentialRemoteCallsPerRequest;
            Assert.True(worstCase < timeout, $"{service}: {SequentialRemoteCallsPerRequest} calls of {client.TotalTimeout} must end before the gateway cluster {cluster} gives up ({timeout})");
            Assert.True(timeout < nginx, $"gateway cluster {cluster} ({timeout}) must be shorter than nginx ({nginx})");
        }
    }

    [Fact]
    public void ComposeDoesNotOverrideTheTimeouts()
    {
        string compose = File.ReadAllText(Path.Combine(Root, "compose.yaml"));

        Assert.DoesNotMatch(ComposeTimeoutOverride(), compose);
    }

    private static Dictionary<string, TimeSpan> GatewayActivityTimeouts()
    {
        using JsonDocument gateway = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "gateway", "appsettings.json")));
        return gateway.RootElement.GetProperty("ReverseProxy").GetProperty("Clusters").EnumerateObject()
            .ToDictionary(
                cluster => cluster.Name,
                cluster => TimeSpan.Parse(
                    cluster.Value.GetProperty("HttpRequest").GetProperty("ActivityTimeout").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
    }

    private static TimeSpan NginxReadTimeout()
    {
        Match setting = NginxReadTimeoutSetting().Match(File.ReadAllText(Path.Combine(Root, "frontend", "nginx.conf")));
        return setting.Success
            ? TimeSpan.FromSeconds(int.Parse(setting.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            : NginxDefaultReadTimeout;
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TemplateApp.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("TemplateApp.slnx not found above the test output.");
    }

    [GeneratedRegex(@"proxy_read_timeout\s+(\d+)s?\s*;")]
    private static partial Regex NginxReadTimeoutSetting();

    [GeneratedRegex(@"Services__\w+__\w*Timeout|ReverseProxy__Clusters__\w+__HttpRequest__ActivityTimeout")]
    private static partial Regex ComposeTimeoutOverride();
}
