using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.Presentation;

public static class AdminCommerceDisplay
{
    public static string OrderStatusText(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "Chờ thanh toán",
        OrderStatus.Placed => "Đã tiếp nhận",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Processing => "Đang chuẩn bị",
        OrderStatus.Completed => "Hoàn tất",
        OrderStatus.Cancelled => "Đã hủy",
        OrderStatus.Closed => "Đã đóng",
        _ => status.ToString()
    };

    public static string PaymentStatusText(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Chờ thanh toán",
        PaymentStatus.CodPending => "Chờ thu khi giao",
        PaymentStatus.Paid => "Đã thanh toán",
        PaymentStatus.Failed => "Thanh toán chưa thành công",
        PaymentStatus.Cancelled => "Đã hủy thanh toán",
        PaymentStatus.PartiallyRefunded => "Đã hoàn một phần",
        PaymentStatus.Refunded => "Đã hoàn tiền",
        _ => status.ToString()
    };

    public static string FulfillmentStatusText(FulfillmentStatus status) => status switch
    {
        FulfillmentStatus.Unfulfilled => "Chưa chuẩn bị",
        FulfillmentStatus.Preparing => "Đang chuẩn bị hàng",
        FulfillmentStatus.ReadyToShip => "Sẵn sàng bàn giao",
        FulfillmentStatus.Shipped => "Đang giao hàng",
        FulfillmentStatus.Delivered => "Giao thành công",
        FulfillmentStatus.DeliveryFailed => "Giao chưa thành công",
        FulfillmentStatus.Returning => "Đang hoàn về",
        FulfillmentStatus.Returned => "Đã hoàn về",
        FulfillmentStatus.Cancelled => "Đã hủy xử lý hàng",
        _ => status.ToString()
    };

    public static string ShipmentStatusText(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Draft => "Chưa tạo vận đơn",
        ShipmentStatus.PendingCreation => "Đang tạo vận đơn",
        ShipmentStatus.Created => "Đã tạo vận đơn",
        ShipmentStatus.CancelRequested => "Đang chờ xác nhận hủy",
        ShipmentStatus.Picking => "Đang lấy hàng",
        ShipmentStatus.InTransit => "Đang vận chuyển",
        ShipmentStatus.Delivered => "Giao thành công",
        ShipmentStatus.DeliveryFailed => "Giao chưa thành công",
        ShipmentStatus.Returning => "Đang hoàn về",
        ShipmentStatus.Returned => "Đã hoàn về",
        ShipmentStatus.Cancelled => "Đã hủy vận đơn",
        ShipmentStatus.Exception => "Cần kiểm tra",
        _ => status.ToString()
    };

    public static string ShipmentDirectionText(ShipmentDirection direction) => direction switch
    {
        ShipmentDirection.Outbound => "Giao tới khách hàng",
        ShipmentDirection.Return => "Hoàn về kho",
        _ => direction.ToString()
    };

    public static string ReturnStatusText(ReturnRequestStatus status) => status switch
    {
        ReturnRequestStatus.Requested => "Mới tiếp nhận",
        ReturnRequestStatus.UnderReview => "Đang xem xét",
        ReturnRequestStatus.Approved => "Đã chấp thuận",
        ReturnRequestStatus.Rejected => "Không chấp thuận",
        ReturnRequestStatus.AwaitingReturnShipment => "Chờ tạo vận đơn hoàn",
        ReturnRequestStatus.AwaitingPickup => "Chờ lấy hàng hoàn",
        ReturnRequestStatus.ReturnInTransit => "Đang hoàn về kho",
        ReturnRequestStatus.ReceivedAtWarehouse => "Kho đã nhận hàng",
        ReturnRequestStatus.Inspecting => "Đang kiểm định",
        ReturnRequestStatus.RefundPending => "Chờ xử lý hoàn tiền",
        ReturnRequestStatus.RejectedAfterInspection => "Từ chối sau kiểm định",
        ReturnRequestStatus.Refunded => "Đã hoàn tiền",
        ReturnRequestStatus.Closed => "Đã kết thúc",
        ReturnRequestStatus.Cancelled => "Đã hủy yêu cầu",
        _ => status.ToString()
    };

    public static string Tone(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "warning",
        OrderStatus.Placed or OrderStatus.Confirmed or OrderStatus.Processing => "info",
        OrderStatus.Completed or OrderStatus.Closed => "success",
        OrderStatus.Cancelled => "danger",
        _ => "neutral"
    };

    public static string Tone(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending or PaymentStatus.CodPending => "warning",
        PaymentStatus.Paid => "success",
        PaymentStatus.Failed or PaymentStatus.Cancelled => "danger",
        PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded => "info",
        _ => "neutral"
    };

    public static string Tone(FulfillmentStatus status) => status switch
    {
        FulfillmentStatus.Unfulfilled => "neutral",
        FulfillmentStatus.Preparing
            or FulfillmentStatus.ReadyToShip
            or FulfillmentStatus.Shipped => "info",
        FulfillmentStatus.Delivered => "success",
        FulfillmentStatus.DeliveryFailed
            or FulfillmentStatus.Cancelled => "danger",
        FulfillmentStatus.Returning
            or FulfillmentStatus.Returned => "warning",
        _ => "neutral"
    };

    public static string Tone(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Draft => "neutral",
        ShipmentStatus.PendingCreation
            or ShipmentStatus.CancelRequested
            or ShipmentStatus.Returning => "warning",
        ShipmentStatus.Created
            or ShipmentStatus.Picking
            or ShipmentStatus.InTransit => "info",
        ShipmentStatus.Delivered => "success",
        ShipmentStatus.DeliveryFailed
            or ShipmentStatus.Exception => "danger",
        ShipmentStatus.Returned
            or ShipmentStatus.Cancelled => "neutral",
        _ => "neutral"
    };

    public static string Tone(ReturnRequestStatus status) => status switch
    {
        ReturnRequestStatus.Requested
            or ReturnRequestStatus.UnderReview
            or ReturnRequestStatus.AwaitingReturnShipment
            or ReturnRequestStatus.AwaitingPickup
            or ReturnRequestStatus.ReturnInTransit
            or ReturnRequestStatus.ReceivedAtWarehouse
            or ReturnRequestStatus.Inspecting => "info",
        ReturnRequestStatus.Approved
            or ReturnRequestStatus.Refunded
            or ReturnRequestStatus.Closed => "success",
        ReturnRequestStatus.RefundPending => "warning",
        ReturnRequestStatus.Rejected
            or ReturnRequestStatus.RejectedAfterInspection
            or ReturnRequestStatus.Cancelled => "danger",
        _ => "neutral"
    };
}
