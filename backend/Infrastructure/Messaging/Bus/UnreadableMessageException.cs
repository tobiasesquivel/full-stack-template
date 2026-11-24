namespace TemplateApp.Infrastructure.Messaging.Bus;

public sealed class UnreadableMessageException : Exception
{
    public UnreadableMessageException()
    {
    }

    public UnreadableMessageException(string message)
        : base(message)
    {
    }

    public UnreadableMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
