using WebApplication2.Services.Shipping;

namespace WebApplication2.Services.Shipping.Ghn;

public sealed class GhnShippingGateway : IShippingGateway
{
    private readonly IGhnShippingClient _client;

    public GhnShippingGateway(IGhnShippingClient client)
    {
        _client = client;
    }

    public string Provider => "GHN";

    public Task<ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>> GetAvailableServicesAsync(
        int toDistrictId,
        CancellationToken cancellationToken) =>
        _client.GetAvailableServicesAsync(toDistrictId, cancellationToken);

    public Task<ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>> GetAvailableServicesAsync(
        int fromDistrictId,
        int toDistrictId,
        CancellationToken cancellationToken) =>
        _client.GetAvailableServicesAsync(fromDistrictId, toDistrictId, cancellationToken);

    public Task<ShippingOperationResult<ShippingQuote>> QuoteAsync(
        ShippingQuoteRequest request,
        CancellationToken cancellationToken) =>
        _client.QuoteAsync(request, cancellationToken);

    public Task<ShippingOperationResult<ShippingCreateResult>> CreateAsync(
        ShippingCreateRequest request,
        CancellationToken cancellationToken) =>
        _client.CreateAsync(request, cancellationToken);

    public Task<ShippingOperationResult<bool>> CancelAsync(
        string externalOrderCode,
        CancellationToken cancellationToken) =>
        _client.CancelAsync(externalOrderCode, cancellationToken);

    public Task<ShippingOperationResult<ShippingDetail>> GetDetailAsync(
        string externalOrderCode,
        CancellationToken cancellationToken) =>
        _client.GetDetailAsync(externalOrderCode, cancellationToken);
}
