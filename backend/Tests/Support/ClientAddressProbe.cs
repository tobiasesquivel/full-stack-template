using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace TemplateApp.Tests.Support;

internal sealed class ClientAddressProbe : IStartupFilter
{
    public const string Header = "X-Test-Client-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((http, following) =>
        {
            if (IPAddress.TryParse(http.Request.Headers[Header], out IPAddress? address))
                http.Connection.RemoteIpAddress = address;
            return following(http);
        });
        next(app);
    };
}
