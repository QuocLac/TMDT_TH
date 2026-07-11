namespace WebApplication2.Services.Commerce.Checkout;

public interface IShippingFeeCalculator
{
    decimal Calculate(decimal subtotal, int totalQuantity);
}
