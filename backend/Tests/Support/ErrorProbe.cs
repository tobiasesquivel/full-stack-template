using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace TemplateApp.Tests.Support;

internal sealed class ErrorProbe : IStartupFilter
{
    public const string ThrowPath = "/test/throw";
    public const string ForbidPath = "/test/forbid";
    public const string SecretMessage = "Host=db;Password=secret";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Map(ThrowPath, probe => probe.Run(_ => throw new InvalidOperationException(SecretMessage)));
        app.Map(ForbidPath, probe => probe.Run(http => http.ForbidAsync()));
    };
}
