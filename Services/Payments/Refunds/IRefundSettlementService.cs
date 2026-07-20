namespace WebApplication2.Services.Payments.Refunds;

public sealed record RefundSettlementCommand(
    long ReturnRequestId,
    string Provider,
    string ProviderRefundReference,
    string? ProviderTransactionId,
    string? RequestPayload,
    string? ResponsePayload,
    string Actor,
    bool IsSimulation);

public sealed record RefundSettlementResult(
    bool Success,
    bool AlreadyProcessed,
    string Code,
    string Message,
    decimal RefundAmount,
    string? RefundReference)
{
    public static RefundSettlementResult Failure(
        string code,
        string message) =>
        new(false, false, code, message, 0m, null);

    public static RefundSettlementResult Completed(
        string code,
        string message,
        decimal refundAmount,
        string refundReference,
        bool alreadyProcessed = false) =>
        new(
            true,
            alreadyProcessed,
            code,
            message,
            refundAmount,
            refundReference);
}

public interface IRefundSettlementService
{
    Task<RefundSettlementResult> SettleAsync(
        RefundSettlementCommand command,
        CancellationToken cancellationToken);

    Task<RefundSettlementResult> CloseAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken);
}
