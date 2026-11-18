using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class ContractsAnalyzerTests
{
    private const string Stubs = """
        namespace ErrorOr
        {
            public readonly record struct Success;
            public readonly record struct ErrorOr<T>(T Value);
        }
        namespace TemplateApp.Shared.Domain
        {
            public interface IOwnedResource { string OwnerId { get; } }
        }
        namespace TemplateApp.Features.Tasks.Domain
        {
            public sealed class TaskItem { }
        }
        """;

    [Fact]
    public async Task ContractsThatAnotherFeatureUsesAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Auth/Contracts/IAuthModule.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using ErrorOr;
                using TemplateApp.Shared.Domain;
                namespace TemplateApp.Features.Auth.Contracts;
                public interface IAuthModule
                {
                    Task<bool> CanAccessAsync(string callerId, IOwnedResource resource, string permission, CancellationToken ct);
                    Task<UserAccountDetails?> FindAccountAsync(string authId, CancellationToken ct);
                    Task<ErrorOr<Success>> DeleteAccountAsync(string authId, CancellationToken ct);
                }
                """),
            ("backend/Features/Auth/Contracts/UserAccountDetails.cs", """
                using System.Collections.Generic;
                namespace TemplateApp.Features.Auth.Contracts;
                public sealed record UserAccountDetails(string AuthId, IReadOnlyList<string> Roles);
                """),
            ("backend/Features/Auth/Contracts/CallerExtensions.cs", """
                namespace TemplateApp.Features.Auth.Contracts;
                public static class CallerExtensions
                {
                    public static string GetCallerId(this string user) => user;
                }
                """),
            ("backend/Features/Auth/AuthModule.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using ErrorOr;
                using TemplateApp.Features.Auth.Contracts;
                using TemplateApp.Shared.Domain;
                namespace TemplateApp.Features.Auth;
                public sealed class AuthModule : IAuthModule
                {
                    public Task<bool> CanAccessAsync(string callerId, IOwnedResource resource, string permission, CancellationToken ct) => Task.FromResult(true);
                    public Task<UserAccountDetails?> FindAccountAsync(string authId, CancellationToken ct) => Task.FromResult<UserAccountDetails?>(null);
                    public Task<ErrorOr<Success>> DeleteAccountAsync(string authId, CancellationToken ct) => Task.FromResult(new ErrorOr<Success>(default));
                }
                """),
            ("backend/Features/Users/Contracts/UserResponse.cs", """
                using TemplateApp.Features.Auth.Contracts;
                namespace TemplateApp.Features.Users.Contracts;
                public sealed record UserResponse(string AuthId)
                {
                    public static UserResponse From(UserAccountDetails account) => new(account.AuthId);
                }
                """),
            ("backend/Features/Users/Contracts/UserPermissions.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                public static class UserPermissions
                {
                    public const string Read = "Permissions.Users.Read";
                }
                """),
            ("backend/Features/Users/Contracts/UserEndpointNames.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                public static class UserEndpointNames
                {
                    public const string GetUser = "Users.GetUser";
                }
                """),
            ("backend/Features/Users/GetUser/GetUser.cs", """
                using System.Threading;
                using System.Threading.Tasks;
                using TemplateApp.Features.Auth.Contracts;
                using TemplateApp.Shared.Domain;
                namespace TemplateApp.Features.Users.GetUser;
                public sealed class GetUser
                {
                    private readonly IAuthModule _auth;
                    public GetUser(IAuthModule auth) => _auth = auth;
                    public async Task<string?> HandleAsync(string caller, IOwnedResource resource, CancellationToken ct)
                    {
                        if (!await _auth.CanAccessAsync(caller.GetCallerId(), resource, "read", ct))
                            return null;
                        await _auth.DeleteAccountAsync(caller, ct);
                        UserAccountDetails? account = await _auth.FindAccountAsync(caller, ct);
                        return account?.AuthId;
                    }
                }
                """),
            ("backend/Features/Auth/RegisterUser/RegisterUser.cs", """
                using TemplateApp.Features.Users.Contracts;
                namespace TemplateApp.Features.Auth.RegisterUser;
                public static class RegisterUser
                {
                    public static string Describe(UserResponse user) => UserEndpointNames.GetUser + UserPermissions.Read + user.AuthId;
                }
                """),
            ("backend/Features/Tasks/TaskResponse.cs", """
                namespace TemplateApp.Features.Tasks;
                public sealed record TaskResponse(string Title);
                """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TypesThatAreNotContractsAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Auth/Contracts/IAccessPolicy.cs", """
                namespace TemplateApp.Features.Auth.Contracts;
                public interface IAccessPolicy { }
                """),
            ("backend/Features/Users/Contracts/UserProfileSnapshot.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                public sealed record UserProfileSnapshot(string DisplayName);
                """),
            ("backend/Features/Tasks/Contracts/TaskState.cs", """
                namespace TemplateApp.Features.Tasks.Contracts;
                public enum TaskState { Open, Done }
                """),
            ("backend/Features/Auth/Contracts/SessionUser.cs", """
                namespace TemplateApp.Features.Auth.Contracts;
                public sealed class SessionUser { }
                """),
            ("backend/Features/Auth/Contracts/AppIdentityUser.cs", """
                namespace TemplateApp.Features.Auth.Contracts;
                public sealed class AppIdentityUser : Microsoft.AspNetCore.Identity.IdentityUser { }
                """),
            ("backend/Features/Tasks/Contracts/TaskPermissions.cs", """
                namespace TemplateApp.Features.Tasks.Contracts;
                public static class TaskPermissions
                {
                    public static string Read() => "Permissions.Tasks.Read";
                }
                """));

        Assert.Equal(
            new[] { "AppIdentityUser.cs:2", "IAccessPolicy.cs:2", "SessionUser.cs:2", "TaskPermissions.cs:2", "TaskState.cs:2", "UserProfileSnapshot.cs:2" },
            Locations(diagnostics, compilationEnd: false));
    }

    [Fact]
    public async Task DoorThatExposesResponsesOrEntitiesIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Users/Contracts/UserResponse.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                public sealed record UserResponse(string AuthId);
                """),
            ("backend/Features/Auth/Contracts/IAuthModule.cs", """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                using TemplateApp.Features.Tasks.Domain;
                using TemplateApp.Features.Users.Contracts;
                namespace TemplateApp.Features.Auth.Contracts;
                public interface IAuthModule
                {
                    Task<UserResponse> DescribeAsync(string authId);
                    bool Owns(IReadOnlyList<TaskItem> tasks);
                    Task<bool> HasPermissionAsync(string userId, string permission);
                }
                """));

        Assert.Equal(
            new[] { "IAuthModule.cs:8", "IAuthModule.cs:9" },
            Locations(diagnostics, compilationEnd: false));
    }

    [Fact]
    public async Task ContractsThatNoOtherFeatureUsesAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/Contracts/TaskDetails.cs", """
                namespace TemplateApp.Features.Tasks.Contracts;
                public sealed record TaskDetails(string Title);
                """),
            ("backend/Features/Tasks/GetTask/GetTask.cs", """
                using TemplateApp.Features.Tasks.Contracts;
                namespace TemplateApp.Features.Tasks.GetTask;
                public static class GetTask
                {
                    public static TaskDetails Describe() => new("title");
                }
                """),
            ("backend/Features/Users/Contracts/UserSummary.cs", """
                namespace TemplateApp.Features.Users.Contracts;
                public sealed record UserSummary(string DisplayName);
                """),
            ("backend/Infrastructure/Persistence/Seed.cs", """
                using TemplateApp.Features.Users.Contracts;
                namespace TemplateApp.Infrastructure.Persistence;
                public static class Seed
                {
                    public static UserSummary Admin() => new("admin");
                }
                """),
            ("backend/Features/Auth/Contracts/IAuthModule.cs", """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                namespace TemplateApp.Features.Auth.Contracts;
                public interface IAuthModule
                {
                    Task<bool> HasPermissionAsync(string userId, string permission);
                    Task<IReadOnlySet<string>> GetPermissionsAsync(string userId);
                }
                """),
            ("backend/Features/Auth/AuthModule.cs", """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                using TemplateApp.Features.Auth.Contracts;
                namespace TemplateApp.Features.Auth;
                public sealed class AuthModule : IAuthModule
                {
                    public async Task<bool> HasPermissionAsync(string userId, string permission) =>
                        (await GetPermissionsAsync(userId)).Contains(permission);
                    public Task<IReadOnlySet<string>> GetPermissionsAsync(string userId) =>
                        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
                    public static Task<IReadOnlySet<string>> ReadAsync(IAuthModule auth) => auth.GetPermissionsAsync("me");
                }
                """),
            ("backend/Features/Tasks/CreateTask/CreateTask.cs", """
                using System.Threading.Tasks;
                using TemplateApp.Features.Auth.Contracts;
                namespace TemplateApp.Features.Tasks.CreateTask;
                public static class CreateTask
                {
                    public static Task<bool> CanCreateAsync(IAuthModule auth) => auth.HasPermissionAsync("me", "create");
                }
                """));

        Assert.Equal(
            new[] { "IAuthModule.cs:7", "TaskDetails.cs:2", "UserSummary.cs:2" },
            Locations(diagnostics, compilationEnd: true));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("stubs/Stubs.cs", Stubs) },
            AnalyzerHost.Only<ContractsAnalyzer>());

    private static string[] Locations(
        System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics,
        bool compilationEnd) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0015")
            .Where(diagnostic => diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.CompilationEnd) == compilationEnd)
            .Select(diagnostic =>
            {
                var span = diagnostic.Location.GetLineSpan();
                return $"{System.IO.Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}";
            })
            .OrderBy(location => location, System.StringComparer.Ordinal)
            .ToArray();
}
