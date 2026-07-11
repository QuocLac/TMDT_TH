using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Returns;

public sealed record ReturnEligibilityLine(
    int OrderItemId,
    string ProductName,
    string Sku,
    string? VariantDescription,
    string? ImageUrl,
    int PurchasedQuantity,
    int CancelledQuantity,
    int ReservedReturnQuantity,
    int AvailableQuantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record ReturnEligibilitySnapshot(
    int OrderId,
    Guid OrderPublicToken,
    string OrderCode,
    string CustomerName,
    string CustomerEmail,
    OrderStatus OrderStatus,
    FulfillmentStatus FulfillmentStatus,
    DateTime? DeliveredAt,
    DateTime? ReturnDeadlineAt,
    bool IsEligible,
    string? ErrorCode,
    string Message,
    IReadOnlyList<ReturnEligibilityLine> Items);

public sealed record ReturnRequestLineCommand(int OrderItemId, int Quantity);

public sealed record ReturnEvidenceCommand(
    ReturnEvidenceType Type,
    string Url,
    string? Caption);

public sealed record CreateReturnRequestCommand(
    Guid OrderPublicToken,
    string ReasonCode,
    string ReasonText,
    string RequestedBy,
    string IdempotencyKey,
    IReadOnlyCollection<ReturnRequestLineCommand> Lines,
    IReadOnlyCollection<ReturnEvidenceCommand> Evidence);

public sealed record StartReturnReviewCommand(
    long ReturnRequestId,
    byte[] RowVersion,
    string Actor,
    string Note);

public sealed record ReturnApprovalLineCommand(
    long ReturnItemId,
    int ApprovedQuantity);

public sealed record DecideReturnRequestCommand(
    long ReturnRequestId,
    bool Approve,
    byte[] RowVersion,
    string Actor,
    string Note,
    IReadOnlyCollection<ReturnApprovalLineCommand> Lines);

public sealed record ReturnReceiptLineCommand(
    long ReturnItemId,
    int ReceivedQuantity);

public sealed record BeginReturnInspectionCommand(
    long ReturnRequestId,
    byte[] RowVersion,
    string Actor,
    string Note,
    IReadOnlyCollection<ReturnReceiptLineCommand> Lines);

public sealed record ReturnInspectionLineCommand(
    long ReturnItemId,
    int AcceptedQuantity,
    int RejectedQuantity,
    int RestockQuantity,
    int WriteOffQuantity,
    ReturnItemCondition ConditionCode,
    string? Note);

public sealed record CompleteReturnInspectionCommand(
    long ReturnRequestId,
    byte[] RowVersion,
    string Actor,
    string Note,
    string IdempotencyKey,
    IReadOnlyCollection<ReturnInspectionLineCommand> Lines);

public interface IReturnWorkflowService
{
    Task<ReturnEligibilitySnapshot?> GetEligibilityAsync(
        Guid orderPublicToken,
        CancellationToken cancellationToken);

    Task<ReturnRequest> CreateRequestAsync(
        CreateReturnRequestCommand command,
        CancellationToken cancellationToken);

    Task<ReturnRequest> StartReviewAsync(
        StartReturnReviewCommand command,
        CancellationToken cancellationToken);

    Task<ReturnRequest> DecideAsync(
        DecideReturnRequestCommand command,
        CancellationToken cancellationToken);

    Task<ReturnRequest> BeginInspectionAsync(
        BeginReturnInspectionCommand command,
        CancellationToken cancellationToken);

    Task<ReturnRequest> CompleteInspectionAsync(
        CompleteReturnInspectionCommand command,
        CancellationToken cancellationToken);
}
