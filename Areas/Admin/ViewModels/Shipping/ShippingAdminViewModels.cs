using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

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
    public ShipmentDirection Direction { get; init; }
    public ShipmentStatus Status { get; init; }
    public string? ProviderStatus { get; init; }
    public string? TrackingCode { get; init; }
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
    public int? DistrictId { get; init; }
    public string? WardCode { get; init; }
    public ShipmentDirection Direction { get; init; }
    public ShipmentStatus Status { get; init; }
    public string? ProviderStatus { get; init; }
    public FulfillmentStatus FulfillmentStatus { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? ServiceCode { get; init; }
    public string? ServiceName { get; init; }
    public string? ExternalOrderCode { get; init; }
    public string? TrackingCode { get; init; }
    public decimal Fee { get; init; }
    public decimal CodAmount { get; init; }
    public int WeightGram { get; init; }
    public int LengthCm { get; init; }
    public int WidthCm { get; init; }
    public int HeightCm { get; init; }
    public DateTime? EstimatedDeliveryAt { get; init; }
    public DateTime? ProviderUpdatedAt { get; init; }
    public DateTime? LastSyncedAt { get; init; }
    public DateTime? CarrierHandoffAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? ReturnDeadlineAt { get; init; }
    public DateTime? CancelRequestedAt { get; init; }
    public DateTime? CancelledAt { get; init; }
    public bool CanQueueCreate { get; init; }
    public bool HasPendingOrderCancellation { get; init; }
    public bool CanRequestCancellation { get; init; }
    public string? ShipperName { get; init; }
    public string? ShipperPhone { get; init; }
    public string? CurrentHub { get; init; }
    public string? ProviderReason { get; init; }
    public IReadOnlyList<ShippingOutboxItemViewModel> Outbox { get; init; } = [];
    public IReadOnlyList<ShippingInboxItemViewModel> Inbox { get; init; } = [];
}

public sealed class ShippingOutboxItemViewModel
{
    public long Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public IntegrationOutboxStatus Status { get; init; }
    public int AttemptCount { get; init; }
    public DateTime? NextAttemptAt { get; init; }
    public string? LastError { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class ShippingInboxItemViewModel
{
    public long Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public IntegrationEventStatus Status { get; init; }
    public string? ExternalEventId { get; init; }
    public DateTime? ProviderOccurredAt { get; init; }
    public string? LastError { get; init; }
    public DateTime ReceivedAt { get; init; }
}

public sealed class ShippingExecutionRequest
{
    [Range(0, int.MaxValue)]
    public int ServiceId { get; set; }

    [Range(2, 5)]
    public int ServiceTypeId { get; set; } = 2;

    [Range(1, 50_000)]
    public int WeightGram { get; set; } = 500;

    [Range(1, 200)]
    public int LengthCm { get; set; } = 20;

    [Range(1, 200)]
    public int WidthCm { get; set; } = 15;

    [Range(1, 200)]
    public int HeightCm { get; set; } = 10;

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed class ShippingCancelRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}
