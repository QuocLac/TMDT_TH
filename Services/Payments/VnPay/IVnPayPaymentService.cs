namespace WebApplication2.Services.Payments.VnPay;

public sealed record VnPayPaymentLinkResult(
    bool Success,
    string? PaymentUrl,
    Guid? PublicToken,
    string? ErrorCode,
    string Message)
{
    public static VnPayPaymentLinkResult Failure(
        string errorCode,
        string message,
        Guid? publicToken = null) =>
        new(false, null, publicToken, errorCode, message);
}

public sealed record VnPayCallbackProcessResult(
    string RspCode,
    string Message,
    bool SignatureValid,
    bool PaymentSucceeded,
    bool AlreadyProcessed,
    Guid? PublicToken,
    int? CustomerId,
    string? CheckoutClientRequestId,
    IReadOnlyList<int> PurchasedVariantIds);

public interface IVnPayPaymentService
{
    Task<VnPayPaymentLinkResult> CreatePaymentUrlAsync(
        int orderId,
        int customerId,
        string clientIpAddress,
        CancellationToken cancellationToken);

    Task<VnPayCallbackProcessResult> ProcessCallbackAsync(
        IReadOnlyDictionary<string, string> parameters,
        string source,
        CancellationToken cancellationToken);

    Task<int> ExpirePendingPaymentsAsync(
        CancellationToken cancellationToken);
}
