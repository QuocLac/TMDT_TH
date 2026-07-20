namespace WebApplication2.Services.Payments.Reconciliation;

public enum DevelopmentPaymentIssueType
{
    PaidTransactionOrderPending,
    RefundLedgerReturnPending,
    LegacyRefundLedgerMissing,
    OrderPaymentStatusMismatch
}

public sealed record DevelopmentPaymentIssueSnapshot(
    DevelopmentPaymentIssueType Type,
    string Title,
    string Description,
    int OrderId,
    string OrderCode,
    long? ReturnRequestId,
    string? ReturnCode,
    decimal Amount,
    bool IsSafeAutomaticRepair);

public sealed record DevelopmentPaymentRepairResult(
    bool Success,
    bool AlreadyConsistent,
    string Code,
    string Message)
{
    public static DevelopmentPaymentRepairResult Failure(
        string code,
        string message) =>
        new(false, false, code, message);

    public static DevelopmentPaymentRepairResult Completed(
        string code,
        string message,
        bool alreadyConsistent = false) =>
        new(true, alreadyConsistent, code, message);
}

public sealed record DevelopmentPaymentRepairBatchResult(
    int ScannedCount,
    int RepairedCount,
    int FailedCount,
    IReadOnlyList<string> Failures);

public interface IDevelopmentPaymentReconciliationService
{
    bool IsEnabled { get; }

    Task<IReadOnlyList<DevelopmentPaymentIssueSnapshot>> ScanAsync(
        CancellationToken cancellationToken);

    Task<DevelopmentPaymentRepairResult> RepairPaidOrderAsync(
        int orderId,
        string actor,
        CancellationToken cancellationToken);

    Task<DevelopmentPaymentRepairResult> RepairRefundStateAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken);

    Task<DevelopmentPaymentRepairResult> BackfillLegacyRefundLedgerAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken);

    Task<DevelopmentPaymentRepairResult> RepairOrderPaymentStatusAsync(
        int orderId,
        string actor,
        CancellationToken cancellationToken);

    Task<DevelopmentPaymentRepairBatchResult> RepairSafeIssuesAsync(
        string actor,
        CancellationToken cancellationToken);
}
