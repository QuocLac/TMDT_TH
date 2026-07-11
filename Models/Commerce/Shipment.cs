using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class Shipment : BaseEntity
{
    public long Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public ShipmentDirection Direction { get; set; } = ShipmentDirection.Outbound;

    public long? ParentShipmentId { get; set; }
    public Shipment? ParentShipment { get; set; }
    public ICollection<Shipment> ChildShipments { get; set; } = [];

    public long? ReturnRequestId { get; set; }
    public ReturnRequest? ReturnRequest { get; set; }

    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Draft;

    [MaxLength(100)]
    public string? ProviderStatus { get; set; }

    public DateTime? ProviderUpdatedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime? CarrierHandoffAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelRequestedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(50)]
    public string? ServiceCode { get; set; }

    [MaxLength(100)]
    public string? ServiceName { get; set; }

    [MaxLength(100)]
    public string? ExternalOrderCode { get; set; }

    [MaxLength(100)]
    public string? TrackingCode { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Fee { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CodAmount { get; set; }

    public int WeightGram { get; set; }
    public int LengthCm { get; set; }
    public int WidthCm { get; set; }
    public int HeightCm { get; set; }

    public DateTime? EstimatedDeliveryAt { get; set; }

    [MaxLength(100)]
    public string? ShipperName { get; set; }

    [MaxLength(30)]
    public string? ShipperPhone { get; set; }

    [MaxLength(150)]
    public string? CurrentHub { get; set; }

    [MaxLength(500)]
    public string? ProviderReason { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
