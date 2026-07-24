using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Shipping.Internal;

namespace WebApplication2.Areas.Admin.ViewModels.Shipping;

public sealed class ShippingAdminIndexViewModel
{
    public IReadOnlyList<ShippingAdminListItemViewModel> Items { get; init; } = [];
    public string? Search { get; init; }
    public ShipmentStatus? Status { get; init; }
}

public sealed class ShippingAdminListItemViewModel
{
    public long Id { get; init; }
    public int OrderId { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public ShipmentStatus Status { get; init; }
    public string StatusText => InternalShippingLifecycleService.StatusTitle(Status);
    public string? TrackingCode { get; init; }
    public string ServiceName { get; init; } = "Giao hàng nhanh";
    public decimal Fee { get; init; }
    public decimal CodAmount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class ShippingAdminDetailsViewModel
{
    public long Id { get; init; }
    public int OrderId { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;
    public ShipmentStatus Status { get; init; }
    public string StatusText => InternalShippingLifecycleService.StatusTitle(Status);
    public FulfillmentStatus FulfillmentStatus { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? TrackingCode { get; init; }
    public string ServiceName { get; init; } = "Giao hàng nhanh";
    public decimal Fee { get; init; }
    public decimal CodAmount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? EstimatedDeliveryAt { get; init; }
    public DateTime? CarrierHandoffAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public string? Note { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    public IReadOnlyList<ShippingProgressStepViewModel> Progress { get; init; } = [];
    public IReadOnlyList<ShipmentStatus> AllowedTargets { get; init; } = [];
    public IReadOnlyList<ShippingHistoryItemViewModel> History { get; init; } = [];
}

public sealed class ShippingProgressStepViewModel
{
    public int Position { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsComplete { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed class ShippingHistoryItemViewModel
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ChangedBy { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
}

public sealed class ShippingTransitionInput
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    public ShipmentStatus TargetStatus { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
