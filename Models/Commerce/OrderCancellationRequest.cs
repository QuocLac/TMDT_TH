using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

[Table("OrderCancellationRequests")]
[Index(nameof(IdempotencyKey), IsUnique = true)]
[Index(nameof(OrderId), nameof(Status), nameof(RequestedAt))]
public sealed class OrderCancellationRequest : BaseEntity
{
    public long Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [Required, MaxLength(100)]
    public string RequestedBy { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string ReasonCode { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string ReasonText { get; set; } = string.Empty;

    public OrderCancellationStatus Status { get; set; } = OrderCancellationStatus.Pending;

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(100)]
    public string? ReviewedBy { get; set; }

    [MaxLength(500)]
    public string? ReviewNote { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<OrderCancellationItem> Items { get; set; } = [];
}
