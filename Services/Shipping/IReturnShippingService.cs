using WebApplication2.Models;

namespace WebApplication2.Services.Shipping;

public sealed record ReturnShippingExecutionInput(
    int ServiceId,
    int ServiceTypeId,
    int WeightGram,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    string? Note);

public interface IReturnShippingService
{
    Task<ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>> GetServicesAsync(
        long returnRequestId,
        CancellationToken cancellationToken);

    Task<ShippingOperationResult<ShippingQuote>> QuoteAsync(
        long returnRequestId,
        ReturnShippingExecutionInput input,
        CancellationToken cancellationToken);

    Task<ShippingQueueResult> QueueCreateAsync(
        long returnRequestId,
        ReturnShippingExecutionInput input,
        byte[] rowVersion,
        string actor,
        CancellationToken cancellationToken);

    Task<ShippingOperationResult<Shipment>> SyncAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken);
}
