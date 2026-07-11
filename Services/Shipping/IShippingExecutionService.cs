using WebApplication2.Models;

namespace WebApplication2.Services.Shipping;

public sealed record ShippingExecutionInput(
    int ServiceId,
    int ServiceTypeId,
    int WeightGram,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    string? Note);

public interface IShippingExecutionService
{
    Task<ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>> GetServicesAsync(
        long shipmentId,
        CancellationToken cancellationToken);

    Task<ShippingOperationResult<ShippingQuote>> QuoteAsync(
        long shipmentId,
        ShippingExecutionInput input,
        CancellationToken cancellationToken);

    Task<ShippingQueueResult> QueueCreateAsync(
        long shipmentId,
        ShippingExecutionInput input,
        string actor,
        CancellationToken cancellationToken);

    Task<ShippingQueueResult> QueueCancelAsync(
        long shipmentId,
        string reason,
        string actor,
        CancellationToken cancellationToken);

    Task<ShippingOperationResult<bool>> SyncAsync(
        long shipmentId,
        string actor,
        CancellationToken cancellationToken);
}
