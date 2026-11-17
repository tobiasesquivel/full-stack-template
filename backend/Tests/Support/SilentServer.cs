using System.Net;
using System.Net.Sockets;

namespace TemplateApp.Tests.Support;

public sealed class SilentServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public SilentServer() => _listener.Start();

    public string ConnectionString =>
        $"Host=127.0.0.1;Port={((IPEndPoint)_listener.LocalEndpoint).Port};Database=silent;Username=postgres;Password=silent;Timeout=15;Command Timeout=15";

    public void Dispose() => _listener.Dispose();
}
