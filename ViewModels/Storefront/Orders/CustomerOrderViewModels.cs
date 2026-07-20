using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.ViewModels.Storefront.Orders;

public sealed class CustomerOrderIndexViewModel
{
    public string Query { get; init; } = string.Empty;
    public string Status { get; init; } = "all";
    public int Page { get; init; } = 1;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public CustomerOrderSummaryViewModel Summary { get; init; } = new();
    public IReadOnlyList<CustomerOrderCardViewModel> Orders { get; init; } = [];
}

public sealed class CustomerOrderSummaryViewModel
{
    public int All { get; init; }
    public int AwaitingPayment { get; init; }
    public int Processing { get; init; }
    public int Shipping { get; init; }
    public int Completed { get; init; }
    public int Cancelled { get; init; }
    public int Returns { get; init; }
}

public sealed class CustomerOrderCardViewModel
{
    public Guid PublicToken { get; init; }
    public string Code { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public string StatusTone { get; init; } = "neutral";
    public string PaymentStatusText { get; init; } = string.Empty;
    public string FulfillmentStatusText { get; init; } = string.Empty;
    public decimal GrandTotal { get; init; }
    public int TotalQuantity { get; init; }
    public string? TrackingCode { get; init; }
    public bool CanRetryPayment { get; init; }
    public int AdditionalItemCount { get; init; }
    public IReadOnlyList<CustomerOrderCardLineViewModel> Items { get; init; } = [];
}

public sealed class CustomerOrderCardLineViewModel
{
    public string ProductName { get; init; } = string.Empty;
    public string? VariantDescription { get; init; }
    public string? ImageUrl { get; init; }
    public int Quantity { get; init; }
}

public sealed class CustomerOrderDetailsViewModel
{
    public int OrderId { get; init; }
    public Guid PublicToken { get; init; }
    public string Code { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public OrderStatus OrderStatus { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public FulfillmentStatus FulfillmentStatus { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public string StatusTone { get; init; } = "neutral";
    public string PaymentStatusText { get; init; } = string.Empty;
    public string FulfillmentStatusText { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;
    public decimal Subtotal { get; init; }
    public decimal ShippingFee { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal GrandTotal { get; init; }
    public string PaymentMethodText { get; init; } = "Chưa xác định";
    public bool CanRetryPayment { get; init; }
    public bool CanRequestCancellation { get; init; }
    public string? CancellationMessage { get; init; }
    public bool CanRequestReturn { get; init; }
    public string? ReturnMessage { get; init; }
    public string? CancelReason { get; init; }
    public string? ErrorMessage { get; init; }
    public CustomerShipmentViewModel? Shipment { get; init; }
    public CustomerCancellationInputModel Cancellation { get; init; } = new();
    public IReadOnlyList<CustomerOrderProgressStepViewModel> Progress { get; init; } = [];
    public IReadOnlyList<CustomerOrderLineViewModel> Items { get; init; } = [];
    public IReadOnlyList<CustomerOrderTimelineViewModel> Timeline { get; init; } = [];
    public IReadOnlyList<CustomerPaymentAttemptViewModel> Payments { get; init; } = [];
    public IReadOnlyList<CustomerCancellationRequestViewModel> CancellationRequests { get; init; } = [];
    public IReadOnlyList<CustomerReturnRequestViewModel> ReturnRequests { get; init; } = [];
}

public sealed class CustomerOrderProgressStepViewModel
{
    public int Position { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsComplete { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed class CustomerOrderLineViewModel
{
    public int OrderItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? VariantDescription { get; init; }
    public string? ImageUrl { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public int Quantity { get; init; }
    public int CancellableQuantity { get; init; }
}

public sealed class CustomerShipmentViewModel
{
    public string Provider { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string StatusTone { get; init; } = "neutral";
    public string? ServiceName { get; init; }
    public string? TrackingCode { get; init; }
    public DateTime? EstimatedDeliveryAt { get; init; }
    public string? CurrentHub { get; init; }
    public string? ProviderReason { get; init; }
}

public sealed class CustomerPaymentAttemptViewModel
{
    public string Provider { get; init; } = string.Empty;
    public string MethodText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string StatusTone { get; init; } = "neutral";
    public int AttemptNumber { get; init; }
    public decimal Amount { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed class CustomerOrderTimelineViewModel
{
    public string CategoryText { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime OccurredAt { get; init; }
}

public sealed class CustomerCancellationRequestViewModel
{
    public long Id { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public string StatusTone { get; init; } = "neutral";
    public string ReasonText { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
    public string? ReviewNote { get; init; }
    public decimal RefundAmount { get; init; }
    public IReadOnlyList<CustomerCancellationRequestLineViewModel> Items { get; init; } = [];
}

public sealed class CustomerCancellationRequestLineViewModel
{
    public string ProductName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public int RequestedQuantity { get; init; }
    public int ApprovedQuantity { get; init; }
}

public sealed class CustomerReturnRequestViewModel
{
    public string Code { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string StatusTone { get; init; } = "neutral";
    public string ReasonText { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
    public int RequestedQuantity { get; init; }
    public decimal RefundAmount { get; init; }
    public string? TrackingCode { get; init; }
}

public sealed class CustomerCancellationInputModel
{
    [Range(1, int.MaxValue)]
    public int OrderId { get; set; }

    public Guid OrderPublicToken { get; set; }

    [Required]
    public string OrderRowVersion { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [RegularExpression("^(ChangedMind|WrongAddress|DuplicateOrder|PaymentIssue|Other)$")]
    public string ReasonCode { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 5)]
    public string ReasonText { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 16)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [MinLength(1)]
    public List<CustomerCancellationLineInputModel> Lines { get; set; } = [];
}

public sealed class CustomerCancellationLineInputModel
{
    [Range(1, int.MaxValue)]
    public int OrderItemId { get; set; }

    [Range(0, 99)]
    public int Quantity { get; set; }
}

public static class CustomerOrderDisplay
{
    public static string OrderStatusText(OrderStatus value) => value switch
    {
        WebApplication2.Models.Enums.OrderStatus.PendingPayment => "Chờ thanh toán",
        WebApplication2.Models.Enums.OrderStatus.Placed => "Đã đặt hàng",
        WebApplication2.Models.Enums.OrderStatus.Confirmed => "Đã xác nhận",
        WebApplication2.Models.Enums.OrderStatus.Processing => "Đang xử lý",
        WebApplication2.Models.Enums.OrderStatus.Completed => "Hoàn tất",
        WebApplication2.Models.Enums.OrderStatus.Cancelled => "Đã hủy",
        WebApplication2.Models.Enums.OrderStatus.Closed => "Đã đóng",
        _ => value.ToString()
    };

    public static string PaymentStatusText(PaymentStatus value) => value switch
    {
        WebApplication2.Models.Enums.PaymentStatus.Pending => "Chờ thanh toán",
        WebApplication2.Models.Enums.PaymentStatus.CodPending => "Thanh toán khi nhận hàng",
        WebApplication2.Models.Enums.PaymentStatus.Paid => "Đã thanh toán",
        WebApplication2.Models.Enums.PaymentStatus.Failed => "Thanh toán thất bại",
        WebApplication2.Models.Enums.PaymentStatus.Cancelled => "Đã hủy thanh toán",
        WebApplication2.Models.Enums.PaymentStatus.PartiallyRefunded => "Đã hoàn một phần",
        WebApplication2.Models.Enums.PaymentStatus.Refunded => "Đã hoàn tiền",
        _ => value.ToString()
    };

    public static string FulfillmentStatusText(FulfillmentStatus value) => value switch
    {
        WebApplication2.Models.Enums.FulfillmentStatus.Unfulfilled => "Chưa chuẩn bị",
        WebApplication2.Models.Enums.FulfillmentStatus.Preparing => "Đang chuẩn bị hàng",
        WebApplication2.Models.Enums.FulfillmentStatus.ReadyToShip => "Sẵn sàng giao",
        WebApplication2.Models.Enums.FulfillmentStatus.Shipped => "Đang giao hàng",
        WebApplication2.Models.Enums.FulfillmentStatus.Delivered => "Đã giao hàng",
        WebApplication2.Models.Enums.FulfillmentStatus.DeliveryFailed => "Giao hàng chưa thành công",
        WebApplication2.Models.Enums.FulfillmentStatus.Returning => "Đang hoàn về",
        WebApplication2.Models.Enums.FulfillmentStatus.Returned => "Đã hoàn về",
        WebApplication2.Models.Enums.FulfillmentStatus.Cancelled => "Đã hủy giao hàng",
        _ => value.ToString()
    };

    public static string ShipmentStatusText(ShipmentStatus value) => value switch
    {
        WebApplication2.Models.Enums.ShipmentStatus.Draft => "Đang chuẩn bị vận đơn",
        WebApplication2.Models.Enums.ShipmentStatus.PendingCreation => "Đang tạo vận đơn",
        WebApplication2.Models.Enums.ShipmentStatus.Created => "Đã tạo vận đơn",
        WebApplication2.Models.Enums.ShipmentStatus.CancelRequested => "Đang yêu cầu hủy vận đơn",
        WebApplication2.Models.Enums.ShipmentStatus.Picking => "Đang lấy hàng",
        WebApplication2.Models.Enums.ShipmentStatus.InTransit => "Đang vận chuyển",
        WebApplication2.Models.Enums.ShipmentStatus.Delivered => "Giao thành công",
        WebApplication2.Models.Enums.ShipmentStatus.DeliveryFailed => "Giao chưa thành công",
        WebApplication2.Models.Enums.ShipmentStatus.Returning => "Đang hoàn hàng",
        WebApplication2.Models.Enums.ShipmentStatus.Returned => "Đã hoàn hàng",
        WebApplication2.Models.Enums.ShipmentStatus.Cancelled => "Vận đơn đã hủy",
        WebApplication2.Models.Enums.ShipmentStatus.Exception => "Vận đơn cần kiểm tra",
        _ => value.ToString()
    };

    public static string ReturnStatusText(ReturnRequestStatus value) => value switch
    {
        ReturnRequestStatus.Requested => "Đã gửi yêu cầu",
        ReturnRequestStatus.UnderReview => "Đang xem xét",
        ReturnRequestStatus.Approved => "Đã chấp nhận",
        ReturnRequestStatus.Rejected => "Đã từ chối",
        ReturnRequestStatus.AwaitingReturnShipment => "Chờ tạo vận đơn hoàn",
        ReturnRequestStatus.AwaitingPickup => "Chờ lấy hàng",
        ReturnRequestStatus.ReturnInTransit => "Đang chuyển về kho",
        ReturnRequestStatus.ReceivedAtWarehouse => "Kho đã nhận hàng",
        ReturnRequestStatus.Inspecting => "Đang kiểm tra hàng",
        ReturnRequestStatus.RefundPending => "Chờ hoàn tiền",
        ReturnRequestStatus.RejectedAfterInspection => "Không đạt điều kiện hoàn",
        ReturnRequestStatus.Refunded => "Đã hoàn tiền",
        ReturnRequestStatus.Closed => "Đã hoàn tất",
        ReturnRequestStatus.Cancelled => "Đã hủy yêu cầu",
        _ => value.ToString()
    };

    public static string Category(OrderHistoryCategory value) => value switch
    {
        OrderHistoryCategory.Order => "Đơn hàng",
        OrderHistoryCategory.Payment => "Thanh toán",
        OrderHistoryCategory.Fulfillment => "Giao hàng",
        OrderHistoryCategory.Inventory => "Chuẩn bị hàng",
        OrderHistoryCategory.Integration => "Đối tác vận hành",
        OrderHistoryCategory.Return => "Hoàn trả",
        _ => value.ToString()
    };

    public static string PaymentMethod(string? value) => value?.ToUpperInvariant() switch
    {
        "COD" => "Thanh toán khi nhận hàng",
        "VNPAY" => "Thanh toán trực tuyến qua VNPay",
        "REFUND" => "Hoàn tiền",
        null or "" => "Chưa xác định",
        _ => value ?? "Chưa xác định"
    };

    public static string CancellationStatus(string value) => value switch
    {
        "Pending" => "Đang chờ xử lý",
        "Approved" => "Đã chấp nhận",
        "Rejected" => "Đã từ chối",
        "Cancelled" => "Đã hủy yêu cầu",
        _ => value
    };

    public static string Tone(OrderStatus value) => value switch
    {
        WebApplication2.Models.Enums.OrderStatus.PendingPayment => "warning",
        WebApplication2.Models.Enums.OrderStatus.Placed or WebApplication2.Models.Enums.OrderStatus.Confirmed or WebApplication2.Models.Enums.OrderStatus.Processing => "info",
        WebApplication2.Models.Enums.OrderStatus.Completed or WebApplication2.Models.Enums.OrderStatus.Closed => "success",
        WebApplication2.Models.Enums.OrderStatus.Cancelled => "danger",
        _ => "neutral"
    };

    public static string Tone(PaymentStatus value) => value switch
    {
        WebApplication2.Models.Enums.PaymentStatus.Pending or WebApplication2.Models.Enums.PaymentStatus.CodPending => "warning",
        WebApplication2.Models.Enums.PaymentStatus.Paid => "success",
        WebApplication2.Models.Enums.PaymentStatus.Failed or WebApplication2.Models.Enums.PaymentStatus.Cancelled => "danger",
        WebApplication2.Models.Enums.PaymentStatus.PartiallyRefunded or WebApplication2.Models.Enums.PaymentStatus.Refunded => "info",
        _ => "neutral"
    };

    public static string Tone(FulfillmentStatus value) => value switch
    {
        WebApplication2.Models.Enums.FulfillmentStatus.Preparing or WebApplication2.Models.Enums.FulfillmentStatus.ReadyToShip
            or WebApplication2.Models.Enums.FulfillmentStatus.Shipped or WebApplication2.Models.Enums.FulfillmentStatus.Returning => "info",
        WebApplication2.Models.Enums.FulfillmentStatus.Delivered => "success",
        WebApplication2.Models.Enums.FulfillmentStatus.DeliveryFailed or WebApplication2.Models.Enums.FulfillmentStatus.Cancelled => "danger",
        WebApplication2.Models.Enums.FulfillmentStatus.Returned => "warning",
        _ => "neutral"
    };

    public static string Tone(ShipmentStatus value) => value switch
    {
        WebApplication2.Models.Enums.ShipmentStatus.Picking or WebApplication2.Models.Enums.ShipmentStatus.InTransit or WebApplication2.Models.Enums.ShipmentStatus.Returning => "info",
        WebApplication2.Models.Enums.ShipmentStatus.Delivered => "success",
        WebApplication2.Models.Enums.ShipmentStatus.DeliveryFailed or WebApplication2.Models.Enums.ShipmentStatus.Exception or WebApplication2.Models.Enums.ShipmentStatus.Cancelled => "danger",
        WebApplication2.Models.Enums.ShipmentStatus.Returned or WebApplication2.Models.Enums.ShipmentStatus.CancelRequested => "warning",
        _ => "neutral"
    };

    public static string Tone(ReturnRequestStatus value) => value switch
    {
        ReturnRequestStatus.Approved or ReturnRequestStatus.Refunded or ReturnRequestStatus.Closed => "success",
        ReturnRequestStatus.Rejected or ReturnRequestStatus.RejectedAfterInspection or ReturnRequestStatus.Cancelled => "danger",
        _ => "info"
    };

    public static string CancellationTone(string value) => value switch
    {
        "Pending" => "warning",
        "Approved" => "success",
        "Rejected" or "Cancelled" => "danger",
        _ => "neutral"
    };
}
