using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class ErrorCatalogAnalyzerTests
{
    private const string ErrorOrStub = """
        namespace ErrorOr
        {
            using System.Collections.Generic;
            public readonly record struct Error
            {
                public string Code { get; init; }
                public string Description { get; init; }
                public static Error Validation(string code = "General.Validation", string description = "", Dictionary<string, object>? metadata = null) => default;
                public static Error NotFound(string code = "General.NotFound", string description = "", Dictionary<string, object>? metadata = null) => default;
                public static Error Forbidden(string code = "General.Forbidden", string description = "", Dictionary<string, object>? metadata = null) => default;
                public static Error Conflict(string code = "General.Conflict", string description = "", Dictionary<string, object>? metadata = null) => default;
                public static Error Failure(string code = "General.Failure", string description = "", Dictionary<string, object>? metadata = null) => default;
                public static Error Unexpected(string code = "General.Unexpected", string description = "", Dictionary<string, object>? metadata = null) => default;
            }
        }
        """;

    private const string IdentityErrorStub = """
        namespace Microsoft.AspNetCore.Identity
        {
            public sealed class IdentityError
            {
                public string Code { get; set; } = "";
                public string Description { get; set; } = "";
            }
        }
        """;

    [Fact]
    public async Task ErrorsBuiltInTheirCatalogAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/TaskErrors.cs", """
                using System;
                using ErrorOr;
                namespace TemplateApp.Features.Tasks;
                public static class TaskErrors
                {
                    public static Error NotFound(Guid taskId) => Error.NotFound(
                        code: "Task.NotFound",
                        description: $"Task with ID '{taskId}' not found.");
                    public static readonly Error Forbidden = Error.Forbidden("Task.Forbidden", "Access denied.");
                    public static readonly Error InvalidPage = Error.Validation(code: "Page", description: "Page must be positive.");
                }
                """),
            ("backend/Features/Auth/AuthErrors.cs", """
                using ErrorOr;
                using Microsoft.AspNetCore.Identity;
                namespace TemplateApp.Features.Auth;
                public static class AuthErrors
                {
                    public static Error IdentityRejected(IdentityError error) => Error.Validation(code: error.Code, description: error.Description);
                    public static Error AccountDeleteFailed(IdentityError error) => Error.Failure(code: "Auth.AccountDeleteFailed", description: error.Description);
                }
                """),
            ("backend/Features/Tasks/GetTask/GetTask.cs", """
                using ErrorOr;
                namespace TemplateApp.Features.Tasks.GetTask;
                public static class GetTask
                {
                    public static Error Deny() => TaskErrors.Forbidden;
                }
                """),
            ("backend/Shared/Http/HttpErrors.cs", """
                using ErrorOr;
                namespace TemplateApp.Shared.Http;
                public static class HttpErrors
                {
                    public static readonly Error ValidationFailed = Error.Validation(code: "Validation.Failed", description: "Invalid.");
                    public static readonly Error Unexpected = Error.Unexpected(code: "Http.Unexpected", description: "Unexpected.");
                }
                """),
            ("backend/Tests/Support/FakeErrors.cs", """
                using ErrorOr;
                namespace TemplateApp.Tests.Support;
                public static class Fake
                {
                    public static Error Any() => Error.Failure(code: "Fake.Any");
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ErrorsBuiltOutsideTheCatalogAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/GetTask/GetTask.cs", """
                using ErrorOr;
                namespace TemplateApp.Features.Tasks.GetTask;
                public static class GetTask
                {
                    public static Error Deny() =>
                        Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");
                }
                """),
            ("backend/Features/Auth/RegisterUser/RegisterUser.cs", """
                using ErrorOr;
                using Microsoft.AspNetCore.Identity;
                namespace TemplateApp.Features.Auth.RegisterUser;
                public static class RegisterUser
                {
                    public static Error Reject(IdentityError e) =>
                        Error.Validation(code: e.Code, description: e.Description);
                }
                """),
            ("backend/Infrastructure/Http/GlobalExceptionHandler.cs", """
                using ErrorOr;
                namespace TemplateApp.Infrastructure.Http;
                public static class GlobalExceptionHandler
                {
                    public static Error Fail() => Error.Unexpected();
                }
                """));

        Assert.Equal(
            new[] { "GetTask.cs:6", "GlobalExceptionHandler.cs:5", "RegisterUser.cs:7" },
            Locations(diagnostics));
    }

    [Fact]
    public async Task CatalogCodesWithAForeignPrefixAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/TaskErrors.cs", """
                using ErrorOr;
                namespace TemplateApp.Features.Tasks;
                public static class TaskErrors
                {
                    public static readonly Error Owner = Error.NotFound(code: "User.NotFound", description: "Missing.");
                    public static readonly Error Duplicate = Error.Conflict("Tasks.Duplicate", "Duplicate.");
                    public static readonly Error Broken = Error.Failure();
                    public static readonly Error Ok = Error.Conflict(code: "Task.Duplicate", description: "Duplicate.");
                }
                """));

        Assert.Equal(
            new[] { "TaskErrors.cs:5", "TaskErrors.cs:6", "TaskErrors.cs:7" },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files)
            {
                ("stubs/ErrorOr.cs", ErrorOrStub),
                ("stubs/Identity.cs", IdentityErrorStub),
            },
            AnalyzerHost.Only<ErrorCatalogAnalyzer>());

    private static string[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0014")
            .Select(diagnostic =>
            {
                var span = diagnostic.Location.GetLineSpan();
                return $"{System.IO.Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}";
            })
            .OrderBy(location => location, System.StringComparer.Ordinal)
            .ToArray();
}
