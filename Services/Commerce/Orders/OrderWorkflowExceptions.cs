namespace WebApplication2.Services.Commerce.Orders;

public sealed class OrderTransitionException : InvalidOperationException
{
    public OrderTransitionException(string message)
        : base(message)
    {
    }
}

public sealed class OrderConcurrencyException : InvalidOperationException
{
    public OrderConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
