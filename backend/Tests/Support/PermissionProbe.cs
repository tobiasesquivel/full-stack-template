using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Tests.Support;

internal sealed class PermissionProbe : IStartupFilter
{
    public const string Path = "/test/permissions";

    public static string Route(string userId, int checks) =>
        $"{Path.TrimStart('/')}?userId={Uri.EscapeDataString(userId)}&checks={checks}";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Map(Path, probe => probe.Run(CheckAsync));
    };

    private static async Task CheckAsync(HttpContext http)
    {
        IAuthModule auth = http.RequestServices.GetRequiredService<IAuthModule>();
        string userId = http.Request.Query["userId"].ToString();
        int checks = int.Parse(http.Request.Query["checks"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
        ForeignResource foreign = new($"foreign-{Guid.NewGuid():N}");

        List<bool> results = [];
        for (int i = 0; i < checks; i++)
        {
            results.Add(i % 2 == 0
                ? await auth.HasPermissionAsync(userId, TaskPermissionNames.Create, http.RequestAborted)
                : await auth.CanAccessAsync(userId, foreign, TaskPermissionNames.Read, http.RequestAborted));
        }

        await http.Response.WriteAsJsonAsync(results, http.RequestAborted);
    }
}

file sealed record ForeignResource(string OwnerId) : IOwnedResource;
