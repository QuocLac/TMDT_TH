using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Shipping.Internal;

namespace WebApplication2.Areas.Admin.ViewModels.Returns;

public sealed class ReturnAdminIndexViewModel
{
    public IReadOnlyList<ReturnAdminListItemViewModel> Items { get; init; } = [];
    public string? Search { get; init; }
    public ReturnRequestStatus? Status { get; init; }
}

public sealed class ReturnAdminListItemViewModel
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public ReturnRequestStatus Status { get; init; }
    public string StatusText => ReturnAdminDisplay.StatusText(Status);
    public int RequestedQuantity { get; init; }
    public int ApprovedQuantity { get; init; }
    public decimal RefundAmount { get; init; }
    public DateTime RequestedAt { get; init; }
    public DateTime ReturnWindowExpiresAt { get; init; }
}

public sealed class ReturnAdminDetailsViewModel
{
    public long Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public Guid OrderPublicToken { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public string CustomerAddress { get; init; } = string.Empty;
    public ReturnRequestStatus Status { get; init; }
    public string StatusText => ReturnAdminDisplay.StatusText(Status);
    public string ReasonCode { get; init; } = string.Empty;
    public string ReasonText { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
    public DateTime ReturnWindowExpiresAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? ReviewNote { get; init; }
    public DateTime? ReceivedAt { get; init; }
    public DateTime? InspectedAt { get; init; }
    public ReturnInspectionResult? InspectionResult { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    public decimal RefundAmount { get; init; }
    public string RefundBankName { get; init; } = string.Empty;
    public string RefundBankCode { get; init; } = string.Empty;
    public string RefundAccountNumber { get; init; } = string.Empty;
    public string RefundAccountName { get; init; } = string.Empty;
    public string? VietQrUrl { get; init; }
    public IReadOnlyList<ReturnAdminProgressStepViewModel> Progress { get; init; } = [];
    public IReadOnlyList<ReturnAdminItemViewModel> Items { get; init; } = [];
    public IReadOnlyList<ReturnAdminEvidenceViewModel> Evidence { get; init; } = [];
    public ReturnAdminShipmentViewModel? Shipment { get; init; }
    public IReadOnlyList<ReturnAdminHistoryViewModel> History { get; init; } = [];
}

public sealed class ReturnAdminProgressStepViewModel
{
    public int Position { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsComplete { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed class ReturnAdminItemViewModel
{
    public long Id { get; init; }
    public int OrderItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? VariantDescription { get; init; }
    public int PurchasedQuantity { get; init; }
    public int RequestedQuantity { get; init; }
    public int ApprovedQuantity { get; init; }
    public int ReceivedQuantity { get; init; }
    public int AcceptedQuantity { get; init; }
    public int RejectedQuantity { get; init; }
    public int RestockQuantity { get; init; }
    public int WriteOffQuantity { get; init; }
    public ReturnItemCondition? ConditionCode { get; init; }
    public string? InspectionNote { get; init; }
    public decimal RefundAmount { get; init; }
}

public sealed class ReturnAdminEvidenceViewModel
{
    public ReturnEvidenceType Type { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Caption { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class ReturnAdminShipmentViewModel
{
    public long Id { get; init; }
    public ShipmentStatus Status { get; init; }
    public string StatusText => InternalShippingLifecycleService.StatusTitle(Status);
    public string? TrackingCode { get; init; }
    public DateTime? CarrierHandoffAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public string? Note { get; init; }
}

public sealed class ReturnAdminHistoryViewModel
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ChangedBy { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
}

public sealed class StartReturnReviewInput
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Note { get; set; }
}

public sealed class DecideReturnInput
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    public bool Approve { get; set; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Note { get; set; } = string.Empty;

    public List<ReturnApprovalItemInput> Items { get; set; } = [];
}

public sealed class ReturnApprovalItemInput
{
    public long ReturnItemId { get; set; }

    [Range(0, 99)]
    public int ApprovedQuantity { get; set; }
}

public sealed class ReturnShippingTransitionInput
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    public InternalReturnShippingAction Action { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed class BeginReturnInspectionInput
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Note { get; set; }

    public List<ReturnReceiptItemInput> Items { get; set; } = [];
}

public sealed class ReturnReceiptItemInput
{
    public long ReturnItemId { get; set; }

    [Range(0, 99)]
    public int ReceivedQuantity { get; set; }
}

public sealed class CompleteReturnInspectionInput
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [Required, StringLength(1000, MinimumLength = 3)]
    public string Note { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 16)]
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

    public List<ReturnInspectionItemInput> Items { get; set; } = [];
}

public sealed class ReturnInspectionItemInput
{
    public long ReturnItemId { get; set; }

    [Range(0, 99)]
    public int AcceptedQuantity { get; set; }

    [Range(0, 99)]
    public int RejectedQuantity { get; set; }

    [Range(0, 99)]
    public int RestockQuantity { get; set; }

    [Range(0, 99)]
    public int WriteOffQuantity { get; set; }

    public ReturnItemCondition ConditionCode { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed class CompleteReturnRefundInput
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;
}

public static class ReturnAdminDisplay
{
    public static string StatusText(ReturnRequestStatus status) => status switch
    {
        ReturnRequestStatus.Requested => "Đã gửi yêu cầu",
        ReturnRequestStatus.UnderReview => "Đang xem xét",
        ReturnRequestStatus.Approved => "Đã chấp nhận",
        ReturnRequestStatus.Rejected => "Không được chấp nhận",
        ReturnRequestStatus.AwaitingReturnShipment => "Chờ tiếp nhận hàng hoàn",
        ReturnRequestStatus.AwaitingPickup => "Chờ khách bàn giao",
        ReturnRequestStatus.ReturnInTransit => "Hàng hoàn đang vận chuyển",
        ReturnRequestStatus.ReceivedAtWarehouse => "Đã nhận hàng hoàn",
        ReturnRequestStatus.Inspecting => "Đang kiểm tra sản phẩm",
        ReturnRequestStatus.RefundPending => "Chờ hoàn tiền",
        ReturnRequestStatus.RejectedAfterInspection => "Không đủ điều kiện hoàn tiền",
        ReturnRequestStatus.Refunded => "Đã hoàn tiền",
        ReturnRequestStatus.Closed => "Đã hoàn tất",
        ReturnRequestStatus.Cancelled => "Đã hủy",
        _ => "Đang xử lý"
    };
}
