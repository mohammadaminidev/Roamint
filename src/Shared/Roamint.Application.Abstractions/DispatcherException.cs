namespace Roamint.Application.Abstractions;

/// <summary>Raised when a command/query cannot be routed to exactly one handler.</summary>
public sealed class DispatcherException : Exception
{
    public DispatcherException()
    {
    }

    public DispatcherException(string message) : base(message)
    {
    }

    public DispatcherException(string message, Exception innerException) : base(message, innerException)
    {
    }

    internal static DispatcherException NoHandler(Type messageType) =>
        new($"No handler registered for '{messageType.FullName}'.");

    internal static DispatcherException MultipleHandlers(Type messageType) =>
        new($"Multiple handlers registered for '{messageType.FullName}'. Commands and queries require exactly one handler.");
}
