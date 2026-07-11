using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Shipping;

public sealed record ShippingServiceOption(int ServiceId, int ServiceTypeId, string Name);

public sealed record ShippingParty(
    string Name,
    string Phone,
    string Address,
    int DistrictId,
    string WardCode);

public sealed record ShippingParcelItem(
    string Name,
    string Code,
    int Quantity,
    int Price,
    int WeightGram,
    int LengthCm,
    int WidthCm,
    int HeightCm);

public sealed record ShippingQuoteRequest(
    int FromDistrictId,
    string FromWardCode,
    int ToDistrictId,
    string ToWardCode,
    int ServiceId,
    int ServiceTypeId,
    int WeightGram,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    int InsuranceValue,
    int CodAmount,
    IReadOnlyList<ShippingParcelItem> Items);

public sealed record ShippingQuote(
    decimal TotalFee,
    decimal ServiceFee,
    decimal InsuranceFee,
    decimal CodFee,
    string RawResponse);

public sealed record ShippingCreateRequest(
    string ClientOrderCode,
    ShippingParty Sender,
    ShippingParty Recipient,
    ShippingParty ReturnAddress,
    int ServiceId,
    int ServiceTypeId,
    int PaymentTypeId,
    string RequiredNote,
    string Note,
    int WeightGram,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    int InsuranceValue,
    int CodAmount,
    IReadOnlyList<ShippingParcelItem> Items);

public sealed record ShippingCreateResult(
    string ExternalOrderCode,
    string TrackingCode,
    decimal TotalFee,
    DateTime? ExpectedDeliveryAt,
    string RawResponse);

public sealed record ShippingDetail(
    string ExternalOrderCode,
    string ProviderStatus,
    ShipmentStatus Status,
    decimal Fee,
    decimal CodAmount,
    int WeightGram,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    DateTime? ExpectedDeliveryAt,
    DateTime? ProviderUpdatedAt,
    string? ShipperName,
    string? ShipperPhone,
    string? CurrentHub,
    string? Reason,
    string RawResponse);

public sealed record ShippingOperationResult<T>(
    bool Success,
    T? Data,
    string? ErrorCode,
    string Message,
    bool Retryable)
{
    public static ShippingOperationResult<T> Ok(T data) =>
        new(true, data, null, string.Empty, false);

    public static ShippingOperationResult<T> Failure(
        string errorCode,
        string message,
        bool retryable = false) =>
        new(false, default, errorCode, message, retryable);
}

public sealed record ShippingQueueResult(
    long? OutboxMessageId,
    bool AlreadyQueued,
    string Message);
