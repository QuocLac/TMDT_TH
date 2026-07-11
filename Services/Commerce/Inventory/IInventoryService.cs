using WebApplication2.Models;

namespace WebApplication2.Services.Commerce.Inventory;

public sealed record InventoryReservationLine(
    int OrderItemId,
    int ProductVariantId,
    int Quantity);

public interface IInventoryService
{
    Task<IReadOnlyList<StockReservation>> ReserveAsync(
        int orderId,
        IReadOnlyCollection<InventoryReservationLine> lines,
        DateTime expiresAtUtc,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken);

    Task CommitAsync(
        int orderId,
        string correlationId,
        string actor,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        int orderId,
        string reason,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken);
}
