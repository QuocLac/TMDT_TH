using WebApplication2.Services.Shipping;

namespace WebApplication2.Services.Shipping.Ghn;

public interface IGhnShippingClient
{
    Task<ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>> GetAvailableServicesAsync(
        int toDistrictId,
        CancellationToken cancellationToken);

    Task<ShippingOperationResult<ShippingQuote>> QuoteAsync(
        ShippingQuoteRequest request,
        CancellationToken cancellationToken);

    Task<ShippingOperationResult<ShippingCreateResult>> CreateAsync(
        ShippingCreateRequest request,
        CancellationToken cancellationToken);

    Task<ShippingOperationResult<bool>> CancelAsync(
        string externalOrderCode,
        CancellationToken cancellationToken);

    Task<ShippingOperationResult<ShippingDetail>> GetDetailAsync(
        string externalOrderCode,
        CancellationToken cancellationToken);
}
