namespace WebApplication2.Services.Commerce.Inventory;

public sealed class InventoryValidationException : InvalidOperationException
{
    public InventoryValidationException(string message)
        : base(message)
    {
    }
}

public sealed class InventoryConflictException : InvalidOperationException
{
    public InventoryConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
