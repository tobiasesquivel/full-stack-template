using System.Net;
using System.Text.Json;
using Xunit;

namespace TemplateApp.Tests.Support;

internal static class ProblemAssert
{
    public static JsonElement Problem(SessionResponse response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.Status);
        Assert.StartsWith("application/problem+json", Assert.Contains("Content-Type", response.Headers), StringComparison.Ordinal);
        Assert.NotNull(response.Payload);
        JsonElement problem = response.Payload.RootElement.Clone();
        response.Payload.Dispose();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        return problem;
    }
}
