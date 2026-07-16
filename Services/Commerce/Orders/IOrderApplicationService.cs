using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Orders;

public sealed record PlaceOrderLine(
    int VariantId,
    int Quantity,
    decimal ExpectedUnitPrice);

public sealed record PlaceOrderCommand(
    int CustomerId,
    string ClientRequestId,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string ShippingAddressLine,
    int ShippingProvinceId,
    string ShippingProvinceName,
    int ShippingDistrictId,
    string ShippingDistrictName,
    string ShippingWardCode,
    string ShippingWardName,
    string PaymentMethod,
    string MockPaymentOutcome,
    IReadOnlyCollection<PlaceOrderLine> Lines);

public sealed record PlaceOrderResult(
    int OrderId,
    string OrderCode,
    Guid PublicToken,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    bool WasExisting,
    bool ShouldClearPurchasedItems,
    IReadOnlyList<int> PurchasedVariantIds);

public sealed record OrderReceiptLine(
    string ProductName,
    string VariantDescription,
    string Sku,
    string? ImageUrl,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderReceipt(
    string OrderCode,
    Guid PublicToken,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string ShippingAddress,
    decimal Subtotal,
    decimal ShippingFee,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    string Currency,
    DateTime CreatedAt,
    IReadOnlyList<OrderReceiptLine> Items);

public interface IOrderApplicationService
{
    Task<PlaceOrderResult?> FindByClientRequestIdAsync(
        string clientRequestId,
        int customerId,
        CancellationToken cancellationToken);

    Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderCommand command,
        CancellationToken cancellationToken);

    Task<OrderReceipt?> GetReceiptAsync(
        Guid publicToken,
        int customerId,
        CancellationToken cancellationToken);
}
