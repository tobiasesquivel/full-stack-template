namespace TemplateApp.Infrastructure.Http;

public sealed class RemoteServiceUnavailableException : Exception
{
    public RemoteServiceUnavailableException()
    {
    }

    public RemoteServiceUnavailableException(string message)
        : base(message)
    {
    }

    public RemoteServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
