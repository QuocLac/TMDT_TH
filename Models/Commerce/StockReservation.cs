using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class StockReservation : BaseEntity
{
    public long Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }
    public StockReservationStatus Status { get; set; } = StockReservationStatus.Reserved;

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTime ReservedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CommittedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }

    [MaxLength(500)]
    public string? ReleaseReason { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
