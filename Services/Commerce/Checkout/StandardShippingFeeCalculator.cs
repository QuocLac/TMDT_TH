namespace WebApplication2.Services.Commerce.Checkout;

public sealed class StandardShippingFeeCalculator : IShippingFeeCalculator
{
    private const decimal FreeShippingThreshold = 500_000m;
    private const decimal StandardFee = 30_000m;

    public decimal Calculate(decimal subtotal, int totalQuantity)
    {
        if (subtotal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subtotal));
        }

        if (totalQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalQuantity));
        }

        return subtotal >= FreeShippingThreshold ? 0m : StandardFee;
    }
}
