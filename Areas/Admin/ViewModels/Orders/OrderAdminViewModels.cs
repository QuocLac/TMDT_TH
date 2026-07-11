using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.ViewModels.Orders;

public sealed class OrderAdminIndexQuery
{
    [StringLength(100)]
    public string? Search { get; set; }

    public OrderStatus? OrderStatus { get; set; }

    public PaymentStatus? PaymentStatus { get; set; }

    public FulfillmentStatus? FulfillmentStatus { get; set; }

    [DataType(DataType.Date)]
    public DateTime? FromDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? ToDate { get; set; }

    [StringLength(50)]
    public string? Provider { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;
}

public sealed class OrderAdminIndexPageViewModel
{
    public IReadOnlyList<OrderAdminListItemViewModel> Items { get; init; } = [];

    public OrderAdminSummaryViewModel Summary { get; init; } = new();

    public IReadOnlyList<string> Providers { get; init; } = [];

    public string? Search { get; init; }

    public OrderStatus? OrderStatus { get; init; }

    public PaymentStatus? PaymentStatus { get; init; }

    public FulfillmentStatus? FulfillmentStatus { get; init; }

    public DateTime? FromDate { get; init; }

    public DateTime? ToDate { get; init; }

    public string? Provider { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalItems { get; init; }

    public int TotalPages { get; init; }
}

public sealed class OrderAdminSummaryViewModel
{
    public int TotalOrders { get; init; }

    public int PendingConfirmation { get; init; }

    public int Processing { get; init; }

    public int ReadyToShip { get; init; }

    public int DeliveryFailed { get; init; }
}

public sealed class OrderAdminListItemViewModel
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerPhone { get; init; } = string.Empty;

    public decimal GrandTotal { get; init; }

    public string Currency { get; init; } = "VND";

    public OrderStatus OrderStatus { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    public FulfillmentStatus FulfillmentStatus { get; init; }

    public string? Provider { get; init; }

    public string? TrackingCode { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? PlacedAt { get; init; }
}

public sealed class OrderAdminDetailsViewModel
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public Guid PublicToken { get; init; }

    public string ClientRequestId { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerEmail { get; init; } = string.Empty;

    public string CustomerPhone { get; init; } = string.Empty;

    public string ShippingAddress { get; init; } = string.Empty;

    public decimal Subtotal { get; init; }

    public decimal ShippingFee { get; init; }

    public decimal DiscountTotal { get; init; }

    public decimal TaxTotal { get; init; }

    public decimal GrandTotal { get; init; }

    public string Currency { get; init; } = "VND";

    public OrderStatus OrderStatus { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    public FulfillmentStatus FulfillmentStatus { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? PlacedAt { get; init; }

    public DateTime? ConfirmedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public DateTime? CancelledAt { get; init; }

    public string? CancelReason { get; init; }

    public string RowVersion { get; init; } = string.Empty;

    public IReadOnlyList<OrderStatus> AllowedOrderTransitions { get; init; } = [];

    public IReadOnlyList<PaymentStatus> AllowedPaymentTransitions { get; init; } = [];

    public IReadOnlyList<FulfillmentStatus> AllowedFulfillmentTransitions { get; init; } = [];

    public IReadOnlyList<OrderAdminItemViewModel> Items { get; init; } = [];

    public IReadOnlyList<OrderAdminPaymentViewModel> Payments { get; init; } = [];

    public IReadOnlyList<OrderAdminShipmentViewModel> Shipments { get; init; } = [];

    public IReadOnlyList<OrderAdminReservationViewModel> Reservations { get; init; } = [];

    public IReadOnlyList<OrderAdminTimelineViewModel> Timeline { get; init; } = [];

    public IReadOnlyList<OrderAdminIntegrationViewModel> IntegrationMessages { get; init; } = [];
}

public sealed class OrderAdminItemViewModel
{
    public int Id { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string? VariantDescription { get; init; }

    public string? ImageUrl { get; init; }

    public decimal ListPrice { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal DiscountAmount { get; init; }

    public decimal TaxAmount { get; init; }

    public int Quantity { get; init; }

    public decimal LineTotal { get; init; }
}

public sealed class OrderAdminPaymentViewModel
{
    public long Id { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public PaymentStatus Status { get; init; }

    public int AttemptNumber { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "VND";

    public string MerchantReference { get; init; } = string.Empty;

    public string? ProviderTransactionId { get; init; }

    public string? FailureCode { get; init; }

    public string? FailureMessage { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? CompletedAt { get; init; }
}

public sealed class OrderAdminShipmentViewModel
{
    public long Id { get; init; }

    public string Provider { get; init; } = string.Empty;

    public ShipmentStatus Status { get; init; }

    public string? ServiceCode { get; init; }

    public string? ServiceName { get; init; }

    public string? ExternalOrderCode { get; init; }

    public string? TrackingCode { get; init; }

    public decimal Fee { get; init; }

    public decimal CodAmount { get; init; }

    public DateTime? EstimatedDeliveryAt { get; init; }

    public string? ShipperName { get; init; }

    public string? ShipperPhone { get; init; }

    public string? CurrentHub { get; init; }

    public string? ProviderReason { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed class OrderAdminReservationViewModel
{
    public long Id { get; init; }

    public int OrderItemId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public StockReservationStatus Status { get; init; }

    public DateTime ReservedAt { get; init; }

    public DateTime ExpiresAt { get; init; }

    public DateTime? CommittedAt { get; init; }

    public DateTime? ReleasedAt { get; init; }

    public string? ReleaseReason { get; init; }
}

public sealed class OrderAdminTimelineViewModel
{
    public long Id { get; init; }

    public OrderHistoryCategory Category { get; init; }

    public string? FromStatus { get; init; }

    public string ToStatus { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string ChangedBy { get; init; } = string.Empty;

    public bool CustomerVisible { get; init; }

    public DateTime OccurredAt { get; init; }

    public string? CorrelationId { get; init; }
}

public sealed class OrderAdminIntegrationViewModel
{
    public string Direction { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public string? LastError { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? CompletedAt { get; init; }
}

public sealed class ChangeOrderStatusRequest
{
    [Required]
    public OrderStatus TargetStatus { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class ChangePaymentStatusRequest
{
    [Required]
    public PaymentStatus TargetStatus { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class ChangeFulfillmentStatusRequest
{
    [Required]
    public FulfillmentStatus TargetStatus { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public static class OrderAdminDisplay
{
    public static string OrderStatusText(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "Chờ thanh toán",
        OrderStatus.Placed => "Đã đặt hàng",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Processing => "Đang xử lý",
        OrderStatus.Completed => "Đã hoàn tất",
        OrderStatus.Cancelled => "Đã hủy",
        OrderStatus.Closed => "Đã đóng",
        _ => status.ToString()
    };

    public static string PaymentStatusText(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Chờ thanh toán",
        PaymentStatus.CodPending => "Chờ thu COD",
        PaymentStatus.Paid => "Đã thanh toán",
        PaymentStatus.Failed => "Thất bại",
        PaymentStatus.Cancelled => "Đã hủy",
        PaymentStatus.PartiallyRefunded => "Hoàn một phần",
        PaymentStatus.Refunded => "Đã hoàn tiền",
        _ => status.ToString()
    };

    public static string FulfillmentStatusText(FulfillmentStatus status) => status switch
    {
        FulfillmentStatus.Unfulfilled => "Chưa xử lý",
        FulfillmentStatus.Preparing => "Đang chuẩn bị",
        FulfillmentStatus.ReadyToShip => "Sẵn sàng giao",
        FulfillmentStatus.Shipped => "Đã bàn giao",
        FulfillmentStatus.Delivered => "Đã giao",
        FulfillmentStatus.DeliveryFailed => "Giao thất bại",
        FulfillmentStatus.Returning => "Đang hoàn hàng",
        FulfillmentStatus.Returned => "Đã hoàn hàng",
        FulfillmentStatus.Cancelled => "Đã hủy",
        _ => status.ToString()
    };

    public static string ShipmentStatusText(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Draft => "Bản nháp",
        ShipmentStatus.PendingCreation => "Chờ tạo vận đơn",
        ShipmentStatus.Created => "Đã tạo vận đơn",
        ShipmentStatus.CancelRequested => "Đang chờ GHN xác nhận hủy",
        ShipmentStatus.Picking => "Đang lấy hàng",
        ShipmentStatus.InTransit => "Đang vận chuyển",
        ShipmentStatus.Delivered => "Đã giao",
        ShipmentStatus.DeliveryFailed => "Giao thất bại",
        ShipmentStatus.Returning => "Đang hoàn",
        ShipmentStatus.Returned => "Đã hoàn",
        ShipmentStatus.Cancelled => "Đã hủy",
        ShipmentStatus.Exception => "Ngoại lệ",
        _ => status.ToString()
    };

    public static string ReservationStatusText(StockReservationStatus status) => status switch
    {
        StockReservationStatus.Reserved => "Đang giữ",
        StockReservationStatus.Committed => "Đã trừ kho",
        StockReservationStatus.Released => "Đã hoàn kho",
        StockReservationStatus.Expired => "Đã hết hạn",
        _ => status.ToString()
    };

    public static string CategoryText(OrderHistoryCategory category) => category switch
    {
        OrderHistoryCategory.Order => "Đơn hàng",
        OrderHistoryCategory.Payment => "Thanh toán",
        OrderHistoryCategory.Fulfillment => "Giao hàng",
        OrderHistoryCategory.Inventory => "Tồn kho",
        OrderHistoryCategory.Integration => "Tích hợp",
        _ => category.ToString()
    };

    public static string CssToken(Enum value)
    {
        return value.ToString()
            .Replace("_", "-", StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
