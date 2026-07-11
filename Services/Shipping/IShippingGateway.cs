namespace WebApplication2.Services.Shipping;

public interface IShippingGateway
{
    string Provider { get; }

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
