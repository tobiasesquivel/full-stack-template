using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class RemoteCallInTransactionAnalyzerTests
{
    private const string Platform = """
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Transactions;
        namespace TemplateApp.Infrastructure.Persistence
        {
            public static class CrossContextTransaction
            {
                public static TransactionScope Begin() => new(TransactionScopeAsyncFlowOption.Enabled);
            }
        }
        namespace TemplateApp.Infrastructure.Http
        {
            public static class RemoteServices
            {
                public static Task<HttpResponseMessage> SendRemoteAsync(this HttpClient client, HttpRequestMessage request, CancellationToken ct) =>
                    client.SendAsync(request, ct);
            }
        }
        namespace TemplateApp.Features.Users
        {
            public sealed class OwnedTasksClient
            {
                private readonly HttpClient _http;

                public OwnedTasksClient(HttpClient http)
                {
                    _http = http;
                }

                public Task DeleteOwnedTasksAsync(string authId, CancellationToken ct) =>
                    _http.DeleteAsync("internal/v1/users/" + authId + "/tasks", ct);
            }

            public sealed class LocalProjects
            {
                public Task DeleteOwnedProjectsAsync(string authId, CancellationToken ct) => Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public async Task RemoteCallsOutsideTheScopeAndLocalCallsInsideAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/DeleteUser/DeleteUser.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using System.Transactions;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Users.DeleteUser;
                public sealed class DeleteUser(OwnedTasksClient tasks, LocalProjects projects)
                {
                    public async Task ExecuteAsync(string authId, CancellationToken ct)
                    {
                        await tasks.DeleteOwnedTasksAsync(authId, ct);
                        using TransactionScope transaction = CrossContextTransaction.Begin();
                        await projects.DeleteOwnedProjectsAsync(authId, ct);
                        transaction.Complete();
                    }
                }
                """),
            ("backend/Features/Users/ArchiveUser/ArchiveUser.cs", """
                using System.Net.Http;
                using System.Threading;
                using System.Threading.Tasks;
                using System.Transactions;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Users.ArchiveUser;
                public sealed class ArchiveUser(HttpClient http, LocalProjects projects)
                {
                    public async Task ExecuteAsync(string authId, CancellationToken ct)
                    {
                        using (TransactionScope transaction = CrossContextTransaction.Begin())
                        {
                            await projects.DeleteOwnedProjectsAsync(authId, ct);
                            transaction.Complete();
                        }
                        await http.GetAsync("internal/v1/ping", ct);
                    }
                }
                """));

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Id == "DUNE0022"));
    }

    [Fact]
    public async Task RemoteCallsInsideACrossContextTransactionAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/DeleteUser/DeleteUser.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using System.Transactions;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Users.DeleteUser;
                public sealed class DeleteUser(OwnedTasksClient tasks, LocalProjects projects)
                {
                    public async Task ExecuteAsync(string authId, CancellationToken ct)
                    {
                        using TransactionScope transaction = CrossContextTransaction.Begin();
                        await projects.DeleteOwnedProjectsAsync(authId, ct);
                        await tasks.DeleteOwnedTasksAsync(authId, ct);
                        transaction.Complete();
                    }
                }
                """),
            ("backend/Features/Users/ArchiveUser/ArchiveUser.cs", """
                using System.Net.Http;
                using System.Threading;
                using System.Threading.Tasks;
                using System.Transactions;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Users.ArchiveUser;
                public sealed class ArchiveUser(HttpClient http)
                {
                    public async Task ExecuteAsync(CancellationToken ct)
                    {
                        using (TransactionScope transaction = CrossContextTransaction.Begin())
                        {
                            if (!ct.IsCancellationRequested)
                            {
                                await http.GetAsync("internal/v1/ping", ct);
                            }
                            transaction.Complete();
                        }
                    }
                }
                """),
            ("backend/Infrastructure/Http/Forwarder.cs", """
                using System.Net.Http;
                using System.Threading;
                using System.Threading.Tasks;
                using System.Transactions;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Infrastructure.Http;
                public sealed class Forwarder(HttpClient http)
                {
                    public async Task ForwardAsync(HttpRequestMessage request, CancellationToken ct)
                    {
                        using TransactionScope transaction = CrossContextTransaction.Begin();
                        await http.SendRemoteAsync(request, ct);
                        transaction.Complete();
                    }
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Users/ArchiveUser/ArchiveUser.cs", 15),
                ("backend/Features/Users/DeleteUser/DeleteUser.cs", 12),
                ("backend/Infrastructure/Http/Forwarder.cs", 12),
            },
            Locations(diagnostics));
    }

    [Fact]
    public async Task TestsAreNotChecked()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/DeleteUser/DeleteUserTests.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using System.Transactions;
                using TemplateApp.Infrastructure.Persistence;
                namespace TemplateApp.Features.Users.DeleteUser;
                public sealed class DeleteUserTests(OwnedTasksClient tasks)
                {
                    public async Task RunsAsync(CancellationToken ct)
                    {
                        using TransactionScope transaction = CrossContextTransaction.Begin();
                        await tasks.DeleteOwnedTasksAsync("auth", ct);
                    }
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("backend/Platform.cs", Platform) },
            AnalyzerHost.Only<RemoteCallInTransactionAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0022")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
