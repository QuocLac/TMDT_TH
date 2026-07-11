namespace WebApplication2.Services.Commerce.Orders;

public sealed class CheckoutValidationException : InvalidOperationException
{
    public CheckoutValidationException(string message)
        : base(message)
    {
    }
}

public sealed class CheckoutConflictException : InvalidOperationException
{
    public CheckoutConflictException(string message)
        : base(message)
    {
    }

    public CheckoutConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
