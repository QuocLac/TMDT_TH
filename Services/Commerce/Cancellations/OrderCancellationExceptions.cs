namespace WebApplication2.Services.Commerce.Cancellations;

public class OrderCancellationException : InvalidOperationException
{
    public OrderCancellationException(string message)
        : base(message)
    {
    }

    public OrderCancellationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class OrderCancellationConcurrencyException : OrderCancellationException
{
    public OrderCancellationConcurrencyException(string message)
        : base(message)
    {
    }

    public OrderCancellationConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
