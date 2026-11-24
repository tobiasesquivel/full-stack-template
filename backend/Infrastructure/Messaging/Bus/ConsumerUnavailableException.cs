namespace TemplateApp.Infrastructure.Messaging.Bus;

public sealed class ConsumerUnavailableException : Exception
{
    public ConsumerUnavailableException()
    {
    }

    public ConsumerUnavailableException(string message)
        : base(message)
    {
    }

    public ConsumerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
