namespace WebApplication2.Services.Payments.VnPay;

public sealed record VnPayPaymentRequest(
    string MerchantReference,
    decimal Amount,
    string OrderDescription,
    string ClientIpAddress,
    DateTime CreatedAtUtc);

public sealed record VnPayPaymentUrlResult(
    bool Success,
    string? PaymentUrl,
    string? RequestPayload,
    DateTime? ExpiresAtUtc,
    string? ErrorCode,
    string Message)
{
    public static VnPayPaymentUrlResult Failure(
        string errorCode,
        string message) =>
        new(false, null, null, null, errorCode, message);
}

public sealed record VnPayCallbackData(
    bool SignatureValid,
    string MerchantReference,
    decimal Amount,
    string ResponseCode,
    string TransactionStatus,
    string? ProviderTransactionId,
    string? BankCode,
    string? CardType,
    DateTime? PaidAtUtc,
    string RawPayload,
    string? ErrorCode,
    string Message)
{
    public bool PaymentSucceeded =>
        SignatureValid
        && ResponseCode == "00"
        && TransactionStatus == "00"
        && !string.IsNullOrWhiteSpace(ProviderTransactionId);
}

public interface IVnPayGateway
{
    VnPayPaymentUrlResult CreatePaymentUrl(VnPayPaymentRequest request);

    VnPayCallbackData ParseCallback(
        IReadOnlyDictionary<string, string> parameters);
}
