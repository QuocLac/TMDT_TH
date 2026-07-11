namespace WebApplication2.Services.Commerce.Orders;

public interface IOrderNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken);
}
