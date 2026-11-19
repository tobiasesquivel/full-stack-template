namespace TemplateApp.Infrastructure.Messaging;

public sealed class BrokerUnavailableException : Exception
{
    public BrokerUnavailableException()
    {
    }

    public BrokerUnavailableException(string message)
        : base(message)
    {
    }

    public BrokerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
