using System.Net;
using System.Net.Sockets;
using WireMock.Server;
using WireMock.Settings;

namespace TemplateApp.Tests.Support;

public static class LoopbackWireMock
{
    public static WireMockServer Start() =>
        WireMockServer.Start(new WireMockServerSettings { Urls = [$"http://127.0.0.1:{FreePort()}"] });

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
