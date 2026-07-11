using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Cancellations;

public sealed record CancellationRequestLineCommand(
    int OrderItemId,
    int Quantity);

public sealed record CreateOrderCancellationCommand(
    int OrderId,
    byte[] OrderRowVersion,
    string ReasonCode,
    string ReasonText,
    string RequestedBy,
    string IdempotencyKey,
    IReadOnlyCollection<CancellationRequestLineCommand> Lines);

public sealed record ReviewOrderCancellationCommand(
    int OrderId,
    long CancellationRequestId,
    byte[] OrderRowVersion,
    byte[] CancellationRowVersion,
    bool Approve,
    string ReviewedBy,
    string? ReviewNote);

public sealed record OrderCancellationItemSummary(
    long Id,
    int OrderItemId,
    string ProductName,
    string Sku,
    int RequestedQuantity,
    int ApprovedQuantity,
    decimal RefundAmount);

public sealed record OrderCancellationRequestSummary(
    long Id,
    string Status,
    string ReasonCode,
    string ReasonText,
    string RequestedBy,
    DateTime RequestedAt,
    DateTime? ReviewedAt,
    string? ReviewedBy,
    string? ReviewNote,
    string RowVersion,
    decimal RefundAmount,
    IReadOnlyList<OrderCancellationItemSummary> Items);

public sealed record OrderCancellationLineAvailability(
    int OrderItemId,
    string ProductName,
    string Sku,
    int OrderedQuantity,
    int ApprovedCancelledQuantity,
    int PendingQuantity,
    int CancellableQuantity);

public sealed record OrderCancellationSummary(
    int OrderId,
    string OrderCode,
    string OrderRowVersion,
    bool CanRequest,
    string? IneligibilityMessage,
    IReadOnlyList<OrderCancellationLineAvailability> Items,
    IReadOnlyList<OrderCancellationRequestSummary> Requests);

public interface IOrderCancellationService
{
    Task<OrderCancellationSummary> GetSummaryAsync(
        int orderId,
        CancellationToken cancellationToken);

    Task<OrderCancellationSummary> RequestAsync(
        CreateOrderCancellationCommand command,
        CancellationToken cancellationToken);

    Task<OrderCancellationSummary> ReviewAsync(
        ReviewOrderCancellationCommand command,
        CancellationToken cancellationToken);
}
